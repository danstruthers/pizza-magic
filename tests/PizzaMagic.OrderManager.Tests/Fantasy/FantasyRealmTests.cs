using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tax;
using PizzaMagic.FantasySeedGen;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.OrderManager.Tests.Persistence;

namespace PizzaMagic.OrderManager.Tests.Fantasy;

/// <summary>One fantasy realm database (0000 + 0001 + test/fantasy-realm.sql) shared by the read-only tests.</summary>
public sealed class FantasyRealmDatabase : IAsyncLifetime
{
    private readonly Dictionary<long, PricingCatalog> _catalogs = [];

    private TestDatabase? _database;

    public TestDatabase Database => _database ?? throw new InvalidOperationException("The fantasy realm database failed to initialize.");

    public async Task InitializeAsync() => _database = await TestDatabase.CreateSeededAsync(SeedProfile.Fantasy);

    public async Task<PricingCatalog> CatalogAsync(long restaurantId)
    {
        if (!_catalogs.TryGetValue(restaurantId, out var catalog))
        {
            await using var db = Database.NewContext();
            catalog = await new CatalogLoader(db).LoadAsync(restaurantId);
            _catalogs[restaurantId] = catalog;
        }
        return catalog;
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }
}

/// <summary>
/// M15F core part (plan section 7.5, UQ26): the fantasy realm loads, validates and prices correctly far outside the
/// spec's single restaurant. Golden numbers were computed independently by a Node script (BigInt, from the plan's
/// rules; the arithmetic is in docs/ai-log.md), not by this code.
/// </summary>
public class FantasyRealmTests(FantasyRealmDatabase realm) : IClassFixture<FantasyRealmDatabase>
{
    private const long Main = 1, Eldoria = 1001, Dwarrowdeep = 1002, Sunspire = 1003, Mistmarsh = 1004, Moonhollow = 1005, Frostholm = 1006;

    private static readonly long[] Restaurants = [Main, Eldoria, Dwarrowdeep, Sunspire, Mistmarsh, Moonhollow, Frostholm];

    private static readonly PricingEngine Engine = new(new PromotionEngine(PromotionRegistry.Default, new RejectLaterConflicts()));

    private static readonly string[] FantasyToppings =
    [
        "GOBLIN_MUSHROOMS", "ELVEN_HERBS", "KRAKEN_CALAMARI", "GRIFFIN_SAUSAGE", "DRAGONFIRE_PEPPERS", "PHOENIX_BASIL", "UNICORN_TRUFFLE",
    ];

    // ---- Delivery -------------------------------------------------------------------------------------------------

    [Fact]
    public void Committed_fantasy_realm_equals_generator_output()
    {
        var committed = File.ReadAllText(Path.Combine(TestDatabase.DataDirectory, "test", "fantasy-realm.sql")).ReplaceLineEndings("\n");

        committed.ShouldBe(FantasyRealmGenerator.Generate());
    }

    [Fact]
    public void Generator_is_deterministic()
    {
        FantasyRealmGenerator.Generate().ShouldBe(FantasyRealmGenerator.Generate());
    }

    [Fact]
    public async Task Fantasy_catalog_validates()
    {
        await using var db = realm.Database.NewContext();

        (await new CatalogValidator(db, PromotionRegistry.Default, PricingMethodRegistry.Default, realm.Database.Clock).ValidateAsync()).ShouldBeEmpty();
    }

