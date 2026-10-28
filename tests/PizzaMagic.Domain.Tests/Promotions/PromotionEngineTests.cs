using PizzaMagic.Contracts;
using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tests.Time;
using static PizzaMagic.Domain.Tests.Promotions.PromotionFixtures;

namespace PizzaMagic.Domain.Tests.Promotions;

public class PromotionEngineTests
{
    private static PromotionResult Run(long grossCents, params string[] codes) => Evaluate(Seed(), Context(grossCents), codes);

    private static string Text(CodeOutcome outcome) => outcome.Message!.Render(MoneyFormatter.Format);

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    // PR1: 10% of 1524 = 152.4 -> 152.
    [Fact]
    public void ILovePizza_takes_ten_percent_rounded()
    {
        var result = Run(1524, "ILOVEPIZZA");

        result.TotalDiscount.ShouldBe(new Money(152));
        result.Outcomes.Single().Status.ShouldBe(PromoStatus.Applied);
        result.Outcomes.Single().Discount.ShouldBe(new Money(152));
    }

    // PR3: the earlier code holds; the later conflicting code is rejected and names the held code.
    [Fact]
    public void Later_conflicting_code_is_rejected_naming_held_code()
    {
        var result = Run(2955, "ILOVEPIZZA", "SAVE5");

        result.TotalDiscount.ShouldBe(new Money(296));
        result.Outcomes[0].Status.ShouldBe(PromoStatus.Applied);
        result.Outcomes[1].Status.ShouldBe(PromoStatus.Rejected);
        result.Outcomes[1].ConflictsWith.ShouldBe(["ILOVEPIZZA"]);
        result.Outcomes[1].Discount.ShouldBe(Money.Zero);
        Text(result.Outcomes[1]).ShouldBe("SAVE5 can't be combined with ILOVEPIZZA. Remove ILOVEPIZZA to use SAVE5.");
        result.NormalizedPromoCodes.ShouldBe(["ILOVEPIZZA"]);
    }

    // PR4: entry order decides which code holds.
    [Fact]
    public void Entry_order_decides_which_conflicting_code_holds()
    {
        var result = Run(2955, "SAVE5", "ILOVEPIZZA");

        result.TotalDiscount.ShouldBe(new Money(500));
        result.Outcomes[1].Status.ShouldBe(PromoStatus.Rejected);
        result.Outcomes[1].ConflictsWith.ShouldBe(["SAVE5"]);
        result.NormalizedPromoCodes.ShouldBe(["SAVE5"]);
    }

    // PR5: a rejected code is never evaluated for eligibility.
    [Fact]
    public void Rejected_code_is_not_evaluated()
    {
        var result = Run(1524, "ILOVEPIZZA", "SAVE5");

        result.TotalDiscount.ShouldBe(new Money(152));
        result.Outcomes[1].Status.ShouldBe(PromoStatus.Rejected);
        result.NormalizedPromoCodes.ShouldBe(["ILOVEPIZZA"]);
    }

    // PR5b: conflicts are static, so a held NotEligible code still blocks.
    [Fact]
    public void Held_NotEligible_code_still_blocks()
    {
        var result = Run(1524, "SAVE5", "ILOVEPIZZA");

        result.TotalDiscount.ShouldBe(Money.Zero);
        result.Outcomes[0].Status.ShouldBe(PromoStatus.NotEligible);
        Text(result.Outcomes[0]).ShouldBe("SAVE5 requires an order subtotal over $20.00 (yours is $15.24).");
        result.Outcomes[1].Status.ShouldBe(PromoStatus.Rejected);
        Text(result.Outcomes[1]).ShouldBe("ILOVEPIZZA can't be combined with SAVE5. Remove SAVE5 to use ILOVEPIZZA.");
        result.NormalizedPromoCodes.ShouldBe(["SAVE5"]);
    }

    // PR6: SAVE5 is kept while not eligible and applies once the subtotal passes $20.00.
    [Fact]
    public void NotEligible_code_is_kept_and_applies_once_the_cart_qualifies()
    {
        var small = Run(1500, "SAVE5");
        small.Outcomes.Single().Status.ShouldBe(PromoStatus.NotEligible);
        small.NormalizedPromoCodes.ShouldBe(["SAVE5"]);

        var bigger = Run(2200, "SAVE5");
        bigger.Outcomes.Single().Status.ShouldBe(PromoStatus.Applied);
        bigger.TotalDiscount.ShouldBe(new Money(500));
    }

