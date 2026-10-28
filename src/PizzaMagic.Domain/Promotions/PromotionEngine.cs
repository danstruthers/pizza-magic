using System.Text.Json;
using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Time;

namespace PizzaMagic.Domain.Promotions;

/// <summary>
/// Evaluates entered promo codes against the catalog (plan section 2.6): normalize, gates, conflicts,
/// eligibility, then effects in (priority, id) order. Deterministic and pure.
/// </summary>
public sealed class PromotionEngine(PromotionRegistry registry, IConflictPolicy conflictPolicy)
{
    private sealed record Parsed(IEffectCalculator Effect, object EffectParams, IReadOnlyList<(IConditionEvaluator Kind, object Params)> Conditions);

    private sealed record Candidate(int Index, NormalizedCode Code, PromotionDefinition Promotion, Parsed Parsed);

    public PromotionResult Evaluate(PromotionCatalog catalog, IReadOnlyList<string> enteredCodes, PromotionContext context)
    {
        var normalized = CodeNormalizer.Normalize(enteredCodes, catalog, context.MaxPromoCodes);
        var outcomes = new CodeOutcome[normalized.Count];

        var passed = ApplyGates(normalized, catalog, context, outcomes);
        var held = ResolveConflicts(passed, catalog, outcomes);
        var eligible = CheckEligibility(held, context, outcomes);
        var totalDiscount = ApplyEffects(eligible, context.GrossSubtotal, outcomes);

        return new PromotionResult(outcomes, totalDiscount, held.Select(c => c.Code.Code!).ToList());
    }

    /// <summary>Steps 1-2: drop malformed, duplicate and over-limit codes, then apply the row-level gates.</summary>
    private List<Candidate> ApplyGates(
        IReadOnlyList<NormalizedCode> normalized, PromotionCatalog catalog, PromotionContext context, CodeOutcome[] outcomes)
    {
        var promotions = catalog.Promotions.ToDictionary(p => p.Id);
        var passed = new List<Candidate>();
        for (var i = 0; i < normalized.Count; i++)
        {
            var code = normalized[i];
            if (code.DroppedAs is { } dropped)
            {
                outcomes[i] = Outcome(code, dropped, DroppedMessage(code, dropped, context.MaxPromoCodes));
                continue;
            }

            var (status, promotion, parsed) = Gate(code, promotions, context);
            if (status is { } failed)
            {
                outcomes[i] = Outcome(code, failed, GateMessage(code.Code!, failed), promotion?.Id);
                continue;
            }
            passed.Add(new Candidate(i, code, promotion!, parsed!));
        }
        return passed;
    }

    /// <summary>Step 3: conflicts. Rejected codes are never evaluated further.</summary>
    private List<Candidate> ResolveConflicts(List<Candidate> passed, PromotionCatalog catalog, CodeOutcome[] outcomes)
    {
        var decisions = conflictPolicy.Resolve(passed.Select(c => new ConflictCandidate(c.Code.Code!, c.Promotion)).ToList(), catalog);
        var held = new List<Candidate>();
        for (var i = 0; i < passed.Count; i++)
        {
            var candidate = passed[i];
            if (decisions[i].Held)
            {
                held.Add(candidate);
                continue;
            }
            var blockers = decisions[i].ConflictsWith;
            var code = candidate.Code.Code!;
            var message = PromoMessage.Plain($"{code} can't be combined with {Join(blockers, "or")}. Remove {Join(blockers, "and")} to use {code}.");
            outcomes[candidate.Index] = Outcome(candidate.Code, PromoStatus.Rejected, message, candidate.Promotion.Id) with { ConflictsWith = blockers };
        }
        return held;
    }

    /// <summary>Step 4: eligibility. Every failing condition is reported.</summary>
    private static List<Candidate> CheckEligibility(List<Candidate> held, PromotionContext context, CodeOutcome[] outcomes)
    {
        var eligible = new List<Candidate>();
        foreach (var candidate in held)
        {
            var failures = candidate.Parsed.Conditions
                .Select(c => c.Kind.Evaluate(c.Params, context, candidate.Code.Code!))
                .Where(r => !r.Passed)
                .Select(r => r.Failure!)
                .ToList();
            if (failures.Count > 0)
            {
                outcomes[candidate.Index] = Outcome(candidate.Code, PromoStatus.NotEligible, PromoMessage.Combine(failures), candidate.Promotion.Id);
                continue;
            }
            eligible.Add(candidate);
        }
        return eligible;
    }

    /// <summary>Step 5: effects, lower priority first, each on what remains. Returns the total discount.</summary>
    private static Money ApplyEffects(List<Candidate> eligible, Money grossSubtotal, CodeOutcome[] outcomes)
    {
        var remaining = grossSubtotal;
        foreach (var candidate in eligible.OrderBy(c => c.Promotion.Priority).ThenBy(c => c.Promotion.Id))
        {
            var code = candidate.Code.Code!;
            var discount = candidate.Parsed.Effect.Apply(candidate.Parsed.EffectParams, remaining);
            if (discount.Cents < 0 || discount.Cents > remaining.Cents)
            {
                throw new InvalidOperationException($"Effect '{candidate.Promotion.EffectType}' returned {discount.Cents} with {remaining.Cents} remaining.");
            }
            if (discount == Money.Zero)
            {
                outcomes[candidate.Index] = Outcome(candidate.Code, PromoStatus.NotEligible,
                    PromoMessage.Plain($"{code} doesn't reduce this order any further."), candidate.Promotion.Id);
                continue;
            }

            remaining -= discount;
            var applied = new PromoMessage($"{code} saves {{discount}}.", new Dictionary<string, long> { ["discount"] = discount.Cents });
            outcomes[candidate.Index] = Outcome(candidate.Code, PromoStatus.Applied, applied, candidate.Promotion.Id) with { Discount = discount };
        }
        return grossSubtotal - remaining;
    }

