using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Pricing;

/// <summary>
/// One priced component of a single pizza (already rounded to whole cents). A topping also carries the rule that
/// priced it, which the order snapshot stores, and its multiplier: <see cref="UnitCharge"/> is then the topping's
/// rounded unit price x <see cref="Multiplier"/> (UQ29).
/// </summary>
public sealed record PricedComponent(
    ComponentKind Kind,
    EffectiveProduct Product,
    Money UnitCharge,
    PriceSource Source,
    long TaxClassId,
    ToppingPrice? ToppingPrice = null,
    int Multiplier = 1);

/// <summary>A priced pizza line: unit price = sum of components; line total = unit price x quantity.</summary>
public sealed record PricedPizzaLine(
    EffectiveProduct Size,
    EffectiveProduct Crust,
    EffectiveProduct Sauce,
    IReadOnlyList<PricedComponent> Components,
    int Quantity,
    Money UnitPrice,
    Money LineTotal);