    [Theory]
    [InlineData(2000, false, false)]   // SAVE5 is strictly "over $20"
    [InlineData(2001, false, true)]
    [InlineData(2000, true, true)]
    [InlineData(1999, true, false)]
    public void Min_subtotal_strict_and_inclusive(long gross, bool inclusive, bool eligible)
    {
        var promo = new PromotionDefinition(70, "MIN20", "fixed_off_order", """{"amountCents":100}""")
        {
            Conditions = [new ConditionDefinition("min_subtotal", $$"""{"amountCents":2000,"inclusive":{{(inclusive ? "true" : "false")}}}""")],
        };

        var outcome = Evaluate(Seed(promo), Context(gross), "MIN20").Outcomes.Single();

        outcome.Status.ShouldBe(eligible ? PromoStatus.Applied : PromoStatus.NotEligible);
        if (!eligible)
        {
            Text(outcome).ShouldBe(inclusive
                ? $"MIN20 requires an order subtotal of at least $20.00 (yours is {MoneyFormatter.Format(gross)})."
                : $"MIN20 requires an order subtotal over $20.00 (yours is {MoneyFormatter.Format(gross)}).");
        }
    }

    // PR7 / PR8: compatible promos stack in (priority, id) order, each on what remains.
    [Theory]
    [InlineData(200, 500)]
    [InlineData(50, 480)]
    public void Compatible_promos_stack_in_priority_order(int extraPriority, long expectedDiscount)
    {
        var catalog = Seed(Fixed(50, "EXTRA2", 200, extraPriority));

        Evaluate(catalog, Context(3000), "ILOVEPIZZA", "EXTRA2").TotalDiscount.ShouldBe(new Money(expectedDiscount));
        Evaluate(catalog, Context(3000), "EXTRA2", "ILOVEPIZZA").TotalDiscount.ShouldBe(new Money(expectedDiscount));
    }

    // PR9: a fixed discount never exceeds what remains.
    [Fact]
    public void Fixed_discount_is_clamped_to_the_remaining_amount()
    {
        Evaluate(Seed(Fixed(51, "BIG10", 1000)), Context(500), "BIG10").TotalDiscount.ShouldBe(new Money(500));
    }

    [Fact]
    public void Effect_that_computes_zero_is_not_eligible()
    {
        // BIG10 (priority 10) applies first and uses up the whole 500; ILOVEPIZZA then computes 0.
        var result = Evaluate(Seed(Fixed(51, "BIG10", 1000, priority: 10)), Context(500), "BIG10", "ILOVEPIZZA");

        result.Outcomes[0].Discount.ShouldBe(new Money(500));
        result.Outcomes[1].Status.ShouldBe(PromoStatus.NotEligible);
        Text(result.Outcomes[1]).ShouldBe("ILOVEPIZZA doesn't reduce this order any further.");
    }

    [Fact]
    public void Percent_discount_respects_its_cap()
    {
        var capped = new PromotionDefinition(52, "HALFOFF", "percent_off_order", """{"ratePpm":500000,"maxDiscountCents":800}""");

        Evaluate(Seed(capped), Context(4800), "HALFOFF").TotalDiscount.ShouldBe(new Money(800));
        Evaluate(Seed(capped), Context(1000), "HALFOFF").TotalDiscount.ShouldBe(new Money(500));
    }

    // PR12: duplicates collapse silently, whether the same code or another code for the same promotion.
    [Fact]
    public void Duplicate_codes_collapse_into_the_first()
    {
        var catalog = Seed() with { Codes = [.. Seed().Codes, new PromotionCodeRow("SAVEFIVE", 2)] };

        var result = Evaluate(catalog, Context(2100), "SAVE5", "save5", "SAVEFIVE");

        result.Outcomes.Select(o => o.Status).ShouldBe([PromoStatus.Applied, PromoStatus.Duplicate, PromoStatus.Duplicate]);
        result.TotalDiscount.ShouldBe(new Money(500));
        result.NormalizedPromoCodes.ShouldBe(["SAVE5"]);
    }

    // PR17 and code format rules.
    [Theory]
    [InlineData("I LOVE PIZZA")]
    [InlineData("")]
    [InlineData("SAVE_5")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]   // 33 characters
    public void Malformed_code_is_InvalidFormat(string code)
    {
        var outcome = Run(2100, code).Outcomes.Single();

        outcome.Status.ShouldBe(PromoStatus.InvalidFormat);
        Text(outcome).ShouldBe("Promo codes contain only letters, digits and dashes.");
        Run(2100, code).NormalizedPromoCodes.ShouldBeEmpty();
    }

