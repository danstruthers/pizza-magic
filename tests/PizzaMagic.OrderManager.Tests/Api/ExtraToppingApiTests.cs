using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PizzaMagic.Contracts.Api;
using static PizzaMagic.OrderManager.Tests.Api.OrderApiTests;

namespace PizzaMagic.OrderManager.Tests.Api;

/// <summary>
/// Extra toppings over HTTP (UQ29): 1 Large with Pepperoni 2x = 1200 + 96 x 2 = 1392, tax 90.48 -> 90, total 1482.
/// The quote, the stored order and its description all carry the multiplier.
/// </summary>
public class ExtraToppingApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly LineItemRequest LargePepperoni2x = new("Pizza", "LARGE", 1, [new ToppingRequest("PEPPERONI", Multiplier: 2)]);

    [Fact]
    public async Task Quote_prices_and_describes_an_extra_topping()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/quotes", new QuoteRequest(null, null, [LargePepperoni2x], [], null), ApiFactory.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var quote = await response.ReadAsync<QuoteResponse>();
        var topping = quote.LineItems.Single().Toppings.Single();
        (topping.Multiplier, topping.ToppingUnitPriceCents, topping.PlacementUnitChargeCents, topping.PlacementChargeCents)
            .ShouldBe((2, 96L, 96L, 192L));
        (quote.SubtotalCents, quote.TaxCents, quote.TotalCents).ShouldBe((1392L, 90L, 1482L));
        quote.LineItems.Single().Description.ShouldBe("1 x Large pizza with Pepperoni 2x");
        quote.NormalizedDraft.LineItems.Single().Toppings!.Single().ShouldBe(new ToppingRequest("PEPPERONI", "WHOLE", 2));
    }

    [Fact]
    public async Task Submitted_order_stores_the_multiplier()
    {
        var client = factory.CreateClient();
        var created = await SubmitAsync(client, new SubmitOrderRequest(null, null, [LargePepperoni2x], [], null, 1482, null), Guid.NewGuid().ToString());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await created.ReadAsync<OrderResponse>()).OrderId;

        var order = await client.GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{id}", ApiFactory.Json);

        order!.Pricing.TotalCents.ShouldBe(1482);
        var topping = order.Pricing.LineItems.Single().Toppings.Single();
        (topping.Multiplier, topping.ToppingUnitPriceCents, topping.PlacementChargeCents).ShouldBe((2, 96L, 192L));
        order.Description.ShouldContain("1 x Large pizza with Pepperoni 2x");

        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT multiplier, coverage_phrase, placement_unit_charge_cents, placement_charge_cents FROM order_line_topping t "
            + "JOIN order_line l ON l.id = t.order_line_id WHERE l.order_id = $id";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        (reader.GetInt32(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)).ShouldBe((2, "Pepperoni 2x", 96L, 192L));
    }

    [Fact]
    public async Task Menu_lists_each_toppings_max_multiplier()
    {
        var menu = await factory.CreateClient().GetFromJsonAsync<MenuResponse>("/api/v1/menu", ApiFactory.Json);

        menu!.ToppingTiers.SelectMany(t => t.Toppings).ShouldAllBe(t => t.MaxMultiplier == 3);
    }

    [Fact]
    public async Task Multiplier_above_the_max_is_422_at_its_path()
    {
        var request = new QuoteRequest(null, null, [new LineItemRequest("Pizza", "LARGE", 1, [new ToppingRequest("PEPPERONI", Multiplier: 4)])], [], null);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/quotes", request, ApiFactory.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadJsonAsync()).FieldErrors().ShouldBe([("lineItems[0].toppings[0].multiplier", "MULTIPLIER_OUT_OF_RANGE")]);
    }
}
