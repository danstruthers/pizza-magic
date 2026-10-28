using PizzaMagic.Domain.Pricing;

namespace PizzaMagic.Domain.Ordering;

public enum DescriptionLineKind
{
    Item,
    Promotion,
    Tax,
    Tip,
    Total,
}

/// <summary>
/// One structured line of an order description. <see cref="Text"/> never contains money: amounts are in
/// cents and each host renders them with its formatter. A tip line carries its mode and rate (the entered
/// percent, or a dollar tip's effective rate) in basis points.
/// </summary>
public sealed record DescriptionLine(
    DescriptionLineKind Kind,
    string Text,
    long AmountCents,
    string? Code = null,
    TipMode? TipMode = null,
    long? RateBasisPoints = null);

/// <summary>The items (one per order line, in line order) and the footer (promotions, tax, tip, total).</summary>
public sealed record OrderDescription(IReadOnlyList<DescriptionLine> Items, IReadOnlyList<DescriptionLine> Footer);
