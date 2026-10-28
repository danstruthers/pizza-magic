namespace PizzaMagic.Domain.Monetary;

/// <summary>
/// The only two rounding functions in the codebase, in pure integer arithmetic (plan section 2.2).
/// Both take a non-negative numerator and a positive denominator and return whole cents.
/// </summary>
public static class Rounding
{
    /// <summary>
    /// Rounds numerator / denominator to the nearest integer, with exact halves rounding up
    /// (away from zero). Used for every price rounding point and for tax when the jurisdiction's
    /// method is HALF_UP (RCW 82.08.054 for the seed jurisdiction).
    /// </summary>
    public static long RoundHalfAwayFromZero(long numerator, long denominator)
    {
        Validate(numerator, denominator);
        return checked(numerator + denominator / 2) / denominator;
    }

    /// <summary>
    /// Drops the fractional part of numerator / denominator. Used only for tax when the
    /// jurisdiction's method is TRUNCATE.
    /// </summary>
    public static long Truncate(long numerator, long denominator)
    {
        Validate(numerator, denominator);
        return numerator / denominator;
    }

    private static void Validate(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
    }
}
