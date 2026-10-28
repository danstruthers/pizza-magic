using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Catalog;

/// <summary>
/// Prices one topping on one pizza from a stored rule (rounding point RP1). A new method is a new
/// registered class; tiers and toppings that use existing methods are pure data.
/// </summary>
public interface IToppingPricingMethod
{
    string Code { get; }

    Money UnitPrice(PricingRule rule, Money effectiveBase);
}