    [Fact]
    public void Codes_are_trimmed_and_upper_cased()
    {
        var result = Run(1524, "  ilovepizza ");

        result.Outcomes.Single().Code.ShouldBe("ILOVEPIZZA");
        result.TotalDiscount.ShouldBe(new Money(152));
    }

    // PR18: distinct codes beyond the policy limit are LimitExceeded.
    [Fact]
    public void Codes_beyond_the_limit_are_LimitExceeded()
    {
        var result = Run(2100, "ILOVEPIZZA", "A", "B", "C");

        result.Outcomes[3].Status.ShouldBe(PromoStatus.LimitExceeded);
        Text(result.Outcomes[3]).ShouldBe("You can use up to 3 promo codes per order.");
    }

    // PR18b: duplicates collapse before the limit is applied.
    [Fact]
    public void Duplicates_collapse_before_the_limit()
    {
        var result = Run(2100, "SAVE5", "save5", "A", "B");

        result.Outcomes.Select(o => o.Status).ShouldBe(
            [PromoStatus.Applied, PromoStatus.Duplicate, PromoStatus.Unknown, PromoStatus.Unknown]);
        result.TotalDiscount.ShouldBe(new Money(500));
    }

    // PR13: row-level gates.
    [Fact]
    public void Unknown_and_inactive_codes()
    {
        var inactivePromo = Fixed(53, "OLDPROMO", 100) with { IsActive = false };
        var catalog = Seed(inactivePromo) with { Codes = [.. Seed(inactivePromo).Codes, new PromotionCodeRow("RETIRED", 1, IsActive: false)] };

        var result = Evaluate(catalog, Context(2100), "PIZZA99", "OLDPROMO", "RETIRED");

        result.Outcomes.Select(o => o.Status).ShouldBe([PromoStatus.Unknown, PromoStatus.Inactive, PromoStatus.Inactive]);
        Text(result.Outcomes[0]).ShouldBe("PIZZA99 isn't a valid promo code.");
        result.TotalDiscount.ShouldBe(Money.Zero);
    }

    [Theory]
    [InlineData("2026-10-27T06:59:59.999Z", PromoStatus.NotYetActive)]   // Mon 23:59:59.999 PDT
    [InlineData("2026-10-27T07:00:00.000Z", PromoStatus.Applied)]        // Tue 00:00 PDT (= from)
    [InlineData("2026-10-28T06:59:59.999Z", PromoStatus.Applied)]
    [InlineData("2026-10-28T07:00:00.000Z", PromoStatus.Expired)]        // Wed 00:00 PDT (= to, exclusive)
    public void Validity_window_is_local_and_half_open(string now, PromoStatus expected)
    {
        var tuesday = Fixed(54, "TUESDAY", 100) with
        {
            ValidFromLocal = new DateTime(2026, 10, 27, 0, 0, 0),
            ValidToLocal = new DateTime(2026, 10, 28, 0, 0, 0),
        };

        Evaluate(Seed(tuesday), Context(2100, Utc(now)), "TUESDAY").Outcomes.Single().Status.ShouldBe(expected);
    }

    // PR13b: boundaries resolve to the earliest UTC instant, so nothing re-opens in the repeated hour.
    [Theory]
    [InlineData("2026-11-01T01:30:00", "2026-11-01T08:29:59.999Z", PromoStatus.Applied)]
    [InlineData("2026-11-01T01:30:00", "2026-11-01T08:30:00.000Z", PromoStatus.Expired)]
    [InlineData("2026-11-01T01:30:00", "2026-11-01T09:00:00.000Z", PromoStatus.Expired)]   // 01:00 PST again
    [InlineData("2026-11-01T01:30:00", "2026-11-01T09:15:00.000Z", PromoStatus.Expired)]
    [InlineData("2026-03-08T02:30:00", "2026-03-08T09:59:59.999Z", PromoStatus.Applied)]   // 01:59:59.999 PST
    [InlineData("2026-03-08T02:30:00", "2026-03-08T10:00:00.000Z", PromoStatus.Expired)]   // 03:00 PDT
    public void Validity_end_across_dst(string validToLocal, string now, PromoStatus expected)
    {
        var promo = Fixed(55, "DSTNIGHT", 100) with
        {
            ValidToLocal = DateTime.Parse(validToLocal, System.Globalization.CultureInfo.InvariantCulture),
        };

        Evaluate(Seed(promo), Context(2100, Utc(now)), "DSTNIGHT").Outcomes.Single().Status.ShouldBe(expected);
    }

