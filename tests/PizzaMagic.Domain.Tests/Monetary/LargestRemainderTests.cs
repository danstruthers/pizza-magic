using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Tests.Monetary;

public class LargestRemainderTests
{
    // M5-M9 (plan section 7.3). Ties go to the larger remainder, then the larger weight, then the lower ordinal (index).
    public static TheoryData<long, long[], long[]> Vectors => new()
    {
        { 500, new long[] { 1200, 96, 132, 700 }, new long[] { 282, 23, 31, 164 } },          // M5
        { 101, new long[] { 500, 500 }, new long[] { 51, 50 } },                              // M6: full tie -> lower ordinal
        { 152, new long[] { 1000, 524 }, new long[] { 100, 52 } },                            // M7
        { 300, new long[] { 1200, 150, 50, 116, 132, 53 }, new long[] { 212, 26, 9, 21, 23, 9 } }, // M8
        { 100, new long[] { 700, 299 }, new long[] { 70, 30 } },                              // M9
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Allocate_matches_worked_vectors(long amount, long[] weights, long[] expected)
    {
        LargestRemainder.Allocate(amount, weights).ShouldBe(expected);
    }

    [Fact]
    public void Equal_remainders_go_to_the_larger_weight_even_at_a_higher_ordinal()
    {
        // 2 over [1, 3]: exact shares 0.5 and 1.5 -> floors 0 and 1, one cent left over.
        // Remainders are 2 x 1 mod 4 = 2 and 2 x 3 mod 4 = 2 (a tie), so the larger weight (index 1) gets it.
        LargestRemainder.Allocate(2, new long[] { 1, 3 }).ShouldBe(new long[] { 0, 2 });
    }

    [Fact]
    public void Equal_remainders_and_weights_go_to_the_lower_ordinal()
    {
        // 2 over [100, 200, 100]: exact shares 0.5, 1.0, 0.5 -> floors 0, 1, 0, one cent left over.
        // Indexes 0 and 2 tie on remainder and weight, so index 0 gets it.
        LargestRemainder.Allocate(2, new long[] { 100, 200, 100 }).ShouldBe(new long[] { 1, 1, 0 });
    }

    [Fact]
    public void Zero_amount_gives_all_zeros()
    {
        LargestRemainder.Allocate(0, new long[] { 5, 0, 7 }).ShouldBe(new long[] { 0, 0, 0 });
    }

    [Fact]
    public void Positive_amount_with_all_zero_weights_throws()
    {
        Should.Throw<ArgumentException>(() => LargestRemainder.Allocate(1, new long[] { 0, 0 }));
    }

    [Fact]
    public void Negative_amount_or_weight_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => LargestRemainder.Allocate(-1, new long[] { 1 }));
        Should.Throw<ArgumentOutOfRangeException>(() => LargestRemainder.Allocate(1, new long[] { 1, -1 }));
    }

    [Fact]
    public void Large_values_do_not_overflow()
    {
        // amount x weight is about 10^37 here, far beyond long, so the Int128 path is exercised.
        var shares = LargestRemainder.Allocate(long.MaxValue / 2, new long[] { long.MaxValue / 3, long.MaxValue / 3 });
        shares.Sum().ShouldBe(long.MaxValue / 2);
    }

    // Allocator properties (fixed seed, 1000 cases).
    [Fact]
    public void Allocate_properties_hold_for_random_inputs()
    {
        var random = new Random(20261028);
        for (var i = 0; i < 1000; i++)
        {
            var weights = Enumerable.Range(0, random.Next(1, 9)).Select(_ => (long)random.Next(0, 5000)).ToArray();
            var total = weights.Sum();
            if (total == 0)
            {
                weights[0] = 1;
                total = 1;
            }
            var amount = (long)random.Next(0, (int)Math.Min(total * 2, int.MaxValue));

            var shares = LargestRemainder.Allocate(amount, weights);

            shares.Length.ShouldBe(weights.Length);
            shares.Sum().ShouldBe(amount);
            for (var j = 0; j < weights.Length; j++)
            {
                var floor = (long)((Int128)amount * weights[j] / total);
                shares[j].ShouldBeInRange(floor, floor + 1);
                if (amount <= total)
                {
                    shares[j].ShouldBeLessThanOrEqualTo(weights[j]);
                }
            }
            LargestRemainder.Allocate(amount, weights).ShouldBe(shares);
        }
    }
}
