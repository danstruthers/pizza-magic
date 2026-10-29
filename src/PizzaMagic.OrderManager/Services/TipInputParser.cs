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

    /// <summary>
    /// Far above any tip limit, and small enough to scale safely (decimal overflows near 7.9e28): a longer whole part
    /// is out of range without being multiplied.
    /// </summary>
    private const int MaxWholeDigits = 15;

    public static TipInput Parse(TipRequest? tip, out FieldError? error)
    {
        error = null;
        var text = tip?.Value?.Trim();
        if (tip?.Mode is not { } modeText || modeText.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(text))
            {
                return TipInput.None;
            }
            error = new FieldError("tip.mode", "TIP_INVALID", "Choose Amount or Percent for the tip, or leave the tip empty.");
            return TipInput.None;
        }

        var mode = modeText.Equals("Amount", StringComparison.OrdinalIgnoreCase) ? TipMode.Amount
            : modeText.Equals("Percent", StringComparison.OrdinalIgnoreCase) ? TipMode.Percent
            : (TipMode?)null;
        if (mode is null)
        {
            error = new FieldError("tip.mode", "TIP_INVALID", "Tip mode must be Amount or Percent.");
            return TipInput.None;
        }

        if (string.IsNullOrEmpty(text))
        {
            error = new FieldError("tip.value", "TIP_INVALID", "Enter a tip, or choose No tip.");
            return TipInput.None;
        }
        if (!PlainDecimal().IsMatch(text))
        {
            error = new FieldError("tip.value", "TIP_INVALID", InvalidMessage);
            return TipInput.None;
        }
        if (text.Split('.')[0].TrimStart('0').Length > MaxWholeDigits)
        {
            error = new FieldError("tip.value", "TIP_OUT_OF_RANGE", "The tip is too large.");
            return TipInput.None;
        }

        var value = decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return new TipInput(mode.Value, (long)(value * (mode == TipMode.Amount ? CentsPerDollar : PpmPerPercent)));
    }

    /// <summary>Digits, optionally a point and one or two digits. No sign, exponent, spaces or thousands separators.</summary>
    [GeneratedRegex(@"^\d+(\.\d{1,2})?$")]
    private static partial Regex PlainDecimal();
}
