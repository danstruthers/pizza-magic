using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;

namespace PizzaMagic.Domain.Ordering;

/// <summary>
/// Snapshot-shaped input for <see cref="OrderDescriber"/>: names, default flags and amounts exactly as an order
/// stores them, so a historical order is described from its snapshot alone and never from the live catalog.
/// </summary>
public sealed record DescriptionSource(
    IReadOnlyList<DescribedPizzaLine> Lines,
    IReadOnlyList<DescribedPromotion> Promotions,
    long TaxCents,
    TipMode TipMode,
    long? TipRateBasisPoints,
    long TipCents,
    long TotalCents)
{
    /// <summary>The description source for a quote or a freshly priced order.</summary>
    public static DescriptionSource FromBreakdown(PriceBreakdown breakdown, EffectiveMenu menu)
    {
        var lines = breakdown.Lines.Select(l => new DescribedPizzaLine(
            l.Line.Quantity,
            l.Line.Size.DisplayName,
            l.Line.Crust.DisplayName,
            l.Line.Crust.Code == menu.DefaultCrust?.Code,
            l.Line.Sauce.DisplayName,
            l.Line.Sauce.Code == menu.DefaultSauce?.Code,
            l.Line.Components.Where(c => c.Kind == ComponentKind.Topping).Select(c => ToppingPhrase.Of(c.Product.DisplayName, c.Multiplier)).ToList(),
            l.Line.LineTotal.Cents)).ToList();

        // Entry order, as stored orders list them, so a quote and its order describe promotions identically.
        var promotions = breakdown.Promotions.Outcomes
            .Where(o => o.Status == PromoStatus.Applied)
            .Select(o => new DescribedPromotion(o.Code!, o.Discount.Cents))
            .ToList();

        var tip = breakdown.NormalizedDraft.Tip;
        var tipRate = tip.Mode switch
        {
            // Entered percents have at most two decimals, so ppm / 100 is exact; rounding only guards bad input.
            TipMode.Percent => Rounding.RoundHalfAwayFromZero(tip.Value, Rate.MaxPpm / TipCalculator.BasisPointScale),
            TipMode.Amount => breakdown.TipEffectiveRateBasisPoints,
            _ => (long?)null,
        };

        return new DescriptionSource(lines, promotions, breakdown.Tax.Tax.Cents, tip.Mode, tipRate, breakdown.Tip.Cents, breakdown.Total.Cents);
    }
}

/// <summary>One order line as described: quantity, names, whether crust and sauce are the defaults, and its total.</summary>
public sealed record DescribedPizzaLine(
    int Quantity,
    string SizeName,
    string CrustName,
    bool CrustIsDefault,
    string SauceName,
    bool SauceIsDefault,
    IReadOnlyList<string> ToppingNames,
    long LineTotalCents);

/// <summary>An applied promotion and its discount.</summary>
public sealed record DescribedPromotion(string Code, long DiscountCents);