    /// <summary>Row-level gates (step 2). Returns a terminal status, or the promotion and its parsed kinds.</summary>
    private (PromoStatus? Status, PromotionDefinition? Promotion, Parsed? Parsed) Gate(
        NormalizedCode code, Dictionary<long, PromotionDefinition> promotions, PromotionContext context)
    {
        if (code.Row is null)
        {
            return (PromoStatus.Unknown, null, null);
        }
        if (!promotions.TryGetValue(code.Row.PromotionId, out var promotion))
        {
            return (PromoStatus.Misconfigured, null, null);
        }
        if (!code.Row.IsActive || !promotion.IsActive)
        {
            return (PromoStatus.Inactive, promotion, null);
        }
        if (WindowStatus(promotion, context) is { } windowStatus)
        {
            return (windowStatus, promotion, null);
        }
        if (promotion.RestaurantIds.Count > 0 && !promotion.RestaurantIds.Contains(context.RestaurantId))
        {
            return (PromoStatus.NotAvailableHere, promotion, null);
        }
        try
        {
            return (null, promotion, Parse(promotion));
        }
        catch (Exception e) when (e is PromotionDataException or JsonException)
        {
            return (PromoStatus.Misconfigured, promotion, null);
        }
    }

    /// <summary>
    /// The validity window in the restaurant's local time (UQ16): each boundary resolves to the earliest UTC
    /// instant its local clock reads it, then the comparison is half-open on UTC. Outside coverage: fail closed.
    /// </summary>
    private static PromoStatus? WindowStatus(PromotionDefinition promotion, PromotionContext context)
    {
        if (promotion.ValidFromLocal is null && promotion.ValidToLocal is null)
        {
            return null;
        }
        if (LocalTimeResolver.ToLocal(context.Zone, context.NowUtc) is null)
        {
            return PromoStatus.Misconfigured;
        }
        if (promotion.ValidFromLocal is { } fromLocal)
        {
            var from = LocalTimeResolver.Boundary(context.Zone, fromLocal);
            if (from is null)
            {
                return PromoStatus.Misconfigured;
            }
            if (context.NowUtc < from)
            {
                return PromoStatus.NotYetActive;
            }
        }
        if (promotion.ValidToLocal is { } toLocal)
        {
            var to = LocalTimeResolver.Boundary(context.Zone, toLocal);
            if (to is null)
            {
                return PromoStatus.Misconfigured;
            }
            if (context.NowUtc >= to)
            {
                return PromoStatus.Expired;
            }
        }
        return null;
    }

    private Parsed Parse(PromotionDefinition promotion)
    {
        var effect = registry.Effect(promotion.EffectType);
        var effectParams = ParseJson(promotion.EffectParams, effect.Parse);
        var conditions = promotion.Conditions
            .Select(c =>
            {
                var kind = registry.Condition(c.Type);
                return (kind, ParseJson(c.Params, kind.Parse));
            })
            .ToList();
        return new Parsed(effect, effectParams, conditions);
    }

    private static object ParseJson(string json, Func<JsonElement, object> parse)
    {
        using var document = JsonDocument.Parse(json);
        return parse(document.RootElement);
    }

    private static CodeOutcome Outcome(NormalizedCode code, PromoStatus status, PromoMessage? message, long? promotionId = null) =>
        new(code.Entered, code.Code, promotionId ?? code.Row?.PromotionId, status, Money.Zero, message, []);

    private static PromoMessage DroppedMessage(NormalizedCode code, PromoStatus status, int maxCodes) => status switch
    {
        PromoStatus.InvalidFormat => PromoMessage.Plain("Promo codes contain only letters, digits and dashes."),
        PromoStatus.Duplicate => PromoMessage.Plain($"{code.Code} is already entered."),
        PromoStatus.LimitExceeded => PromoMessage.Plain($"You can use up to {maxCodes} promo codes per order."),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static PromoMessage GateMessage(string code, PromoStatus status) => PromoMessage.Plain(status switch
    {
        PromoStatus.Unknown => $"{code} isn't a valid promo code.",
        PromoStatus.Inactive => $"{code} is no longer available.",
        PromoStatus.NotYetActive => $"{code} isn't active yet.",
        PromoStatus.Expired => $"{code} has expired.",
        PromoStatus.NotAvailableHere => $"{code} isn't valid at this restaurant.",
        PromoStatus.Misconfigured => $"{code} can't be applied right now.",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    });

    /// <summary>"A", "A or B", "A, B or C" (with "and" for the removal hint).</summary>
    private static string Join(IReadOnlyList<string> codes, string conjunction) => codes.Count switch
    {
        1 => codes[0],
        _ => string.Join(", ", codes.Take(codes.Count - 1)) + $" {conjunction} " + codes[^1],
    };
}
