using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;
using static PizzaMagic.Web.Tests.Clients.StubHandler;

namespace PizzaMagic.Web.Tests.Clients;

/// <summary>The typed client behind the real resilience pipeline, with a stub as the network.</summary>
public class OrderManagerClientTests
{
    private const string OrderJson = """
        { "orderId": "0192f3a4-5b6c-7d8e-9f01-23456789abcd", "restaurantId": 1, "channel": "ONLINE", "customerName": null,
          "status": "NotStarted", "statusDisplay": "Not Started", "version": 1,
          "createdAt": "2026-10-27T19:00:00.000Z", "statusChangedAt": "2026-10-27T19:00:00.000Z",
          "createdAtLocal": null, "statusChangedAtLocal": null, "allowedActions": ["update", "cancel"],
          "pricing": { "lineItems": [], "subtotalCents": 2856, "promotions": [], "discountCents": 286, "taxableCents": 2570,
                       "taxRounding": { "method": "HALF_UP", "scope": "COMBINED" }, "taxLines": [], "taxCents": 167, "billCents": 2737,
                       "tip": { "mode": "Percent", "input": "15", "ratePpm": 150000, "tipCents": 411, "effectiveRateBasisPoints": null },
                       "totalCents": 3148 },
          "description": "…" }
        """;

    private static readonly Guid OrderId = Guid.Parse("0192f3a4-5b6c-7d8e-9f01-23456789abcd");

    private static readonly SubmitOrderRequest Submit = new(null, null,
        [new LineItemRequest("Pizza", "LARGE", 2, [new ToppingRequest("PEPPERONI"), new ToppingRequest("BACON")])],
        ["ILOVEPIZZA"], new TipRequest("Percent", "15"), 3148, "Dana");