    // PR13c: each restaurant resolves the same local values in its own zone.
    [Fact]
    public void Each_restaurant_resolves_local_boundaries_in_its_own_zone()
    {
        var promo = Fixed(56, "NOVEMBER", 100) with { ValidToLocal = new DateTime(2026, 12, 1, 0, 0, 0) };
        var now = Utc("2026-12-01T06:00:00.000Z");

        var newYork = Context(2100, now) with { Zone = UsTimeZones.NewYork() };
        Evaluate(Seed(promo), newYork, "NOVEMBER").Outcomes.Single().Status.ShouldBe(PromoStatus.Expired);
        Evaluate(Seed(promo), Context(2100, now), "NOVEMBER").Outcomes.Single().Status.ShouldBe(PromoStatus.Applied);
    }

    [Fact]
    public void Promotion_restricted_to_other_restaurants_is_NotAvailableHere()
    {
        var promo = Fixed(57, "R2ONLY", 100) with { RestaurantIds = [2] };

        Evaluate(Seed(promo), Context(2100), "R2ONLY").Outcomes.Single().Status.ShouldBe(PromoStatus.NotAvailableHere);
        Evaluate(Seed(promo), Context(2100, restaurantId: 2), "R2ONLY").Outcomes.Single().Status.ShouldBe(PromoStatus.Applied);
    }

    // TZ7 / fail closed: a window that cannot be resolved makes the promo Misconfigured; others still apply.
    [Fact]
    public void Window_outside_time_zone_coverage_is_Misconfigured()
    {
        var promo = Fixed(58, "FARFUTURE", 100) with { ValidToLocal = new DateTime(2045, 1, 1, 0, 0, 0) };

        var result = Evaluate(Seed(promo), Context(2100), "FARFUTURE", "ILOVEPIZZA");

        result.Outcomes[0].Status.ShouldBe(PromoStatus.Misconfigured);
        result.Outcomes[1].Status.ShouldBe(PromoStatus.Applied);
        result.TotalDiscount.ShouldBe(new Money(210));
    }

    // PR14 (built cases): bad data fails closed as Misconfigured, $0, and other codes still apply.
    [Theory]
    [InlineData("bogus", """{}""")]
    [InlineData("min_subtotal", """{"amountCents":-1,"inclusive":false}""")]
    [InlineData("min_subtotal", """{"amountCents":20.5,"inclusive":false}""")]
    [InlineData("min_subtotal", """{"inclusive":false}""")]
    public void Bad_condition_data_is_Misconfigured(string type, string parameters)
    {
        var promo = Fixed(59, "BROKEN", 100) with { Conditions = [new ConditionDefinition(type, parameters)] };

        var result = Evaluate(Seed(promo), Context(2100), "BROKEN", "ILOVEPIZZA");

        result.Outcomes[0].Status.ShouldBe(PromoStatus.Misconfigured);
        result.Outcomes[0].Discount.ShouldBe(Money.Zero);
        Text(result.Outcomes[0]).ShouldBe("BROKEN can't be applied right now.");
        result.Outcomes[1].Status.ShouldBe(PromoStatus.Applied);
        result.TotalDiscount.ShouldBe(new Money(210));
    }

    [Theory]
    [InlineData("free_pizza", """{}""")]
    [InlineData("fixed_off_order", """{"amountCents":"500"}""")]
    [InlineData("percent_off_order", """{"ratePpm":1000001,"maxDiscountCents":null}""")]
    [InlineData("percent_off_order", """{"ratePpm":100000,"maxDiscountCents":-5}""")]
    [InlineData("fixed_off_order", """not json""")]
    public void Bad_effect_data_is_Misconfigured(string type, string parameters)
    {
        var promo = new PromotionDefinition(59, "BROKEN", type, parameters);

        Evaluate(Seed(promo), Context(2100), "BROKEN").Outcomes.Single().Status.ShouldBe(PromoStatus.Misconfigured);
    }

