using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PizzaMagic.Contracts.Api;
using PizzaMagic.EndToEnd.Tests.Hosting;

namespace PizzaMagic.EndToEnd.Tests.Flows;

/// <summary>
/// The customer's journeys through the real order builder and the real order service (plan section 7.4, M15): every
/// total the browser sees, Review shows and the database stores is the same number.
/// </summary>
public class FlowTests
{
    /// <summary>P9: 2 Large with Pepperoni and Bacon, ILOVEPIZZA, 15% tip = $31.48. As the API takes it.</summary>
    private static readonly object P9Draft = new
    {
        lineItems = new[]
        {
            new { kind = "Pizza", sizeCode = "LARGE", quantity = 2, toppings = new[] { new { code = "PEPPERONI" }, new { code = "BACON" } } },
        },
        promoCodes = new[] { "ILOVEPIZZA" },
        tip = new { mode = "Percent", value = "15" },
    };

    /// <summary>P9 as the builder page posts it: the same draft plus its revision.</summary>
    private static JsonObject P9(long revision)
    {
        var draft = JsonSerializer.SerializeToNode(P9Draft, WebHost.Json)!.AsObject();
        draft["revision"] = revision;
        return draft;
    }

    /// <summary>One Medium with Pepperoni, no promo or tip: $7.56 + $0.49 tax = $8.05.</summary>
    private static object MediumPepperoni(long revision) => new
    {
        revision,
        lineItems = new[] { new { kind = "Pizza", sizeCode = "MEDIUM", quantity = 1, toppings = new[] { new { code = "PEPPERONI" } } } },
        promoCodes = Array.Empty<string>(),
        tip = (object?)null,
    };

    private static async Task<(HttpClient Browser, string Token)> OpenBuilderAsync(WebHost web)
    {
        var browser = web.Browser();
        var html = await browser.GetStringAsync("/");
        return (browser, WebHost.Token(html));
    }

    private static async Task<(string Html, string Ticket, string Token)> ReviewAsync(HttpClient browser)
    {
        var html = await browser.GetStringAsync("/Checkout");
        return (WebHost.Text(html), WebHost.Ticket(html)!, WebHost.Token(html));
    }

