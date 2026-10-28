using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Data.Sqlite;
using PizzaMagic.Contracts.Api;
using static PizzaMagic.OrderManager.Tests.Api.OrderApiTests;

namespace PizzaMagic.OrderManager.Tests.Api;

public class OrderLifecycleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    public static async Task<OrderResponse> CreateAsync(HttpClient client, SubmitOrderRequest? request = null)
    {
        var response = await SubmitAsync(client, request ?? P9(), Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<OrderResponse>();
    }

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string orderId, object body, string? ifMatch, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/orders/{orderId}") { Content = JsonContent.Create(body, options: ApiFactory.Json) };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> CancelAsync(HttpClient client, string orderId, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderId}/cancel");
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> AdvanceAsync(HttpClient client, string orderId, string? expectedStatus) =>
        client.PostAsJsonAsync($"/api/v1/kitchen/orders/{orderId}/advance", new AdvanceOrderRequest(expectedStatus), ApiFactory.Json);

    /// <summary>P29w: P9's order changed to 1 x L+pep+bacon, ILOVEPIZZA, 15% -> $15.74.</summary>
    public static UpdateOrderRequest P29w(long expectedTotal = 1574) =>
        new([Pizza("LARGE", 1, "PEPPERONI", "BACON")], ["ILOVEPIZZA"], new TipRequest("Percent", "15"), expectedTotal, "Dana");

    private async Task<long> CountAsync(string sql)
    {
        _ = factory.Server;
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> TypeOf(HttpResponseMessage response) => (await response.ReadJsonAsync()).GetProperty("type").GetString()!;

    // ---- Update ----

    [Fact]
    public async Task Put_updates_P9_order_to_P29w()
    {
        var client = Client;
        var order = await CreateAsync(client);
        var quote = await (await client.PostAsJsonAsync("/api/v1/quotes",
            new QuoteRequest(null, null, P29w().LineItems, P29w().PromoCodes, P29w().Tip), ApiFactory.Json)).ReadAsync<QuoteResponse>();

        var response = await PutAsync(client, order.OrderId, P29w(), "\"1\"");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.ETag!.Tag.ShouldBe("\"2\"");
        var updated = await response.ReadAsync<OrderResponse>();
        (updated.Version, updated.Status, updated.Pricing.TotalCents).ShouldBe((2, "NotStarted", 1574L));
        updated.CreatedAt.ShouldBe(order.CreatedAt);
        quote.TotalCents.ShouldBe(1574);
        (await (await client.GetAsync($"/api/v1/orders/{order.OrderId}")).ReadAsync<OrderResponse>()).Pricing.TotalCents.ShouldBe(1574);
        (await CountAsync($"SELECT COUNT(*) FROM order_line WHERE order_id = '{order.OrderId}' AND quantity = 1")).ShouldBe(1);
        (await CountAsync($"SELECT COUNT(*) FROM order_status_history WHERE order_id = '{order.OrderId}' AND event_type = 'Updated' AND order_version = 2")).ShouldBe(1);
    }

    // Edit_path_SaveChanges_after_version_bump_does_not_throw: consecutive edits each bump the version.
    [Fact]
    public async Task Consecutive_updates_bump_the_version_each_time()
    {
        var client = Client;
        var order = await CreateAsync(client);

        (await PutAsync(client, order.OrderId, P29w(), "\"1\"")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await PutAsync(client, order.OrderId, P29w() with { CustomerName = "Robin" }, "\"2\"");

        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        (await second.ReadAsync<OrderResponse>()).Version.ShouldBe(3);
    }

    [Fact]
    public async Task Put_replay_after_lost_response_returns_200_not_412()
    {
        var client = Client;
        var order = await CreateAsync(client);
        var key = Guid.NewGuid().ToString();

        var first = await PutAsync(client, order.OrderId, P29w(), "\"1\"", key);
        var retry = await PutAsync(client, order.OrderId, P29w(), "\"1\"", key);

        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        retry.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        retry.Headers.ETag!.Tag.ShouldBe("\"2\"");
        (await retry.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Put_same_key_different_body_is_422_reused()
    {
        var client = Client;
        var order = await CreateAsync(client);
        var key = Guid.NewGuid().ToString();
        await PutAsync(client, order.OrderId, P29w(), "\"1\"", key);

        var response = await PutAsync(client, order.OrderId, P29w() with { CustomerName = "Robin" }, "\"1\"", key);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await TypeOf(response)).ShouldBe(ProblemTypes.IdempotencyKeyReused);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.PreconditionRequired, "precondition-required")]
    [InlineData("1", HttpStatusCode.BadRequest, "validation")]
    [InlineData("\"abc\"", HttpStatusCode.BadRequest, "validation")]
    public async Task If_match_is_required_and_must_be_a_quoted_version(string? ifMatch, HttpStatusCode status, string type)
    {
        var client = Client;
        var order = await CreateAsync(client);

        var response = await PutAsync(client, order.OrderId, P29w(), ifMatch);

        response.StatusCode.ShouldBe(status);
        (await TypeOf(response)).ShouldBe(ProblemTypes.Base + type);
    }

    [Fact]
    public async Task Update_rejects_restaurantId_and_channel_400()
    {
        var client = Client;
        var order = await CreateAsync(client);

        foreach (var field in new[] { "\"restaurantId\": 1", "\"channel\": \"ONLINE\"" })
        {
            var body = $$"""{ "lineItems": [ { "kind": "Pizza", "sizeCode": "LARGE", "quantity": 1 } ], "expectedTotalCents": 1278, {{field}} }""";
            var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/orders/{order.OrderId}") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

            (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, field);
        }
    }

    // P14b: removing the pepperoni takes the cart to exactly $20.00, so SAVE5 no longer applies.
    [Fact]
    public async Task Update_that_invalidates_promo_is_422_and_order_unchanged()
    {
        var client = Client;
        var order = await CreateAsync(client, new SubmitOrderRequest(null, null, [Pizza("SMALL", 1, "PEPPERONI"), Pizza("SMALL", 3)], ["SAVE5"], null, 1640, null));

        var response = await PutAsync(client, order.OrderId,
            new UpdateOrderRequest([Pizza("SMALL", 1), Pizza("SMALL", 3)], ["SAVE5"], null, 2130, null), "\"1\"");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await TypeOf(response)).ShouldBe(ProblemTypes.PromotionNotApplied);
        var stored = await (await client.GetAsync($"/api/v1/orders/{order.OrderId}")).ReadAsync<OrderResponse>();
        (stored.Version, stored.Pricing.TotalCents).ShouldBe((1, 1640L));
    }

    [Fact]
    public async Task Update_with_retired_topping_is_422_naming_item()
    {
        var client = Client;
        var order = await CreateAsync(client, new SubmitOrderRequest(null, null, [Pizza("LARGE", 1, "ARTICHOKE_HEARTS")], null, null, 1419, null));
        await using (var connection = new SqliteConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var retire = connection.CreateCommand();
            retire.CommandText = "UPDATE product SET is_active = 0 WHERE code = 'ARTICHOKE_HEARTS'";
            await retire.ExecuteNonQueryAsync();
        }

        try
        {
            var response = await PutAsync(client, order.OrderId,
                new UpdateOrderRequest([Pizza("LARGE", 2, "ARTICHOKE_HEARTS")], null, null, 2837, null), "\"1\"");

            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            var problem = await response.ReadJsonAsync();
            problem.FieldErrors().ShouldBe([("lineItems[0].toppings[0].code", "PRODUCT_UNAVAILABLE")]);
            problem.GetProperty("errors").GetProperty("lineItems[0].toppings[0].code")[0].GetProperty("message").GetString()!
                .ShouldContain("ARTICHOKE_HEARTS");
        }
        finally
        {
            await using var connection = new SqliteConnection(factory.ConnectionString);
            await connection.OpenAsync();
            await using var restore = connection.CreateCommand();
            restore.CommandText = "UPDATE product SET is_active = 1 WHERE code = 'ARTICHOKE_HEARTS'";
            await restore.ExecuteNonQueryAsync();
        }
    }

    // ---- Precedence (plan section 2.11): status, then version, then fields, then promotions, then price. ----

    [Fact]
    public async Task Started_order_with_stale_total_and_bad_promo_gets_409_not_modifiable()
    {
        var client = Client;
        var order = await CreateAsync(client);
        (await AdvanceAsync(client, order.OrderId, "NotStarted")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PutAsync(client, order.OrderId, P29w(expectedTotal: 1) with { PromoCodes = ["SAVE5"] }, "\"2\"");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ReadJsonAsync();
        problem.GetProperty("type").GetString().ShouldBe(ProblemTypes.OrderNotModifiable);
        problem.GetProperty("currentStatus").GetString().ShouldBe("Started");
    }

    [Fact]
    public async Task Stale_version_with_invalid_fields_gets_412()
    {
        var client = Client;
        var order = await CreateAsync(client);
        (await PutAsync(client, order.OrderId, P29w(), "\"1\"")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PutAsync(client, order.OrderId, P29w() with { LineItems = [Pizza("LARGE", 0)] }, "\"1\"");

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var problem = await response.ReadJsonAsync();
        problem.GetProperty("type").GetString().ShouldBe(ProblemTypes.VersionMismatch);
        problem.GetProperty("currentVersion").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Invalid_fields_and_stale_total_gets_422()
    {
        var client = Client;
        var order = await CreateAsync(client);

        var response = await PutAsync(client, order.OrderId, P29w(expectedTotal: 1) with { LineItems = [Pizza("LARGE", 0)] }, "\"1\"");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await TypeOf(response)).ShouldBe(ProblemTypes.Validation);
    }

    [Fact]
    public async Task Ineligible_promo_and_stale_total_gets_422_promotion_not_applied()
    {
        var client = Client;
        var order = await CreateAsync(client);

        var response = await PutAsync(client, order.OrderId,
            new UpdateOrderRequest([Pizza("SMALL", 1)], ["SAVE5"], null, 1, null), "\"1\"");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await TypeOf(response)).ShouldBe(ProblemTypes.PromotionNotApplied);
    }

    [Fact]
    public async Task Update_with_stale_total_is_409_price_changed()
    {
        var client = Client;
        var order = await CreateAsync(client);

        var response = await PutAsync(client, order.OrderId, P29w(expectedTotal: 1500), "\"1\"");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ReadJsonAsync();
        problem.GetProperty("type").GetString().ShouldBe(ProblemTypes.PriceChanged);
        problem.GetProperty("currentQuote").GetProperty("totalCents").GetInt64().ShouldBe(1574);
    }

    [Fact]
    public async Task Update_rebuilds_snapshots_at_current_rates()
    {
        await using var isolated = new ApiFactory();
        var client = isolated.CreateClient();
        var order = await CreateAsync(client, new SubmitOrderRequest(null, null, [Pizza("SMALL", 1)], null, null, 533, null));
        await using (var connection = new SqliteConnection(isolated.ConnectionString))
        {
            await connection.OpenAsync();
            await using var rate = connection.CreateCommand();
            rate.CommandText = "UPDATE tax_jurisdiction SET default_rate_ppm = 100000 WHERE code = 'WA'";
            await rate.ExecuteNonQueryAsync();
        }

        // Same content, re-priced at today's 10%: 500 + 50 = 550 (the customer confirms the new total).
        var response = await PutAsync(client, order.OrderId, new UpdateOrderRequest([Pizza("SMALL", 1)], null, null, 550, null), "\"1\"");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var updated = await response.ReadAsync<OrderResponse>();
        updated.Pricing.TaxLines.Single().RatePpm.ShouldBe(100_000);
        updated.Pricing.TotalCents.ShouldBe(550);
    }

    // ---- Cancel ----

    [Fact]
    public async Task Cancel_not_started_order()
    {
        var client = Client;
        var order = await CreateAsync(client);

        var response = await CancelAsync(client, order.OrderId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelled = await response.ReadAsync<OrderResponse>();
        (cancelled.Status, cancelled.Version).ShouldBe(("Cancelled", 2));
        cancelled.AllowedActions.ShouldBeEmpty();
        (await CountAsync($"SELECT COUNT(*) FROM order_status_history WHERE order_id = '{order.OrderId}' AND event_type = 'Cancelled' AND to_status = 'Cancelled' AND order_version = 2")).ShouldBe(1);
    }

    [Fact]
    public async Task Cancel_twice_is_idempotent_with_no_new_history_row()
    {
        var client = Client;
        var order = await CreateAsync(client);
        await CancelAsync(client, order.OrderId);

        var again = await CancelAsync(client, order.OrderId);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.ReadAsync<OrderResponse>()).Version.ShouldBe(2);
        (await CountAsync($"SELECT COUNT(*) FROM order_status_history WHERE order_id = '{order.OrderId}'")).ShouldBe(2);
    }

    [Fact]
    public async Task Cancel_when_started_is_409_and_the_row_is_unchanged()
    {
        var client = Client;
        var order = await CreateAsync(client);
        await AdvanceAsync(client, order.OrderId, "NotStarted");

        var response = await CancelAsync(client, order.OrderId);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ReadJsonAsync();
        problem.GetProperty("type").GetString().ShouldBe(ProblemTypes.OrderNotCancellable);
        problem.GetProperty("currentStatus").GetString().ShouldBe("Started");
        problem.GetProperty("statusChangedAt").GetString().ShouldBe("2026-10-27T19:00:00.000Z");
        problem.GetProperty("statusChangedAtLocal").GetProperty("localTime").GetString().ShouldBe("2026-10-27T12:00:00");
        problem.GetProperty("detail").GetString().ShouldBe("The kitchen has started this order; it can no longer be cancelled.");
        (await (await client.GetAsync($"/api/v1/orders/{order.OrderId}")).ReadAsync<OrderResponse>()).Status.ShouldBe("Started");
    }

    [Fact]
    public async Task Cancel_with_stale_if_match_is_412_and_unknown_order_is_404()
    {
        var client = Client;
        var order = await CreateAsync(client);
        await PutAsync(client, order.OrderId, P29w(), "\"1\"");

        (await CancelAsync(client, order.OrderId, "\"1\"")).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await CancelAsync(client, Guid.NewGuid().ToString())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- Kitchen ----

    [Fact]
    public async Task Advance_moves_through_sequence()
    {
        var client = Client;
        var order = await CreateAsync(client);

        var steps = new List<(string, string, int)>();
        foreach (var expected in new[] { "NotStarted", "Started", "ReadyForPickup" })
        {
            var response = await AdvanceAsync(client, order.OrderId, expected);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.ReadAsync<AdvanceOrderResponse>();
            body.AlreadyApplied.ShouldBeFalse();
            body.LineItems.Single().Description.ShouldBe("2 x Large pizza with Pepperoni, Bacon");
            steps.Add((body.PreviousStatus, body.Status, body.Version));
        }

        steps.ShouldBe([("NotStarted", "Started", 2), ("Started", "ReadyForPickup", 3), ("ReadyForPickup", "Delivered", 4)]);
        (await CountAsync($"SELECT COUNT(*) FROM order_status_history WHERE order_id = '{order.OrderId}' AND event_type = 'StatusChanged' AND actor = 'Kitchen'")).ShouldBe(3);
    }

    [Fact]
    public async Task Advance_repeat_returns_alreadyApplied()
    {
        var client = Client;
        var order = await CreateAsync(client);
        await AdvanceAsync(client, order.OrderId, "NotStarted");

        var repeat = await AdvanceAsync(client, order.OrderId, "NotStarted");

        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await repeat.ReadAsync<AdvanceOrderResponse>();
        (body.Status, body.Version, body.AlreadyApplied).ShouldBe(("Started", 2, true));
        (await CountAsync($"SELECT COUNT(*) FROM order_status_history WHERE order_id = '{order.OrderId}'")).ShouldBe(2);
    }

    [Fact]
    public async Task Advance_terminal_409()
    {
        var client = Client;
        var order = await CreateAsync(client);
        await CancelAsync(client, order.OrderId);

        var response = await AdvanceAsync(client, order.OrderId, "NotStarted");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ReadJsonAsync();
        problem.GetProperty("type").GetString().ShouldBe(ProblemTypes.InvalidTransition);
        problem.GetProperty("currentStatus").GetString().ShouldBe("Cancelled");
        problem.GetProperty("detail").GetString().ShouldBe("Order was cancelled at 2026-10-27T19:00:00.000Z.");

        var delivered = await CreateAsync(client);
        foreach (var expected in new[] { "NotStarted", "Started", "ReadyForPickup" })
        {
            await AdvanceAsync(client, delivered.OrderId, expected);
        }
        (await AdvanceAsync(client, delivered.OrderId, "Delivered")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Baking")]
    public async Task Advance_without_a_valid_expectedStatus_400(string? expected)
    {
        var client = Client;
        var order = await CreateAsync(client);

        var response = await AdvanceAsync(client, order.OrderId, expected);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).FieldErrors().Single().Path.ShouldBe("expectedStatus");
    }
}
