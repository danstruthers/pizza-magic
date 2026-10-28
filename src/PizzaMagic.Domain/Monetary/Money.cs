namespace PizzaMagic.Domain.Monetary;

/// <summary>
/// An amount in whole cents. All arithmetic is checked, so overflow throws instead of wrapping.
/// </summary>
public readonly record struct Money(long Cents)
{
    public const long PpmScale = Rate.MaxPpm;

    public static Money Zero => new(0);

    public static Money operator +(Money left, Money right) => new(checked(left.Cents + right.Cents));

    public static Money operator -(Money left, Money right) => new(checked(left.Cents - right.Cents));

    public static Money operator *(Money amount, int count) => new(checked(amount.Cents * count));

    /// <summary>The rate applied to this amount, rounded half away from zero to whole cents.</summary>
    public Money ApplyRate(Rate rate) =>
        new(Rounding.RoundHalfAwayFromZero(checked(Cents * rate.Ppm), PpmScale));

    /// <summary>The rate applied to this amount, truncated to whole cents. Tax TRUNCATE only.</summary>
    public Money ApplyRateTruncated(Rate rate) =>
        new(Rounding.Truncate(checked(Cents * rate.Ppm), PpmScale));
}
