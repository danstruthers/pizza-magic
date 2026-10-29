using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Tests.Monetary;

public class RoundingTests
{
    // M1, M3: exact midpoints round away from zero (banker's rounding would give 32, 38 and 66).
    [Theory]
    [InlineData(32_500_000, 33)]
    [InlineData(38_500_000, 39)]
    [InlineData(66_500_000, 67)]
    public void RoundHalfAwayFromZero_midpoint_rounds_up(long numerator, long expected)
    {
        Rounding.RoundHalfAwayFromZero(numerator, 1_000_000).ShouldBe(expected);
    }

    [Theory]
    [InlineData(46_200_000, 46)]
    [InlineData(32_499_999, 32)]
    [InlineData(78_000_000, 78)]
    [InlineData(0, 0)]
    public void RoundHalfAwayFromZero_below_half_rounds_down(long numerator, long expected)
    {
        Rounding.RoundHalfAwayFromZero(numerator, 1_000_000).ShouldBe(expected);
    }

    [Fact]
    public void RoundHalfAwayFromZero_negative_numerator_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Rounding.RoundHalfAwayFromZero(-1, 1_000_000));
    }

    // M11, M12: truncation drops fractional cents and never rounds up.
    [Theory]
    [InlineData(32_500_000, 32)]
    [InlineData(47_970_000, 47)]
    [InlineData(78_000_000, 78)]
    [InlineData(0, 0)]
    public void Truncate_drops_fractional_cents(long numerator, long expected)
    {
        Rounding.Truncate(numerator, 1_000_000).ShouldBe(expected);
    }

    [Fact]
    public void Truncate_negative_numerator_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Rounding.Truncate(-1, 1_000_000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1_000_000)]
    public void Non_positive_denominator_throws(long denominator)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Rounding.RoundHalfAwayFromZero(100, denominator));
        Should.Throw<ArgumentOutOfRangeException>(() => Rounding.Truncate(100, denominator));
    }
}
