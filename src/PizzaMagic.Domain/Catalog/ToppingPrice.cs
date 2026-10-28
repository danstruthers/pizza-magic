using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Catalog;

/// <summary>One topping's unit price on one pizza, with the rule that produced it (snapshotted on orders).</summary>
public sealed record ToppingPrice(Money UnitPrice, string PricingMethod, Rate? Rate, PriceSource Source);
