using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Pricing;

/// <summary>
/// One priced component of a single pizza (already rounded to whole cents). A topping also carries the rule that
/// priced it, which the order snapshot stores.
/// </summary>
public sealed record PricedComponent(
    ComponentKind Kind,
    EffectiveProduct Product,
    Money UnitCharge,
    PriceSource Source,
    long TaxClassId,
    ToppingPrice? ToppingPrice = null);

/// <summary>A priced pizza line: unit price = sum of components; line total = unit price x quantity.</summary>
public sealed record PricedPizzaLine(
    EffectiveProduct Size,
    EffectiveProduct Crust,
    EffectiveProduct Sauce,
    IReadOnlyList<PricedComponent> Components,
    int Quantity,
    Money UnitPrice,
    Money LineTotal);
