using System.Text;
using static System.FormattableString;

namespace PizzaMagic.FantasySeedGen;

/// <summary>
/// Generates data/test/fantasy-realm.sql (plan section 7.5, UQ26, core part): invented time zones, restaurants,
/// single-level tax jurisdictions from 0% to 75%, a MYTHIC topping tier and realm promotions. The output is
/// deterministic: the only "random" values (the zone offsets) come from SplitMix64 with a fixed seed, and a test
/// requires the committed file to equal this output.
/// <para>
/// Only data the built code reads is generated. The extended part of section 7.5 (multi-level tax, per-class rates,
/// rounding scope, themed crusts and sauces with upcharges) is designed, not built: CatalogValidator refuses that data
/// (A70). Add each piece here once its code is built and the matching validator check is removed.
/// </para>
/// </summary>
public static class FantasyRealmGenerator
{
    public const ulong Seed = 20261028;
    public const string CoverageStart = "2020-01-01T00:00:00.000Z";
    public const string CoverageEnd = "2041-01-01T00:00:00.000Z";

    private const long Basic = 1, Premium = 2, Mythic = 1001;
    private const long PreparedFood = 1;
    private const long Main = 1;

    public sealed record Zone(long Id, string IanaId, string Name, string Abbreviation, int OffsetMinutes);

    public sealed record Jurisdiction(long Id, string Code, string Name, string Kind, int RatePpm, string RoundingMethod);

    public sealed record Policy(int MaxLines, int MaxLineQuantity, int MaxToppings, int MaxPromoCodes, long MaxTipCents, int MaxTipPpm, int[] TipPresetsPpm);

    public sealed record Restaurant(long Id, string Code, string Name, long ZoneId, long JurisdictionId, Policy Policy);

    public sealed record Topping(long Id, string Code, string Name, long TierId, int MaxMultiplier, int SortOrder);

    /// <summary>A sparse restaurant_product_override row: availability and/or a size price.</summary>
    public sealed record Override(long RestaurantId, long ProductId, bool? IsAvailable, long? PriceCents);

    public sealed record Promotion(
        long Id, string Name, string Description, string EffectType, string EffectParams, int Priority, bool IsExclusive,
        string? ExclusionGroup, string? ValidFromLocal, string? ValidToLocal, string[] Codes, long[] RestaurantIds, (string Type, string Params)[] Conditions);

    /// <summary>The realm's zones; offsets drawn per role so the plan's cases are always present (section 7.5).</summary>
    public static IReadOnlyList<Zone> Zones { get; } = DrawZones();

    public static IReadOnlyList<Jurisdiction> Jurisdictions { get; } =
    [
        new(1001, "ELDORIA", "Kingdom of Eldoria", "Kingdom", 88_750, "HALF_UP"),          // 8.875%: three decimals
        new(1002, "DWARROWDEEP", "Free Holds of Dwarrowdeep", "Hold", 0, "TRUNCATE"),      // the exempt realm, exactly 0%
        new(1003, "SUNSPIRE", "Sunspire Dominion", "Dominion", 750_000, "TRUNCATE"),       // exactly 75%
        new(1004, "MISTMARSH", "Mistmarsh Fens", "Fen", 171_250, "TRUNCATE"),              // 17.125%
        new(1005, "MOONHOLLOW", "Moonhollow Vale", "Vale", 333_330, "HALF_UP"),            // 33.333%
        new(1006, "FROSTHOLM", "Frostholm Jarldom", "Jarldom", 50_000, "HALF_UP"),         // 5%
    ];

    public static IReadOnlyList<Restaurant> Restaurants { get; } =
    [
        new(1001, "ELDORIA", "The Gilded Crust of Eldoria", 1001, 1001, new(10, 12, 8, 2, 20_000, 500_000, [100_000, 150_000, 200_000])),
        new(1002, "DWARROWDEEP", "Dwarrowdeep Forge Pizzeria", 1002, 1002, new(25, 50, 15, 3, 100_000, 1_000_000, [180_000, 250_000])),
        new(1003, "SUNSPIRE", "Sunspire Ember Oven", 1003, 1003, new(5, 5, 5, 1, 10_000, 300_000, [125_000])),
        new(1004, "MISTMARSH", "Mistmarsh Bog & Bake", 1004, 1004, new(8, 10, 6, 2, 15_000, 400_000, [])),
        new(1005, "MOONHOLLOW", "Moonhollow Lantern Kitchen", 1003, 1005, new(12, 20, 10, 3, 30_000, 750_000, [150_000, 175_000, 200_000])),   // shares Realm/Sunspire
        new(1006, "FROSTHOLM", "Frostholm Hearth", 1005, 1006, new(6, 8, 4, 2, 25_000, 250_000, [50_000, 100_000])),
    ];

