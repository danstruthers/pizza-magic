using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tests.Catalog;
using static PizzaMagic.Domain.Tests.Pricing.TestCatalogs;

namespace PizzaMagic.Domain.Tests.Pricing;

public class PricingEngineTests
{
    private static PizzaDraftLine LargePepBacon(int quantity = 1) => Pizza("LARGE", quantity, "PEPPERONI", "BACON");

    private static PizzaDraftLine[] GoldenCart =>
    [
        LargePepBacon(2),
        Pizza("MEDIUM", 1, "EXTRA_CHEESE", "PINEAPPLE"),
        Pizza("SMALL", 3),
    ];

    private static TipInput Percent(int ppm) => new(TipMode.Percent, ppm);

    private static TipInput Amount(long cents) => new(TipMode.Amount, cents);

    private static PriceBreakdown Ok(PriceResult result)
    {
        result.Errors.ShouldBeEmpty();
        return result.Breakdown!;
    }

    // Plan section 7.3, spec pricing goldens: (S, D, tax, tip, total) to the cent.
    public static TheoryData<string, OrderDraft, long, long, long, long, long> Goldens => new()
    {
        { "P1", Draft([Pizza("SMALL")]), 500, 0, 33, 0, 533 },
        { "P2", Draft([Pizza("MEDIUM")]), 700, 0, 46, 0, 746 },
        { "P3", Draft([Pizza("LARGE")]), 1200, 0, 78, 0, 1278 },
        { "P4", Draft([Pizza("SMALL"), Pizza("SMALL")]), 1000, 0, 65, 0, 1065 },
        { "P5", Draft(GoldenCart), 5189, 0, 337, 0, 5526 },
        { "P6", Draft(GoldenCart, Percent(180_000), "ILOVEPIZZA"), 5189, 519, 304, 895, 5869 },
        { "P7", Draft(GoldenCart, Amount(500), "SAVE5"), 5189, 500, 305, 500, 5494 },
        { "P8", Draft([LargePepBacon(2)], null, "ILOVEPIZZA"), 2856, 286, 167, 0, 2737 },
        { "P9", Draft([LargePepBacon(2)], Percent(150_000), "ILOVEPIZZA"), 2856, 286, 167, 411, 3148 },
        { "P10", Draft([LargePepBacon(2)], Amount(300), "ILOVEPIZZA"), 2856, 286, 167, 300, 3037 },
        { "P11", Draft([LargePepBacon(2)], null, "SAVE5"), 2856, 500, 153, 0, 2509 },
        { "P12", Draft([Pizza("SMALL", 1, "BACON", "CHICKEN", "GARLIC")], null, "ILOVEPIZZA"), 665, 67, 39, 0, 637 },
        { "P13", Draft([Pizza("SMALL", 4)], null, "SAVE5"), 2000, 0, 130, 0, 2130 },
        { "P14", Draft([Pizza("SMALL", 1, "PEPPERONI"), Pizza("SMALL", 3)], null, "SAVE5"), 2040, 500, 100, 0, 1640 },
        { "P15", Draft([Pizza("LARGE"), Pizza("MEDIUM", 2)], null, "SAVE5"), 2600, 500, 137, 0, 2237 },
        { "P16", Draft([Pizza("MEDIUM", 3, "PEPPERONI", "BACON")], Amount(300), "ILOVEPIZZA"), 2499, 250, 146, 300, 2695 },
        { "P17", Draft([Pizza("MEDIUM", 3)], null, "SAVE5"), 2100, 500, 104, 0, 1704 },
        { "P19", Draft([Pizza("SMALL")], Percent(150_000)), 500, 0, 33, 80, 613 },
        { "P20", Draft([Pizza("SMALL")], Percent(200_000)), 500, 0, 33, 107, 640 },
        { "P21", Draft([Pizza("SMALL"), Pizza("SMALL")], Percent(100_000)), 1000, 0, 65, 107, 1172 },
        { "P22", Draft([LargePepBacon(2)], Percent(175_000)), 2856, 0, 186, 532, 3574 },
        { "P24", Draft([LargePepBacon(2)], Percent(150_000), "ILOVEPIZZA", "SAVE5"), 2856, 286, 167, 411, 3148 },
        { "P25", Draft([LargePepBacon(2)], null, "  ilovepizza "), 2856, 286, 167, 0, 2737 },
    };

    [Theory]
    [MemberData(nameof(Goldens))]
    public void Golden_vectors_price_to_the_cent(string id, OrderDraft draft, long s, long d, long tax, long tip, long total)
    {
        var breakdown = Ok(Price(draft));

        (breakdown.Subtotal.Cents, breakdown.Discount.Cents, breakdown.Tax.Tax.Cents, breakdown.Tip.Cents, breakdown.Total.Cents)
            .ShouldBe((s, d, tax, tip, total), id);
    }

