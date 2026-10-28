using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Tests.Catalog;

public class EffectiveMenuTests
{
    private static EffectiveMenu Build(CatalogSnapshot snapshot, RestaurantOverrides overrides) =>
        EffectiveMenuBuilder.Build(snapshot, overrides, PricingMethodRegistry.Default);

    private static RestaurantOverrides R2(CatalogSnapshot snapshot, params (string Code, bool? IsAvailable, long? PriceCents)[] rows) =>
        new(SeedCatalog.FixtureRestaurantId, RestaurantIsActive: true,
            rows.Select(r => new ProductOverrideRow(snapshot.IdOf(r.Code), r.IsAvailable, r.PriceCents)).ToList());

    private static EffectiveProduct Get(EffectiveMenu menu, string code, ProductType type)
    {
        menu.TryGetAvailable(code, type, out var product).ShouldBeTrue($"{code} should be available");
        return product!;
    }

    private static Money ToppingPrice(EffectiveMenu menu, string topping, string size) =>
        menu.ToppingUnitPrice(Get(menu, topping, ProductType.Topping), Get(menu, size, ProductType.PizzaSize).Price!.Value).UnitPrice;

    [Theory]
    [InlineData("SMALL", 40)]
    [InlineData("MEDIUM", 56)]
    [InlineData("LARGE", 96)]
    public void Basic_is_8pct_of_effective_base(string size, long expected)
    {
        ToppingPrice(SeedCatalog.DefaultMenu(), "PEPPERONI", size).ShouldBe(new Money(expected));
    }

    [Theory]
    [InlineData("SMALL", 55)]
    [InlineData("MEDIUM", 77)]
    [InlineData("LARGE", 132)]
    public void Premium_is_11pct(string size, long expected)
    {
        ToppingPrice(SeedCatalog.DefaultMenu(), "BACON", size).ShouldBe(new Money(expected));
    }

    [Fact]
    public void Topping_price_reports_method_rate_and_global_source()
    {
        var menu = SeedCatalog.DefaultMenu();
        var price = menu.ToppingUnitPrice(Get(menu, "BACON", ProductType.Topping), new Money(1200));

        price.ShouldBe(new ToppingPrice(new Money(132), "percent_of_base", new Rate(110_000), PriceSource.Global));
    }

    [Fact]
    public void New_size_from_data_prices_without_code()
    {
        var snapshot = SeedCatalog.Snapshot().WithProduct(SeedCatalog.Size(4, "XL", "Extra large", 1500, 4));
        var menu = Build(snapshot, RestaurantOverrides.None(SeedCatalog.DefaultRestaurantId));

        ToppingPrice(menu, "PEPPERONI", "XL").ShouldBe(new Money(120));
        ToppingPrice(menu, "BACON", "XL").ShouldBe(new Money(165));
    }

    [Fact]
    public void Fixed_amount_tier_ignores_the_base()
    {
        var snapshot = SeedCatalog.Snapshot()
            .WithTierChanged("PREMIUM", t => t with { Rule = new PricingRule("fixed_amount", null, 100) });
        var menu = Build(snapshot, RestaurantOverrides.None(SeedCatalog.DefaultRestaurantId));

        ToppingPrice(menu, "BACON", "SMALL").ShouldBe(new Money(100));
        ToppingPrice(menu, "BACON", "LARGE").ShouldBe(new Money(100));
        menu.ToppingUnitPrice(Get(menu, "BACON", ProductType.Topping), new Money(500)).Rate.ShouldBeNull();
    }

    [Fact]
    public void Unknown_pricing_method_fails_when_the_menu_is_built()
    {
        var snapshot = SeedCatalog.Snapshot()
            .WithTierChanged("BASIC", t => t with { Rule = new PricingRule("per_slice", 80_000, null) });

        Should.Throw<InvalidOperationException>(() => Build(snapshot, RestaurantOverrides.None(SeedCatalog.DefaultRestaurantId)))
            .Message.ShouldContain("per_slice");
    }

