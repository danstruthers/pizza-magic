using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Ordering;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Tests.Catalog;
using static PizzaMagic.Domain.Tests.Pricing.TestCatalogs;

namespace PizzaMagic.Domain.Tests.Pricing;

/// <summary>
/// Extra toppings on the whole pizza (UQ29): a topping may be added 1x to its product's max_multiplier times (seed 3).
/// The charge is the topping's rounded unit price x multiplier, then x quantity (decision J: round before multiplying).
/// Half and quarter placements stay designed, not built (A69).
/// </summary>
public class ExtraToppingTests
{
    private static PizzaDraftLine Line(string size, int quantity, params DraftTopping[] toppings) => new(size, quantity, toppings);

    private static PriceBreakdown Ok(PriceResult result)
    {
        result.Errors.ShouldBeEmpty();
        return result.Breakdown!;
    }

    [Fact]
    public void Extra_topping_multiplies_the_rounded_unit_price_then_the_quantity()
    {
        var breakdown = Ok(Price(Draft([Line("LARGE", 2, new DraftTopping("PEPPERONI", Multiplier: 2))])));

        var topping = breakdown.Lines.Single().Line.Components.Single(c => c.Kind == ComponentKind.Topping);
        topping.Multiplier.ShouldBe(2);
        topping.ToppingPrice!.UnitPrice.Cents.ShouldBe(96);
        topping.UnitCharge.Cents.ShouldBe(192);
        breakdown.Lines.Single().Line.UnitPrice.Cents.ShouldBe(1392);
        (breakdown.Subtotal.Cents, breakdown.Tax.Tax.Cents, breakdown.Total.Cents).ShouldBe((2784L, 181L, 2965L));
    }

    [Fact]
    public void Triple_premium_topping()
    {
        var breakdown = Ok(Price(Draft([Line("MEDIUM", 1, new DraftTopping("BACON", "WHOLE", 3))])));

        (breakdown.Subtotal.Cents, breakdown.Tax.Tax.Cents, breakdown.Total.Cents).ShouldBe((931L, 61L, 992L));
    }

    // 11% of 750 = 82.5 -> 83 per layer, so 3 layers are 249, never round(247.5) = 248.
    [Fact]
    public void Each_layer_is_the_rounded_unit_price()
    {
        var catalog = Spec(SeedCatalog.Snapshot().WithProduct(SeedCatalog.Size(9, "TEST_750", "Test 750", 750, 9)));

        var breakdown = Ok(Price(Draft([Line("TEST_750", 1, new DraftTopping("BACON", Multiplier: 3))]), catalog));

        breakdown.Lines.Single().Line.Components.Single(c => c.Kind == ComponentKind.Topping).UnitCharge.Cents.ShouldBe(249);
        breakdown.Subtotal.Cents.ShouldBe(999);
    }

    [Fact]
    public void Discount_allocation_covers_the_extra_layers()
    {
        var breakdown = Ok(Price(Draft([Line("LARGE", 1, new DraftTopping("PEPPERONI", Multiplier: 2))], null, "ILOVEPIZZA")));

        (breakdown.Subtotal.Cents, breakdown.Discount.Cents, breakdown.Tax.Tax.Cents, breakdown.Total.Cents).ShouldBe((1392L, 139L, 81L, 1334L));
        breakdown.Allocations.Sum(a => a.Amount.Cents).ShouldBe(139);
        breakdown.Allocations.Single(a => a.Kind == ComponentKind.Topping).Amount.Cents.ShouldBe(19);
        breakdown.Allocations.Single(a => a.Kind == ComponentKind.Base).Amount.Cents.ShouldBe(120);
    }

    [Fact]
    public void Normalized_draft_keeps_the_multiplier_and_description_says_2x()
    {
        var catalog = Spec();
        var breakdown = Ok(Price(Draft([Line("LARGE", 1, new DraftTopping("bacon"), new DraftTopping("pepperoni", Multiplier: 2))]), catalog));

        breakdown.NormalizedDraft.LineItems.Single().Toppings.ShouldBe(
        [
            new DraftTopping("PEPPERONI", "WHOLE", 2),
            new DraftTopping("BACON", "WHOLE", 1),
        ]);
        OrderDescriber.Describe(DescriptionSource.FromBreakdown(breakdown, catalog.Menu)).Items.Single().Text
            .ShouldBe("1 x Large pizza with Pepperoni 2x, Bacon");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void Multiplier_outside_one_to_max_is_refused(int multiplier)
    {
        var errors = Price(Draft([Line("LARGE", 1, new DraftTopping("PEPPERONI", Multiplier: multiplier))])).Errors;

        errors.ShouldBe([new FieldError("lineItems[0].toppings[0].multiplier", "MULTIPLIER_OUT_OF_RANGE", "Pepperoni can be added at most 3 times.")]);
    }

    [Fact]
    public void Max_multiplier_comes_from_the_product()
    {
        var snapshot = SeedCatalog.Snapshot();
        var limited = snapshot with
        {
            Products = snapshot.Products.Select(p => p.Code == "PINEAPPLE" ? p with { MaxMultiplier = 1 } : p).ToList(),
        };

        var errors = Price(Draft([Line("LARGE", 1, new DraftTopping("PINEAPPLE", Multiplier: 2))]), Spec(limited)).Errors;

        errors.Single().Message.ShouldBe("Pineapple can be added at most once.");
    }

    // Listing a topping twice is not how to ask for extra: it gets its own message pointing at 2x / 3x.
    [Fact]
    public void Repeated_topping_points_at_the_multiplier()
    {
        var errors = Price(Draft([Line("LARGE", 1, new DraftTopping("SAUSAGE"), new DraftTopping("sausage"))])).Errors;

        errors.ShouldBe([new FieldError("lineItems[0].toppings[1]", "DUPLICATE_TOPPING",
            "Sausage is already on this pizza. To add more, change its amount (2x, 3x) instead.")]);
    }
}
