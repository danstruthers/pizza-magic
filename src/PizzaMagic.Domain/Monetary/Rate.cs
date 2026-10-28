namespace PizzaMagic.Domain.Monetary;

/// <summary>
/// A percentage stored as an integer in parts per million: 6.5% = 65 000, 60% = 600 000.
/// Valid range is 0 to 1 000 000 (0% to 100%).
/// </summary>
public readonly record struct Rate
{
    public const int MaxPpm = 1_000_000;

    public Rate(int ppm)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ppm);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ppm, MaxPpm);
        Ppm = ppm;
    }

    public int Ppm { get; }
}
