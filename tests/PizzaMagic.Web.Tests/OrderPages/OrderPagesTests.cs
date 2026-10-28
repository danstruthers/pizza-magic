using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;
using PizzaMagic.Web.Tests.Builder;

namespace PizzaMagic.Web.Tests.OrderPages;

/// <summary>Order details, status polling, edit through Review, cancel, recent orders and lookup (plan sections 6.1, 6.4).</summary>
public class OrderPagesTests
{
    private static readonly string DetailsUrl = $"/Orders/{SampleOrders.Id}";

    private static async Task<(HttpClient Client, string Html)> DetailsAsync(WebFactory factory)
    {
        var (client, _) = await factory.BrowserAsync();
        return (client, await client.GetStringAsync(DetailsUrl));
    }

    /// <summary>Opens the order, presses Edit, and returns the Review page for the loaded cart.</summary>
    private static async Task<(HttpClient Client, (string Html, string? Ticket, string Token) Review)> EditAndReviewAsync(WebFactory factory)
    {
        var (client, details) = await DetailsAsync(factory);
        var edit = await WebFactory.PostFormAsync(client, $"{DetailsUrl}/Edit", WebFactory.Token(details));
        edit.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        edit.Headers.Location!.OriginalString.ShouldBe("/");
        return (client, await WebFactory.ReviewAsync(client));
    }

    [Fact]
    public async Task Details_shows_the_order_from_its_snapshot()
    {
        await using var factory = new WebFactory();

        var (_, raw) = await DetailsAsync(factory);
        var html = WebFactory.Text(raw);

        html.ShouldContain(SampleOrders.Id.ToString());
        html.ShouldContain("Not Started");
        html.ShouldContain("1 x Large pizza with Pepperoni");
        html.ShouldContain("$13.80");
        html.ShouldContain("12:05 PM PDT");                       // status changed, in the restaurant's local time
        html.ShouldContain($"data-status-url=\"{DetailsUrl}/Status\"");
    }

    [Fact]
    public async Task Details_buttons_follow_allowedActions()
    {
        await using var factory = new WebFactory();
        var (_, notStarted) = await DetailsAsync(factory);
        factory.OrderManager.Order = id => ApiResult<OrderResponse>.Success(SampleOrders.Order("Started", 2, id), "\"2\"", false);
        var (_, started) = await DetailsAsync(factory);

        notStarted.ShouldContain($"action=\"{DetailsUrl}/Edit\"");
        notStarted.ShouldContain($"action=\"{DetailsUrl}/Cancel\"");
        started.ShouldNotContain($"action=\"{DetailsUrl}/Edit\"");
        started.ShouldNotContain($"action=\"{DetailsUrl}/Cancel\"");
        WebFactory.Text(started).ShouldContain("Started");
    }

    [Fact]
    public async Task Details_404_for_an_unknown_order()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Order = _ => ApiResult<OrderResponse>.Failed(Problems.Of(ProblemTypes.OrderNotFound, 404));
        var (client, _) = await factory.BrowserAsync();

        var response = await client.GetAsync(DetailsUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        WebFactory.Text(await response.Content.ReadAsStringAsync()).ShouldContain("We couldn't find that order.");
    }

