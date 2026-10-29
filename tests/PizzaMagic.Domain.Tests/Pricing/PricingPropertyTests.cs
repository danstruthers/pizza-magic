using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Pricing;
using static PizzaMagic.Domain.Tests.Pricing.TestCatalogs;

namespace PizzaMagic.Domain.Tests.Pricing;

/// <summary>
/// Properties over 500 drafts from the built generator (plan section 7.1): pizza lines of every size and
/// quantity with whole 1x toppings (each at most once), the default crust and sauce, built promo codes,
/// and both tip modes. The catalog has a single tax rate and only order-scoped promotions.
/// </summary>
public class PricingPropertyTests
{
    private static readonly string[] Sizes = ["SMALL", "MEDIUM", "LARGE"];
    private static readonly string[] Toppings =
    [
        "PEPPERONI", "SAUSAGE", "EXTRA_CHEESE", "ONIONS", "BLACK_OLIVES", "GREEN_PEPPERS",
        "BACON", "CHICKEN", "GARLIC", "ARTICHOKE_HEARTS", "SUN_DRIED_TOMATOES", "PINEAPPLE",
    ];
    private static readonly string[] Codes = ["ILOVEPIZZA", "SAVE5", "NOPE"];

    public static IEnumerable<object[]> Drafts()
    {
        var random = new Random(20261028);
        for (var i = 0; i < 500; i++)
        {
            var lines = Enumerable.Range(0, random.Next(1, 5))
                .Select(_ => new PizzaDraftLine(
                    Sizes[random.Next(Sizes.Length)],
                    random.Next(1, 6),
                    Toppings.OrderBy(_ => random.Next()).Take(random.Next(0, 5)).Select(t => new DraftTopping(t)).ToList()))
                .ToList();
            var codes = Codes.OrderBy(_ => random.Next()).Take(random.Next(0, 3)).ToArray();
            var tip = random.Next(3) switch
            {
                0 => TipInput.None,
                1 => new TipInput(TipMode.Amount, random.Next(0, 2000)),
                _ => new TipInput(TipMode.Percent, random.Next(0, 300_001)),
            };
            yield return [i, new OrderDraft(lines, codes, tip)];
        }
    }

    private static PriceBreakdown Ok(OrderDraft draft)
    {
        var result = Price(draft);
        result.Errors.ShouldBeEmpty();
        return result.Breakdown!;
    }

    [Theory]
    [MemberData(nameof(Drafts))]
    public void Pricing_invariants_hold(int seed, OrderDraft draft)
    {
        var b = Ok(draft);

        // Identity and bounds.
        b.Total.ShouldBe(b.Subtotal - b.Discount + b.Tax.Tax + b.Tip, $"draft {seed}");
        b.Bill.ShouldBe(b.Subtotal - b.Discount + b.Tax.Tax);
        b.Taxable.ShouldBe(b.Subtotal - b.Discount);
        b.Discount.Cents.ShouldBeInRange(0, b.Subtotal.Cents);
        new[] { b.Subtotal, b.Tax.Tax, b.Tip, b.Total }.ShouldAllBe(m => m.Cents >= 0);
        b.Subtotal.Cents.ShouldBe(b.Lines.Sum(l => l.Line.LineTotal.Cents));
        b.Tax.Tax.Cents.ShouldBe(b.Tax.Lines.Sum(l => l.Amount.Cents));
        b.Tax.Lines.Sum(l => l.Taxable.Cents).ShouldBe(b.Taxable.Cents);

        // Allocation: each applied promo's allocations sum to its discount; no component goes negative.
        foreach (var applied in b.Promotions.Outcomes.Where(o => o.Discount.Cents > 0))
        {
            b.Allocations.Where(a => a.Code == applied.Code).Sum(a => a.Amount.Cents).ShouldBe(applied.Discount.Cents);
        }
        foreach (var line in b.Lines)
        {
            foreach (var component in line.Line.Components)
            {
                var gross = component.UnitCharge.Cents * line.Line.Quantity;
                var allocated = b.Allocations
                    .Where(a => a.LineNumber == line.LineNumber && a.Kind == component.Kind && a.ProductCode == component.Product.Code)
                    .Sum(a => a.Amount.Cents);
                (gross - allocated).ShouldBeGreaterThanOrEqualTo(0);
            }
        }

        // Determinism.
        Ok(draft).ShouldBeEquivalentTo(b);
    }

