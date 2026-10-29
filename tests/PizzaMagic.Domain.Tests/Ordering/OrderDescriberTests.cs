using PizzaMagic.Domain.Ordering;
using PizzaMagic.Domain.Pricing;
using static PizzaMagic.Domain.Tests.Pricing.TestCatalogs;

namespace PizzaMagic.Domain.Tests.Ordering;

public class OrderDescriberTests
{
    private static OrderDescription Describe(OrderDraft draft)
    {
        var catalog = Spec();
        var breakdown = Engine.Price(draft, catalog, Now).Breakdown!;
        return OrderDescriber.Describe(DescriptionSource.FromBreakdown(breakdown, catalog.Menu));
    }

    [Fact]
    public void Describes_qty_size_toppings()
    {
        var item = Describe(Draft([Pizza("LARGE", 2, "PEPPERONI", "BACON")])).Items.Single();

        item.ShouldBe(new DescriptionLine(DescriptionLineKind.Item, "2 x Large pizza with Pepperoni, Bacon", 2856));
    }

    [Fact]
    public void No_toppings_phrase()
    {
        Describe(Draft([Pizza("SMALL", 3)])).Items.Single().Text.ShouldBe("3 x Small pizza (no toppings)");
    }

    [Fact]
    public void Toppings_in_canonical_order()
    {
        Describe(Draft([Pizza("MEDIUM", 1, "PINEAPPLE", "BACON", "EXTRA_CHEESE")])).Items.Single().Text
            .ShouldBe("1 x Medium pizza with Extra cheese, Bacon, Pineapple");
    }

    [Fact]
    public void One_item_per_line_in_line_order()
    {
        Describe(Draft([Pizza("SMALL"), Pizza("LARGE", 1, "GARLIC")])).Items.Select(i => i.Text)
            .ShouldBe(["1 x Small pizza (no toppings)", "1 x Large pizza with Garlic"]);
    }

    // P9: footer lines carry amounts in cents and the tip's mode and rate; each host formats the money.
    [Fact]
    public void Footer_lines_structured()
    {
        var footer = Describe(Draft([Pizza("LARGE", 2, "PEPPERONI", "BACON")], new TipInput(TipMode.Percent, 150_000), "ILOVEPIZZA")).Footer;

        footer.ShouldBe(
        [
            new DescriptionLine(DescriptionLineKind.Promotion, "Promo ILOVEPIZZA", -286, Code: "ILOVEPIZZA"),
            new DescriptionLine(DescriptionLineKind.Tax, "Tax", 167),
            new DescriptionLine(DescriptionLineKind.Tip, "Tip", 411, TipMode: TipMode.Percent, RateBasisPoints: 1500),
            new DescriptionLine(DescriptionLineKind.Total, "Total", 3148),
        ]);
    }

    [Fact]
    public void Dollar_tip_carries_its_effective_rate()
    {
        var tip = Describe(Draft([Pizza("LARGE", 2, "PEPPERONI", "BACON")], new TipInput(TipMode.Amount, 300), "ILOVEPIZZA"))
            .Footer.Single(l => l.Kind == DescriptionLineKind.Tip);

        tip.ShouldBe(new DescriptionLine(DescriptionLineKind.Tip, "Tip", 300, TipMode: TipMode.Amount, RateBasisPoints: 1096));
    }

    [Fact]
    public void No_promo_or_tip_lines_when_there_are_none()
    {
        Describe(Draft([Pizza("SMALL")], null, "SAVE5")).Footer.Select(l => l.Kind)
            .ShouldBe([DescriptionLineKind.Tax, DescriptionLineKind.Total]);   // SAVE5 is NotEligible: no discount line
    }

    [Fact]
    public void Default_crust_and_sauce_are_not_named()
    {
        var source = DescriptionSource.FromBreakdown(Engine.Price(Draft([Pizza("SMALL")]), Spec(), Now).Breakdown!, Spec().Menu);

        var line = source.Lines.Single();
        (line.CrustName, line.CrustIsDefault, line.SauceName, line.SauceIsDefault).ShouldBe(("Hand tossed", true, "Classic tomato", true));
        OrderDescriber.Describe(source).Items.Single().Text.ShouldNotContain("Hand tossed");
    }
}
