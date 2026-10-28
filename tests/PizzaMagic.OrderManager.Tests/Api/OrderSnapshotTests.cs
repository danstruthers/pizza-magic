using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PizzaMagic.Contracts.Api;
using static PizzaMagic.OrderManager.Tests.Api.OrderApiTests;

namespace PizzaMagic.OrderManager.Tests.Api;

/// <summary>Orders keep their snapshot: catalog, tax and promotion changes never alter a placed order (A25).</summary>
public class OrderSnapshotTests
{
    [Fact]
    public async Task Historical_order_unchanged_after_price_rate_label_and_promo_change()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var created = await (await SubmitAsync(client, P9(), Guid.NewGuid().ToString())).ReadAsync<OrderResponse>();
        var before = await client.GetStringAsync($"/api/v1/orders/{created.OrderId}");
        var descriptionBefore = await client.GetStringAsync($"/api/v1/orders/{created.OrderId}/description");

        await using (var connection = new SqliteConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE product SET price_cents = 1300 WHERE code = 'LARGE';
                UPDATE product SET display_name = 'Thin and crispy' WHERE code = 'HAND_TOSSED';
                UPDATE product SET is_default = 0 WHERE code = 'HAND_TOSSED';
                UPDATE product SET display_name = 'Spicy pepperoni' WHERE code = 'PEPPERONI';
                UPDATE product SET is_active = 0 WHERE code = 'BACON';
                UPDATE portion SET label = 'entire pizza' WHERE code = 'WHOLE';
                UPDATE tax_jurisdiction SET default_rate_ppm = 100000 WHERE code = 'WA';
                UPDATE promotion SET effect_params = '{"ratePpm":500000,"maxDiscountCents":null}' WHERE id = 1;
                """;
            await command.ExecuteNonQueryAsync();
        }

        (await client.GetStringAsync($"/api/v1/orders/{created.OrderId}")).ShouldBe(before);
        (await client.GetStringAsync($"/api/v1/orders/{created.OrderId}/description")).ShouldBe(descriptionBefore);

        // N8: new quotes see the retired topping as unavailable.
        var quote = await client.PostAsJsonAsync("/api/v1/quotes", new QuoteRequest(null, null, [Pizza("LARGE", 1, "BACON")], null, null), ApiFactory.Json);
        quote.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await quote.ReadJsonAsync()).FieldErrors().ShouldBe([("lineItems[0].toppings[0].code", "PRODUCT_UNAVAILABLE")]);
    }

    // Records live 24 h; an expired key can be reused for a new order (the expired row is deleted in the write).
    [Fact]
    public async Task Expired_key_reuse_creates_new_order()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var key = Guid.NewGuid().ToString();

        var first = await (await SubmitAsync(client, P9(), key)).ReadAsync<OrderResponse>();
        factory.Clock.Advance(TimeSpan.FromHours(25));
        var second = await SubmitAsync(client, P9(customerName: "Someone else"), key);

        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        (await second.ReadAsync<OrderResponse>()).OrderId.ShouldNotBe(first.OrderId);
    }
}
