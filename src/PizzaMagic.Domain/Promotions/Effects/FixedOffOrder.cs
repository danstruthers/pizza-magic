using System.Text.Json;
using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Promotions;

/// <summary><c>fixed_off_order</c> <c>{"amountCents":500}</c>: the amount, never more than what remains.</summary>
public sealed class FixedOffOrder : IEffectCalculator
{
    public string Type => "fixed_off_order";

    public object Parse(JsonElement parameters) => new Money(JsonParams.RequiredNonNegativeInt64(parameters, "amountCents"));

    public Money Apply(object parsed, Money remaining)
    {
        var amount = (Money)parsed;
        return amount.Cents < remaining.Cents ? amount : remaining;
    }
}
