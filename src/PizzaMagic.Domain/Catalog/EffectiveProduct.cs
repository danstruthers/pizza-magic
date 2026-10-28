using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Catalog;

/// <summary>
/// A product as one restaurant sells it: overrides applied. <see cref="Price"/> is null for toppings,
/// which are priced per pizza by their tier rule.
/// </summary>
public sealed record EffectiveProduct(
    long Id,
    ProductType Type,
    string Code,
    string DisplayName,
    Money? Price,
    PriceSource PriceSource,
    long TaxClassId,
    long? ToppingTierId,
    int SortOrder);
