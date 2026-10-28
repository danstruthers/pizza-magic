namespace PizzaMagic.Domain.Catalog;

/// <summary>Raw global catalog rows, loaded from the database. Nothing here is restaurant-specific.</summary>
public sealed record CatalogSnapshot(
    IReadOnlyList<ProductRow> Products,
    IReadOnlyList<ToppingTierRow> Tiers,
    IReadOnlyList<ProductCategoryRow> Categories,
    IReadOnlyList<TaxClassRow> TaxClasses);

public sealed record ProductRow(
    long Id,
    ProductType Type,
    string Code,
    string DisplayName,
    long? PriceCents,
    long? ToppingTierId,
    long? CategoryId,
    long TaxClassId,
    bool IsDefault,
    int SortOrder,
    bool IsActive = true,
    int? MaxMultiplier = null);   // toppings: how many times one may go on a pizza (product.max_multiplier)

public sealed record ToppingTierRow(long Id, string Code, string DisplayName, PricingRule Rule, int SortOrder, bool IsActive = true);

public sealed record ProductCategoryRow(long Id, string Code, bool IsActive = true);

public sealed record TaxClassRow(long Id, string Code, bool IsActive = true);
