using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PizzaMagic.OrderManager.Tests.Persistence;

public class SchemaTests
{
    private const string Ts = "'2026-10-27T19:00:00.000Z'";

    private static readonly string[] ExpectedTables =
    [
        "customer_order", "data_script_log", "idempotency_record", "jurisdiction_tax_class_rate", "order_channel",
        "order_discount_allocation", "order_line", "order_line_topping", "order_promotion", "order_status",
        "order_status_history", "order_tax_line", "ordering_policy", "portion", "portion_type", "product",
        "product_category", "product_type", "promotion", "promotion_code", "promotion_condition",
        "promotion_incompatibility", "promotion_restaurant", "promotion_target", "restaurant",
        "restaurant_portion_type_override", "restaurant_product_override", "restaurant_topping_tier_override",
        "tax_class", "tax_jurisdiction", "time_zone", "time_zone_offset_period", "topping_tier",
    ];

    [Fact]
    public async Task Migration_creates_expected_tables()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        await using var db = database.NewContext();

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name")
            .ToListAsync();

        tables.ShouldBe(ExpectedTables);
    }

    // A valid order, so each CHECK test changes exactly one thing.
    private static string OrderInsert(string overrides = "") => $"""
        INSERT INTO customer_order (id, restaurant_id, channel_code, status, version, tax_jurisdiction_id, time_zone_id, priced_at_utc,
            subtotal_cents, discount_cents, taxable_cents, tax_cents, tax_rounding_method, tax_rounding_scope,
            tip_mode, tip_input, tip_rate_ppm, tip_cents, total_cents, created_at_utc, updated_at_utc, status_changed_at_utc)
        SELECT 'o1', 1, 'ONLINE', 'NotStarted', 1, 1, 1, {Ts}, 500, 0, 500, 33, 'HALF_UP', 'COMBINED',
            'None', NULL, NULL, 0, 533, {Ts}, {Ts}, {Ts} {overrides};
        """;

    private const string LineInsert = """
        INSERT INTO order_line (id, order_id, line_number, line_kind, product_id, product_code, product_name, base_price_cents,
            base_price_source, tax_class_id, crust_product_id, crust_code, crust_name, crust_is_default, crust_price_cents,
            crust_tax_class_id, sauce_product_id, sauce_code, sauce_name, sauce_is_default, sauce_price_cents, sauce_tax_class_id,
            quantity, unit_price_cents, line_total_cents, discount_cents)
        VALUES ({0}, 'o1', {0}, 'PIZZA', 1, 'SMALL', 'Small', 500, 'Global', 1, 201, 'HAND_TOSSED', 'Hand tossed', 1, 0, 1,
            301, 'CLASSIC_TOMATO', 'Classic tomato', 1, 0, 1, {1}, 500, {2}, 0);
        """;

    private static string Line(int id, int quantity = 1) => string.Format(LineInsert, id, quantity, 500 * quantity);

    private static string Topping(int id, int lineId, int sortOrder, string portion = "WHOLE", int charge = 40) => $"""
        INSERT INTO order_line_topping (id, order_line_id, sort_order, topping_product_id, topping_code, topping_name, tier_code,
            portion_code, portion_label, coverage_mask, coverage_phrase, portion_type_code, multiplier, price_source, pricing_method,
            rate_ppm, topping_unit_price_cents, coverage_factor_ppm, coverage_factor_source, placement_unit_charge_cents,
            placement_charge_cents, tax_class_id)
        VALUES ({id}, {lineId}, {sortOrder}, 101, 'PEPPERONI', 'Pepperoni', 'BASIC', '{portion}', 'whole', 15, 'Pepperoni', 'WHOLE', 1,
            'GlobalTier', 'percent_of_base', 80000, 40, 1000000, 'Global', 40, {charge}, 1);
        """;

    private const string Promotion = """
        INSERT INTO order_promotion (id, order_id, sequence, code, promotion_id, effect_type, phase, discount_cents, message, definition_snapshot)
        VALUES (1, 'o1', 1, 'ILOVEPIZZA', 1, 'percent_off_order', 'Order', 50, 'ILOVEPIZZA saves $0.50.', '{}');
        """;

    private static async Task ShouldViolateAsync(TestDatabase database, string sql)
    {
        var error = await Should.ThrowAsync<SqliteException>(() => database.ExecuteAsync(sql));
        error.SqliteErrorCode.ShouldBe(19);   // SQLITE_CONSTRAINT
    }

    [Fact]
    public async Task Valid_rows_are_accepted()
    {
        await using var database = await TestDatabase.CreateSeededAsync();

        await database.ExecuteAsync(OrderInsert() + Line(1) + Topping(1, 1, 1) + Promotion);

        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM order_line_topping")).ShouldBe(1);
    }

    [Theory]
    [InlineData("total", "UPDATE customer_order SET total_cents = 534")]
    [InlineData("discount > S", "UPDATE customer_order SET discount_cents = 600, taxable_cents = -100, total_cents = -67")]
    [InlineData("tip mode", "UPDATE customer_order SET tip_mode = 'Amount'")]
    [InlineData("rounding method", "UPDATE customer_order SET tax_rounding_method = 'ROUND'")]
    [InlineData("rounding scope", "UPDATE customer_order SET tax_rounding_scope = 'EACH'")]
    [InlineData("timestamp format", "UPDATE customer_order SET priced_at_utc = '2026-10-27 19:00:00'")]
    [InlineData("qty 0", "UPDATE order_line SET quantity = 0, line_total_cents = 0")]
    [InlineData("line total", "UPDATE order_line SET line_total_cents = 501")]
    [InlineData("pizza line without default flags", "UPDATE order_line SET crust_is_default = NULL")]
    [InlineData("placement charge", "UPDATE order_line_topping SET placement_charge_cents = 41")]
    public async Task Order_checks_reject_invalid_rows(string rule, string mutation)
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        await database.ExecuteAsync(OrderInsert() + Line(1) + Topping(1, 1, 1));

        await ShouldViolateAsync(database, mutation);
        rule.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("self-parent", "INSERT INTO tax_jurisdiction (id, code, name, kind, parent_jurisdiction_id, default_rate_ppm) VALUES (9, 'X', 'X', 'City', 9, 0)")]
    [InlineData("rate range", "UPDATE tax_jurisdiction SET default_rate_ppm = 1000001")]
    [InlineData("jurisdiction rounding method", "UPDATE tax_jurisdiction SET tax_rounding_method = 'ROUND'")]
    [InlineData("jurisdiction rounding scope", "UPDATE tax_jurisdiction SET tax_rounding_scope = 'EACH'")]
    [InlineData("duplicate class rate", "INSERT INTO jurisdiction_tax_class_rate VALUES (1, 1, 1000); INSERT INTO jurisdiction_tax_class_rate VALUES (1, 1, 2000)")]
    [InlineData("promo code with a space", "INSERT INTO promotion_code (code, promotion_id) VALUES ('SAVE 5', 2)")]
    [InlineData("topping with price_cents", "UPDATE product SET price_cents = 100 WHERE code = 'PEPPERONI'")]
    [InlineData("second default crust", "INSERT INTO product (id, product_type_code, code, display_name, price_cents, tax_class_id, is_default) VALUES (202, 'CRUST', 'THIN', 'Thin', 0, 1, 1)")]
    [InlineData("second default restaurant", "INSERT INTO restaurant (id, code, name, time_zone_id, tax_jurisdiction_id, is_default) VALUES (2, 'R2', 'Two', 1, 1, 1)")]
    [InlineData("local column with Z", "UPDATE promotion SET valid_from_local = '2026-10-28T00:00:00Z' WHERE id = 1")]
    [InlineData("utc column without Z", "INSERT INTO data_script_log VALUES ('x.sql', '" + "0000000000000000000000000000000000000000000000000000000000000000" + "', '2026-10-28T00:00:00')")]
    public async Task Catalog_checks_reject_invalid_rows(string rule, string sql)
    {
        await using var database = await TestDatabase.CreateSeededAsync();

        await ShouldViolateAsync(database, sql);
        rule.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Duplicate_topping_and_portion_on_a_line_is_rejected()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        await database.ExecuteAsync(OrderInsert() + Line(1) + Topping(1, 1, 1));

        await ShouldViolateAsync(database, Topping(2, 1, 2));
    }

    [Fact]
    public async Task Allocation_grain_is_unique()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        await database.ExecuteAsync(OrderInsert() + Line(1) + Topping(1, 1, 1) + Promotion + """
            INSERT INTO order_discount_allocation (order_promotion_id, order_line_id, component_kind, order_line_topping_id, tax_class_id, amount_cents)
            VALUES (1, 1, 'BASE', NULL, 1, 10), (1, 1, 'TOPPING', 1, 1, 5);
            """);

        await ShouldViolateAsync(database, "INSERT INTO order_discount_allocation (order_promotion_id, order_line_id, component_kind, order_line_topping_id, tax_class_id, amount_cents) VALUES (1, 1, 'BASE', NULL, 1, 1)");
        await ShouldViolateAsync(database, "INSERT INTO order_discount_allocation (order_promotion_id, order_line_id, component_kind, order_line_topping_id, tax_class_id, amount_cents) VALUES (1, 1, 'TOPPING', 1, 1, 1)");
    }

    [Fact]
    public async Task Allocation_topping_must_belong_to_its_line()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        await database.ExecuteAsync(OrderInsert() + Line(1) + Line(2) + Topping(1, 1, 1) + Promotion);

        // Topping 1 is on line 1, so an allocation claiming it for line 2 breaks the composite FK.
        await ShouldViolateAsync(database, "INSERT INTO order_discount_allocation (order_promotion_id, order_line_id, component_kind, order_line_topping_id, tax_class_id, amount_cents) VALUES (1, 2, 'TOPPING', 1, 1, 5)");
    }

    [Fact]
    public async Task FK_restrict_blocks_topping_delete()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        await database.ExecuteAsync(OrderInsert() + Line(1) + Topping(1, 1, 1));

        await ShouldViolateAsync(database, "DELETE FROM product WHERE code = 'PEPPERONI'");
    }

    [Fact]
    public async Task Time_zone_fk_integrity()
    {
        await using var database = await TestDatabase.CreateSeededAsync();

        await ShouldViolateAsync(database, "UPDATE restaurant SET time_zone_id = 99");
        await ShouldViolateAsync(database, "DELETE FROM time_zone WHERE id = 1");
    }

    // A72: every time column is either a UTC instant (*_utc) or a local business value (*_local).
    [Fact]
    public async Task Every_time_column_is_utc_or_local_text()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        await using var db = database.NewContext();

        var columns = await db.Database.SqlQueryRaw<string>("""
            SELECT m.name || '.' || p.name AS Value
            FROM sqlite_master m JOIN pragma_table_info(m.name) p
            WHERE m.type = 'table' AND (p.name LIKE '%\_utc' ESCAPE '\' OR p.name LIKE '%\_local' ESCAPE '\'
                OR p.name LIKE '%\_at' ESCAPE '\' OR p.name LIKE '%date%' OR p.name LIKE '%time%')
            """).ToListAsync();

        columns.ShouldNotBeEmpty();
        columns.Where(c => !c.EndsWith("_utc") && !c.EndsWith("_local") && !c.EndsWith("time_zone_id"))
            .ShouldBeEmpty();

        foreach (var property in db.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            property.ClrType.ShouldNotBe(typeof(DateTimeOffset));
            property.ClrType.ShouldNotBe(typeof(DateTimeOffset?));
            if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
            {
                var column = property.GetColumnName();
                (column.EndsWith("_utc") || column.EndsWith("_local")).ShouldBeTrue(column);
            }
        }
    }
}
