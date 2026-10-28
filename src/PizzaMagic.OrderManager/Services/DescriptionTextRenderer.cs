using PizzaMagic.Contracts;
using PizzaMagic.Domain.Ordering;
using PizzaMagic.Domain.Pricing;

namespace PizzaMagic.OrderManager.Services;

/// <summary>
/// Renders a structured <see cref="OrderDescription"/> as text, e.g. "2 x Large pizza with Pepperoni, Bacon — $28.56".
/// All money goes through <see cref="MoneyFormatter"/> and all rates through <see cref="PercentFormatter"/>.
/// </summary>
public static class DescriptionTextRenderer
{
    public static string Render(OrderDescription description) =>
        string.Join("\n", description.Items.Concat(description.Footer).Select(RenderLine));

    public static string RenderLine(DescriptionLine line) => line.Kind switch
    {
        DescriptionLineKind.Item => $"{line.Text} — {MoneyFormatter.Format(line.AmountCents)}",
        DescriptionLineKind.Tip => RenderTip(line),
        _ => $"{line.Text}: {MoneyFormatter.Format(line.AmountCents)}",
    };

    /// <summary>
    /// A percent tip shows the rate as entered in its label ("Tip (15%): $4.11"); a dollar tip shows its effective
    /// rate of the bill after the amount, with two decimals ("Tip: $3.00 (10.96%)", UQ22).
    /// </summary>
    private static string RenderTip(DescriptionLine line)
    {
        var amount = MoneyFormatter.Format(line.AmountCents);
        return (line.TipMode, line.RateBasisPoints) switch
        {
            (TipMode.Percent, { } rate) => $"{line.Text} ({PercentFormatter.FormatBasisPointsCompact(rate)}): {amount}",
            (TipMode.Amount, { } rate) => $"{line.Text}: {amount} ({PercentFormatter.FormatBasisPoints(rate)})",
            _ => $"{line.Text}: {amount}",
        };
    }
}
