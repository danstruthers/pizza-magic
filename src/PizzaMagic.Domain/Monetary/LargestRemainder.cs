namespace PizzaMagic.Domain.Monetary;

/// <summary>
/// Splits an amount into parts proportional to weights so that the parts sum exactly to the amount
/// (plan section 2.8). This is an exact split, not a rounding point.
/// </summary>
public static class LargestRemainder
{
    /// <summary>
    /// Each part gets floor(amount x weight / total weight). The leftover cents go one each by
    /// remainder descending, then the larger weight, then the lower index. Callers pass weights
    /// in ordinal order, so the index is the ordinal.
    /// </summary>
    public static long[] Allocate(long amount, IReadOnlyList<long> weights)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentNullException.ThrowIfNull(weights);

        Int128 total = 0;
        foreach (var weight in weights)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(weight, nameof(weights));
            total += weight;
        }

        var shares = new long[weights.Count];
        if (amount == 0)
        {
            return shares;
        }
        if (total == 0)
        {
            throw new ArgumentException("A positive amount cannot be allocated over all-zero weights.", nameof(weights));
        }

        var remainders = new Int128[weights.Count];
        long allocated = 0;
        for (var i = 0; i < weights.Count; i++)
        {
            var product = (Int128)amount * weights[i];
            shares[i] = (long)(product / total);
            remainders[i] = product % total;
            allocated += shares[i];
        }

        var leftover = amount - allocated;
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(i => remainders[i])
            .ThenByDescending(i => weights[i])
            .ThenBy(i => i);
        foreach (var i in order.Take((int)leftover))
        {
            shares[i]++;
        }

        return shares;
    }
}
