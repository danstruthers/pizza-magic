namespace PizzaMagic.Domain.Ordering;

/// <summary>
/// The written description of an order (spec requirement 3): quantity, size and toppings for each line, then
/// the promotions, tax, tip and total. Structured and formatting-free; hosts render the text.
/// </summary>
public static class OrderDescriber
{
    public static OrderDescription Describe(DescriptionSource source)
    {
        var items = source.Lines.Select(l => new DescriptionLine(DescriptionLineKind.Item, ItemText(l), l.LineTotalCents)).ToList();

        var footer = new List<DescriptionLine>();
        footer.AddRange(source.Promotions.Select(p =>
            new DescriptionLine(DescriptionLineKind.Promotion, $"Promo {p.Code}", -p.DiscountCents, Code: p.Code)));
        footer.Add(new DescriptionLine(DescriptionLineKind.Tax, "Tax", source.TaxCents));
        if (source.TipCents > 0)
        {
            footer.Add(new DescriptionLine(DescriptionLineKind.Tip, "Tip", source.TipCents, TipMode: source.TipMode, RateBasisPoints: source.TipRateBasisPoints));
        }
        footer.Add(new DescriptionLine(DescriptionLineKind.Total, "Total", source.TotalCents));

        return new OrderDescription(items, footer);
    }

    /// <summary>"2 x Large pizza with Pepperoni, Bacon". The default crust and sauce are not named.</summary>
    private static string ItemText(DescribedPizzaLine line)
    {
        var extras = new List<string>();
        if (!line.CrustIsDefault)
        {
            extras.Add($"{line.CrustName} crust");
        }
        if (!line.SauceIsDefault)
        {
            extras.Add($"{line.SauceName} sauce");
        }
        extras.AddRange(line.ToppingNames);

        var pizza = $"{line.Quantity} x {line.SizeName} pizza";
        return extras.Count == 0 ? $"{pizza} (no toppings)" : $"{pizza} with {string.Join(", ", extras)}";
    }
}
