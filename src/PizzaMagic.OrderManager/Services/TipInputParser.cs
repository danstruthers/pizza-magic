using System.Globalization;
using System.Text.RegularExpressions;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Pricing;

namespace PizzaMagic.OrderManager.Services;

/// <summary>
/// Turns the tip text the customer typed into whole cents (Amount) or parts per million (Percent), at the API
/// boundary (plan section 2.1). Syntax only: zero or more, at most two decimals. Ranges are the Domain validator's.
/// This is the only place a decimal is used; it never reaches the Domain.
/// </summary>
public static partial class TipInputParser
{
    private const decimal CentsPerDollar = 100m;
    private const decimal PpmPerPercent = 10_000m;
    private const string InvalidMessage = "Tip must be zero or more, with at most two decimal places.";

    public static TipInput Parse(TipRequest? tip, out FieldError? error)
    {
        error = null;
        if (tip is null || tip.Mode is null || tip.Mode.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return TipInput.None;
        }

        var mode = tip.Mode.Equals("Amount", StringComparison.OrdinalIgnoreCase) ? TipMode.Amount
            : tip.Mode.Equals("Percent", StringComparison.OrdinalIgnoreCase) ? TipMode.Percent
            : (TipMode?)null;
        if (mode is null)
        {
            error = new FieldError("tip.mode", "TIP_INVALID", "Tip mode must be Amount or Percent.");
            return TipInput.None;
        }

        if (tip.Value is null || !PlainDecimal().IsMatch(tip.Value)
            || !decimal.TryParse(tip.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            error = new FieldError("tip.value", "TIP_INVALID", InvalidMessage);
            return TipInput.None;
        }

        var scaled = value * (mode == TipMode.Amount ? CentsPerDollar : PpmPerPercent);
        if (scaled > long.MaxValue)
        {
            error = new FieldError("tip.value", "TIP_OUT_OF_RANGE", "The tip is outside the allowed range.");
            return TipInput.None;
        }
        return new TipInput(mode.Value, (long)scaled);
    }

    /// <summary>Digits, optionally a point and one or two digits. No sign, exponent, spaces or thousands separators.</summary>
    [GeneratedRegex(@"^\d+(\.\d{1,2})?$")]
    private static partial Regex PlainDecimal();
}