    private const long Small = 1, Medium = 2, Large = 3, Giant = 1001;
    private const long BaconId = 107, PineappleId = 112;

    public static IReadOnlyList<Topping> Toppings { get; } =
    [
        new(1101, "GOBLIN_MUSHROOMS", "Goblin-forest mushrooms", Basic, 3, 13),
        new(1102, "ELVEN_HERBS", "Elven herb blend", Basic, 3, 14),
        new(1103, "KRAKEN_CALAMARI", "Kraken calamari", Premium, 3, 15),
        new(1104, "GRIFFIN_SAUSAGE", "Griffin sausage", Premium, 2, 16),
        new(1105, "DRAGONFIRE_PEPPERS", "Dragon-fire peppers", Mythic, 2, 17),
        new(1106, "PHOENIX_BASIL", "Phoenix-feather basil", Mythic, 1, 18),
        new(1107, "UNICORN_TRUFFLE", "Unicorn truffle shavings", Mythic, 1, 19),
    ];

    public static IReadOnlyList<Override> Overrides { get; } =
    [
        // Pizza Magic keeps the spec menu: none of the realm's products are sold there.
        new(Main, Giant, false, null),
        .. Toppings.Select(t => new Override(Main, t.Id, false, null)),
        new(1001, Large, null, 1350), new(1001, PineappleId, false, null),
        new(1002, Medium, null, 777), new(1002, Giant, null, 2400), new(1002, 1102, false, null),
        new(1003, Small, null, 450), new(1003, Giant, null, 2222), new(1003, 1103, false, null),
        new(1004, Small, false, null), new(1004, Large, null, 1275), new(1004, 1107, false, null),
        new(1005, Medium, null, 725), new(1005, Giant, false, null),
        new(1006, Giant, null, 1875), new(1006, 1105, false, null), new(1006, BaconId, false, null),
    ];

    public static IReadOnlyList<Promotion> Promotions { get; } =
    [
        new(1001, "Moonrise Feast", "15% off from local midnight on October 28 until November 1", "percent_off_order",
            """{"ratePpm":150000,"maxDiscountCents":null}""", 100, false, null, "2026-10-28T00:00:00", "2026-11-01T00:00:00",
            ["MOONRISE"], [], []),
        new(1002, "Dragon Hoard", "$7.50 off orders over $30 before tax", "fixed_off_order",
            """{"amountCents":750}""", 100, false, "REALM_FEAST", null, null,
            ["HOARD"], [1001, 1002, 1003], [("min_subtotal", """{"amountCents":3000,"inclusive":false}""")]),
        new(1003, "Forge Twenty", "20% off your order, up to $4", "percent_off_order",
            """{"ratePpm":200000,"maxDiscountCents":400}""", 100, false, "REALM_FEAST", null, null,
            ["FORGE20", "DWARF20"], [1002], []),
        new(1004, "Archmage's Decree", "30% off until 6 pm local time on October 27; cannot be combined", "percent_off_order",
            """{"ratePpm":300000,"maxDiscountCents":null}""", 100, true, null, null, "2026-10-27T18:00:00",
            ["ARCHMAGE"], [], []),
        new(1005, "Frost Giant Supper", "$5 off orders of $25 or more, taken before other discounts", "fixed_off_order",
            """{"amountCents":500}""", 50, false, null, null, null,
            ["FROSTFEAST"], [1006], [("min_subtotal", """{"amountCents":2500,"inclusive":true}""")]),
    ];