    // An oracle written straight from the spec's table, sharing no code with the engine, so a wrong rate or rule can't
    // pass by being consistent with itself: sizes $5/$7/$12; basic 8% and premium 11% of the size, per pizza; ILOVEPIZZA
    // 10% before tax; SAVE5 $5 over $20 before tax; never both (the code entered first holds); tax 6.5% of the
    // discounted total; tip in dollars or as a % of the bill. Half-up to whole cents at each step, before any x qty.
    [Theory]
    [MemberData(nameof(Drafts))]
    public void Matches_an_independent_calculation_of_the_spec_rules(int seed, OrderDraft draft)
    {
        var sizePrice = new Dictionary<string, long> { ["SMALL"] = 500, ["MEDIUM"] = 700, ["LARGE"] = 1200 };
        var premium = new HashSet<string> { "BACON", "CHICKEN", "GARLIC", "ARTICHOKE_HEARTS", "SUN_DRIED_TOMATOES", "PINEAPPLE" };
        static long HalfUp(long numerator, long denominator) => (numerator * 2 + denominator) / (denominator * 2);

        var subtotal = draft.LineItems.Sum(line =>
        {
            var size = sizePrice[line.SizeCode];
            var toppings = line.Toppings.Sum(t => HalfUp(size * (premium.Contains(t.Code) ? 11 : 8), 100));
            return (size + toppings) * line.Quantity;
        });
        var held = draft.PromoCodes.FirstOrDefault(c => c is "ILOVEPIZZA" or "SAVE5");
        var discount = held switch
        {
            "ILOVEPIZZA" => HalfUp(subtotal * 10, 100),
            "SAVE5" when subtotal > 2000 => 500,
            _ => 0,
        };
        var tax = HalfUp((subtotal - discount) * 65, 1000);
        var bill = subtotal - discount + tax;
        var tip = draft.Tip.Mode switch
        {
            TipMode.Amount => draft.Tip.Value,
            TipMode.Percent => HalfUp(bill * draft.Tip.Value, 1_000_000),
            _ => 0,
        };

        var b = Ok(draft);

        (b.Subtotal.Cents, b.Discount.Cents, b.Tax.Tax.Cents, b.Tip.Cents, b.Total.Cents)
            .ShouldBe((subtotal, discount, tax, tip, bill + tip), $"draft {seed}");
        b.Promotions.Outcomes.Count(o => o.Code is "ILOVEPIZZA" or "SAVE5" && o.Discount.Cents > 0).ShouldBeLessThanOrEqualTo(1);
    }

    [Theory]
    [MemberData(nameof(Drafts))]
    public void Line_order_does_not_change_the_price(int seed, OrderDraft draft)
    {
        var b = Ok(draft);
        var reversed = Ok(draft with { LineItems = draft.LineItems.Reverse().ToList() });

        (reversed.Subtotal, reversed.Discount, reversed.Tax.Tax, reversed.Total).ShouldBe((b.Subtotal, b.Discount, b.Tax.Tax, b.Total), $"draft {seed}");
    }

    [Theory]
    [MemberData(nameof(Drafts))]
    public void Splitting_a_line_into_single_pizzas_keeps_the_subtotal(int seed, OrderDraft draft)
    {
        var split = draft with
        {
            LineItems = draft.LineItems.SelectMany(l => Enumerable.Repeat(l with { Quantity = 1 }, l.Quantity)).ToList(),
        };
        var policy = SeedPolicy with { MaxLines = 100 };

        var b = Ok(draft);
        var s = Engine.Price(split, Spec(policy: policy), Now).Breakdown!;

        s.Subtotal.ShouldBe(b.Subtotal, $"draft {seed}");
    }
}