    [Fact]
    public void Registry_rejects_duplicate_method_codes()
    {
        Should.Throw<ArgumentException>(() => new PricingMethodRegistry([new PercentOfBaseMethod(), new PercentOfBaseMethod()]));
    }

    // N7 (defaults part): omitted crust and sauce resolve to the seed defaults.
    [Fact]
    public void Default_crust_and_sauce_are_the_seed_defaults()
    {
        var menu = SeedCatalog.DefaultMenu();

        menu.DefaultCrust!.Code.ShouldBe("HAND_TOSSED");
        menu.DefaultSauce!.Code.ShouldBe("CLASSIC_TOMATO");
        menu.DefaultCrust.Price.ShouldBe(Money.Zero);
    }

    [Fact]
    public void Global_default_wins_over_a_lower_sort_order()
    {
        // THIN sorts before HAND_TOSSED, but HAND_TOSSED carries is_default, so it stays the default.
        var snapshot = SeedCatalog.Snapshot()
            .WithProduct(new ProductRow(202, ProductType.Crust, "THIN", "Thin", 0, null, null, SeedCatalog.PreparedFood, false, SortOrder: 0));
        var menu = Build(snapshot, RestaurantOverrides.None(SeedCatalog.DefaultRestaurantId));

        menu.DefaultCrust!.Code.ShouldBe("HAND_TOSSED");
    }

    [Fact]
    public void Default_crust_falls_back_to_the_lowest_sort_order_available_here()
    {
        var snapshot = SeedCatalog.Snapshot()
            .WithProduct(new ProductRow(203, ProductType.Crust, "THICK", "Thick", 0, null, null, SeedCatalog.PreparedFood, false, SortOrder: 3))
            .WithProduct(new ProductRow(202, ProductType.Crust, "THIN", "Thin", 0, null, null, SeedCatalog.PreparedFood, false, SortOrder: 2));
        var menu = Build(snapshot, R2(snapshot, ("HAND_TOSSED", false, null)));

        menu.DefaultCrust!.Code.ShouldBe("THIN");
    }

    [Fact]
    public void Default_crust_is_null_when_no_crust_is_available()
    {
        var snapshot = SeedCatalog.Snapshot();
        var menu = Build(snapshot, R2(snapshot, ("HAND_TOSSED", false, null)));

        menu.DefaultCrust.ShouldBeNull();
    }

    // O1: a restaurant with no override rows sees exactly the global menu.
    [Fact]
    public void Restaurant_without_overrides_matches_the_default_menu()
    {
        var snapshot = SeedCatalog.Snapshot();
        var defaultMenu = SeedCatalog.DefaultMenu();
        var r2 = Build(snapshot, R2(snapshot));

        foreach (var size in new[] { "SMALL", "MEDIUM", "LARGE" })
        {
            Get(r2, size, ProductType.PizzaSize).Price.ShouldBe(Get(defaultMenu, size, ProductType.PizzaSize).Price);
            foreach (var topping in snapshot.Products.Where(p => p.Type == ProductType.Topping))
            {
                ToppingPrice(r2, topping.Code, size).ShouldBe(ToppingPrice(defaultMenu, topping.Code, size));
            }
        }
    }

    // O2: a size price override changes the size and every percent-of-base topping on it.
    [Fact]
    public void Size_override_changes_base_and_percent_toppings()
    {
        var snapshot = SeedCatalog.Snapshot();
        var menu = Build(snapshot, R2(snapshot, ("LARGE", null, 1300)));

        var large = Get(menu, "LARGE", ProductType.PizzaSize);
        large.Price.ShouldBe(new Money(1300));
        large.PriceSource.ShouldBe(PriceSource.Restaurant);
        ToppingPrice(menu, "BACON", "LARGE").ShouldBe(new Money(143));
        ToppingPrice(menu, "PEPPERONI", "LARGE").ShouldBe(new Money(104));
        Get(menu, "MEDIUM", ProductType.PizzaSize).PriceSource.ShouldBe(PriceSource.Global);
    }

