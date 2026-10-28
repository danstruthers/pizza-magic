using System.Text.Json;

namespace PizzaMagic.Domain.Promotions;

/// <summary>
/// <c>min_subtotal</c> <c>{"amountCents":2000,"inclusive":false}</c>: the pre-discount merchandise subtotal
/// must be over the amount (at least the amount when inclusive).
/// </summary>
public sealed class MinSubtotal : IConditionEvaluator
{
    private sealed record Params(long AmountCents, bool Inclusive);

    public string Type => "min_subtotal";

    public object Parse(JsonElement parameters) => new Params(
        JsonParams.RequiredNonNegativeInt64(parameters, "amountCents"),
        JsonParams.OptionalBool(parameters, "inclusive", defaultValue: false));

    public ConditionResult Evaluate(object parsed, PromotionContext context, string code)
    {
        var p = (Params)parsed;
        var subtotal = context.GrossSubtotal.Cents;
        if (p.Inclusive ? subtotal >= p.AmountCents : subtotal > p.AmountCents)
        {
            return ConditionResult.Pass;
        }

        var comparison = p.Inclusive ? "of at least" : "over";
        return ConditionResult.Fail(new PromoMessage(
            $"{code} requires an order subtotal {comparison} {{required}} (yours is {{subtotal}}).",
            new Dictionary<string, long> { ["required"] = p.AmountCents, ["subtotal"] = subtotal }));
    }
}
