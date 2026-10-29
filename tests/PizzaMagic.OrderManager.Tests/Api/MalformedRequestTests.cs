using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PizzaMagic.Contracts.Api;
using static PizzaMagic.OrderManager.Tests.Api.OrderApiTests;

namespace PizzaMagic.OrderManager.Tests.Api;

/// <summary>
/// Malformed and oversized requests: each is a 4xx problem with a field path, never a 500, and an oversized list gets
/// one error rather than one per entry, so a response is never many times larger than its request.
/// </summary>
public class MalformedRequestTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<HttpResponseMessage> QuoteAsync(string json) =>
        await factory.CreateClient().PostAsync("/api/v1/quotes", Json(json));

    private async Task<HttpResponseMessage> SubmitJsonAsync(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = Json(json) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await factory.CreateClient().SendAsync(request);
    }

    private static async Task ShouldBe422(HttpResponseMessage response, params (string Path, string Code)[] errors)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadJsonAsync()).FieldErrors().ShouldBe(errors, ignoreOrder: true);
    }

    [Fact]
    public async Task Null_line_is_a_field_error_not_a_500()
    {
        const string quote = """{"lineItems":[null],"promoCodes":[],"tip":null}""";
        const string submit = """{"lineItems":[null],"promoCodes":[],"tip":null,"expectedTotalCents":0}""";

        await ShouldBe422(await QuoteAsync(quote), ("lineItems[0]", "INVALID_LINE"));
        await ShouldBe422(await SubmitJsonAsync(submit), ("lineItems[0]", "INVALID_LINE"));
    }

    [Fact]
    public async Task Null_topping_is_a_field_error_not_a_500()
    {
        const string quote = """{"lineItems":[{"kind":"Pizza","sizeCode":"SMALL","quantity":1,"toppings":[{"code":"PEPPERONI"},null]}]}""";
        const string submit = """{"lineItems":[{"kind":"Pizza","sizeCode":"SMALL","quantity":1,"toppings":[{"code":"PEPPERONI"},null]}],"expectedTotalCents":0}""";

        await ShouldBe422(await QuoteAsync(quote), ("lineItems[0].toppings[1]", "INVALID_TOPPING"));
        await ShouldBe422(await SubmitJsonAsync(submit), ("lineItems[0].toppings[1]", "INVALID_TOPPING"));
    }

    [Fact]
    public async Task Huge_tip_is_out_of_range_not_a_500()
    {
        var response = await QuoteAsync("""{"lineItems":[{"kind":"Pizza","sizeCode":"SMALL","quantity":1}],"tip":{"mode":"Amount","value":"1000000000000000000000000000"}}""");

        await ShouldBe422(response, ("tip.value", "TIP_OUT_OF_RANGE"));
    }

    // productCode belongs to add-on lines (designed, not built); on a pizza line it is refused, not silently ignored.
    [Fact]
    public async Task Product_code_on_a_pizza_line_is_refused()
    {
        var response = await QuoteAsync("""{"lineItems":[{"kind":"Pizza","sizeCode":"SMALL","quantity":1,"productCode":"JUNK"}]}""");

        await ShouldBe422(response, ("lineItems[0].productCode", "FIELD_NOT_ALLOWED"));
    }

    // Too many lines is one error, not one per line, so a huge request can't produce an even larger response.
    [Fact]
    public async Task Too_many_lines_is_one_error()
    {
        var lines = string.Join(",", Enumerable.Repeat("""{"kind":"Pizza","sizeCode":"NOPE","quantity":0}""", 500));

        var response = await QuoteAsync($$"""{"lineItems":[{{lines}}]}""");

        await ShouldBe422(response, ("lineItems", "TOO_MANY_LINES"));
    }

    [Fact]
    public async Task Too_many_toppings_skips_the_per_topping_errors()
    {
        var toppings = string.Join(",", Enumerable.Repeat("""{"code":"NOPE"}""", 200));

        var response = await QuoteAsync($$"""{"lineItems":[{"kind":"Pizza","sizeCode":"SMALL","quantity":1,"toppings":[{{toppings}}]}]}""");

        await ShouldBe422(response, ("lineItems[0].toppings", "TOO_MANY_TOPPINGS"));
    }

    // Duplicates collapse, but the raw list is capped too: thousands of copies of a code can't return an outcome each.
    [Fact]
    public async Task Too_many_promo_code_entries_is_one_error()
    {
        var codes = string.Join(",", Enumerable.Repeat("\"ILOVEPIZZA\"", 1000));

        var response = await QuoteAsync($$"""{"lineItems":[{"kind":"Pizza","sizeCode":"SMALL","quantity":1}],"promoCodes":[{{codes}}]}""");

        await ShouldBe422(response, ("promoCodes", "TOO_MANY_PROMO_CODES"));
    }

    [Fact]
    public async Task Unknown_route_is_a_problem_document()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/orders/not-a-guid");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    // An unexpected exception is a problem document too, so clients always get a body to read.
    [Fact]
    public async Task Unexpected_exception_is_a_problem_document()
    {
        var throwing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddTransient<IStartupFilter, ThrowingRoute>()));

        var response = await throwing.CreateClient().GetAsync("/test/throws");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("boom");   // no internals leak
    }

    [Fact]
    public async Task Advancing_a_delivered_order_says_it_is_already_delivered()
    {
        var client = factory.CreateClient();
        var created = await SubmitAsync(client, P9(), Guid.NewGuid().ToString());
        var id = (await created.ReadAsync<OrderResponse>()).OrderId;
        foreach (var step in new[] { "NotStarted", "Started", "ReadyForPickup" })
        {
            (await client.PostAsJsonAsync($"/api/v1/kitchen/orders/{id}/advance", new AdvanceOrderRequest(step))).EnsureSuccessStatusCode();
        }

        var response = await client.PostAsJsonAsync($"/api/v1/kitchen/orders/{id}/advance", new AdvanceOrderRequest("Delivered"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadJsonAsync()).GetProperty("detail").GetString().ShouldBe("Order is already Delivered.");
    }

    /// <summary>A route that throws, inside the service's own pipeline.</summary>
    private sealed class ThrowingRoute : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/test/throws", branch => branch.Run(_ => throw new InvalidOperationException("boom")));
        };
    }
}