    // The extended part of section 7.5 stays designed, not built: the validator still refuses its data on this
    // database (A70), so it cannot be added to the realm until M4 EXT / X2 + UQ20 remove the matching rule.
    [Theory]
    [InlineData("multi-level tax (kingdom -> city)",
        "INSERT INTO tax_jurisdiction (id, code, name, kind, parent_jurisdiction_id, default_rate_ppm) VALUES (1101, 'ELDORIA_CITY', 'Eldoria City', 'City', 1001, 12500); UPDATE restaurant SET tax_jurisdiction_id = 1101 WHERE id = 1001",
        "which has a parent: multi-level tax is designed, not built (A70).")]
    [InlineData("a POTION tax-class rate",
        "INSERT INTO tax_class (id, code, name) VALUES (1001, 'POTION', 'Potion'); INSERT INTO jurisdiction_tax_class_rate VALUES (1001, 1001, 0)",
        "per-class tax rates are designed, not built (A70).")]
    [InlineData("a themed crust with an upcharge",
        "INSERT INTO product (id, product_type_code, code, display_name, price_cents, tax_class_id) VALUES (1201, 'CRUST', 'DWARVEN_STONE_BAKED', 'Dwarven stone-baked', 150, 1)",
        "Product DWARVEN_STONE_BAKED is a non-default crust or sauce: upgrades are designed, not built (A70).")]
    public async Task Extended_part_is_still_refused(string feature, string sql, string error)
    {
        await using var database = await TestDatabase.CreateSeededAsync(SeedProfile.Fantasy);
        await database.ExecuteAsync(sql);
        await using var db = database.NewContext();

        var errors = await new CatalogValidator(db, PromotionRegistry.Default, PricingMethodRegistry.Default, database.Clock).ValidateAsync();

        errors.ShouldContain(e => e.EndsWith(error, StringComparison.Ordinal), feature);
    }

