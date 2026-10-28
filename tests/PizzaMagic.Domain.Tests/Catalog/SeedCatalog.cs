using PizzaMagic.Domain.Catalog;

namespace PizzaMagic.Domain.Tests.Catalog;

/// <summary>
/// Mirrors the planned seed rows in data/seed/0001-core.sql (plan section 4.5), so Domain tests
/// can run without a database. Tests derive variants with <c>with</c> expressions.
/// </summary>
public static class SeedCatalog
{
    public const long DefaultRestaurantId = 1;
    public const long FixtureRestaurantId = 2;
    public const long PreparedFood = 1;

    public static readonly PricingRule BasicRule = new("percent_of_base", RatePpm: 80_000, AmountCents: null);
    public static readonly PricingRule PremiumRule = new("percent_of_base", RatePpm: 110_000, AmountCents: null);

    public static CatalogSnapshot Snapshot() => new(
        Products:
        [
            Size(1, "SMALL", "Small", 500, 1),
            Size(2, "MEDIUM", "Medium", 700, 2),
            Size(3, "LARGE", "Large", 1200, 3),
            Topping(101, "PEPPERONI", "Pepperoni", tier: 1, sortOrder: 1),
            Topping(102, "SAUSAGE", "Sausage", tier: 1, sortOrder: 2),
            Topping(103, "EXTRA_CHEESE", "Extra cheese", tier: 1, sortOrder: 3),
            Topping(104, "ONIONS", "Onions", tier: 1, sortOrder: 4),
            Topping(105, "BLACK_OLIVES", "Black olives", tier: 1, sortOrder: 5),
            Topping(106, "GREEN_PEPPERS", "Green peppers", tier: 1, sortOrder: 6),
            Topping(107, "BACON", "Bacon", tier: 2, sortOrder: 7),
            Topping(108, "CHICKEN", "Chicken", tier: 2, sortOrder: 8),
            Topping(109, "GARLIC", "Garlic", tier: 2, sortOrder: 9),
            Topping(110, "ARTICHOKE_HEARTS", "Artichoke hearts", tier: 2, sortOrder: 10),
            Topping(111, "SUN_DRIED_TOMATOES", "Sun-dried tomatoes", tier: 2, sortOrder: 11),
            Topping(112, "PINEAPPLE", "Pineapple", tier: 2, sortOrder: 12),
            new ProductRow(201, ProductType.Crust, "HAND_TOSSED", "Hand tossed", 0, null, null, PreparedFood, IsDefault: true, SortOrder: 1),
            new ProductRow(301, ProductType.Sauce, "CLASSIC_TOMATO", "Classic tomato", 0, null, null, PreparedFood, IsDefault: true, SortOrder: 1),
        ],
        Tiers:
        [
            new ToppingTierRow(1, "BASIC", "Basic", BasicRule, SortOrder: 1),
            new ToppingTierRow(2, "PREMIUM", "Premium", PremiumRule, SortOrder: 2),
        ],
        Categories: [],
        TaxClasses:
        [
            new TaxClassRow(1, "PREPARED_FOOD"),
            new TaxClassRow(2, "BEVERAGE"),
            new TaxClassRow(3, "GENERAL"),
        ]);

    public static EffectiveMenu DefaultMenu() =>
        EffectiveMenuBuilder.Build(Snapshot(), RestaurantOverrides.None(DefaultRestaurantId), PricingMethodRegistry.Default);

    public static ProductRow Size(long id, string code, string name, long priceCents, int sortOrder) =>
        new(id, ProductType.PizzaSize, code, name, priceCents, null, null, PreparedFood, IsDefault: false, SortOrder: sortOrder);

    public static ProductRow Topping(long id, string code, string name, long tier, int sortOrder) =>
        new(id, ProductType.Topping, code, name, null, tier, null, PreparedFood, IsDefault: false, SortOrder: sortOrder);

    public static CatalogSnapshot WithProduct(this CatalogSnapshot snapshot, ProductRow product) =>
        snapshot with { Products = [.. snapshot.Products, product] };

    public static CatalogSnapshot WithProductChanged(this CatalogSnapshot snapshot, string code, Func<ProductRow, ProductRow> change) =>
        snapshot with { Products = snapshot.Products.Select(p => p.Code == code ? change(p) : p).ToList() };

    public static CatalogSnapshot WithTierChanged(this CatalogSnapshot snapshot, string code, Func<ToppingTierRow, ToppingTierRow> change) =>
        snapshot with { Tiers = snapshot.Tiers.Select(t => t.Code == code ? change(t) : t).ToList() };

    public static long IdOf(this CatalogSnapshot snapshot, string code) => snapshot.Products.Single(p => p.Code == code).Id;
}