    // P18: a size worth 750 makes the premium topping 82.5 -> 83 per pizza, before x10.
    [Fact]
    public void New_size_from_data_rounds_toppings_per_pizza()
    {
        var catalog = Spec(SeedCatalog.Snapshot().WithProduct(SeedCatalog.Size(9, "TEST_750", "Test 750", 750, 9)));

        var breakdown = Ok(Price(Draft([Pizza("TEST_750", 10, "BACON")]), catalog));

        breakdown.Subtotal.ShouldBe(new Money(8330));
        breakdown.Tax.Tax.ShouldBe(new Money(541));
        breakdown.Total.ShouldBe(new Money(8871));
    }

    // PR totals (PR1, PR3-PR8) on top of M3's discounts.
    public static TheoryData<string, OrderDraft, long, long> PromotionTotals => new()
    {
        { "PR1", Draft([Pizza("LARGE", 1, "PEPPERONI", "SAUSAGE", "BACON")], null, "ILOVEPIZZA"), 152, 1461 },
        { "PR3", Draft([Pizza("LARGE"), Pizza("LARGE"), Pizza("SMALL", 1, "BACON")], null, "ILOVEPIZZA", "SAVE5"), 296, 2832 },
        { "PR4", Draft([Pizza("LARGE"), Pizza("LARGE"), Pizza("SMALL", 1, "BACON")], null, "SAVE5", "ILOVEPIZZA"), 500, 2615 },
        { "PR5", Draft([Pizza("LARGE", 1, "PEPPERONI", "SAUSAGE", "BACON")], null, "ILOVEPIZZA", "SAVE5"), 152, 1461 },
        { "PR5b", Draft([Pizza("LARGE", 1, "PEPPERONI", "SAUSAGE", "BACON")], null, "SAVE5", "ILOVEPIZZA"), 0, 1623 },
        { "PR6", Draft([Pizza("SMALL", 3), Pizza("MEDIUM")], null, "SAVE5"), 500, 1811 },
    };

    [Theory]
    [MemberData(nameof(PromotionTotals))]
    public void Promotion_vectors_total_to_the_cent(string id, OrderDraft draft, long discount, long total)
    {
        var breakdown = Ok(Price(draft));

        (breakdown.Discount.Cents, breakdown.Total.Cents).ShouldBe((discount, total), id);
    }

    // PR7 / PR8: stacking with a compatible test promo on 6 x Small.
    [Theory]
    [InlineData(200, 500, 2663)]
    [InlineData(50, 480, 2684)]
    public void Stacked_promotions_total_to_the_cent(int extraPriority, long discount, long total)
    {
        var catalog = Spec(promotions: Promotions.PromotionFixtures.Seed(Promotions.PromotionFixtures.Fixed(50, "EXTRA2", 200, extraPriority)));

        var breakdown = Ok(Price(Draft([Pizza("SMALL", 6)], null, "ILOVEPIZZA", "EXTRA2"), catalog));

        (breakdown.Discount.Cents, breakdown.Total.Cents).ShouldBe((discount, total));
    }

    // A dollar tip shows its effective percentage of the bill, the same base a percent tip uses (A18):
    // P10: 300 / 2737 = 10.9609...% -> 109 609 ppm; it follows the bill when the cart changes.
    [Fact]
    public void Dollar_tip_reports_its_effective_rate_of_the_bill()
    {
        var p10 = Ok(Price(Draft([LargePepBacon(2)], Amount(300), "ILOVEPIZZA")));
        p10.Bill.ShouldBe(new Money(2737));
        p10.TipEffectiveRatePpm.ShouldBe(109_609);

        var smaller = Ok(Price(Draft([LargePepBacon(1)], Amount(300), "ILOVEPIZZA")));
        smaller.TipEffectiveRatePpm.ShouldBe(219_138);   // 300 / 1369 = 21.9138...%

        Ok(Price(Draft([LargePepBacon(2)], Percent(150_000)))).TipEffectiveRatePpm.ShouldBeNull();
        Ok(Price(Draft([LargePepBacon(2)]))).TipEffectiveRatePpm.ShouldBeNull();
        Ok(Price(Draft([], Amount(300)))).TipEffectiveRatePpm.ShouldBeNull();   // no bill to compare with
    }

    [Fact]
    public void Tip_is_not_taxed()
    {
        var without = Ok(Price(Draft([LargePepBacon(2)])));
        var with = Ok(Price(Draft([LargePepBacon(2)], Amount(10_000))));

        with.Tax.ShouldBe(without.Tax);
        with.Total.ShouldBe(without.Total + new Money(10_000));
    }

