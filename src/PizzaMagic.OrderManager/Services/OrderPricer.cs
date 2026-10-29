using Microsoft.EntityFrameworkCore;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.OrderManager.Data;

namespace PizzaMagic.OrderManager.Services;

/// <summary>The outcome of pricing a request: field errors, or the breakdown with the catalog it was priced on.</summary>
public sealed record PricingOutcome(
    IReadOnlyList<FieldError> Errors,
    PriceBreakdown? Breakdown,
    PricingCatalog? Catalog,
    string Channel,
    TipRequest? Tip);

/// <summary>
/// The single pricing entry point (plan section 2.3): quote, submit and update all call this method on the same rows,
/// so the price the customer sees is the price charged.
/// </summary>
public sealed class OrderPricer(PizzaMagicDbContext db, CatalogLoader loader, PricingEngine engine, TimeProvider clock)
{
    public const string DefaultChannel = "ONLINE";
    private const string PizzaKind = "Pizza";
    private const string AddOnKind = "AddOn";

    // Raw request sizes, far above any ordering policy, checked before the catalog loads, so a response is never many
    // times larger than its request (policy limits, e.g. 20 lines, are the Domain validator's). Toppings past
    // MaxToppingEntries are not scanned here; the per-pizza policy limit reports them.
    private const int MaxLineEntries = 100;
    private const int MaxToppingEntries = 100;
    private const int MaxPromoCodeEntries = 10;

    public async Task<PricingOutcome> PriceAsync(QuoteRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new List<FieldError>();
        var channel = (request.Channel ?? DefaultChannel).Trim().ToUpperInvariant();
        var channels = await db.Database.SqlQueryRaw<string>("SELECT code AS Value FROM order_channel WHERE is_active = 1").ToListAsync(cancellationToken);
        if (!channels.Contains(channel))
        {
            errors.Add(new FieldError("channel", "UNKNOWN_CHANNEL", $"'{request.Channel}' isn't an order channel."));
        }

        var restaurantValid = request.RestaurantId is not { } id
            || await db.Restaurants.AsNoTracking().AnyAsync(r => r.Id == id && r.IsActive, cancellationToken);
        if (!restaurantValid)
        {
            errors.Add(new FieldError("restaurantId", "UNKNOWN_RESTAURANT", $"Restaurant {request.RestaurantId} doesn't exist or isn't open."));
        }

        var lines = request.LineItems ?? [];
        var shapeErrors = ShapeErrors(lines, request.PromoCodes);
        errors.AddRange(shapeErrors);

        var tip = TipInputParser.Parse(request.Tip, out var tipError);
        if (!restaurantValid || shapeErrors.Count > 0)
        {
            return Failed(errors, tipError, channel);
        }

        var catalog = await loader.LoadAsync(request.RestaurantId, cancellationToken);
        var draft = new OrderDraft(
            lines.Select(l => new PizzaDraftLine(
                l.SizeCode ?? "",
                l.Quantity,
                (l.Toppings ?? []).Select(t => new DraftTopping(t?.Code ?? "", t?.Portion, t?.Multiplier)).ToList(),
                l.CrustCode,
                l.SauceCode)).ToList(),
            request.PromoCodes ?? [],
            tip);
        var result = engine.Price(draft, catalog, new PricingContext(clock.GetUtcNow()));
        errors.AddRange(result.Errors);
        if (errors.Count > 0 || tipError is not null)
        {
            return Failed(errors, tipError, channel);
        }

        return new PricingOutcome([], result.Breakdown, catalog, channel, NormalizeTip(request.Tip, tip));
    }

    private static PricingOutcome Failed(List<FieldError> errors, FieldError? tipError, string channel)
    {
        if (tipError is not null)
        {
            errors.Add(tipError);
        }
        return new PricingOutcome(errors, null, null, channel, null);
    }

    /// <summary>
    /// Structural problems the Domain can't see: list sizes, null entries, unsupported line kinds and fields that only
    /// unbuilt line kinds use. Reported with their paths, before pricing.
    /// </summary>
    private static List<FieldError> ShapeErrors(IReadOnlyList<LineItemRequest?> lines, IReadOnlyList<string?>? promoCodes)
    {
        var errors = new List<FieldError>();
        if (lines.Count > MaxLineEntries)
        {
            errors.Add(new FieldError("lineItems", "TOO_MANY_LINES", $"An order can have at most {MaxLineEntries} lines."));
            return errors;
        }
        if (promoCodes?.Count > MaxPromoCodeEntries)
        {
            errors.Add(new FieldError("promoCodes", "TOO_MANY_PROMO_CODES", $"Enter at most {MaxPromoCodeEntries} promo codes."));
        }
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i] is not { } line)
            {
                errors.Add(new FieldError($"lineItems[{i}]", "INVALID_LINE", "Each line must be an object."));
                continue;
            }
            if (KindError(line.Kind, i) is { } kindError)
            {
                errors.Add(kindError);
                continue;
            }
            if (line.ProductCode is not null)
            {
                errors.Add(new FieldError($"lineItems[{i}].productCode", "FIELD_NOT_ALLOWED",
                    "productCode is for add-on lines, which are designed but not built in this version."));
            }
            var toppings = line.Toppings ?? [];
            for (var j = 0; j < Math.Min(toppings.Count, MaxToppingEntries); j++)
            {
                if (toppings[j] is null)
                {
                    errors.Add(new FieldError($"lineItems[{i}].toppings[{j}]", "INVALID_TOPPING", "Each topping must be an object."));
                }
            }
        }
        return errors;
    }

    private static FieldError? KindError(string? kind, int index)
    {
        if (kind is null || kind.Equals(PizzaKind, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return kind.Equals(AddOnKind, StringComparison.OrdinalIgnoreCase)
            ? new FieldError($"lineItems[{index}].kind", "LINE_KIND_NOT_SUPPORTED", "Add-on lines are designed but not built in this version.")
            : new FieldError($"lineItems[{index}].kind", "INVALID_LINE_KIND", "Line kind must be Pizza.");
    }

    /// <summary>The tip as the server accepts it: canonical mode, the value as entered; null for no tip.</summary>
    private static TipRequest? NormalizeTip(TipRequest? entered, TipInput parsed) =>
        parsed.Mode == TipMode.None ? null : new TipRequest(parsed.Mode.ToString(), entered!.Value);
}
