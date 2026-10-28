using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Tests.Catalog;

namespace PizzaMagic.Domain.Tests.Pricing;

public class PizzaLinePricerTests
{
    private static PricedPizzaLine Price(string size, int quantity, params string[] toppings) =>
        PizzaLinePricer.Price(SeedCatalog.DefaultMenu(), new PizzaLineRequest(size, quantity, toppings));

    [Theory]
    [InlineData("SMALL", 500)]
    [InlineData("MEDIUM", 700)]
    [InlineData("LARGE", 1200)]
    public void Plain_pizza_costs_size_base(string size, long expected)
    {
        var line = Price(size, 1);

        line.UnitPrice.ShouldBe(new Money(expected));
        line.LineTotal.ShouldBe(new Money(expected));
    }

    // "L+pep+bacon" = 1200 + 96 + 132 = 1428 (plan section 7.3).
    [Fact]
    public void Unit_is_base_plus_crust_plus_sauce_plus_toppings()
    {
        var line = Price("LARGE", 1, "PEPPERONI", "BACON");

        line.UnitPrice.ShouldBe(new Money(1428));
        line.Components.Select(c => (c.Kind, c.Product.Code, c.UnitCharge.Cents)).ShouldBe(
        [
            (ComponentKind.Base, "LARGE", 1200L),
            (ComponentKind.Crust, "HAND_TOSSED", 0L),
            (ComponentKind.Sauce, "CLASSIC_TOMATO", 0L),
            (ComponentKind.Topping, "PEPPERONI", 96L),
            (ComponentKind.Topping, "BACON", 132L),
        ]);
        line.Components.ShouldAllBe(c => c.TaxClassId == SeedCatalog.PreparedFood);
    }

    [Fact]
    public void Line_total_is_unit_times_qty()
    {
        var line = Price("LARGE", 2, "PEPPERONI", "BACON");

        line.Quantity.ShouldBe(2);
        line.LineTotal.ShouldBe(new Money(2856));
    }

    [Fact]
    public void Topping_order_doesnt_change_price()
    {
        var forward = Price("MEDIUM", 1, "EXTRA_CHEESE", "PINEAPPLE");
        var reversed = Price("MEDIUM", 1, "PINEAPPLE", "EXTRA_CHEESE");

        forward.UnitPrice.ShouldBe(new Money(833));
        reversed.UnitPrice.ShouldBe(forward.UnitPrice);
        reversed.Components.Select(c => c.Product.Code).ShouldBe(forward.Components.Select(c => c.Product.Code));
    }

    // N7 (defaults part): crust and sauce omitted -> HAND_TOSSED + CLASSIC_TOMATO at 0.
    [Fact]
    public void Omitted_crust_and_sauce_use_the_defaults()
    {
        var line = Price("LARGE", 1);

        line.Crust.Code.ShouldBe("HAND_TOSSED");
        line.Sauce.Code.ShouldBe("CLASSIC_TOMATO");
        line.UnitPrice.ShouldBe(new Money(1200));
    }

    // P18: the topping is rounded per pizza (82.5 -> 83) before multiplying by quantity,
    // so 10 pizzas cost 8330, not round(10 x 832.5) = 8325.
    [Fact]
    public void Fractional_topping_rounds_per_pizza()
    {
        var snapshot = SeedCatalog.Snapshot().WithProduct(SeedCatalog.Size(9, "TEST_750", "Test 750", 750, 9));
        var menu = EffectiveMenuBuilder.Build(snapshot, RestaurantOverrides.None(1), PricingMethodRegistry.Default);

        var line = PizzaLinePricer.Price(menu, new PizzaLineRequest("TEST_750", 10, ["BACON"]));

        line.Components.Single(c => c.Kind == ComponentKind.Topping).UnitCharge.ShouldBe(new Money(83));
        line.UnitPrice.ShouldBe(new Money(833));
        line.LineTotal.ShouldBe(new Money(8330));
    }

    [Fact]
    public void Size_override_flows_into_the_line()
    {
        var snapshot = SeedCatalog.Snapshot();
        var overrides = new RestaurantOverrides(2, true, [new ProductOverrideRow(snapshot.IdOf("LARGE"), null, 1300)]);
        var menu = EffectiveMenuBuilder.Build(snapshot, overrides, PricingMethodRegistry.Default);

        var line = PizzaLinePricer.Price(menu, new PizzaLineRequest("LARGE", 1, ["PEPPERONI", "BACON"]));

        line.UnitPrice.ShouldBe(new Money(1300 + 104 + 143));
        line.Components[0].Source.ShouldBe(PriceSource.Restaurant);
    }

    // Input is validated before pricing (DraftValidator, M5); the pricer refuses anything it cannot price.
    [Fact]
    public void Unavailable_or_unknown_input_throws()
    {
        var menu = SeedCatalog.DefaultMenu();

        Should.Throw<InvalidOperationException>(() => PizzaLinePricer.Price(menu, new PizzaLineRequest("HUGE", 1, [])));
        Should.Throw<InvalidOperationException>(() => PizzaLinePricer.Price(menu, new PizzaLineRequest("LARGE", 1, ["ANCHOVIES"])));
        Should.Throw<InvalidOperationException>(() => PizzaLinePricer.Price(menu, new PizzaLineRequest("LARGE", 1, [], CrustCode: "THIN")));
        Should.Throw<ArgumentOutOfRangeException>(() => PizzaLinePricer.Price(menu, new PizzaLineRequest("LARGE", 0, [])));
    }
}
