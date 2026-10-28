using System.Globalization;

namespace PizzaMagic.Contracts;

/// <summary>Formats basis points (hundredths of a percent) as "10.96%". Always invariant, never the current culture.</summary>
public static class PercentFormatter
{
    public static string FormatBasisPoints(long basisPoints)
    {
        var negative = basisPoints < 0;
        var magnitude = negative ? (ulong)(-(basisPoints + 1)) + 1 : (ulong)basisPoints;
        var whole = (magnitude / 100).ToString(CultureInfo.InvariantCulture);
        var fraction = (magnitude % 100).ToString("00", CultureInfo.InvariantCulture);
        return $"{(negative ? "-" : "")}{whole}.{fraction}%";
    }

    /// <summary>A rate as the customer entered it, without trailing zeros: "15%", "17.5%", "18.25%".</summary>
    public static string FormatBasisPointsCompact(long basisPoints)
    {
        var negative = basisPoints < 0;
        var magnitude = negative ? (ulong)(-(basisPoints + 1)) + 1 : (ulong)basisPoints;
        var whole = (magnitude / 100).ToString(CultureInfo.InvariantCulture);
        var fraction = (magnitude % 100).ToString("00", CultureInfo.InvariantCulture).TrimEnd('0');
        return $"{(negative ? "-" : "")}{whole}{(fraction.Length > 0 ? "." + fraction : "")}%";
    }
}
