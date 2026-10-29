using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Tax;

/// <summary>One order_tax_line: a (jurisdiction, tax class) pair with its base, rate and amount.</summary>
public sealed record TaxLine(
    long JurisdictionId,
    string JurisdictionCode,
    int Depth,
    long TaxClassId,
    Money Gross,
    Money Discount,
    Money Taxable,
    Rate Rate,
    Money Amount);

/// <summary>The order's tax (the sum of its lines) and the rounding settings it was computed with.</summary>
public sealed record TaxResult(Money Tax, IReadOnlyList<TaxLine> Lines, TaxRoundingSettings Settings);

/// <summary>
/// Computes an order's tax on invoice basis (plan section 2.9, rounding point RP5): the exact tax over every
/// tax class is rounded once with the jurisdiction's method, then split across the class lines so they sum
/// exactly to it. Built scope: one jurisdiction level (A70); the multi-level hierarchy is designed, not built.
/// </summary>
public static class TaxCalculator
{
    public static TaxResult Calculate(IReadOnlyList<ClassBase> bases, IReadOnlyList<JurisdictionNode> chain, TaxRoundingSettings settings)
    {
        if (chain.Count != 1)
        {
            throw new NotSupportedException("Multi-level tax jurisdictions are designed, not built (A70).");
        }
        if (bases.Select(b => b.TaxClassId).Distinct().Count() != bases.Count)
        {
            throw new ArgumentException("Each tax class may appear only once.", nameof(bases));
        }

        var level = chain[0];
        var rate = new Rate(level.DefaultRatePpm);
        // Classes absent from the order (zero gross) emit no line (T12). Lines are in class display order.
        var present = bases.Where(b => b.Gross.Cents > 0).OrderBy(b => b.SortOrder).ThenBy(b => b.TaxClassId).ToList();

        var numerators = present.Select(b => checked(b.Taxable.Cents * rate.Ppm)).ToList();
        var tax = RoundTax(numerators.Sum(), settings.Method);
        var amounts = LargestRemainder.Allocate(tax, numerators);

        var lines = present
            .Select((b, i) => new TaxLine(level.Id, level.Code, Depth: 0, b.TaxClassId, b.Gross, b.Discount, b.Taxable, rate, new Money(amounts[i])))
            .ToList();
        return new TaxResult(new Money(tax), lines, settings);
    }

    /// <summary>The only caller of <see cref="Rounding.Truncate"/>; a source-scan test enforces this (plan section 9.2).</summary>
    private static long RoundTax(long numerator, TaxRoundingMethod method) => method switch
    {
        TaxRoundingMethod.HalfUp => Rounding.RoundHalfAwayFromZero(numerator, Money.PpmScale),
        TaxRoundingMethod.Truncate => Rounding.Truncate(numerator, Money.PpmScale),
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };
}
