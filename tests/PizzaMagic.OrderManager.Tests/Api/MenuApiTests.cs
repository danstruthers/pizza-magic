using System.Net;
using PizzaMagic.Contracts.Api;

namespace PizzaMagic.OrderManager.Tests.Api;

public class MenuApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GET_menu_returns_server_computed_grid()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/menu");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var menu = await response.ReadAsync<MenuResponse>();
        menu.RestaurantId.ShouldBe(1);
        menu.RestaurantName.ShouldBe("Pizza Magic");
        menu.Sizes.ShouldBe([new("SMALL", "Small", 500), new("MEDIUM", "Medium", 700), new("LARGE", "Large", 1200)]);
        menu.Crusts.ShouldBe([new("HAND_TOSSED", "Hand tossed", 0, true)]);
        menu.Sauces.ShouldBe([new("CLASSIC_TOMATO", "Classic tomato", 0, true)]);
        menu.AddOnCategories.ShouldBeEmpty();
        menu.ToppingTiers.Select(t => (t.Code, t.Name, t.Toppings.Count)).ShouldBe([("BASIC", "Basic", 6), ("PREMIUM", "Premium", 6)]);

        var pepperoni = menu.ToppingTiers[0].Toppings[0];
        pepperoni.Code.ShouldBe("PEPPERONI");
        pepperoni.Prices.Keys.ShouldBe(["SMALL", "MEDIUM", "LARGE"]);
        pepperoni.Prices.Select(p => (p.Key, p.Value["WHOLE"])).ShouldBe([("SMALL", 40L), ("MEDIUM", 56L), ("LARGE", 96L)]);
        pepperoni.Prices["SMALL"].Keys.ShouldBe(["WHOLE"]);   // built scope: whole only (A69)
        var bacon = menu.ToppingTiers[1].Toppings[0];
        bacon.Prices.Select(p => p.Value["WHOLE"]).ShouldBe([55L, 77L, 132L]);

        menu.Policy.ShouldBe(new MenuPolicyResponse(20, 20, 12, 3, 50_000, 1_000_000, [150_000, 180_000, 200_000]));
    }

    [Fact]
    public async Task GET_menu_unknown_restaurant_is_404()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/menu?restaurantId=99");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadJsonAsync()).GetProperty("type").GetString().ShouldBe(ProblemTypes.RestaurantNotFound);
    }
}
