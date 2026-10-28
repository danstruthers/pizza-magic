using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Catalog;

/// <summary>A percentage of the pizza's effective base price, rounded half away from zero.</summary>
public sealed class PercentOfBaseMethod : IToppingPricingMethod
{
    public string Code => "percent_of_base";

    public Money UnitPrice(PricingRule rule, Money effectiveBase) =>
        effectiveBase.ApplyRate(new Rate(rule.RatePpm ?? throw new InvalidOperationException($"{Code} requires a rate.")));
}
