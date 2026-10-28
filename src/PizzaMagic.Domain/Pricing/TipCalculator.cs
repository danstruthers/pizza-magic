using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Pricing;

/// <summary>
/// The tip on the bill (subtotal - discount + tax, A18). It is never taxed or discounted (A6).
/// </summary>
public static class TipCalculator
{
    /// <summary>Hundredths of a percent: 10.96% = 1096.</summary>
    public const long BasisPointScale = 10_000;

    /// <summary>The tip in cents. A percent tip is rounded half away from zero (rounding point RP6).</summary>
    public static Money Calculate(TipInput tip, Money bill) => tip.Mode switch
    {
        TipMode.None => Money.Zero,
        TipMode.Amount => new Money(tip.Value),
        TipMode.Percent => bill.ApplyRate(new Rate(checked((int)tip.Value))),
        _ => throw new ArgumentOutOfRangeException(nameof(tip)),
    };

    /// <summary>
    /// For a dollar tip, its share of the bill in basis points, rounded half away from zero (UQ22). This is a
    /// display value, never charged. Null for other tip modes or when there is no bill to compare with.
    /// </summary>
    public static long? EffectiveRateBasisPoints(TipInput tip, Money tipAmount, Money bill) =>
        tip.Mode == TipMode.Amount && bill.Cents > 0
            ? Rounding.RoundHalfAwayFromZero(checked(tipAmount.Cents * BasisPointScale), bill.Cents)
            : null;
}