    [Fact]
    public async Task Status_proxy_returns_json()
    {
        await using var factory = new WebFactory();
        var (client, _) = await factory.BrowserAsync();

        var status = await client.GetFromJsonAsync<JsonElement>($"{DetailsUrl}/Status");

        status.GetProperty("status").GetString().ShouldBe("Started");
        status.GetProperty("statusDisplay").GetString().ShouldBe("Started");
        status.GetProperty("allowedActions").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Status_proxy_503_when_unavailable()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Status = _ => ApiResult<OrderStatusResponse>.Unavailable("timeout");
        var (client, _) = await factory.BrowserAsync();

        var response = await client.GetAsync($"{DetailsUrl}/Status");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    // Edit loads the order into the cart; Review then builds an update ticket, and Place sends PUT with If-Match.
    [Fact]
    public async Task Edit_review_ticket_submits_put_with_if_match()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Order = id => ApiResult<OrderResponse>.Success(SampleOrders.Order(version: 3, id: id), "\"3\"", false);
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Success(
            Quotes.Simple(1500, normalizedLines: [Quotes.NormalizedLarge]), null, false);

        var (client, review) = await EditAndReviewAsync(factory);
        review.Html.ShouldContain("Update order — $15.00");
        var response = await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(DetailsUrl);
        factory.OrderManager.SubmitRequests.ShouldBeEmpty();
        var (id, version, request, _) = factory.OrderManager.UpdateRequests.ShouldHaveSingleItem();
        id.ShouldBe(SampleOrders.Id);
        version.ShouldBe(3);
        request.ExpectedTotalCents.ShouldBe(1500);
        WebFactory.RenderedCart(await client.GetStringAsync("/")).GetProperty("editingOrderId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Edit_loads_the_order_into_the_builder()
    {
        await using var factory = new WebFactory();
        var (client, details) = await DetailsAsync(factory);

        await WebFactory.PostFormAsync(client, $"{DetailsUrl}/Edit", WebFactory.Token(details));
        var builder = await client.GetStringAsync("/");

        var cart = WebFactory.RenderedCart(builder);
        cart.GetProperty("editingOrderId").GetString().ShouldBe(SampleOrders.Id.ToString());
        cart.GetProperty("editingVersion").GetInt32().ShouldBe(1);
        var line = cart.GetProperty("lineItems")[0];
        line.GetProperty("sizeCode").GetString().ShouldBe("LARGE");
        line.GetProperty("crustCode").GetString().ShouldBe("HAND_TOSSED");
        line.GetProperty("toppings")[0].GetProperty("code").GetString().ShouldBe("PEPPERONI");
        WebFactory.Text(builder).ShouldContain("You're changing order");
    }

    [Fact]
    public async Task Start_over_leaves_edit_mode()
    {
        await using var factory = new WebFactory();
        var (client, details) = await DetailsAsync(factory);
        await WebFactory.PostFormAsync(client, $"{DetailsUrl}/Edit", WebFactory.Token(details));
        var builder = await client.GetStringAsync("/");

        var response = await WebFactory.PostFormAsync(client, "/Builder/StartOver", WebFactory.Token(builder));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var cart = WebFactory.RenderedCart(await client.GetStringAsync("/"));
        cart.GetProperty("editingOrderId").ValueKind.ShouldBe(JsonValueKind.Null);
        cart.GetProperty("lineItems").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Update_412_reloads()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Success(Quotes.Simple(1500, normalizedLines: [Quotes.NormalizedLarge]), null, false);
        factory.OrderManager.Update = (_, _, _, _) => ApiResult<OrderResponse>.Failed(Problems.Of(ProblemTypes.VersionMismatch, 412));
        var (client, review) = await EditAndReviewAsync(factory);

        var response = await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(DetailsUrl);
        WebFactory.Text(await client.GetStringAsync(DetailsUrl)).ShouldContain("This order was changed in another window. Here's the latest version.");
        WebFactory.RenderedCart(await client.GetStringAsync("/")).GetProperty("editingOrderId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Update_409_says_the_kitchen_started()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Success(Quotes.Simple(1500, normalizedLines: [Quotes.NormalizedLarge]), null, false);
        factory.OrderManager.Update = (_, _, _, _) => ApiResult<OrderResponse>.Failed(Problems.Of(ProblemTypes.OrderNotModifiable, 409));
        var (client, review) = await EditAndReviewAsync(factory);

        var response = await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);

        response.Headers.Location!.OriginalString.ShouldBe(DetailsUrl);
        WebFactory.Text(await client.GetStringAsync(DetailsUrl))
            .ShouldContain("The kitchen has already started your order, so it can't be changed or cancelled.");
    }

    [Fact]
    public async Task Cancel_success_message()
    {
        await using var factory = new WebFactory();
        var (client, details) = await DetailsAsync(factory);

        var response = await WebFactory.PostFormAsync(client, $"{DetailsUrl}/Cancel", WebFactory.Token(details));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(DetailsUrl);
        factory.OrderManager.CancelRequests.ShouldBe([SampleOrders.Id]);
        WebFactory.Text(await client.GetStringAsync(DetailsUrl)).ShouldContain("Your order was cancelled.");
    }

    [Fact]
    public async Task Cancel_409_message()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Cancel = _ => ApiResult<OrderResponse>.Failed(Problems.Of(ProblemTypes.OrderNotCancellable, 409));
        var (client, details) = await DetailsAsync(factory);

        var response = await WebFactory.PostFormAsync(client, $"{DetailsUrl}/Cancel", WebFactory.Token(details));

        response.Headers.Location!.OriginalString.ShouldBe(DetailsUrl);
        WebFactory.Text(await client.GetStringAsync(DetailsUrl))
            .ShouldContain("The kitchen has already started your order, so it can't be changed or cancelled.");
    }

    [Fact]
    public async Task Cancel_requires_antiforgery()
    {
        await using var factory = new WebFactory();
        var (client, _) = await DetailsAsync(factory);

        var response = await WebFactory.PostFormAsync(client, $"{DetailsUrl}/Cancel", "not-a-token");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.OrderManager.CancelRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Recent_orders_are_empty_for_a_new_browser()
    {
        await using var factory = new WebFactory();
        var (client, _) = await factory.BrowserAsync();

        WebFactory.Text(await client.GetStringAsync("/Orders")).ShouldContain("No recent orders in this browser.");
    }

    /// <summary>A browser that has placed the sample order (1 Large Pepperoni, $13.80) through Review and Place.</summary>
    private static async Task<HttpClient> PlacedAsync(WebFactory factory)
    {
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Success(Quotes.Simple(1380, normalizedLines: [Quotes.NormalizedLarge]), null, false);
        var (client, token) = await factory.BrowserAsync();
        await WebFactory.PostQuoteAsync(client, token, WebFactory.Draft(1, "LARGE"));
        var review = await WebFactory.ReviewAsync(client);
        (await WebFactory.PlaceAsync(client, review.Token, review.Ticket!)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return client;
    }

    // Each recent order shows its current status, when it was placed (restaurant time), what's on it and the total.
    [Fact]
    public async Task Recent_orders_show_status_date_and_summary()
    {
        await using var factory = new WebFactory();
        var client = await PlacedAsync(factory);
        factory.OrderManager.Order = id => ApiResult<OrderResponse>.Success(SampleOrders.Order("Started", 2, id), "\"2\"", false);

        var raw = await client.GetStringAsync("/Orders");
        var html = WebFactory.Text(raw);

        raw.ShouldContain($"href=\"{DetailsUrl}\"");
        html.ShouldContain("Started");
        html.ShouldContain("Oct 28, 2026, 12:00 PM PDT");
        html.ShouldContain("1 x Large pizza with Pepperoni");
        html.ShouldContain("$13.80");
    }

    // The list never breaks because one order can't be read: that order shows its number and a short note.
    [Fact]
    public async Task Recent_orders_list_an_order_whose_details_are_unavailable()
    {
        await using var factory = new WebFactory();
        var client = await PlacedAsync(factory);

        factory.OrderManager.Order = _ => ApiResult<OrderResponse>.Unavailable("timeout");
        var unavailable = WebFactory.Text(await client.GetStringAsync("/Orders"));
        factory.OrderManager.Order = _ => ApiResult<OrderResponse>.Failed(Problems.Of(ProblemTypes.OrderNotFound, 404));
        var missing = WebFactory.Text(await client.GetStringAsync("/Orders"));

        unavailable.ShouldContain(SampleOrders.Id.ToString());
        unavailable.ShouldContain("Details unavailable right now.");
        missing.ShouldContain(SampleOrders.Id.ToString());
        missing.ShouldContain("Order not found.");
    }

    [Fact]
    public async Task Lookup_redirects_to_a_valid_order_number()
    {
        await using var factory = new WebFactory();
        var (client, _) = await factory.BrowserAsync();

        var found = await client.GetAsync($"/Orders/Lookup?orderId=%20{SampleOrders.Id}%20");
        var invalid = await client.GetStringAsync("/Orders/Lookup?orderId=pizza");

        found.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        found.Headers.Location!.OriginalString.ShouldBe(DetailsUrl);
        WebFactory.Text(invalid).ShouldContain("That isn't a valid order number.");
    }
}
