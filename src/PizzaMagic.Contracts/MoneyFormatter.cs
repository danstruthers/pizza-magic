using System.Globalization;

namespace PizzaMagic.Contracts;

/// <summary>
/// The only place cents become dollar text: "$1,234.56", "-$2.90". Always en-US,
/// never the machine's current culture. Used by both hosts.
/// </summary>
public static class MoneyFormatter
{
    private static readonly CultureInfo UnitedStates = CultureInfo.GetCultureInfo("en-US");

    public static string Format(long cents)
    {
        var negative = cents < 0;
        // Negate via ulong so long.MinValue does not overflow.
        var magnitude = negative ? (ulong)(-(cents + 1)) + 1 : (ulong)cents;
        var dollars = (magnitude / 100).ToString("N0", UnitedStates);
        var remainder = (magnitude % 100).ToString("00", UnitedStates);
        return $"{(negative ? "-" : "")}${dollars}.{remainder}";
    }
}
