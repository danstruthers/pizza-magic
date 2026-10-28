using System.Net;
using System.Net.Http.Json;
using PizzaMagic.Contracts.Api;
using static PizzaMagic.OrderManager.Tests.Api.OrderApiTests;

namespace PizzaMagic.OrderManager.Tests.Api;

/// <summary>
/// GET /api/v1/kitchen/orders: what the kitchen still has to do. Active orders only (Not Started, Started, Ready for
/// Pickup), oldest first, each with what to make and the status it moves to next. Each test uses its own database.
/// </summary>
public class KitchenQueueApiTests
{
    private static async Task<string> PlaceAsync(HttpClient client, SubmitOrderRequest request)
    {
        var response = await SubmitAsync(client, request, Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.ReadAsync<OrderResponse>()).OrderId;
    }

    private static async Task AdvanceAsync(HttpClient client, string id, string expected) =>
        (await client.PostAsJsonAsync($"/api/v1/kitchen/orders/{id}/advance", new AdvanceOrderRequest(expected))).StatusCode.ShouldBe(HttpStatusCode.OK);

    private static async Task<KitchenQueueResponse> QueueAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/v1/kitchen/orders" + query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.ReadAsync<KitchenQueueResponse>();
    }

    [Fact]
    public async Task Queue_lists_active_orders_oldest_first_with_their_next_step()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var first = await PlaceAsync(client, P9());
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        var second = await PlaceAsync(client, P9(customerName: "Robin"));
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        var third = await PlaceAsync(client, P9(customerName: null));
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        var delivered = await PlaceAsync(client, P9());
        var cancelled = await PlaceAsync(client, P9());
        await AdvanceAsync(client, second, "NotStarted");
        await AdvanceAsync(client, third, "NotStarted");
        await AdvanceAsync(client, third, "Started");
        foreach (var step in new[] { "NotStarted", "Started", "ReadyForPickup" })
        {
            await AdvanceAsync(client, delivered, step);
        }
        (await client.PostAsync($"/api/v1/orders/{cancelled}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var queue = await QueueAsync(client);

        queue.RestaurantId.ShouldBe(1);
        queue.Orders.Select(o => (o.OrderId, o.Status, o.NextStatus, o.NextStatusDisplay)).ShouldBe(
        [
            (first, "NotStarted", "Started", "Started"),
            (second, "Started", "ReadyForPickup", "Ready for Pickup"),
            (third, "ReadyForPickup", "Delivered", "Delivered"),
        ]);
        var order = queue.Orders[0];
        (order.StatusDisplay, order.Version, order.CustomerName, order.TotalCents).ShouldBe(("Not Started", 1, "Dana", 3148L));
        order.Items.ShouldBe(["2 x Large pizza with Pepperoni, Bacon"]);
        order.CreatedAt.ShouldBe("2026-10-27T19:00:00.000Z");
        order.CreatedAtLocal!.LocalTime.ShouldBe("2026-10-27T12:00:00");
        queue.Orders[1].CustomerName.ShouldBe("Robin");
    }

    [Fact]
    public async Task Empty_queue_is_an_empty_list()
    {
        await using var factory = new ApiFactory();

        var queue = await QueueAsync(factory.CreateClient(), "?restaurantId=1");

        queue.RestaurantId.ShouldBe(1);
        queue.Orders.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_restaurant_is_404()
    {
        await using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/v1/kitchen/orders?restaurantId=999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadJsonAsync()).GetProperty("type").GetString().ShouldBe(ProblemTypes.RestaurantNotFound);
    }
}
