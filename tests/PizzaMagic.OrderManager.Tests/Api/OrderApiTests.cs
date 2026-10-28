using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PizzaMagic.Contracts.Api;

namespace PizzaMagic.OrderManager.Tests.Api;

public class OrderApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    public static LineItemRequest Pizza(string size, int quantity, params string[] toppings) =>
        new("Pizza", size, quantity, toppings.Select(t => new ToppingRequest(t)).ToList());

    /// <summary>P9: 2 x Large + pepperoni + bacon, ILOVEPIZZA, 15% tip = $31.48.</summary>
    public static SubmitOrderRequest P9(long? expectedTotal = 3148, string? customerName = "Dana", params string[] codes) =>
        new(null, null, [Pizza("LARGE", 2, "PEPPERONI", "BACON")], codes.Length == 0 ? ["ILOVEPIZZA"] : codes,
            new TipRequest("Percent", "15"), expectedTotal, customerName);

    public static Task<HttpResponseMessage> SubmitAsync(HttpClient client, object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = JsonContent.Create(body, options: ApiFactory.Json) };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }
        return client.SendAsync(request);
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> OrdersAsync() => await CountAsync("SELECT COUNT(*) FROM customer_order");

    [Theory]
    [InlineData(null, "idempotency-key-missing")]
    [InlineData("has spaces", "idempotency-key-invalid")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123x", "idempotency-key-invalid")]
    public async Task Submit_requires_a_valid_idempotency_key(string? key, string type)
    {
        var response = await SubmitAsync(factory.CreateClient(), P9(), key);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("type").GetString().ShouldBe(ProblemTypes.Base + type);
    }

    [Fact]
    public async Task Submit_P9_creates_the_order()
    {
        var response = await SubmitAsync(factory.CreateClient(), P9(), NewKey());

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var order = await response.ReadAsync<OrderResponse>();
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/orders/{order.OrderId}");
        response.Headers.ETag!.Tag.ShouldBe("\"1\"");
        Guid.TryParse(order.OrderId, out _).ShouldBeTrue();
        (order.Status, order.StatusDisplay, order.Version, order.CustomerName, order.Channel, order.RestaurantId)
            .ShouldBe(("NotStarted", "Not Started", 1, "Dana", "ONLINE", 1L));
        order.AllowedActions.ShouldBe(["update", "cancel"]);
        order.CreatedAt.ShouldBe("2026-10-27T19:00:00.000Z");
        order.CreatedAtLocal.ShouldBe(new LocalTimeResponse("2026-10-27T12:00:00", -420, "PDT"));
        order.StatusChangedAt.ShouldBe(order.CreatedAt);
        order.Pricing.TotalCents.ShouldBe(3148);
        order.Pricing.Tip.ShouldBe(new TipResponse("Percent", "15", 150_000, 411, null));
        order.Description.ShouldBe(
            "2 x Large pizza with Pepperoni, Bacon — $28.56\nPromo ILOVEPIZZA: -$2.86\nTax: $1.67\nTip (15%): $4.11\nTotal: $31.48");
    }

    [Fact]
    public async Task Same_key_and_body_replays_the_same_order()
    {
        var client = factory.CreateClient();
        var key = NewKey();
        var before = await OrdersAsync();

        var first = await SubmitAsync(client, P9(), key);
        var second = await SubmitAsync(client, P9(), key);

        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        first.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        second.Headers.ETag!.Tag.ShouldBe("\"1\"");
        second.Headers.Location.ShouldBe(first.Headers.Location);
        (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await OrdersAsync()).ShouldBe(before + 1);
    }

    [Fact]
    public async Task Same_key_with_a_different_body_is_422_reused()
    {
        var client = factory.CreateClient();
        var key = NewKey();
        await SubmitAsync(client, P9(customerName: "Dana"), key);

        var response = await SubmitAsync(client, P9(customerName: "Robin"), key);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadJsonAsync()).GetProperty("type").GetString().ShouldBe(ProblemTypes.IdempotencyKeyReused);
    }

    [Fact]
    public async Task First_attempt_422_then_corrected_body_with_the_same_key_is_201()
    {
        var client = factory.CreateClient();
        var key = NewKey();

        (await SubmitAsync(client, P9() with { LineItems = [Pizza("LARGE", 0)] }, key)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await SubmitAsync(client, P9(), key)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Fifty_parallel_submissions_with_one_key_create_one_order()
    {
        var client = factory.CreateClient();
        var key = NewKey();
        var before = await OrdersAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => SubmitAsync(client, P9(), key)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        responses.Select(r => r.Headers.Location!.ToString()).Distinct().Count().ShouldBe(1);
        responses.Count(r => !r.Headers.Contains("Idempotent-Replayed")).ShouldBe(1);
        (await OrdersAsync()).ShouldBe(before + 1);
    }

    [Fact]
    public async Task Stale_expected_total_is_409_price_changed_with_the_current_quote()
    {
        var before = await OrdersAsync();

        var response = await SubmitAsync(factory.CreateClient(), P9(expectedTotal: 3100), NewKey());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ReadJsonAsync();
        problem.GetProperty("type").GetString().ShouldBe(ProblemTypes.PriceChanged);
        problem.GetProperty("currentQuote").GetProperty("totalCents").GetInt64().ShouldBe(3148);
        (await OrdersAsync()).ShouldBe(before);
    }

    // P13: SAVE5 on a $20.00 cart.
    [Fact]
    public async Task Promotion_that_does_not_apply_is_422_with_the_current_quote()
    {
        var before = await OrdersAsync();
        var request = new SubmitOrderRequest(null, null, [Pizza("SMALL", 4)], ["SAVE5"], null, 2130, null);

        var response = await SubmitAsync(factory.CreateClient(), request, NewKey());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadJsonAsync();
        problem.GetProperty("type").GetString().ShouldBe(ProblemTypes.PromotionNotApplied);
        problem.GetProperty("errors").GetProperty("promoCodes[0]")[0].GetProperty("message").GetString()
            .ShouldBe("SAVE5 requires an order subtotal over $20.00 (yours is $20.00).");
        problem.GetProperty("currentQuote").GetProperty("canSubmit").GetBoolean().ShouldBeFalse();
        (await OrdersAsync()).ShouldBe(before);
    }

    // P24: the raw draft with both codes is refused at the second code; its normalized draft is accepted.
    [Fact]
    public async Task Submit_with_conflicting_codes_is_422_at_second_code_path()
    {
        var client = factory.CreateClient();

        var raw = await SubmitAsync(client, P9(codes: ["ILOVEPIZZA", "SAVE5"]), NewKey());

        raw.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await raw.ReadJsonAsync();
        problem.GetProperty("type").GetString().ShouldBe(ProblemTypes.PromotionNotApplied);
        problem.FieldErrors().ShouldBe([("promoCodes[1]", "Rejected")]);
        (await SubmitAsync(client, P9(codes: ["ILOVEPIZZA"]), NewKey())).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // PR12: a duplicate code collapses and is never an error.
    [Fact]
    public async Task Submit_with_duplicate_code_collapses_and_succeeds()
    {
        var request = new SubmitOrderRequest(null, null, [Pizza("MEDIUM", 3)], ["SAVE5", "save5"], null, 1704, null);

        var response = await SubmitAsync(factory.CreateClient(), request, NewKey());

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var order = await response.ReadAsync<OrderResponse>();
        (await CountAsync($"SELECT COUNT(*) FROM order_promotion WHERE order_id = '{order.OrderId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Missing_expected_total_and_empty_order_are_validation_errors()
    {
        var client = factory.CreateClient();

        var noTotal = await SubmitAsync(client, P9(expectedTotal: null), NewKey());
        noTotal.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await noTotal.ReadJsonAsync()).FieldErrors().ShouldBe([("expectedTotalCents", "REQUIRED")]);

        var empty = await SubmitAsync(client, new SubmitOrderRequest(null, null, [], null, null, 0, null), NewKey());
        empty.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await empty.ReadJsonAsync()).FieldErrors().ShouldBe([("lineItems", "ORDER_EMPTY")]);

        var longName = await SubmitAsync(client, P9(customerName: new string('x', 51)), NewKey());
        (await longName.ReadJsonAsync()).FieldErrors().ShouldBe([("customerName", "CUSTOMER_NAME_INVALID")]);
    }

    [Fact]
    public async Task Submit_persists_the_full_snapshot()
    {
        var response = await SubmitAsync(factory.CreateClient(), P9(), NewKey());
        var id = (await response.ReadAsync<OrderResponse>()).OrderId;
        string For(string table) => $"SELECT COUNT(*) FROM {table} WHERE order_id = '{id}'";

        (await CountAsync(For("order_line"))).ShouldBe(1);
        (await CountAsync($"SELECT COUNT(*) FROM order_line_topping t JOIN order_line l ON l.id = t.order_line_id WHERE l.order_id = '{id}'")).ShouldBe(2);
        (await CountAsync(For("order_promotion"))).ShouldBe(1);
        (await CountAsync($"SELECT SUM(a.amount_cents) FROM order_discount_allocation a JOIN order_promotion p ON p.id = a.order_promotion_id WHERE p.order_id = '{id}'")).ShouldBe(286);
        (await CountAsync(For("order_tax_line"))).ShouldBe(1);
        (await CountAsync(For("order_status_history") + " AND event_type = 'Created' AND from_status IS NULL AND to_status = 'NotStarted' AND order_version = 1")).ShouldBe(1);
        (await CountAsync(For("idempotency_record") + " AND operation = 'CreateOrder' AND response_status = 201")).ShouldBe(1);
    }

    [Fact]
    public async Task Header_tax_equals_sum_of_tax_lines()
    {
        var response = await SubmitAsync(factory.CreateClient(), P9(), NewKey());
        var id = (await response.ReadAsync<OrderResponse>()).OrderId;

        var header = await CountAsync($"SELECT tax_cents FROM customer_order WHERE id = '{id}'");
        (await CountAsync($"SELECT SUM(tax_cents) FROM order_tax_line WHERE order_id = '{id}'")).ShouldBe(header);
        header.ShouldBe(167);
    }

    [Fact]
    public async Task GET_order_status_and_description()
    {
        var client = factory.CreateClient();
        var created = await (await SubmitAsync(client, P9(), NewKey())).ReadAsync<OrderResponse>();

        var get = await client.GetAsync($"/api/v1/orders/{created.OrderId}");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        get.Headers.ETag!.Tag.ShouldBe("\"1\"");
        (await get.ReadAsync<OrderResponse>()).ShouldBeEquivalentTo(created);

        var status = await client.GetAsync($"/api/v1/orders/{created.OrderId}/status");
        status.Headers.ETag!.Tag.ShouldBe("\"1\"");
        var body = await status.ReadAsync<OrderStatusResponse>();
        (body.Status, body.StatusDisplay, body.Version).ShouldBe(("NotStarted", "Not Started", 1));

        var description = await client.GetAsync($"/api/v1/orders/{created.OrderId}/description");
        description.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        (await description.Content.ReadAsStringAsync()).ShouldBe(created.Description);
    }

    [Theory]
    [InlineData("/api/v1/orders/0192f3a4-5b6c-7d8e-9f01-23456789abcd", true)]
    [InlineData("/api/v1/orders/not-a-guid", false)]
    [InlineData("/api/v1/orders/0192f3a4-5b6c-7d8e-9f01-23456789abcd/status", true)]
    public async Task Unknown_or_malformed_order_id_is_404(string path, bool problem)
    {
        var response = await factory.CreateClient().GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        if (problem)
        {
            (await response.ReadJsonAsync()).GetProperty("type").GetString().ShouldBe(ProblemTypes.OrderNotFound);
        }
    }
}