    private static IOrderManagerClient ClientFor(StubHandler stub)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OrderManager:BaseUrl"] = "http://ordermanager.test",
                ["OrderManager:RetryBaseDelayMilliseconds"] = "0",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrderManagerClient(configuration, primaryHandler: () => stub);
        return services.BuildServiceProvider().GetRequiredService<IOrderManagerClient>();
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Created(bool replayed = false) =>
        Respond(HttpStatusCode.Created, OrderJson, configure: r =>
        {
            r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"1\"");
            if (replayed)
            {
                r.Headers.Add("Idempotent-Replayed", "true");
            }
        });

    // The 503s are retried with the same Idempotency-Key and byte-identical body, so the server can replay.
    [Fact]
    public async Task Submit_retry_reuses_key_and_body()
    {
        var stub = new StubHandler(Respond(HttpStatusCode.ServiceUnavailable), Respond(HttpStatusCode.ServiceUnavailable), Created(replayed: true));

        var result = await ClientFor(stub).SubmitAsync(Submit, "key-123", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.OrderId.ShouldBe(OrderId.ToString());
        result.Value.Pricing.TotalCents.ShouldBe(3148);
        result.ETag.ShouldBe("\"1\"");
        result.Replayed.ShouldBeTrue();
        stub.Requests.Count.ShouldBe(3);
        stub.Requests.Select(r => r.Headers["Idempotency-Key"]).Distinct().ShouldBe(["key-123"]);
        stub.Requests.Select(r => r.Body).Distinct().Count().ShouldBe(1);
        stub.Requests[0].Method.ShouldBe(HttpMethod.Post);
        stub.Requests[0].PathAndQuery.ShouldBe("/api/v1/orders");
        stub.Requests[0].Body!.ShouldContain("\"expectedTotalCents\":3148");
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "validation")]
    [InlineData(HttpStatusCode.Conflict, "price-changed")]
    [InlineData(HttpStatusCode.BadRequest, "idempotency-key-missing")]
    public async Task Client_errors_are_never_retried(HttpStatusCode status, string type)
    {
        var stub = new StubHandler(Problem(status, type));

        var result = await ClientFor(stub).SubmitAsync(Submit, "key-123", CancellationToken.None);

        stub.Requests.Count.ShouldBe(1);
        result.Kind.ShouldBe(ApiResultKind.Problem);
        result.Problem!.Type.ShouldBe(ProblemTypes.Base + type);
        result.Problem.Status.ShouldBe((int)status);
    }

    [Fact]
    public async Task Maps_problem_types_errors_and_extensions()
    {
        var stub = new StubHandler(Problem(HttpStatusCode.UnprocessableEntity, "promotion-not-applied",
            """, "errors": { "promoCodes[0]": [ { "code": "NotEligible", "message": "SAVE5 requires an order subtotal over $20.00 (yours is $20.00)." } ] }, "currentQuote": { "totalCents": 2130 }"""));

        var result = await ClientFor(stub).SubmitAsync(Submit, "key-123", CancellationToken.None);

        var problem = result.Problem!;
        problem.Type.ShouldBe(ProblemTypes.PromotionNotApplied);
        problem.Errors["promoCodes[0]"].Single().ShouldBe(
            new FieldErrorResponse("NotEligible", "SAVE5 requires an order subtotal over $20.00 (yours is $20.00)."));
        problem.Extension("currentQuote")!.Value.GetProperty("totalCents").GetInt64().ShouldBe(2130);
        problem.Extension("currentVersion").ShouldBeNull();
    }

    [Fact]
    public async Task Version_mismatch_and_not_found_are_problems()
    {
        var stale = await ClientFor(new StubHandler(Problem(HttpStatusCode.PreconditionFailed, "version-mismatch", """, "currentVersion": 3"""))).CancelAsync(OrderId, CancellationToken.None);
        stale.Problem!.Type.ShouldBe(ProblemTypes.VersionMismatch);
        stale.Problem.Extension("currentVersion")!.Value.GetInt32().ShouldBe(3);

        var missing = await ClientFor(new StubHandler(Problem(HttpStatusCode.NotFound, "order-not-found"))).GetOrderAsync(OrderId, CancellationToken.None);
        missing.Problem!.Type.ShouldBe(ProblemTypes.OrderNotFound);
    }

    // Expected failures never throw to controllers: they become Unavailable after the retry budget.
    [Fact]
    public async Task Unavailable_never_throws()
    {
        var down = new StubHandler(Throw(new HttpRequestException("connection refused")));
        var result = await ClientFor(down).SubmitAsync(Submit, "key-123", CancellationToken.None);

        result.Kind.ShouldBe(ApiResultKind.Unavailable);
        result.UnavailableReason.ShouldNotBeNullOrEmpty();
        down.Requests.Count.ShouldBe(4);   // the first attempt plus 3 retries

        var failing = new StubHandler(Respond(HttpStatusCode.InternalServerError));
        (await ClientFor(failing).GetMenuAsync(null, CancellationToken.None)).Kind.ShouldBe(ApiResultKind.Unavailable);

        var garbage = new StubHandler(Respond(HttpStatusCode.OK, "not json"));
        (await ClientFor(garbage).GetMenuAsync(null, CancellationToken.None)).Kind.ShouldBe(ApiResultKind.Unavailable);
    }

    // Quotes have a tighter budget: one retry, then Unavailable (a stale total beats a hung page).
    [Fact]
    public async Task Quotes_retry_once()
    {
        var stub = new StubHandler(Respond(HttpStatusCode.ServiceUnavailable));

        var result = await ClientFor(stub).QuoteAsync(new QuoteRequest(null, null, [], null, null), CancellationToken.None);

        result.Kind.ShouldBe(ApiResultKind.Unavailable);
        stub.Requests.Count.ShouldBe(2);
        stub.Requests[0].PathAndQuery.ShouldBe("/api/v1/quotes");
    }

    [Fact]
    public async Task Update_sends_If_Match_and_idempotency_key()
    {
        var stub = new StubHandler(Respond(HttpStatusCode.OK, OrderJson, configure: r => r.Headers.ETag = new("\"2\"")));
        var update = new UpdateOrderRequest(Submit.LineItems, Submit.PromoCodes, Submit.Tip, 3148, null);

        var result = await ClientFor(stub).UpdateAsync(OrderId, 1, update, "edit-key", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.ETag.ShouldBe("\"2\"");
        var request = stub.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Put);
        request.PathAndQuery.ShouldBe($"/api/v1/orders/{OrderId}");
        request.Headers["If-Match"].ShouldBe("\"1\"");
        request.Headers["Idempotency-Key"].ShouldBe("edit-key");
        request.Body!.ShouldNotContain("restaurantId");
    }

    [Fact]
    public async Task Routes_and_query_strings()
    {
        var stub = new StubHandler(Respond(HttpStatusCode.OK, "{}"));
        var client = ClientFor(stub);

        await client.GetMenuAsync(2, CancellationToken.None);
        await client.GetMenuAsync(null, CancellationToken.None);
        await client.GetStatusAsync(OrderId, CancellationToken.None);
        await client.CancelAsync(OrderId, CancellationToken.None);

        stub.Requests.Select(r => (r.Method.Method, r.PathAndQuery)).ShouldBe(
        [
            ("GET", "/api/v1/menu?restaurantId=2"),
            ("GET", "/api/v1/menu"),
            ("GET", $"/api/v1/orders/{OrderId}/status"),
            ("POST", $"/api/v1/orders/{OrderId}/cancel"),
        ]);
    }

    [Fact]
    public async Task Kitchen_routes_and_body()
    {
        var stub = new StubHandler(
            Respond(HttpStatusCode.OK, """{ "restaurantId": 2, "orders": [] }"""),
            Respond(HttpStatusCode.OK, """{ "restaurantId": 1, "orders": [] }"""),
            Respond(HttpStatusCode.OK, $$"""{ "orderId": "{{OrderId}}", "previousStatus": "NotStarted", "status": "Started", "version": 2, "alreadyApplied": false, "lineItems": [], "description": "" }"""));
        var client = ClientFor(stub);

        var queue = await client.GetKitchenQueueAsync(2, CancellationToken.None);
        await client.GetKitchenQueueAsync(null, CancellationToken.None);
        var advanced = await client.AdvanceAsync(OrderId, "NotStarted", CancellationToken.None);

        queue.Value!.RestaurantId.ShouldBe(2);
        advanced.Value!.Status.ShouldBe("Started");
        stub.Requests.Select(r => (r.Method.Method, r.PathAndQuery)).ShouldBe(
        [
            ("GET", "/api/v1/kitchen/orders?restaurantId=2"),
            ("GET", "/api/v1/kitchen/orders"),
            ("POST", $"/api/v1/kitchen/orders/{OrderId}/advance"),
        ]);
        stub.Requests[2].Body.ShouldBe("""{"expectedStatus":"NotStarted"}""");
    }

    [Fact]
    public async Task Caller_cancellation_is_not_unavailable()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            ClientFor(new StubHandler(Respond(HttpStatusCode.OK, "{}"))).GetMenuAsync(null, cancelled.Token));
    }
}
