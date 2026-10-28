using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.OrderManager.Data;

namespace PizzaMagic.OrderManager.Tests.Persistence;

public class CatalogValidatorTests
{
    private static async Task<IReadOnlyList<string>> ValidateAfterAsync(string? sql, DateTimeOffset? now = null)
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        if (sql is not null)
        {
            await database.ExecuteAsync(sql);
        }
        if (now is { } instant)
        {
            database.Clock.SetUtcNow(instant);
        }
        await using var db = database.NewContext();
        return await new CatalogValidator(db, PromotionRegistry.Default, PricingMethodRegistry.Default, database.Clock).ValidateAsync();
    }

    [Fact]
    public async Task Catalog_validator_passes_seed()
    {
        (await ValidateAfterAsync(null)).ShouldBeEmpty();
    }

    // T18: the root jurisdiction must set both rounding settings.
    [Theory]
    [InlineData("tax_rounding_method")]
    [InlineData("tax_rounding_scope")]
    public async Task Catalog_validator_requires_root_tax_rounding_settings(string column)
    {
        (await ValidateAfterAsync($"UPDATE tax_jurisdiction SET {column} = NULL WHERE code = 'WA'"))
            .ShouldContain("Root tax jurisdiction WA must set tax_rounding_method and tax_rounding_scope.");
    }

    [Theory]
    [InlineData("bogus effect type",
        "INSERT INTO promotion (id, name, customer_description, effect_type, effect_params) VALUES (3, 'Bogus', 'x', 'bogus', '{}')",
        "Promotion 3 (Bogus): Unknown effect kind 'bogus'.")]
    [InlineData("bogus condition type",
        "INSERT INTO promotion_condition (promotion_id, condition_type, params) VALUES (1, 'bogus', '{}')",
        "Promotion 1 (I Love Pizza): Unknown condition kind 'bogus'.")]
    [InlineData("non-integer params",
        """UPDATE promotion SET effect_params = '{"amountCents":5.5}' WHERE id = 2""",
        "Promotion 2 (Save Five): 'amountCents' must be a non-negative integer.")]
    [InlineData("restaurant jurisdiction with a parent (A70)",
        "INSERT INTO tax_jurisdiction (id, code, name, kind, parent_jurisdiction_id, default_rate_ppm) VALUES (2, 'SEATTLE', 'Seattle', 'City', 1, 38500); UPDATE restaurant SET tax_jurisdiction_id = 2",
        "Restaurant MAIN is in jurisdiction SEATTLE, which has a parent: multi-level tax is designed, not built (A70).")]
    [InlineData("promo time key without Local (TZ12)",
        """UPDATE promotion_condition SET params = '{"amountCents":2000,"inclusive":false,"startsAt":"2026-12-24"}' WHERE id = 1""",
        "Promotion 2 (Save Five): time-valued parameter 'startsAt' must use a key ending in 'Local'.")]
    [InlineData("Local key with a UTC value (TZ12)",
        """UPDATE promotion_condition SET params = '{"amountCents":2000,"inclusive":false,"startDateLocal":"2026-12-24T00:00:00Z"}' WHERE id = 1""",
        "Promotion 2 (Save Five): parameter 'startDateLocal' is a local value and must not carry Z or an offset.")]
    [InlineData("active product with inactive tax class",
        "UPDATE tax_class SET is_active = 0 WHERE id = 1",
        "Product SMALL is active but its tax class PREPARED_FOOD is not.")]
    [InlineData("price override on a topping",
        "INSERT INTO restaurant_product_override (restaurant_id, product_id, price_cents) VALUES (1, 101, 150)",
        "Restaurant MAIN overrides the price of PEPPERONI, which is a topping (toppings are priced by rules).")]
    [InlineData("size override not above zero",
        "INSERT INTO restaurant_product_override (restaurant_id, product_id, price_cents) VALUES (1, 1, 0)",
        "Restaurant MAIN overrides the price of SMALL to 0; a pizza size must cost more than zero.")]
    [InlineData("unknown topping pricing method",
        "UPDATE topping_tier SET pricing_method = 'per_slice' WHERE code = 'BASIC'",
        "Topping tier BASIC: Unknown topping pricing method 'per_slice'.")]
    [InlineData("percent method without a rate",
        "UPDATE topping_tier SET rate_ppm = NULL WHERE code = 'BASIC'",
        "Topping tier BASIC: percent_of_base requires a rate.")]
    [InlineData("missing ordering policy",
        "DELETE FROM ordering_policy",
        "Restaurant MAIN has no ordering_policy row.")]
    [InlineData("no default restaurant",
        "UPDATE restaurant SET is_default = 0",
        "Exactly one restaurant must be the default (found 0).")]
    public async Task Catalog_validator_rejects(string rule, string sql, string expected)
    {
        (await ValidateAfterAsync(sql)).ShouldContain(expected, rule);
    }

    // TZ8-TZ10.
    [Fact]
    public async Task Catalog_validator_rejects_time_zone_gap_overlap_and_short_coverage()
    {
        (await ValidateAfterAsync("DELETE FROM time_zone_offset_period WHERE time_zone_id = 1 AND valid_from_utc = '2026-03-08T10:00:00.000Z'"))
            .ShouldContain("Time zone America/Los_Angeles has a gap at 2026-03-08T10:00:00.000Z.");
        (await ValidateAfterAsync("UPDATE time_zone_offset_period SET valid_to_utc = '2026-03-08T11:00:00.000Z' WHERE time_zone_id = 1 AND valid_from_utc = '2025-11-02T09:00:00.000Z'"))
            .ShouldContain("Time zone America/Los_Angeles has overlapping periods at 2026-03-08T10:00:00.000Z.");
        (await ValidateAfterAsync(null, new DateTimeOffset(2036, 6, 1, 0, 0, 0, TimeSpan.Zero)))
            .ShouldContain("Time zone America/Los_Angeles must cover 2035-06-01T00:00:00.000Z to 2041-06-01T00:00:00.000Z (one year back, five years ahead); it ends at 2041-01-01T00:00:00.000Z.");
    }

    // A70: rows that only unbuilt code would read are refused rather than silently ignored.
    [Theory]
    [InlineData("INSERT INTO restaurant_topping_tier_override (restaurant_id, topping_tier_id, pricing_method, rate_ppm) VALUES (1, 2, 'percent_of_base', 120000)", "restaurant_topping_tier_override")]
    [InlineData("INSERT INTO restaurant_portion_type_override VALUES (1, 'HALF', 500000)", "restaurant_portion_type_override")]
    [InlineData("INSERT INTO promotion_target (promotion_id, role, target_kind, product_type_code) VALUES (1, 'DISCOUNT', 'PRODUCT_TYPE', 'PIZZA_SIZE')", "promotion_target")]
    [InlineData("INSERT INTO jurisdiction_tax_class_rate VALUES (1, 2, 100000)", "jurisdiction_tax_class_rate")]
    [InlineData("UPDATE product SET pricing_method = 'fixed_amount', amount_cents = 100 WHERE code = 'PINEAPPLE'", "PINEAPPLE")]
    [InlineData("INSERT INTO restaurant_product_override (restaurant_id, product_id, pricing_method, amount_cents) VALUES (1, 112, 'fixed_amount', 100)", "PINEAPPLE")]
    [InlineData("UPDATE portion_type SET price_factor_ppm = 900000 WHERE code = 'WHOLE'", "WHOLE")]
    [InlineData("INSERT INTO product_category (id, code, name, product_type_code) VALUES (1, 'SIDES', 'Sides', 'ADDON'); INSERT INTO product (id, product_type_code, code, display_name, category_id, price_cents, tax_class_id) VALUES (401, 'ADDON', 'GARLIC_KNOTS', 'Garlic knots', 1, 499, 1)", "GARLIC_KNOTS")]
    [InlineData("INSERT INTO product (id, product_type_code, code, display_name, price_cents, tax_class_id) VALUES (202, 'CRUST', 'STUFFED', 'Stuffed', 150, 1)", "STUFFED")]
    public async Task Catalog_validator_rejects_rows_for_unbuilt_features(string sql, string named)
    {
        var errors = await ValidateAfterAsync(sql);

        errors.ShouldContain(e => e.Contains(named) && e.Contains("designed, not built"), $"expected an A70 error naming {named}; got: {string.Join(" | ", errors)}");
    }

    [Fact]
    public async Task Initialization_fails_fast_on_an_invalid_catalog()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        await database.ExecuteAsync("DELETE FROM ordering_policy");
        await using var db = database.NewContext();

        var error = await Should.ThrowAsync<CatalogValidationException>(() =>
            DatabaseInitializer.InitializeAsync(db, TestDatabase.DataDirectory, database.Clock, PromotionRegistry.Default, PricingMethodRegistry.Default));
        error.Errors.ShouldContain("Restaurant MAIN has no ordering_policy row.");
    }
}