    public static string Generate()
    {
        var sql = new StringBuilder();
        sql.Append("-- GENERATED by tools/PizzaMagic.FantasySeedGen; do not edit by hand (UQ26, plan section 7.5, milestone M15F).\n");
        sql.Append("-- The fantasy realm TEST database. Applied after data/seed/0000 and 0001 only by the Fantasy profile\n");
        sql.Append("-- (dotnet run --project src/PizzaMagic.OrderManager -- --database fantasy, file pizzamagic-fantasy.db); never to pizzamagic.db.\n");
        sql.Append(Invariant($"-- Zone offsets are drawn by SplitMix64 with the fixed seed {Seed}. Core part only: single-level tax, no class rates,\n"));
        sql.Append("-- no crust/sauce upgrades (designed, not built; CatalogValidator refuses that data, A70).\n");
        sql.Append("-- Regenerate: dotnet run --project tools/PizzaMagic.FantasySeedGen (from the repository root).\n");

        Section(sql, "Time zones: one fixed offset each, covering the same span as 0000-time-zones.sql",
            "time_zone (id, iana_id, display_name)",
            Zones.Select(z => Invariant($"({z.Id},{Q(z.IanaId)},{Q($"{z.Name} ({Utc(z.OffsetMinutes)})")})")));
        Section(sql, null,
            "time_zone_offset_period (time_zone_id, valid_from_utc, valid_to_utc, utc_offset_minutes, abbreviation, is_dst)",
            Zones.Select(z => Invariant($"({z.Id},{Q(CoverageStart)},{Q(CoverageEnd)},{z.OffsetMinutes},{Q(z.Abbreviation)},0)")));

        Section(sql, "Tax: single-level jurisdictions (multi-level tax is designed, not built; A70)",
            "tax_jurisdiction (id, code, name, kind, parent_jurisdiction_id, default_rate_ppm, tax_rounding_method, tax_rounding_scope)",
            Jurisdictions.Select(j => Invariant($"({j.Id},{Q(j.Code)},{Q(j.Name)},{Q(j.Kind)},NULL,{j.RatePpm},{Q(j.RoundingMethod)},'COMBINED')")));

        Section(sql, "Restaurants (Pizza Magic, id 1, stays the default); MOONHOLLOW shares Realm/Sunspire with SUNSPIRE",
            "restaurant (id, code, name, time_zone_id, tax_jurisdiction_id, is_default)",
            Restaurants.Select(r => Invariant($"({r.Id},{Q(r.Code)},{Q(r.Name)},{r.ZoneId},{r.JurisdictionId},0)")));
        Section(sql, null,
            "ordering_policy (restaurant_id, max_lines, max_line_quantity, max_toppings_per_pizza, max_promo_codes, max_tip_amount_cents, max_tip_percent_ppm, tip_presets_ppm)",
            Restaurants.Select(r => Invariant(
                $"({r.Id},{r.Policy.MaxLines},{r.Policy.MaxLineQuantity},{r.Policy.MaxToppings},{r.Policy.MaxPromoCodes},{r.Policy.MaxTipCents},{r.Policy.MaxTipPpm},{Q("[" + string.Join(",", r.Policy.TipPresetsPpm.Select(p => Invariant($"{p}"))) + "]")})")));

        Section(sql, "Catalog: a new MYTHIC tier (25% of the base), a Giant size, and realm toppings in every tier",
            "topping_tier (id, code, display_name, pricing_method, rate_ppm, sort_order)",
            [Invariant($"({Mythic},'MYTHIC','Mythic','percent_of_base',250000,3)")]);
        Section(sql, null,
            "product (id, product_type_code, code, display_name, price_cents, tax_class_id, sort_order)",
            [Invariant($"({Giant},'PIZZA_SIZE','GIANT',{Q("Giant's feast")},1999,{PreparedFood},4)")]);
        Section(sql, null,
            "product (id, product_type_code, code, display_name, topping_tier_id, max_multiplier, tax_class_id, sort_order)",
            Toppings.Select(t => Invariant($"({t.Id},'TOPPING',{Q(t.Code)},{Q(t.Name)},{t.TierId},{t.MaxMultiplier},{PreparedFood},{t.SortOrder})")));

        Section(sql, "Per-restaurant size prices and unavailable items",
            "restaurant_product_override (restaurant_id, product_id, is_available, price_cents)",
            Overrides.Select(o => Invariant($"({o.RestaurantId},{o.ProductId},{Nullable(o.IsAvailable is { } a ? (a ? 1 : 0) : null)},{Nullable(o.PriceCents)})")));

        Section(sql, "Promotions: local-time windows (evaluated in each restaurant's zone), restaurant limits, an exclusive code, an exclusion group",
            "promotion (id, name, customer_description, effect_type, effect_params, priority, is_exclusive, exclusion_group, valid_from_local, valid_to_local)",
            Promotions.Select(p => Invariant(
                $"({p.Id},{Q(p.Name)},{Q(p.Description)},{Q(p.EffectType)},{Q(p.EffectParams)},{p.Priority},{(p.IsExclusive ? 1 : 0)},{NullableText(p.ExclusionGroup)},{NullableText(p.ValidFromLocal)},{NullableText(p.ValidToLocal)})")));
        Section(sql, null,
            "promotion_code (code, promotion_id)",
            Promotions.SelectMany(p => p.Codes.Select(c => Invariant($"({Q(c)},{p.Id})"))));
        var conditionId = 1000;
        Section(sql, null,
            "promotion_condition (id, promotion_id, condition_type, params)",
            Promotions.SelectMany(p => p.Conditions.Select(c => Invariant($"({++conditionId},{p.Id},{Q(c.Type)},{Q(c.Params)})"))).ToList());
        Section(sql, null,
            "promotion_restaurant (promotion_id, restaurant_id)",
            Promotions.SelectMany(p => p.RestaurantIds.Select(r => Invariant($"({p.Id},{r})"))));
        return sql.ToString();
    }

    private static void Section(StringBuilder sql, string? comment, string table, IEnumerable<string> rows)
    {
        sql.Append('\n');
        if (comment is not null)
        {
            sql.Append("-- ").Append(comment).Append('\n');
        }
        sql.Append("INSERT INTO ").Append(table).Append(" VALUES\n ");
        sql.Append(string.Join(",\n ", rows));
        sql.Append(";\n");
    }

    /// <summary>
    /// Offsets in 15-minute steps within -12:00..+12:00, one draw per role so the set always has a negative offset, a
    /// positive one with the same local date as UTC at the default test clock (2026-10-27T19:00Z), one whose local date
    /// is already the next day there (+05:00 or more), a :15/:45 offset, and one unconstrained; no two equal.
    /// </summary>
    private static List<Zone> DrawZones()
    {
        var random = new SplitMix64(Seed);
        var chosen = new List<int>();
        int Draw(IReadOnlyList<int> candidates)
        {
            int value;
            do
            {
                value = candidates[(int)(random.Next() % (ulong)candidates.Count)];
            }
            while (chosen.Contains(value));
            chosen.Add(value);
            return value;
        }

        return
        [
            new(1001, "Realm/Eldoria", "Eldorian Time", "ELT", Draw(Steps(-720, -15))),
            new(1002, "Realm/Dwarrowdeep", "Deep Hold Time", "DHT", Draw(Steps(15, 285))),
            new(1003, "Realm/Sunspire", "Sunspire Time", "SST", Draw(Steps(300, 720))),
            new(1004, "Realm/Mistmarsh", "Mistmarsh Time", "MMT", Draw(Steps(-705, 285).Where(m => Math.Abs(m) % 60 is 15 or 45).ToList())),
            new(1005, "Realm/Frostholm", "Frostholm Time", "FHT", Draw(Steps(-720, 720))),
        ];
    }

    private static List<int> Steps(int from, int to) => Enumerable.Range(0, (to - from) / 15 + 1).Select(i => from + 15 * i).ToList();

    private static string Q(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string NullableText(string? text) => text is null ? "NULL" : Q(text);

    private static string Nullable(long? value) => value is { } v ? Invariant($"{v}") : "NULL";

    private static string Utc(int offsetMinutes) =>
        Invariant($"UTC{(offsetMinutes < 0 ? '-' : '+')}{Math.Abs(offsetMinutes) / 60:00}:{Math.Abs(offsetMinutes) % 60:00}");

    /// <summary>SplitMix64 (Steele, Lea and Flood 2014): tiny, fully specified, identical on every runtime.</summary>
    private sealed class SplitMix64(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                var z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