    // P23 / A23: an empty quote is $0.00 with ORDER_EMPTY, and cannot be submitted.
    [Fact]
    public void Empty_order_is_zero_with_ORDER_EMPTY()
    {
        var breakdown = Ok(Price(Draft([], Percent(180_000))));

        breakdown.Total.ShouldBe(Money.Zero);
        breakdown.Tip.ShouldBe(Money.Zero);
        breakdown.Issues.Select(i => i.Code).ShouldBe(["ORDER_EMPTY"]);
        breakdown.CanSubmit.ShouldBeFalse();
    }

    // P13: a held NotEligible code keeps the order from being submitted.
    [Fact]
    public void CanSubmit_false_for_NotEligible_code()
    {
        var breakdown = Ok(Price(Draft([Pizza("SMALL", 4)], null, "SAVE5")));

        breakdown.Promotions.Outcomes.Single().Status.ShouldBe(PromoStatus.NotEligible);
        breakdown.NormalizedDraft.PromoCodes.ShouldBe(["SAVE5"]);
        breakdown.CanSubmit.ShouldBeFalse();
        Ok(Price(Draft([Pizza("SMALL", 5)], null, "SAVE5"))).CanSubmit.ShouldBeTrue();
    }

    // P24: the rejected second code is dropped from the normalized draft, which then prices the same and can be submitted.
    [Fact]
    public void NormalizedDraft_resubmits_to_same_total()
    {
        var first = Ok(Price(Draft([LargePepBacon(2)], Percent(150_000), "ILOVEPIZZA", "SAVE5")));

        first.NormalizedDraft.PromoCodes.ShouldBe(["ILOVEPIZZA"]);
        var again = Ok(Price(first.NormalizedDraft));
        again.Total.ShouldBe(first.Total);
        again.CanSubmit.ShouldBeTrue();
        again.NormalizedDraft.ShouldBeEquivalentTo(first.NormalizedDraft);
    }

    // N7: omitted crust and sauce become explicit defaults; toppings are in canonical order and explicit WHOLE x1.
    [Fact]
    public void NormalizedDraft_names_default_crust_and_sauce()
    {
        var breakdown = Ok(Price(Draft([Pizza("large", 1, "bacon", " Pepperoni ")])));

        var line = breakdown.NormalizedDraft.LineItems.ShouldHaveSingleItem();
        line.SizeCode.ShouldBe("LARGE");
        line.CrustCode.ShouldBe("HAND_TOSSED");
        line.SauceCode.ShouldBe("CLASSIC_TOMATO");
        line.Toppings.ShouldBe([new DraftTopping("PEPPERONI", "WHOLE", 1), new DraftTopping("BACON", "WHOLE", 1)]);
        breakdown.Lines.Single().Line.UnitPrice.ShouldBe(new Money(1428));
    }

    // P26 (range part) and other field rules: every error is collected, each with its JSON path.
    [Fact]
    public void Validator_collects_all_errors_with_paths()
    {
        var draft = Draft(
            [
                Pizza("LARGE", 0),
                Pizza("LARGE", 21),
                Pizza("HUGE"),
                Pizza("PEPPERONI"),
                Pizza("MEDIUM", 1, "PEPPERONI", "ANCHOVIES"),
            ],
            Amount(50_001));

        var result = Price(draft);

        result.Breakdown.ShouldBeNull();
        result.Errors.Select(e => (e.Path, e.Code)).ShouldBe(
        [
            ("lineItems[0].quantity", "QUANTITY_OUT_OF_RANGE"),
            ("lineItems[1].quantity", "QUANTITY_OUT_OF_RANGE"),
            ("lineItems[2].sizeCode", "UNKNOWN_PRODUCT"),
            ("lineItems[3].sizeCode", "WRONG_PRODUCT_TYPE"),
            ("lineItems[4].toppings[1].code", "UNKNOWN_PRODUCT"),
            ("tip.value", "TIP_OUT_OF_RANGE"),
        ]);
        result.Errors[0].Message.ShouldBe("Quantity must be between 1 and 20.");
    }

    [Fact]
    public void Percent_tip_over_the_cap_is_out_of_range()
    {
        Price(Draft([Pizza("SMALL")], Percent(1_000_100))).Errors.Single().Code.ShouldBe("TIP_OUT_OF_RANGE");
        Price(Draft([Pizza("SMALL")], Percent(1_000_000))).Errors.ShouldBeEmpty();
        Price(Draft([Pizza("SMALL")], Amount(50_000))).Errors.ShouldBeEmpty();
    }

