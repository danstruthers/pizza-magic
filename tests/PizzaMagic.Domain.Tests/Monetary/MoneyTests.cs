using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Tests.Monetary;

public class MoneyTests
{
    // M1: 6.5% of $5.00 is 32.5 cents, which rounds to 33 (RCW 82.08.054 for the seed jurisdiction).
    [Fact]
    public void ApplyRate_65000ppm_on_500_is_33()
    {
        new Money(500).ApplyRate(new Rate(65_000)).ShouldBe(new Money(33));
    }

    // M2, M3: premium topping on a Medium (77 cents) at the Half (60%), Quarter (40%) and a 50% factor;
    // and a tax base of 1285 at 6.5%.
    [Theory]
    [InlineData(77, 600_000, 46)]
    [InlineData(77, 400_000, 31)]
    [InlineData(77, 500_000, 39)]
    [InlineData(1285, 65_000, 84)]
    public void ApplyRate_coverage_factors(long cents, int ppm, long expected)
    {
        new Money(cents).ApplyRate(new Rate(ppm)).ShouldBe(new Money(expected));
    }


    [Fact]
    public void ApplyRate_on_negative_amount_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Money(-1).ApplyRate(new Rate(65_000)));
    }

    [Fact]
    public void Money_add_subtract_and_multiply_by_count()
    {
        (new Money(1200) + new Money(96)).ShouldBe(new Money(1296));
        (new Money(1296) - new Money(500)).ShouldBe(new Money(796));
        (new Money(58) * 2).ShouldBe(new Money(116));
        Money.Zero.ShouldBe(new Money(0));
    }

    // M4: overflow is never silent.
    [Fact]
    public void Money_arithmetic_is_checked()
    {
        var max = new Money(long.MaxValue);
        var min = new Money(long.MinValue);

        Should.Throw<OverflowException>(() => max + new Money(1));
        Should.Throw<OverflowException>(() => min - new Money(1));
        Should.Throw<OverflowException>(() => max * 2);
        Should.Throw<OverflowException>(() => max.ApplyRate(new Rate(1_000_000)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Rate_rejects_negative_and_over_max(int ppm)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Rate(ppm));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_000)]
    [InlineData(1_000_000)]
    public void Rate_accepts_zero_through_one_million(int ppm)
    {
        new Rate(ppm).Ppm.ShouldBe(ppm);
    }
}
