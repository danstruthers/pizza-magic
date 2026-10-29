using System.Net;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;
using PizzaMagic.Web.Tests.Builder;

namespace PizzaMagic.Web.Tests.Kitchen;

/// <summary>
/// The simulated restaurant page (/Kitchen): every active order with what to make and one button for its next step.
/// Each button sends the status the order was in when the page was drawn, so a stale page or a double click can't
/// skip a step.
/// </summary>
public class KitchenPageTests
{
    private static readonly string AdvanceUrl = $"/Kitchen/{SampleOrders.Id}/Advance";

    private static async Task<(HttpClient Client, string Html)> KitchenAsync(WebFactory factory)
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        return (client, await client.GetStringAsync("/Kitchen"));
    }

    [Fact]
    public async Task Kitchen_lists_active_orders_with_what_to_make()
    {
        await using var factory = new WebFactory();

        var (_, raw) = await KitchenAsync(factory);
        var html = WebFactory.Text(raw);

        html.ShouldContain(SampleOrders.Id.ToString());
        html.ShouldContain("Not Started");
        html.ShouldContain("1 x Large pizza with Pepperoni 2x");
        html.ShouldContain("2 x Small pizza (no toppings)");
        html.ShouldContain("Dana");
        html.ShouldContain("$13.80");
        html.ShouldContain("12:00 PM PDT");
        raw.ShouldContain($"action=\"{AdvanceUrl}\"");
        raw.ShouldContain("name=\"expectedStatus\" value=\"NotStarted\"");
    }

    [Fact]
    public async Task Each_order_gets_a_button_for_its_next_step()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.KitchenQueue = _ => ApiResult<KitchenQueueResponse>.Success(new KitchenQueueResponse(1,
        [
            SampleOrders.Queued(id: Guid.NewGuid()),
            SampleOrders.Queued("Started", "ReadyForPickup", "Ready for Pickup", Guid.NewGuid()),
            SampleOrders.Queued("ReadyForPickup", "Delivered", "Delivered", Guid.NewGuid()),
        ]), null, false);

        var (_, raw) = await KitchenAsync(factory);
        var html = WebFactory.Text(raw);

        html.ShouldContain(">Start<");
        html.ShouldContain(">Mark ready for pickup<");
        html.ShouldContain(">Mark delivered<");
    }

    [Fact]
    public async Task Empty_kitchen_says_so()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.KitchenQueue = _ => ApiResult<KitchenQueueResponse>.Success(new KitchenQueueResponse(1, []), null, false);

        var (_, raw) = await KitchenAsync(factory);

        WebFactory.Text(raw).ShouldContain("No orders waiting.");
    }

    [Fact]
    public async Task Kitchen_says_so_when_the_service_is_unavailable()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.KitchenQueue = _ => ApiResult<KitchenQueueResponse>.Unavailable("timeout");

        var (_, raw) = await KitchenAsync(factory);

        WebFactory.Text(raw).ShouldContain("Can't reach the order service right now.");
    }

    [Fact]
    public async Task Advance_sends_the_expected_status_and_returns_to_the_list()
    {
        await using var factory = new WebFactory();
        var (client, html) = await KitchenAsync(factory);

        var response = await WebFactory.PostFormAsync(client, AdvanceUrl, WebFactory.Token(html), ("expectedStatus", "NotStarted"));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/Kitchen");
        factory.OrderManager.AdvanceRequests.ShouldBe([(SampleOrders.Id, "NotStarted")]);
        WebFactory.Text(await client.GetStringAsync("/Kitchen")).ShouldContain("Order 0f8fad5b is now Started.");
    }

    // Another tab (or the customer) got there first: say what happened; the redrawn list is current.
    [Fact]
    public async Task Advance_conflict_says_what_the_order_is_now()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Advance = (_, _) => ApiResult<AdvanceOrderResponse>.Failed(
            Problems.Of(ProblemTypes.InvalidTransition, 409, extensions: new { currentStatus = "Cancelled" }));
        var (client, html) = await KitchenAsync(factory);

        var response = await WebFactory.PostFormAsync(client, AdvanceUrl, WebFactory.Token(html), ("expectedStatus", "NotStarted"));

        response.Headers.Location!.OriginalString.ShouldBe("/Kitchen");
        WebFactory.Text(await client.GetStringAsync("/Kitchen"))
            .ShouldContain("Order 0f8fad5b had already changed: it is now Cancelled.");
    }

    [Fact]
    public async Task Advance_requires_antiforgery()
    {
        await using var factory = new WebFactory();
        var (client, _) = await KitchenAsync(factory);

        var response = await WebFactory.PostFormAsync(client, AdvanceUrl, "not-a-token", ("expectedStatus", "NotStarted"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.OrderManager.AdvanceRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Kitchen_explains_an_unknown_restaurant()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.KitchenQueue = _ => ApiResult<KitchenQueueResponse>.Failed(Problems.Of(ProblemTypes.RestaurantNotFound, 404));

        var (_, raw) = await KitchenAsync(factory);

        WebFactory.Text(raw).ShouldContain("This kitchen's restaurant doesn't exist. Check OrderManager:RestaurantId.");
    }

    [Fact]
    public async Task Auto_refresh_is_in_the_head()
    {
        await using var factory = new WebFactory();

        var (_, raw) = await KitchenAsync(factory);

        raw.IndexOf("http-equiv=\"refresh\"", StringComparison.Ordinal).ShouldBeInRange(0, raw.IndexOf("</head>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Layout_links_to_the_kitchen()
    {
        await using var factory = new WebFactory();

        (await factory.CreateClient().GetStringAsync("/")).ShouldContain("href=\"/Kitchen\"");
    }
}