    // O8 at the engine: unavailable here -> PRODUCT_UNAVAILABLE at the right paths; fine at the default restaurant.
    [Fact]
    public void Unavailable_products_are_reported_where_they_are_unavailable()
    {
        var snapshot = SeedCatalog.Snapshot();
        var r2 = Spec(snapshot, new RestaurantOverrides(2, true,
        [
            new ProductOverrideRow(snapshot.IdOf("ARTICHOKE_HEARTS"), false, null),
            new ProductOverrideRow(snapshot.IdOf("MEDIUM"), false, null),
        ]));
        var draft = Draft([Pizza("MEDIUM"), Pizza("LARGE", 1, "ARTICHOKE_HEARTS")]);

        Price(draft, r2).Errors.Select(e => (e.Path, e.Code)).ShouldBe(
        [
            ("lineItems[0].sizeCode", "PRODUCT_UNAVAILABLE"),
            ("lineItems[1].toppings[0].code", "PRODUCT_UNAVAILABLE"),
        ]);
        Price(draft).Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Missing_default_crust_is_CRUST_REQUIRED()
    {
        var snapshot = SeedCatalog.Snapshot();
        var catalog = Spec(snapshot, new RestaurantOverrides(2, true, [new ProductOverrideRow(snapshot.IdOf("HAND_TOSSED"), false, null)]));

        Price(Draft([Pizza("SMALL")]), catalog).Errors.Select(e => (e.Path, e.Code)).ShouldBe([("lineItems[0].crustCode", "CRUST_REQUIRED")]);
    }

    [Fact]
    public void Limits_come_from_ordering_policy()
    {
        var policy = SeedPolicy with { MaxLines = 1, MaxLineQuantity = 2, MaxToppingsPerPizza = 1, MaxPromoCodes = 1, MaxTipAmountCents = 100 };
        var catalog = Spec(policy: policy);

        Price(Draft([Pizza("SMALL"), Pizza("SMALL")]), catalog).Errors.Single().ShouldBe(
            new FieldError("lineItems", "TOO_MANY_LINES", "An order can have at most 1 line."));
        Price(Draft([Pizza("SMALL", 3)]), catalog).Errors.Single().Code.ShouldBe("QUANTITY_OUT_OF_RANGE");
        Price(Draft([Pizza("SMALL", 1, "PEPPERONI", "BACON")]), catalog).Errors.Single().ShouldBe(
            new FieldError("lineItems[0].toppings", "TOO_MANY_TOPPINGS", "A pizza can have at most 1 topping."));
        Price(Draft([Pizza("SMALL")], Amount(101)), catalog).Errors.Single().Code.ShouldBe("TIP_OUT_OF_RANGE");
        Ok(Price(Draft([Pizza("SMALL")], null, "ILOVEPIZZA", "SAVE5"), catalog))
            .Promotions.Outcomes[1].Status.ShouldBe(PromoStatus.LimitExceeded);
    }

    // P26w / A69: half, quarter and extra toppings are designed but not built.
    [Fact]
    public void Built_scope_rejects_partial_and_extra_toppings()
    {
        var line = new PizzaDraftLine("LARGE", 1,
        [
            new DraftTopping("PEPPERONI", Portion: "LEFT"),
            new DraftTopping("BACON", Multiplier: 2),
            new DraftTopping("SAUSAGE"),
            new DraftTopping("SAUSAGE"),
        ]);

        var errors = Price(Draft([line])).Errors;

        errors.Select(e => (e.Path, e.Code)).ShouldBe(
        [
            ("lineItems[0].toppings[0]", "PLACEMENT_NOT_SUPPORTED"),
            ("lineItems[0].toppings[1]", "PLACEMENT_NOT_SUPPORTED"),
            ("lineItems[0].toppings[3]", "PLACEMENT_NOT_SUPPORTED"),
        ]);
        errors[0].Message.ShouldBe("Half, quarter and extra toppings are designed but not built in this version.");

        var explicitWhole = Ok(Price(Draft([new PizzaDraftLine("LARGE", 1, [new DraftTopping("PEPPERONI", "WHOLE", 1)])])));
        (explicitWhole.Subtotal.Cents, explicitWhole.Tax.Tax.Cents, explicitWhole.Total.Cents).ShouldBe((1296L, 84L, 1380L));
    }

    [Fact]
    public void Discount_allocations_sum_to_each_promo_and_cover_components()
    {
        var breakdown = Ok(Price(Draft(GoldenCart, null, "SAVE5")));

        breakdown.Allocations.Sum(a => a.Amount.Cents).ShouldBe(500);
        breakdown.Allocations.ShouldAllBe(a => a.Code == "SAVE5");
        // Allocations are weighted by component net, so the zero-priced crust and sauce get nothing.
        breakdown.Allocations.ShouldAllBe(a => a.Kind != ComponentKind.Crust && a.Kind != ComponentKind.Sauce || a.Amount == Money.Zero);
        breakdown.Tax.Lines.Single().Discount.ShouldBe(new Money(500));
    }
}