    // ---- Time zones -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Eldoria, "Realm/Eldoria", -75)]
    [InlineData(Dwarrowdeep, "Realm/Dwarrowdeep", 255)]
    [InlineData(Sunspire, "Realm/Sunspire", 600)]
    [InlineData(Mistmarsh, "Realm/Mistmarsh", -465)]
    [InlineData(Moonhollow, "Realm/Sunspire", 600)]
    [InlineData(Frostholm, "Realm/Frostholm", -135)]
    public async Task Realm_zone_is_one_fixed_offset_period_over_the_coverage(long restaurantId, string ianaId, int offsetMinutes)
    {
        var zone = (await realm.CatalogAsync(restaurantId)).Zone;

        zone.IanaId.ShouldBe(ianaId);
        var period = zone.Periods.ShouldHaveSingleItem();
        (period.ValidFromUtc, period.ValidToUtc, period.UtcOffsetMinutes, period.IsDst)
            .ShouldBe((new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2041, 1, 1, 0, 0, 0, DateTimeKind.Utc), offsetMinutes, false));
    }

    [Fact]
    public async Task Realm_offsets_cover_the_plan_cases()
    {
        await using var db = realm.Database.NewContext();
        var offsets = await db.TimeZoneOffsetPeriods.AsNoTracking()
            .Where(p => db.TimeZones.Any(z => z.Id == p.TimeZoneId && z.IanaId.StartsWith("Realm/")))
            .Select(p => p.UtcOffsetMinutes)
            .ToListAsync();

        offsets.Count.ShouldBeGreaterThanOrEqualTo(5);
        offsets.ShouldAllBe(m => m % 15 == 0 && m >= -720 && m <= 720);
        offsets.ShouldContain(m => m < 0);
        offsets.ShouldContain(m => m > 0);
        offsets.ShouldContain(m => Math.Abs(m) % 60 == 15 || Math.Abs(m) % 60 == 45);
        offsets.ShouldContain(m => TestDatabase.DefaultNow.ToOffset(TimeSpan.FromMinutes(m)).Date != TestDatabase.DefaultNow.UtcDateTime.Date);
    }

    [Fact]
    public async Task Fantasy_restaurants_use_five_zones_and_two_share_one()
    {
        var zones = new Dictionary<long, long>();
        foreach (var id in Restaurants.Where(r => r != Main))
        {
            zones[id] = (await realm.CatalogAsync(id)).Zone.Id;
        }

        zones.Values.Distinct().Count().ShouldBe(5);
        zones[Sunspire].ShouldBe(zones[Moonhollow]);
    }

    // ---- Restaurants, menus, tax ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(Main, "MAIN", "WA", 65_000, TaxRoundingMethod.HalfUp, "SMALL=500,MEDIUM=700,LARGE=1200", "20/20/12/3/50000/1000000")]
    [InlineData(Eldoria, "ELDORIA", "ELDORIA", 88_750, TaxRoundingMethod.HalfUp, "SMALL=500,MEDIUM=700,LARGE=1350,GIANT=1999", "10/12/8/2/20000/500000")]
    [InlineData(Dwarrowdeep, "DWARROWDEEP", "DWARROWDEEP", 0, TaxRoundingMethod.Truncate, "SMALL=500,MEDIUM=777,LARGE=1200,GIANT=2400", "25/50/15/3/100000/1000000")]
    [InlineData(Sunspire, "SUNSPIRE", "SUNSPIRE", 750_000, TaxRoundingMethod.Truncate, "SMALL=450,MEDIUM=700,LARGE=1200,GIANT=2222", "5/5/5/1/10000/300000")]
    [InlineData(Mistmarsh, "MISTMARSH", "MISTMARSH", 171_250, TaxRoundingMethod.Truncate, "MEDIUM=700,LARGE=1275,GIANT=1999", "8/10/6/2/15000/400000")]
    [InlineData(Moonhollow, "MOONHOLLOW", "MOONHOLLOW", 333_330, TaxRoundingMethod.HalfUp, "SMALL=500,MEDIUM=725,LARGE=1200", "12/20/10/3/30000/750000")]
    [InlineData(Frostholm, "FROSTHOLM", "FROSTHOLM", 50_000, TaxRoundingMethod.HalfUp, "SMALL=500,MEDIUM=700,LARGE=1200,GIANT=1875", "6/8/4/2/25000/250000")]
    public async Task Every_restaurant_resolves_menu_prices_policy_and_tax(
        long restaurantId, string code, string jurisdiction, int ratePpm, TaxRoundingMethod method, string sizes, string policy)
    {
        var catalog = await realm.CatalogAsync(restaurantId);

        catalog.Restaurant!.Code.ShouldBe(code);
        var level = catalog.Chain.ShouldHaveSingleItem();
        (level.Code, level.DefaultRatePpm, level.ParentId).ShouldBe((jurisdiction, ratePpm, (long?)null));
        TaxRoundingSettings.Resolve(catalog.Chain).Method.ShouldBe(method);
        string.Join(",", catalog.Menu.Available(ProductType.PizzaSize).Select(s => $"{s.Code}={s.Price!.Value.Cents}")).ShouldBe(sizes);
        var p = catalog.Policy;
        $"{p.MaxLines}/{p.MaxLineQuantity}/{p.MaxToppingsPerPizza}/{p.MaxPromoCodes}/{p.MaxTipAmountCents}/{p.MaxTipPercentPpm}".ShouldBe(policy);
        catalog.Menu.DefaultCrust!.Code.ShouldBe("HAND_TOSSED");
        catalog.Menu.DefaultSauce!.Code.ShouldBe("CLASSIC_TOMATO");
    }

    [Fact]
    public async Task Rates_span_zero_to_seventy_five_percent_with_both_rounding_methods()
    {
        var levels = new List<(int Rate, TaxRoundingMethod Method)>();
        foreach (var id in Restaurants)
        {
            var chain = (await realm.CatalogAsync(id)).Chain;
            levels.Add((chain.Single().DefaultRatePpm, TaxRoundingSettings.Resolve(chain).Method));
        }

        levels.Min(l => l.Rate).ShouldBe(0);
        levels.Max(l => l.Rate).ShouldBe(750_000);
        levels.ShouldContain(l => l.Rate % 10_000 != 0 && l.Rate % 10 == 0);   // three decimals of a percent, e.g. 8.875%
        levels.Select(l => l.Method).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Pizza_Magic_stays_the_default_with_the_spec_menu()
    {
        await using var db = realm.Database.NewContext();
        var catalog = await new CatalogLoader(db).LoadAsync(restaurantId: null);

        catalog.Restaurant.ShouldBe(new RestaurantInfo(Main, "MAIN", "Pizza Magic", true));
        catalog.Menu.Available(ProductType.Topping).Select(t => t.Code).ShouldNotContain(code => FantasyToppings.Contains(code));
        catalog.Menu.Available(ProductType.Topping).Count.ShouldBe(12);
        // P9: 2 Large pepperoni + bacon, ILOVEPIZZA, 15% tip.
        Price(catalog, "LARGE*2:PEPPERONI+BACON", "ILOVEPIZZA", "pct:150000").Total.Cents.ShouldBe(3148);
    }

    [Fact]
    public async Task Fantasy_toppings_join_every_tier_including_the_new_MYTHIC()
    {
        var menu = (await realm.CatalogAsync(Eldoria)).Menu;
        var tiers = menu.Tiers.ToDictionary(t => t.Id, t => t.Code);

        tiers.Values.ShouldBe(["BASIC", "PREMIUM", "MYTHIC"]);
        var byTier = menu.Available(ProductType.Topping).Where(t => FantasyToppings.Contains(t.Code))
            .GroupBy(t => tiers[t.ToppingTierId!.Value]).ToDictionary(g => g.Key, g => g.Select(t => t.Code).ToList());
        byTier["BASIC"].ShouldBe(["GOBLIN_MUSHROOMS", "ELVEN_HERBS"]);
        byTier["PREMIUM"].ShouldBe(["KRAKEN_CALAMARI", "GRIFFIN_SAUSAGE"]);
        byTier["MYTHIC"].ShouldBe(["DRAGONFIRE_PEPPERS", "PHOENIX_BASIL", "UNICORN_TRUFFLE"]);
    }

    [Theory]
    [InlineData(Eldoria, "PINEAPPLE")]
    [InlineData(Dwarrowdeep, "ELVEN_HERBS")]
    [InlineData(Sunspire, "KRAKEN_CALAMARI")]
    [InlineData(Mistmarsh, "SMALL")]
    [InlineData(Mistmarsh, "UNICORN_TRUFFLE")]
    [InlineData(Moonhollow, "GIANT")]
    [InlineData(Frostholm, "DRAGONFIRE_PEPPERS")]
    [InlineData(Frostholm, "BACON")]
    [InlineData(Main, "GIANT")]
    [InlineData(Main, "DRAGONFIRE_PEPPERS")]
    public async Task Unavailable_items_are_off_that_menu(long restaurantId, string code)
    {
        var menu = (await realm.CatalogAsync(restaurantId)).Menu;
        var type = code is "SMALL" or "GIANT" ? ProductType.PizzaSize : ProductType.Topping;

        menu.Lookup(code, type, out _).ShouldBe(ProductLookup.Unavailable);
    }

    // MYTHIC (25% of the effective base, half away from zero) on every size each restaurant sells.
    [Theory]
    [InlineData(Eldoria, "SMALL", 125)]
    [InlineData(Eldoria, "MEDIUM", 175)]
    [InlineData(Eldoria, "LARGE", 338)]
    [InlineData(Eldoria, "GIANT", 500)]
    [InlineData(Dwarrowdeep, "SMALL", 125)]
    [InlineData(Dwarrowdeep, "MEDIUM", 194)]
    [InlineData(Dwarrowdeep, "LARGE", 300)]
    [InlineData(Dwarrowdeep, "GIANT", 600)]
    [InlineData(Sunspire, "SMALL", 113)]
    [InlineData(Sunspire, "MEDIUM", 175)]
    [InlineData(Sunspire, "LARGE", 300)]
    [InlineData(Sunspire, "GIANT", 556)]
    [InlineData(Mistmarsh, "MEDIUM", 175)]
    [InlineData(Mistmarsh, "LARGE", 319)]
    [InlineData(Mistmarsh, "GIANT", 500)]
    [InlineData(Moonhollow, "SMALL", 125)]
    [InlineData(Moonhollow, "MEDIUM", 181)]
    [InlineData(Moonhollow, "LARGE", 300)]
    [InlineData(Frostholm, "SMALL", 125)]
    [InlineData(Frostholm, "MEDIUM", 175)]
    [InlineData(Frostholm, "LARGE", 300)]
    [InlineData(Frostholm, "GIANT", 469)]
    public async Task Mythic_tier_on_every_size(long restaurantId, string size, long unitCents)
    {
        var menu = (await realm.CatalogAsync(restaurantId)).Menu;
        var basePrice = menu.Available(ProductType.PizzaSize).Single(s => s.Code == size).Price!.Value;
        var mythicTier = menu.Tiers.Single(t => t.Code == "MYTHIC").Id;
        var mythic = menu.Available(ProductType.Topping).Where(t => t.ToppingTierId == mythicTier).ToList();

        mythic.Count.ShouldBeGreaterThanOrEqualTo(2);
        mythic.ShouldAllBe(t => menu.ToppingUnitPrice(t, basePrice).UnitPrice.Cents == unitCents);
    }

    [Fact]
    public async Task Every_restaurant_sells_every_available_size_with_a_MYTHIC_topping()
    {
        foreach (var id in Restaurants.Where(r => r != Main))
        {
            var menu = (await realm.CatalogAsync(id)).Menu;
            var mythicTier = menu.Tiers.Single(t => t.Code == "MYTHIC").Id;
            menu.Available(ProductType.Topping).ShouldContain(t => t.ToppingTierId == mythicTier, $"restaurant {id}");
        }
    }

    // ---- Golden vectors (independently computed; see docs/ai-log.md) ----------------------------------------------

    [Theory]
    [InlineData("F1", Eldoria, "LARGE*2:DRAGONFIRE_PEPPERS+KRAKEN_CALAMARI|GIANT*1:GOBLIN_MUSHROOMS+UNICORN_TRUFFLE", "ARCHMAGE", "pct:150000", 6333, 1900, 393, 724, 5550)]
    [InlineData("F2", Dwarrowdeep, "MEDIUM*3:GRIFFIN_SAUSAGE^2+PHOENIX_BASIL", "FORGE20,ILOVEPIZZA", "amt:500", 3423, 742, 0, 500, 3181)]
    [InlineData("F3", Sunspire, "SMALL*1:DRAGONFIRE_PEPPERS^2|GIANT*1:PHOENIX_BASIL+UNICORN_TRUFFLE", "MOONRISE", "pct:125000", 4010, 602, 2556, 746, 6710)]
    [InlineData("F4", Mistmarsh, "LARGE*2:ELVEN_HERBS+BACON+PHOENIX_BASIL", "ARCHMAGE", "", 3672, 1102, 440, 0, 3010)]
    [InlineData("F5", Moonhollow, "MEDIUM*4:UNICORN_TRUFFLE|SMALL*1:DRAGONFIRE_PEPPERS", "MOONRISE,ILOVEPIZZA", "pct:175000", 4249, 999, 1083, 758, 5091)]
    [InlineData("F6", Frostholm, "GIANT*1:KRAKEN_CALAMARI+PHOENIX_BASIL|MEDIUM*1:GOBLIN_MUSHROOMS", "ILOVEPIZZA,FROSTFEAST", "pct:200000", 3306, 781, 126, 530, 3181)]
    [InlineData("F7", Sunspire, "MEDIUM*1:GRIFFIN_SAUSAGE", "", "", 777, 0, 582, 0, 1359)]       // 75% TRUNCATE: 582.75 -> 582
    [InlineData("F8", Eldoria, "MEDIUM*1:PHOENIX_BASIL", "", "", 875, 0, 78, 0, 953)]             // 8.875% HALF_UP: 77.656 -> 78
    [InlineData("F9", Mistmarsh, "MEDIUM*1:DRAGONFIRE_PEPPERS", "", "", 875, 0, 149, 0, 1024)]    // 17.125% TRUNCATE: 149.844 -> 149
    public async Task Fantasy_golden_vectors(
        string id, long restaurantId, string cart, string codes, string tip, long subtotal, long discount, long tax, long tipCents, long total)
    {
        var breakdown = Price(await realm.CatalogAsync(restaurantId), cart, codes, tip);

        (breakdown.Subtotal.Cents, breakdown.Discount.Cents, breakdown.Tax.Tax.Cents, breakdown.Tip.Cents, breakdown.Total.Cents)
            .ShouldBe((subtotal, discount, tax, tipCents, total), id);
        breakdown.CanSubmit.ShouldBeTrue(id);
        breakdown.Promotions.Outcomes.ShouldAllBe(o => o.Status == PromoStatus.Applied, id);
    }

    // ---- Local-time promo windows at each fixed offset ------------------------------------------------------------

    // MOONRISE opens at local midnight 2026-10-28 in every zone: UTC = local - offset (LA is PDT, -420, that night).
    [Theory]
    [InlineData(Main, "2026-10-28T07:00:00Z")]
    [InlineData(Eldoria, "2026-10-28T01:15:00Z")]
    [InlineData(Dwarrowdeep, "2026-10-27T19:45:00Z")]
    [InlineData(Sunspire, "2026-10-27T14:00:00Z")]
    [InlineData(Mistmarsh, "2026-10-28T07:45:00Z")]
    [InlineData(Moonhollow, "2026-10-27T14:00:00Z")]
    [InlineData(Frostholm, "2026-10-28T02:15:00Z")]
    public async Task Window_opens_at_local_midnight_in_each_zone(long restaurantId, string opensUtc)
    {
        var catalog = await realm.CatalogAsync(restaurantId);
        var opens = Instant(opensUtc);

        Status(catalog, "MOONRISE", opens.AddMilliseconds(-1)).ShouldBe(PromoStatus.NotYetActive);
        Status(catalog, "MOONRISE", opens).ShouldBe(PromoStatus.Applied);
    }

    // ARCHMAGE ends at local 18:00 on 2026-10-27 (exclusive end).
    [Theory]
    [InlineData(Main, "2026-10-28T01:00:00Z")]
    [InlineData(Eldoria, "2026-10-27T19:15:00Z")]
    [InlineData(Dwarrowdeep, "2026-10-27T13:45:00Z")]
    [InlineData(Sunspire, "2026-10-27T08:00:00Z")]
    [InlineData(Mistmarsh, "2026-10-28T01:45:00Z")]
    [InlineData(Moonhollow, "2026-10-27T08:00:00Z")]
    [InlineData(Frostholm, "2026-10-27T20:15:00Z")]
    public async Task Window_ends_at_local_six_pm_in_each_zone(long restaurantId, string endsUtc)
    {
        var catalog = await realm.CatalogAsync(restaurantId);
        var ends = Instant(endsUtc);

        Status(catalog, "ARCHMAGE", ends.AddMilliseconds(-1)).ShouldBe(PromoStatus.Applied);
        Status(catalog, "ARCHMAGE", ends).ShouldBe(PromoStatus.Expired);
    }

    // At one instant (19:00Z) the same window is open where the local date is already Oct 28 and closed elsewhere;
    // the two restaurants sharing Realm/Sunspire always agree.
    [Fact]
    public async Task Same_instant_is_open_in_one_zone_and_closed_in_another()
    {
        var statuses = new Dictionary<long, (PromoStatus Moonrise, PromoStatus Archmage)>();
        foreach (var id in Restaurants)
        {
            var catalog = await realm.CatalogAsync(id);
            statuses[id] = (Status(catalog, "MOONRISE", TestDatabase.DefaultNow), Status(catalog, "ARCHMAGE", TestDatabase.DefaultNow));
        }

        statuses.ShouldBe(new Dictionary<long, (PromoStatus, PromoStatus)>
        {
            [Main] = (PromoStatus.NotYetActive, PromoStatus.Applied),
            [Eldoria] = (PromoStatus.NotYetActive, PromoStatus.Applied),
            [Dwarrowdeep] = (PromoStatus.NotYetActive, PromoStatus.Expired),
            [Sunspire] = (PromoStatus.Applied, PromoStatus.Expired),
            [Mistmarsh] = (PromoStatus.NotYetActive, PromoStatus.Applied),
            [Moonhollow] = (PromoStatus.Applied, PromoStatus.Expired),
            [Frostholm] = (PromoStatus.NotYetActive, PromoStatus.Applied),
        });
    }

    [Fact]
    public async Task Restaurants_sharing_a_zone_agree_at_every_quarter_hour()
    {
        var sunspire = await realm.CatalogAsync(Sunspire);
        var moonhollow = await realm.CatalogAsync(Moonhollow);
        for (var at = Instant("2026-10-27T00:00:00Z"); at <= Instant("2026-10-29T00:00:00Z"); at = at.AddMinutes(15))
        {
            foreach (var code in new[] { "MOONRISE", "ARCHMAGE" })
            {
                Status(moonhollow, code, at).ShouldBe(Status(sunspire, code, at), $"{code} at {at:O}");
            }
        }
    }

    // ---- Restaurant-limited, exclusive and exclusion-group codes --------------------------------------------------

    [Theory]
    [InlineData(Dwarrowdeep, "FORGE20", PromoStatus.Applied)]
    [InlineData(Dwarrowdeep, "DWARF20", PromoStatus.Applied)]
    [InlineData(Sunspire, "FORGE20", PromoStatus.NotAvailableHere)]
    [InlineData(Main, "FORGE20", PromoStatus.NotAvailableHere)]
    [InlineData(Eldoria, "HOARD", PromoStatus.Applied)]
    [InlineData(Sunspire, "HOARD", PromoStatus.Applied)]
    [InlineData(Mistmarsh, "HOARD", PromoStatus.NotAvailableHere)]
    [InlineData(Frostholm, "FROSTFEAST", PromoStatus.Applied)]
    [InlineData(Eldoria, "FROSTFEAST", PromoStatus.NotAvailableHere)]
    public async Task Restaurant_limited_codes(long restaurantId, string code, PromoStatus expected)
    {
        // A cart over every minimum (HOARD over $30, FROSTFEAST at least $25) with sizes every restaurant sells.
        var catalog = await realm.CatalogAsync(restaurantId);

        Price(catalog, "LARGE*3", code, "").Promotions.Outcomes.Single().Status.ShouldBe(expected);
    }

    [Theory]
    [InlineData(Eldoria, "ARCHMAGE,ILOVEPIZZA", "ILOVEPIZZA", "ARCHMAGE")]    // exclusive code held first
    [InlineData(Eldoria, "ILOVEPIZZA,ARCHMAGE", "ARCHMAGE", "ILOVEPIZZA")]    // exclusive code entered second
    [InlineData(Dwarrowdeep, "HOARD,FORGE20", "FORGE20", "HOARD")]            // exclusion group REALM_FEAST
    [InlineData(Dwarrowdeep, "FORGE20,HOARD", "HOARD", "FORGE20")]
    public async Task Exclusive_and_exclusion_group_codes_conflict(long restaurantId, string codes, string rejected, string heldBy)
    {
        var outcomes = Price(await realm.CatalogAsync(restaurantId), "LARGE*3", codes, "").Promotions.Outcomes;

        var outcome = outcomes.Single(o => o.Code == rejected);
        outcome.Status.ShouldBe(PromoStatus.Rejected);
        outcome.ConflictsWith.ShouldBe([heldBy]);
        outcomes.Single(o => o.Code == heldBy).Status.ShouldBe(PromoStatus.Applied);
    }

    [Fact]
    public async Task Sunspire_takes_one_code()
    {
        var outcomes = Price(await realm.CatalogAsync(Sunspire), "LARGE*3", "MOONRISE,HOARD", "").Promotions.Outcomes;

        outcomes.Select(o => o.Status).ShouldBe([PromoStatus.Applied, PromoStatus.LimitExceeded]);
    }

    // ---- Invariants over generated drafts across all restaurants --------------------------------------------------

    [Fact]
    public async Task Invariants_hold_over_generated_drafts_across_restaurants()
    {
        var random = new Random(7_515);
        var instants = new[] { TestDatabase.DefaultNow, TestDatabase.DefaultNow.AddHours(9), TestDatabase.DefaultNow.AddHours(-12) };
        var discounted = new HashSet<long>();
        var priced = 0;

        foreach (var restaurantId in Restaurants)
        {
            var catalog = await realm.CatalogAsync(restaurantId);
            var sizes = catalog.Menu.Available(ProductType.PizzaSize);
            var toppings = catalog.Menu.Available(ProductType.Topping);
            var codes = catalog.Promotions.Codes.Select(c => c.Code).ToList();
            var policy = catalog.Policy;
            var settings = TaxRoundingSettings.Resolve(catalog.Chain);
            var rate = catalog.Chain.Single().DefaultRatePpm;

            for (var n = 0; n < 80; n++)
            {
                var lines = Enumerable.Range(0, random.Next(1, Math.Min(4, policy.MaxLines) + 1))
                    .Select(_ => new PizzaDraftLine(
                        sizes[random.Next(sizes.Count)].Code,
                        random.Next(1, Math.Min(4, policy.MaxLineQuantity) + 1),
                        toppings.OrderBy(_ => random.Next()).Take(random.Next(0, Math.Min(4, policy.MaxToppingsPerPizza) + 1))
                            .Select(t => new DraftTopping(t.Code, null, random.Next(1, (t.MaxMultiplier ?? 1) + 1)))
                            .ToList()))
                    .ToList();
                var entered = codes.OrderBy(_ => random.Next()).Take(random.Next(0, policy.MaxPromoCodes + 1)).ToList();
                var tip = random.Next(3) switch
                {
                    0 => TipInput.None,
                    1 => new TipInput(TipMode.Percent, random.Next(0, policy.MaxTipPercentPpm / 1000 + 1) * 1000L),
                    _ => new TipInput(TipMode.Amount, random.Next(0, (int)Math.Min(policy.MaxTipAmountCents, 2_000) + 1)),
                };
                var now = instants[random.Next(instants.Length)];

                var result = Engine.Price(new OrderDraft(lines, entered, tip), catalog, new PricingContext(now));
                result.Errors.ShouldBeEmpty();
                var b = result.Breakdown!;
                var label = $"restaurant {restaurantId} draft {n}";

                (b.Subtotal - b.Discount + b.Tax.Tax + b.Tip).ShouldBe(b.Total, label);
                b.Taxable.ShouldBe(b.Subtotal - b.Discount, label);
                b.Discount.Cents.ShouldBeInRange(0, b.Subtotal.Cents, label);
                b.Allocations.Sum(a => a.Amount.Cents).ShouldBe(b.Discount.Cents, label);
                foreach (var applied in b.Promotions.AppliedInOrder)
                {
                    b.Allocations.Where(a => a.Code == applied.Code).Sum(a => a.Amount.Cents).ShouldBe(applied.Discount.Cents, label);
                }
                b.Allocations.ShouldAllBe(a => a.Amount.Cents > 0, label);
                b.Tax.Lines.Sum(l => l.Amount.Cents).ShouldBe(b.Tax.Tax.Cents, label);
                b.Tax.Lines.Sum(l => l.Taxable.Cents).ShouldBe(b.Taxable.Cents, label);
                var exact = checked(b.Taxable.Cents * rate);
                b.Tax.Tax.Cents.ShouldBe(
                    settings.Method == TaxRoundingMethod.HalfUp ? Rounding.RoundHalfAwayFromZero(exact, Money.PpmScale) : Rounding.Truncate(exact, Money.PpmScale),
                    label);
                if (b.Discount.Cents > 0)
                {
                    discounted.Add(restaurantId);
                }
                priced++;
            }
        }

        priced.ShouldBe(Restaurants.Length * 80);
        discounted.Count.ShouldBe(Restaurants.Length);   // the invariants were exercised with discounts everywhere
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    /// <summary>Cart: "SIZE*QTY:TOPPING+TOPPING^2|..."; codes: comma-separated; tip: "", "pct:ppm" or "amt:cents".</summary>
    private static PriceBreakdown Price(PricingCatalog catalog, string cart, string codes, string tip, DateTimeOffset? now = null)
    {
        var lines = cart.Split('|').Select(line =>
        {
            var (head, toppings) = line.Split(':') is var parts && parts.Length > 1 ? (parts[0], parts[1]) : (parts[0], "");
            var sizeAndQuantity = head.Split('*');
            return new PizzaDraftLine(
                sizeAndQuantity[0],
                int.Parse(sizeAndQuantity[1], CultureInfo.InvariantCulture),
                toppings.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Split('^') is var tp && tp.Length > 1
                    ? new DraftTopping(tp[0], null, int.Parse(tp[1], CultureInfo.InvariantCulture))
                    : new DraftTopping(t)).ToList());
        }).ToList();
        var tipInput = tip.Split(':') switch
        {
            ["pct", var ppm] => new TipInput(TipMode.Percent, long.Parse(ppm, CultureInfo.InvariantCulture)),
            ["amt", var cents] => new TipInput(TipMode.Amount, long.Parse(cents, CultureInfo.InvariantCulture)),
            _ => TipInput.None,
        };
        var result = Engine.Price(
            new OrderDraft(lines, codes.Split(',', StringSplitOptions.RemoveEmptyEntries), tipInput), catalog, new PricingContext(now ?? TestDatabase.DefaultNow));
        result.Errors.ShouldBeEmpty();
        return result.Breakdown!;
    }

    private static PromoStatus Status(PricingCatalog catalog, string code, DateTimeOffset now) =>
        Price(catalog, "MEDIUM*1", code, "", now).Promotions.Outcomes.Single().Status;

    private static DateTimeOffset Instant(string utc) => DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