    // O8: products can be marked unavailable at one restaurant only.
    [Fact]
    public void Restaurant_can_mark_products_unavailable()
    {
        var snapshot = SeedCatalog.Snapshot();
        var r2 = Build(snapshot, R2(snapshot, ("ARTICHOKE_HEARTS", false, null), ("MEDIUM", false, null)));
        var defaultMenu = SeedCatalog.DefaultMenu();

        r2.TryGetAvailable("ARTICHOKE_HEARTS", ProductType.Topping, out _).ShouldBeFalse();
        r2.TryGetAvailable("MEDIUM", ProductType.PizzaSize, out _).ShouldBeFalse();
        defaultMenu.TryGetAvailable("ARTICHOKE_HEARTS", ProductType.Topping, out _).ShouldBeTrue();
        defaultMenu.TryGetAvailable("MEDIUM", ProductType.PizzaSize, out _).ShouldBeTrue();
    }

    // O9: global retirement wins over a restaurant's is_available = 1.
    [Fact]
    public void Globally_inactive_product_stays_unavailable()
    {
        var snapshot = SeedCatalog.Snapshot().WithProductChanged("GARLIC", p => p with { IsActive = false });
        var menu = Build(snapshot, R2(snapshot, ("GARLIC", true, null)));

        menu.TryGetAvailable("GARLIC", ProductType.Topping, out _).ShouldBeFalse();
    }

    // O10: an inactive tier makes all its toppings unavailable everywhere.
    [Fact]
    public void Inactive_tier_makes_its_toppings_unavailable()
    {
        var snapshot = SeedCatalog.Snapshot().WithTierChanged("PREMIUM", t => t with { IsActive = false });
        var menu = Build(snapshot, RestaurantOverrides.None(SeedCatalog.DefaultRestaurantId));

        menu.TryGetAvailable("BACON", ProductType.Topping, out _).ShouldBeFalse();
        menu.TryGetAvailable("PINEAPPLE", ProductType.Topping, out _).ShouldBeFalse();
        menu.TryGetAvailable("PEPPERONI", ProductType.Topping, out _).ShouldBeTrue();
    }

    [Fact]
    public void Inactive_tax_class_or_category_makes_products_unavailable()
    {
        var snapshot = SeedCatalog.Snapshot() with
        {
            TaxClasses = [new TaxClassRow(1, "PREPARED_FOOD", IsActive: false)],
        };

        SeedCatalog.Snapshot().Products.ShouldAllBe(p => p.TaxClassId == SeedCatalog.PreparedFood);
        Build(snapshot, RestaurantOverrides.None(1)).TryGetAvailable("LARGE", ProductType.PizzaSize, out _).ShouldBeFalse();

        var withCategory = SeedCatalog.Snapshot()
            .WithProductChanged("PINEAPPLE", p => p with { CategoryId = 9 }) with
        {
            Categories = [new ProductCategoryRow(9, "TROPICAL", IsActive: false)],
        };
        Build(withCategory, RestaurantOverrides.None(1)).TryGetAvailable("PINEAPPLE", ProductType.Topping, out _).ShouldBeFalse();
    }

    [Fact]
    public void Inactive_restaurant_has_nothing_available()
    {
        var menu = Build(SeedCatalog.Snapshot(), new RestaurantOverrides(SeedCatalog.FixtureRestaurantId, RestaurantIsActive: false, []));

        menu.TryGetAvailable("LARGE", ProductType.PizzaSize, out _).ShouldBeFalse();
        menu.DefaultCrust.ShouldBeNull();
    }

    [Fact]
    public void Lookup_is_by_type_as_well_as_code()
    {
        var menu = SeedCatalog.DefaultMenu();

        menu.TryGetAvailable("LARGE", ProductType.Topping, out _).ShouldBeFalse();
        menu.TryGetAvailable("NO_SUCH_THING", ProductType.PizzaSize, out _).ShouldBeFalse();
    }

    [Fact]
    public void Available_lists_products_of_a_type_in_sort_order()
    {
        SeedCatalog.DefaultMenu().Available(ProductType.PizzaSize).Select(p => p.Code)
            .ShouldBe(["SMALL", "MEDIUM", "LARGE"]);
    }
}