    // PR15: exclusivity and exclusion groups.
    [Fact]
    public void Exclusive_and_group_conflicts()
    {
        var exclusive = Fixed(60, "EXCL", 200) with { IsExclusive = true };
        var g1 = Fixed(61, "G1", 100) with { ExclusionGroup = "G" };
        var g2 = Fixed(62, "G2", 100) with { ExclusionGroup = "G" };
        var catalog = Seed(exclusive, g1, g2);
        PromotionResult Go(params string[] codes) => Evaluate(catalog, Context(3000), codes);

        var a = Go("EXCL", "ILOVEPIZZA");
        a.TotalDiscount.ShouldBe(new Money(200));
        a.Outcomes[1].ConflictsWith.ShouldBe(["EXCL"]);

        var b = Go("ILOVEPIZZA", "EXCL");
        b.TotalDiscount.ShouldBe(new Money(300));
        b.Outcomes[1].Status.ShouldBe(PromoStatus.Rejected);

        var c = Go("G1", "G2");
        c.TotalDiscount.ShouldBe(new Money(100));
        c.Outcomes[1].ConflictsWith.ShouldBe(["G1"]);

        var d = Go("G1", "ILOVEPIZZA");
        d.TotalDiscount.ShouldBe(new Money(400));   // ILOVEPIZZA (id 1) applies first: 300, then G1: 100

        var e = Go("G1", "ILOVEPIZZA", "EXCL");
        e.TotalDiscount.ShouldBe(new Money(400));
        e.Outcomes[2].ConflictsWith.ShouldBe(["G1", "ILOVEPIZZA"]);
        Text(e.Outcomes[2]).ShouldBe("EXCL can't be combined with G1 or ILOVEPIZZA. Remove G1 and ILOVEPIZZA to use EXCL.");
    }

    [Fact]
    public void Codes_dropped_at_the_gates_block_nothing()
    {
        // Exclusive promos that fail a gate are never held, so they cannot block later codes.
        var expired = Fixed(63, "GONE", 100) with { ValidToLocal = new DateTime(2026, 1, 1, 0, 0, 0), IsExclusive = true };
        var inactive = Fixed(64, "OFF", 100) with { IsActive = false, IsExclusive = true };

        var result = Evaluate(Seed(expired, inactive), Context(2955), "GONE", "OFF", "ILOVEPIZZA");

        result.Outcomes.Select(o => o.Status).ShouldBe([PromoStatus.Expired, PromoStatus.Inactive, PromoStatus.Applied]);
        result.TotalDiscount.ShouldBe(new Money(296));
    }

    [Fact]
    public void Registry_rejects_duplicate_kinds()
    {
        Should.Throw<ArgumentException>(() => new PromotionRegistry([new MinSubtotal(), new MinSubtotal()], [new FixedOffOrder()]));
        Should.Throw<ArgumentException>(() => new PromotionRegistry([new MinSubtotal()], [new FixedOffOrder(), new FixedOffOrder()]));
    }

    // Discount bounds property (fixed seed, 1000 cases): 0 <= each discount, sum <= gross, deterministic.
    [Fact]
    public void Discounts_stay_within_bounds()
    {
        var random = new Random(20261028);
        var promos = new[]
        {
            Fixed(80, "F1", 0), Fixed(81, "F2", 250), Fixed(82, "F3", 5000),
            new PromotionDefinition(83, "P1", "percent_off_order", """{"ratePpm":333333,"maxDiscountCents":null}"""),
            new PromotionDefinition(84, "P2", "percent_off_order", """{"ratePpm":1000000,"maxDiscountCents":700}""", Priority: 10),
        };
        var catalog = Seed(promos);
        var codes = new[] { "ILOVEPIZZA", "SAVE5", "F1", "F2", "F3", "P1", "P2" };

        for (var i = 0; i < 1000; i++)
        {
            var gross = random.Next(0, 10_000);
            var entered = Enumerable.Range(0, random.Next(0, 4)).Select(_ => codes[random.Next(codes.Length)]).ToArray();

            var result = Evaluate(catalog, Context(gross), entered);

            result.Outcomes.ShouldAllBe(o => o.Discount.Cents >= 0);
            result.TotalDiscount.Cents.ShouldBeLessThanOrEqualTo(gross);
            result.TotalDiscount.Cents.ShouldBe(result.Outcomes.Sum(o => o.Discount.Cents));
            Evaluate(catalog, Context(gross), entered).ShouldBeEquivalentTo(result);
        }
    }
}
