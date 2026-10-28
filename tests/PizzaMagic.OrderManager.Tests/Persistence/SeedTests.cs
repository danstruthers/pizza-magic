using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tax;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.TzSeedGen;

namespace PizzaMagic.OrderManager.Tests.Persistence;

public class SeedTests
{
    private static async Task<PricingCatalog> LoadDefaultAsync(TestDatabase database)
    {
        await using var db = database.NewContext();
        return await new CatalogLoader(db).LoadAsync(restaurantId: null);
    }

    [Fact]
    public async Task Seed_matches_spec()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        var catalog = await LoadDefaultAsync(database);
        var menu = catalog.Menu;

        menu.RestaurantId.ShouldBe(1);
        menu.Available(ProductType.PizzaSize).Select(p => (p.Code, p.Price!.Value.Cents)).ShouldBe([("SMALL", 500L), ("MEDIUM", 700L), ("LARGE", 1200L)]);
        menu.Available(ProductType.Topping).Select(p => p.Code).ShouldBe(
        [
            "PEPPERONI", "SAUSAGE", "EXTRA_CHEESE", "ONIONS", "BLACK_OLIVES", "GREEN_PEPPERS",
            "BACON", "CHICKEN", "GARLIC", "ARTICHOKE_HEARTS", "SUN_DRIED_TOMATOES", "PINEAPPLE",
        ]);
        menu.DefaultCrust!.Code.ShouldBe("HAND_TOSSED");
        menu.DefaultCrust.Price.ShouldBe(Money.Zero);
        menu.DefaultSauce!.Code.ShouldBe("CLASSIC_TOMATO");
        menu.DefaultSauce.Price.ShouldBe(Money.Zero);

        catalog.Promotions.Codes.Select(c => c.Code).ShouldBe(["ILOVEPIZZA", "SAVE5"], ignoreOrder: true);
        catalog.Promotions.Incompatibilities.ShouldBe([new IncompatiblePair(1, 2)]);
        catalog.Promotions.Promotions.Single(p => p.Id == 2).Conditions.Single().Type.ShouldBe("min_subtotal");

        catalog.Chain.ShouldBe([new JurisdictionNode(1, "WA", "Washington State", null, 65_000, TaxRoundingMethod.HalfUp, TaxRoundingScope.Combined)]);
        catalog.Zone.IanaId.ShouldBe("America/Los_Angeles");
        catalog.Zone.Periods.Count.ShouldBe(43);
        catalog.Policy.ShouldBe(new OrderingPolicy(20, 20, 12, 3, 50_000, 1_000_000));

        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM product WHERE product_type_code = 'TOPPING' AND max_multiplier = 3 AND tax_class_id = 1")).ShouldBe(12);
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM portion_type WHERE (code, price_factor_ppm) IN (VALUES ('WHOLE', 1000000), ('HALF', 600000), ('QUARTER', 400000))")).ShouldBe(3);
        (await database.ScalarAsync("SELECT SUM(coverage_mask) AS Value FROM portion")).ShouldBe(15 + 6 + 9 + 1 + 2 + 4 + 8);
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM order_channel WHERE code IN ('ONLINE', 'IN_STORE', 'PHONE')")).ShouldBe(3);
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM order_status")).ShouldBe(5);
    }

    // Whole-coverage grid (half and quarter are X1, designed, not built).
    [Fact]
    public async Task Seed_topping_grid()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        var menu = (await LoadDefaultAsync(database)).Menu;

        var grid = menu.Available(ProductType.PizzaSize).Select(size => (
            size.Code,
            menu.ToppingUnitPrice(menu.Available(ProductType.Topping).First(), size.Price!.Value).UnitPrice.Cents,
            menu.ToppingUnitPrice(menu.Available(ProductType.Topping).Last(), size.Price!.Value).UnitPrice.Cents));

        grid.ShouldBe([("SMALL", 40L, 55L), ("MEDIUM", 56L, 77L), ("LARGE", 96L, 132L)]);
    }

    // The golden vectors again, priced with the catalog loaded from the database (catches seed typos).
    [Theory]
    [InlineData("P1", 1, 0, 0L, 533L, true)]
    [InlineData("P6", 2, 1, 180_000L, 5869L, true)]
    [InlineData("P9", 3, 1, 150_000L, 3148L, true)]
    [InlineData("P13", 4, 2, 0L, 2130L, false)]
    [InlineData("P24", 3, 3, 150_000L, 3148L, true)]
    public async Task Seed_golden_vectors_via_DB_catalog(string id, int cart, int codes, long tipPpm, long total, bool canSubmit)
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        var catalog = await LoadDefaultAsync(database);
        PizzaDraftLine Pizza(string size, int qty, params string[] toppings) => new(size, qty, toppings.Select(t => new DraftTopping(t)).ToList());
        IReadOnlyList<PizzaDraftLine> lines = cart switch
        {
            1 => [Pizza("SMALL", 1)],
            2 => [Pizza("LARGE", 2, "PEPPERONI", "BACON"), Pizza("MEDIUM", 1, "EXTRA_CHEESE", "PINEAPPLE"), Pizza("SMALL", 3)],
            3 => [Pizza("LARGE", 2, "PEPPERONI", "BACON")],
            _ => [Pizza("SMALL", 4)],
        };
        string[] promoCodes = codes switch { 1 => ["ILOVEPIZZA"], 2 => ["SAVE5"], 3 => ["ILOVEPIZZA", "SAVE5"], _ => [] };
        var tip = tipPpm > 0 ? new TipInput(TipMode.Percent, tipPpm) : TipInput.None;
        var engine = new PricingEngine(new PromotionEngine(PromotionRegistry.Default, new RejectLaterConflicts()));

        var breakdown = engine.Price(new OrderDraft(lines, promoCodes, tip), catalog, new PricingContext(TestDatabase.DefaultNow)).Breakdown!;

        (breakdown.Total.Cents, breakdown.CanSubmit).ShouldBe((total, canSubmit), id);
    }

    // The committed seed must be exactly what the generator produces from tzdb (regenerate after a NodaTime bump).
    [Fact]
    public void Time_zone_seed_matches_tzdb()
    {
        var committed = File.ReadAllText(Path.Combine(TestDatabase.DataDirectory, "seed", "0000-time-zones.sql")).ReplaceLineEndings("\n");

        committed.ShouldBe(TimeZoneSeedGenerator.Generate());
    }

    [Fact]
    public async Task Time_zone_seed_has_217_periods_with_known_2026_switches()
    {
        await using var database = await TestDatabase.CreateSeededAsync();

        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM time_zone_offset_period")).ShouldBe(217);
        (await database.ScalarAsync("""
            SELECT COUNT(*) AS Value FROM time_zone_offset_period
            WHERE time_zone_id = 1 AND valid_from_utc = '2026-03-08T10:00:00.000Z' AND valid_to_utc = '2026-11-01T09:00:00.000Z'
              AND utc_offset_minutes = -420 AND abbreviation = 'PDT' AND is_dst = 1
            """)).ShouldBe(1);
    }
}
