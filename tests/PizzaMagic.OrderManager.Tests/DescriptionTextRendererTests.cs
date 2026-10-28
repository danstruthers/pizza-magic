using PizzaMagic.Domain.Ordering;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.OrderManager.Services;

namespace PizzaMagic.OrderManager.Tests;

public class DescriptionTextRendererTests
{
    private static readonly DescriptionLine Item = new(DescriptionLineKind.Item, "2 x Large pizza with Pepperoni, Bacon", 2856);

    // P9 rendered as text.
    [Fact]
    public void Renders_items_promos_tax_percent_tip_and_total()
    {
        var description = new OrderDescription(
            [Item],
            [
                new DescriptionLine(DescriptionLineKind.Promotion, "Promo ILOVEPIZZA", -286, Code: "ILOVEPIZZA"),
                new DescriptionLine(DescriptionLineKind.Tax, "Tax", 167),
                new DescriptionLine(DescriptionLineKind.Tip, "Tip", 411, TipMode: TipMode.Percent, RateBasisPoints: 1500),
                new DescriptionLine(DescriptionLineKind.Total, "Total", 3148),
            ]);

        DescriptionTextRenderer.Render(description).ShouldBe(
            "2 x Large pizza with Pepperoni, Bacon — $28.56\n" +
            "Promo ILOVEPIZZA: -$2.86\n" +
            "Tax: $1.67\n" +
            "Tip (15%): $4.11\n" +
            "Total: $31.48");
    }

    // UQ22: a dollar tip shows its effective rate after it, with two decimals.
    [Fact]
    public void Dollar_tip_shows_effective_rate_in_parentheses()
    {
        var tip = new DescriptionLine(DescriptionLineKind.Tip, "Tip", 300, TipMode: TipMode.Amount, RateBasisPoints: 1096);

        DescriptionTextRenderer.RenderLine(tip).ShouldBe("Tip: $3.00 (10.96%)");
    }

    [Theory]
    [InlineData(1750, "Tip (17.5%): $5.32")]
    [InlineData(1825, "Tip (18.25%): $5.32")]
    public void Percent_tip_label_shows_the_rate_as_entered(long basisPoints, string expected)
    {
        var tip = new DescriptionLine(DescriptionLineKind.Tip, "Tip", 532, TipMode: TipMode.Percent, RateBasisPoints: basisPoints);

        DescriptionTextRenderer.RenderLine(tip).ShouldBe(expected);
    }
}