    /// <summary>Builds the draft, reviews it and places it; returns the new order's id.</summary>
    private static async Task<Guid> PlaceAsync(WebHost web, HttpClient browser, string token, object draft)
    {
        (await WebHost.PostQuoteAsync(browser, token, draft)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var review = await ReviewAsync(browser);
        var placed = await WebHost.PostFormAsync(browser, "/Checkout/Place", review.Token, ("ticket", review.Ticket));
        placed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return Guid.Parse(placed.Headers.Location!.OriginalString["/Orders/".Length..]);
    }

    private static async Task<OrderResponse> ApiOrderAsync(OrderManagerHost orderManager, Guid id) =>
        (await orderManager.CreateClient().GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{id}", WebHost.Json))!;

    // The builder's running total is the service's quote, unchanged: the web app adds and drops nothing.
    [Fact]
    public async Task Web_quote_equals_direct_api_quote()
    {
        await using var orderManager = new OrderManagerHost();
        await using var web = new WebHost(orderManager);
        var (browser, token) = await OpenBuilderAsync(web);

        var throughWeb = await (await WebHost.PostQuoteAsync(browser, token, P9(1))).Content.ReadFromJsonAsync<QuoteResponse>(WebHost.Json);
        var direct = await (await orderManager.CreateClient().PostAsJsonAsync("/api/v1/quotes", P9Draft, WebHost.Json))
            .Content.ReadFromJsonAsync<QuoteResponse>(WebHost.Json);

        throughWeb!.TotalCents.ShouldBe(3148);
        JsonSerializer.Serialize(throughWeb, WebHost.Json).ShouldBe(JsonSerializer.Serialize(direct, WebHost.Json));
    }

    // Requirement 4's conflict message reaches the customer exactly as written (UQ3).
    [Fact]
    public async Task Conflicting_promo_message_reaches_the_builder()
    {
        await using var orderManager = new OrderManagerHost();
        await using var web = new WebHost(orderManager);
        var (browser, token) = await OpenBuilderAsync(web);

        var quote = await (await WebHost.PostQuoteAsync(browser, token, new
        {
            revision = 1,
            lineItems = new[] { new { kind = "Pizza", sizeCode = "LARGE", quantity = 2, toppings = new[] { new { code = "PEPPERONI" } } } },
            promoCodes = new[] { "ILOVEPIZZA", "SAVE5" },
            tip = (object?)null,
        })).Content.ReadFromJsonAsync<QuoteResponse>(WebHost.Json);

        var save5 = quote!.Promotions.Single(p => p.Code == "SAVE5");
        save5.Status.ShouldBe("Rejected");
        save5.Message.ShouldBe("SAVE5 can't be combined with ILOVEPIZZA. Remove ILOVEPIZZA to use SAVE5.");
        quote.NormalizedPromoCodes.ShouldBe(["ILOVEPIZZA"]);
    }

    // Seen = charged, across every step: builder total = Review = stored order; then status, change and cancel.
    [Fact]
    public async Task Flow_build_submit_status_edit_cancel()
    {
        await using var orderManager = new OrderManagerHost();
        await using var web = new WebHost(orderManager);
        var (browser, token) = await OpenBuilderAsync(web);

        // Build and review.
        var running = await (await WebHost.PostQuoteAsync(browser, token, P9(1))).Content.ReadFromJsonAsync<QuoteResponse>(WebHost.Json);
        running!.TotalCents.ShouldBe(3148);
        var review = await ReviewAsync(browser);
        review.Html.ShouldContain("Place order — $31.48");

        // Place: the order page and the stored order show the same total.
        var placed = await WebHost.PostFormAsync(browser, "/Checkout/Place", review.Token, ("ticket", review.Ticket));
        placed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var id = Guid.Parse(placed.Headers.Location!.OriginalString["/Orders/".Length..]);
        var details = WebHost.Text(await browser.GetStringAsync($"/Orders/{id}"));
        details.ShouldContain("Not Started");
        details.ShouldContain("$31.48");
        details.ShouldContain("2 x Large pizza with");
        (await ApiOrderAsync(orderManager, id)).Pricing.TotalCents.ShouldBe(3148);
        WebHost.Text(await browser.GetStringAsync("/Orders")).ShouldContain(id.ToString());

        // Status through the web app's polling endpoint.
        var status = await browser.GetFromJsonAsync<OrderStatusResponse>($"/Orders/{id}/Status", WebHost.Json);
        status!.Status.ShouldBe("NotStarted");
        status.AllowedActions.ShouldBe(["update", "cancel"]);

        // Change it through the builder and Review: the same order, a new version and total.
        var edit = await WebHost.PostFormAsync(browser, $"/Orders/{id}/Edit", WebHost.Token(details));
        edit.Headers.Location!.OriginalString.ShouldBe("/");
        var builder = await browser.GetStringAsync("/");
        WebHost.Text(builder).ShouldContain("You're changing order");
        var revision = JsonDocument.Parse(WebHost.Text(System.Text.RegularExpressions.Regex.Match(builder, "data-cart=\"([^\"]*)\"").Groups[1].Value))
            .RootElement.GetProperty("revision").GetInt64();
        (await WebHost.PostQuoteAsync(browser, WebHost.Token(builder), MediumPepperoni(revision + 1))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var changeReview = await ReviewAsync(browser);
        changeReview.Html.ShouldContain("Update order — $8.05");
        var updated = await WebHost.PostFormAsync(browser, "/Checkout/Place", changeReview.Token, ("ticket", changeReview.Ticket));
        updated.Headers.Location!.OriginalString.ShouldBe($"/Orders/{id}");
        var afterUpdate = await ApiOrderAsync(orderManager, id);
        afterUpdate.Version.ShouldBe(2);
        afterUpdate.Pricing.TotalCents.ShouldBe(805);
        (await orderManager.OrderCountAsync()).ShouldBe(1);

        // Cancel.
        var page = await browser.GetStringAsync($"/Orders/{id}");
        var cancel = await WebHost.PostFormAsync(browser, $"/Orders/{id}/Cancel", WebHost.Token(page));
        cancel.Headers.Location!.OriginalString.ShouldBe($"/Orders/{id}");
        var cancelled = WebHost.Text(await browser.GetStringAsync($"/Orders/{id}"));
        cancelled.ShouldContain("Your order was cancelled.");
        cancelled.ShouldNotContain($"/Orders/{id}/Cancel\"");
        (await ApiOrderAsync(orderManager, id)).Status.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task Flow_kitchen_start_then_cancel_refused()
    {
        await using var orderManager = new OrderManagerHost();
        await using var web = new WebHost(orderManager);
        var (browser, token) = await OpenBuilderAsync(web);
        var id = await PlaceAsync(web, browser, token, P9(1));
        var page = await browser.GetStringAsync($"/Orders/{id}");   // opened before the kitchen starts: buttons shown
        page.ShouldContain($"action=\"/Orders/{id}/Cancel\"");

        var advance = await orderManager.CreateClient().PostAsJsonAsync($"/api/v1/kitchen/orders/{id}/advance", new { expectedStatus = "NotStarted" });
        advance.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await browser.GetFromJsonAsync<OrderStatusResponse>($"/Orders/{id}/Status", WebHost.Json);
        status!.Status.ShouldBe("Started");
        status.AllowedActions.ShouldBeEmpty();

        // The customer presses the (now stale) Cancel button: refused with the kitchen message; the order is untouched.
        var cancel = await WebHost.PostFormAsync(browser, $"/Orders/{id}/Cancel", WebHost.Token(page));
        cancel.Headers.Location!.OriginalString.ShouldBe($"/Orders/{id}");
        var after = WebHost.Text(await browser.GetStringAsync($"/Orders/{id}"));
        after.ShouldContain("The kitchen has already started your order, so it can't be changed or cancelled.");
        after.ShouldNotContain($"action=\"/Orders/{id}/Cancel\"");
        after.ShouldNotContain($"action=\"/Orders/{id}/Edit\"");
        (await ApiOrderAsync(orderManager, id)).Status.ShouldBe("Started");

        // Change is refused the same way.
        var edit = await WebHost.PostFormAsync(browser, $"/Orders/{id}/Edit", WebHost.Token(page));
        edit.Headers.Location!.OriginalString.ShouldBe($"/Orders/{id}");
    }

    // The simulated kitchen page moves a real order through every step; the customer's page follows it.
    [Fact]
    public async Task Kitchen_page_moves_an_order_to_delivered()
    {
        await using var orderManager = new OrderManagerHost();
        await using var web = new WebHost(orderManager);
        var (browser, token) = await OpenBuilderAsync(web);
        var id = await PlaceAsync(web, browser, token, P9(1));
        var kitchen = web.Browser();

        foreach (var (expected, now) in new[] { ("NotStarted", "Started"), ("Started", "Ready for Pickup"), ("ReadyForPickup", "Delivered") })
        {
            var page = await kitchen.GetStringAsync("/Kitchen");
            page.ShouldContain($"action=\"/Kitchen/{id}/Advance\"");
            page.ShouldContain($"name=\"expectedStatus\" value=\"{expected}\"");
            var pressed = await WebHost.PostFormAsync(kitchen, $"/Kitchen/{id}/Advance", WebHost.Token(page), ("expectedStatus", expected));
            pressed.Headers.Location!.OriginalString.ShouldBe("/Kitchen");
            WebHost.Text(await kitchen.GetStringAsync("/Kitchen")).ShouldContain($"is now {now}.");
        }

        (await kitchen.GetStringAsync("/Kitchen")).ShouldNotContain($"/Kitchen/{id}/Advance");   // delivered: off the list
        (await ApiOrderAsync(orderManager, id)).Status.ShouldBe("Delivered");
        WebHost.Text(await browser.GetStringAsync($"/Orders/{id}")).ShouldContain("Delivered");
    }

    // The order service commits the order but every response is lost (a phone losing signal). The customer sees "We
    // couldn't confirm" and presses Try again: the same key replays the order, so exactly one exists.
    [Fact]
    public async Task Lost_response_then_try_again_creates_one_order()
    {
        await using var orderManager = new OrderManagerHost();
        await using var web = new WebHost(orderManager);
        var (browser, token) = await OpenBuilderAsync(web);
        (await WebHost.PostQuoteAsync(browser, token, P9(1))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var review = await ReviewAsync(browser);

        web.Network.DropResponses = true;
        var lost = await WebHost.PostFormAsync(browser, "/Checkout/Place", review.Token, ("ticket", review.Ticket));
        lost.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var pending = await lost.Content.ReadAsStringAsync();
        WebHost.Text(pending).ShouldContain("We couldn't confirm your order. Your cart is saved.");
        (await orderManager.OrderCountAsync()).ShouldBe(1);   // it did go through; the customer can't know yet

        web.Network.DropResponses = false;
        var retried = await WebHost.PostFormAsync(browser, "/Checkout/Place", WebHost.Token(pending), ("ticket", WebHost.Ticket(pending)!));

        retried.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var id = Guid.Parse(retried.Headers.Location!.OriginalString["/Orders/".Length..]);
        (await orderManager.OrderCountAsync()).ShouldBe(1);
        (await ApiOrderAsync(orderManager, id)).Pricing.TotalCents.ShouldBe(3148);
        WebHost.Text(await browser.GetStringAsync("/")).ShouldNotContain("We couldn't confirm your order");
    }

    // A double tap on Place (the same ticket posted twice at once, from one session): one order, and both taps land on it.
    [Fact]
    public async Task Double_tap_on_place_creates_one_order()
    {
        await using var orderManager = new OrderManagerHost();
        await using var web = new WebHost(orderManager);
        var (browser, token) = await OpenBuilderAsync(web);
        (await WebHost.PostQuoteAsync(browser, token, P9(1))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var review = await ReviewAsync(browser);

        var taps = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            WebHost.PostFormAsync(browser, "/Checkout/Place", review.Token, ("ticket", review.Ticket))));

        taps.ShouldAllBe(t => t.StatusCode == HttpStatusCode.Redirect);
        taps.Select(t => t.Headers.Location!.OriginalString).Distinct().ShouldHaveSingleItem();
        (await orderManager.OrderCountAsync()).ShouldBe(1);
    }

    // The order service is down: no order, a clear message, and the builder keeps working once it is back.
    [Fact]
    public async Task Service_down_shows_messages_and_recovers()
    {
        await using var orderManager = new OrderManagerHost();
        await using var web = new WebHost(orderManager);
        var (browser, token) = await OpenBuilderAsync(web);

        web.Network.Down = true;
        var quote = await WebHost.PostQuoteAsync(browser, token, P9(1));
        quote.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString().ShouldBe("Total unavailable — retrying");
        WebHost.Text(await browser.GetStringAsync("/Checkout")).ShouldContain("We can't price your order right now.");

        web.Network.Down = false;
        var review = await ReviewAsync(browser);   // the cart was saved while the service was down
        review.Html.ShouldContain("Place order — $31.48");
        (await orderManager.OrderCountAsync()).ShouldBe(0);
    }
}
