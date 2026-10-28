using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;

namespace PizzaMagic.Web.Tests.Builder;

public class BuilderTests
{
    private static object Draft(long revision, string size, params string[] codes) => new
    {
        revision,
        lineItems = new[] { new { kind = "Pizza", sizeCode = size, quantity = 1, toppings = new[] { new { code = "PEPPERONI" } } } },
        promoCodes = codes,
        tip = (object?)null,
    };

    // Nothing is hard-coded: a size that only exists in the service's menu is rendered with its price.
    [Fact]
    public async Task Builder_renders_menu_from_service()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Menu = ApiResult<MenuResponse>.Success(Menus.Spec(new MenuSizeResponse("PARTY", "Party", 2500)), null, false);

        var response = await factory.CreateClient().GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Party");
        html.ShouldContain("$25.00");
        html.ShouldContain("Pepperoni");
        html.ShouldContain("Premium");
        html.ShouldContain(">15%<");                       // tip presets from the ordering policy
        html.ShouldContain("data-menu=");
        html.ShouldContain("id=\"review\"");
        html.ShouldContain("disabled");                    // Review starts disabled until a quote says canSubmit
        html.ShouldContain("Pizza Magic!");
    }

    [Fact]
    public async Task Builder_says_so_when_the_menu_is_unavailable()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Menu = ApiResult<MenuResponse>.Unavailable("connection refused");

        var html = await factory.CreateClient().GetStringAsync("/");

        html.ShouldContain("The menu is unavailable right now");
    }

    [Fact]
    public async Task Quote_proxy_requires_antiforgery()
    {
        await using var factory = new WebFactory();
        var client = factory.CreateClient();
        await client.GetAsync("/");

        var response = await WebFactory.PostQuoteAsync(client, "not-a-token", Draft(1, "SMALL"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.OrderManager.QuoteRequests.ShouldBeEmpty();
    }

    // The newest revision is saved to the Session cart; an older one is still quoted but never overwrites it.
    [Fact]
    public async Task Quote_proxy_saves_newer_revision_and_returns_json()
    {
        await using var factory = new WebFactory();
        var (client, token) = await factory.BrowserAsync();

        var newer = await WebFactory.PostQuoteAsync(client, token, Draft(2, "SMALL"));
        var older = await WebFactory.PostQuoteAsync(client, token, Draft(1, "LARGE"));

        newer.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await newer.Content.ReadFromJsonAsync<QuoteResponse>(WebFactory.Json))!.TotalCents.ShouldBe(533);
        older.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.OrderManager.QuoteRequests.Select(q => q.LineItems!.Single().SizeCode).ShouldBe(["SMALL", "LARGE"]);

        var cart = WebFactory.RenderedCart(await client.GetStringAsync("/"));
        cart.GetProperty("revision").GetInt64().ShouldBe(2);
        cart.GetProperty("lineItems")[0].GetProperty("sizeCode").GetString().ShouldBe("SMALL");
    }

    [Fact]
    public async Task Quote_proxy_uses_the_configured_restaurant_and_no_channel()
    {
        await using var factory = new WebFactory();
        var (client, token) = await factory.BrowserAsync();

        await WebFactory.PostQuoteAsync(client, token, Draft(1, "SMALL"));

        var sent = factory.OrderManager.QuoteRequests.Single();
        sent.RestaurantId.ShouldBeNull();   // empty OrderManager:RestaurantId = the service's default restaurant
        sent.LineItems!.Single().Toppings!.Single().Code.ShouldBe("PEPPERONI");
    }

    [Fact]
    public async Task Quote_proxy_503_message()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Unavailable("timeout");
        var (client, token) = await factory.BrowserAsync();

        var response = await WebFactory.PostQuoteAsync(client, token, Draft(1, "SMALL"));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()
            .ShouldBe("Total unavailable — retrying");
    }

    [Fact]
    public async Task Quote_proxy_passes_field_errors_through()
    {
        await using var factory = new WebFactory();
        var errors = new Dictionary<string, IReadOnlyList<FieldErrorResponse>>
        {
            ["lineItems[0].quantity"] = [new FieldErrorResponse("QUANTITY_OUT_OF_RANGE", "Quantity must be between 1 and 20.")],
        };
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Failed(
            new ApiProblem(ProblemTypes.Validation, 422, "The order has invalid fields.", null, errors, JsonDocument.Parse("{}").RootElement));
        var (client, token) = await factory.BrowserAsync();

        var response = await WebFactory.PostQuoteAsync(client, token, Draft(1, "SMALL"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").GetProperty("lineItems[0].quantity")[0].GetProperty("message").GetString()
            .ShouldBe("Quantity must be between 1 and 20.");
    }

    // The server's normalized promo list replaces the cart's: a rejected conflicting code is dropped from the cart.
    [Fact]
    public async Task Promo_list_replaced_by_normalized_codes()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Success(Quotes.Simple(3148, normalizedCodes: ["ILOVEPIZZA"]), null, false);
        var (client, token) = await factory.BrowserAsync();

        await WebFactory.PostQuoteAsync(client, token, Draft(1, "LARGE", "ILOVEPIZZA", "SAVE5"));

        var cart = WebFactory.RenderedCart(await client.GetStringAsync("/"));
        cart.GetProperty("promoCodes").EnumerateArray().Select(c => c.GetString()).ShouldBe(["ILOVEPIZZA"]);
    }
}
