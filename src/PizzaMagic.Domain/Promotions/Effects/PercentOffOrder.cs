using System.Text.Json;
using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Promotions;

/// <summary>
/// <c>percent_off_order</c> <c>{"ratePpm":100000,"maxDiscountCents":null}</c>: round(remaining x rate)
/// (rounding point RP4), then the optional cap, never more than what remains.
/// </summary>
public sealed class PercentOffOrder : IEffectCalculator
{
    private sealed record Params(Rate Rate, Money? Cap);

    public string Type => "percent_off_order";

    public object Parse(JsonElement parameters)
    {
        var ppm = JsonParams.RequiredNonNegativeInt64(parameters, "ratePpm");
        if (ppm > Rate.MaxPpm)
        {
            throw new PromotionDataException($"'ratePpm' must be at most {Rate.MaxPpm}.");
        }
        var cap = JsonParams.OptionalNonNegativeInt64(parameters, "maxDiscountCents");
        return new Params(new Rate((int)ppm), cap is { } cents ? new Money(cents) : null);
    }

    public Money Apply(object parsed, Money remaining)
    {
        var p = (Params)parsed;
        var discount = remaining.ApplyRate(p.Rate);
        if (p.Cap is { } cap && cap.Cents < discount.Cents)
        {
            discount = cap;
        }
        return discount.Cents < remaining.Cents ? discount : remaining;
    }
}
