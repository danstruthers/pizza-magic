using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Catalog;

/// <summary>A fixed amount per topping, whatever the pizza size.</summary>
public sealed class FixedAmountMethod : IToppingPricingMethod
{
    public string Code => "fixed_amount";

    public Money UnitPrice(PricingRule rule, Money effectiveBase) =>
        new(rule.AmountCents ?? throw new InvalidOperationException($"{Code} requires an amount."));
}
