using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Tax;

namespace PizzaMagic.Domain.Tests.Tax;

public class TaxCalculatorTests
{
    private const long PreparedFood = 1;
    private const long Beverage = 2;
    private const long General = 3;

    /// <summary>Seed jurisdiction: WA, 6.5%, HALF_UP + COMBINED (RCW 82.08.054).</summary>
    private static readonly JurisdictionNode Wa =
        new(1, "WA", "Washington State", ParentId: null, DefaultRatePpm: 65_000, TaxRoundingMethod.HalfUp, TaxRoundingScope.Combined);

    private static readonly TaxRoundingSettings HalfUp = new(TaxRoundingMethod.HalfUp, TaxRoundingScope.Combined);
    private static readonly TaxRoundingSettings Truncate = new(TaxRoundingMethod.Truncate, TaxRoundingScope.Combined);

    private static ClassBase Food(long gross, long discount = 0) => new(PreparedFood, SortOrder: 10, new Money(gross), new Money(discount));

    private static TaxResult Calculate(TaxRoundingSettings settings, params ClassBase[] bases) =>
        TaxCalculator.Calculate(bases, [Wa], settings);

    // T1-T3: the seed's flat 6.5%, rounded half up once per order.
    [Theory]
    [InlineData(1428, 0, 93)]    // T1: 92.82 -> 93
    [InlineData(500, 0, 33)]     // T2: 32.5 -> 33
    [InlineData(1428, 143, 84)]  // T3: 1285 x 6.5% = 83.525 -> 84
    public void Seed_flat_rate_rounds_half_up(long gross, long discount, long expected)
    {
        var result = Calculate(HalfUp, Food(gross, discount));

        result.Tax.ShouldBe(new Money(expected));
        var line = result.Lines.ShouldHaveSingleItem();
        line.ShouldBe(new TaxLine(1, "WA", Depth: 0, PreparedFood, new Money(gross), new Money(discount), new Money(gross - discount), new Rate(65_000), new Money(expected)));
    }

    // T13: the same carts with the jurisdiction's method set to TRUNCATE.
    [Theory]
    [InlineData(500, 32, 33)]
    [InlineData(700, 45, 46)]
    [InlineData(1428, 92, 93)]
    [InlineData(1200, 78, 78)]   // exact: unchanged
    public void Tax_method_HALF_UP_vs_TRUNCATE_on_single_level(long gross, long truncated, long halfUp)
    {
        Calculate(Truncate, Food(gross)).Tax.ShouldBe(new Money(truncated));
        Calculate(HalfUp, Food(gross)).Tax.ShouldBe(new Money(halfUp));
    }

    // T12: classes that are not in the order produce no line.
    [Fact]
    public void Absent_classes_emit_no_line()
    {
        var result = Calculate(HalfUp, Food(1428), new ClassBase(Beverage, 20, Money.Zero, Money.Zero));

        result.Lines.ShouldHaveSingleItem().TaxClassId.ShouldBe(PreparedFood);
    }

    // T6 at the seed's single level (class bases fed directly): tax is rounded once over both classes,
    // then split exactly, so the lines sum to the level total (per-class rounding would give 126).
    [Fact]
    public void Class_lines_sum_to_level()
    {
        var result = Calculate(HalfUp,
            new ClassBase(General, SortOrder: 30, new Money(1000), new Money(206)),
            Food(1428, 294));

        result.Tax.ShouldBe(new Money(125));
        result.Lines.Select(l => (l.TaxClassId, l.Amount.Cents)).ShouldBe([(PreparedFood, 74L), (General, 51L)]);
        result.Lines.Sum(l => l.Amount.Cents).ShouldBe(result.Tax.Cents);
    }

    [Fact]
    public void Lines_are_deterministic_regardless_of_input_order()
    {
        var forward = Calculate(HalfUp, Food(1428, 294), new ClassBase(General, 30, new Money(1000), new Money(206)));
        var reversed = Calculate(HalfUp, new ClassBase(General, 30, new Money(1000), new Money(206)), Food(1428, 294));

        reversed.ShouldBeEquivalentTo(forward);
    }

    [Fact]
    public void Fully_discounted_order_has_zero_tax_but_keeps_its_line()
    {
        var result = Calculate(HalfUp, Food(500, 500));

        result.Tax.ShouldBe(Money.Zero);
        result.Lines.ShouldHaveSingleItem().Amount.ShouldBe(Money.Zero);
    }

    [Fact]
    public void D_greater_than_S_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Food(100, 101));
        Should.Throw<ArgumentOutOfRangeException>(() => Food(-1));
    }

    [Fact]
    public void Same_class_twice_throws()
    {
        Should.Throw<ArgumentException>(() => Calculate(HalfUp, Food(100), Food(200)));
    }

    // Multi-level tax is designed, not built (A70): refuse it rather than half-support it.
    [Fact]
    public void Multi_level_chain_is_refused_in_the_built_scope()
    {
        var seattle = new JurisdictionNode(2, "SEATTLE", "Seattle", ParentId: 1, 38_500, null, null);

        Should.Throw<NotSupportedException>(() => TaxCalculator.Calculate([Food(700)], [Wa, seattle], HalfUp));
    }
}
