using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.OrderManager.Services;

namespace PizzaMagic.OrderManager.Tests;

public class TipInputParserTests
{
    [Theory]
    [InlineData("Amount", "4.50", TipMode.Amount, 450)]
    [InlineData("Amount", "0", TipMode.Amount, 0)]
    [InlineData("amount", "3", TipMode.Amount, 300)]
    [InlineData("Percent", "17.5", TipMode.Percent, 175_000)]
    [InlineData("Percent", "15", TipMode.Percent, 150_000)]
    [InlineData("Percent", "10.96", TipMode.Percent, 109_600)]
    [InlineData("Amount", " 4.50 ", TipMode.Amount, 450)]   // trimmed, like promo and product codes
    public void Parses_amounts_to_cents_and_percents_to_ppm(string mode, string value, TipMode expectedMode, long expectedValue)
    {
        TipInputParser.Parse(new TipRequest(mode, value), out var error).ShouldBe(new TipInput(expectedMode, expectedValue));
        error.ShouldBeNull();
    }

    [Fact]
    public void No_tip_is_none()
    {
        TipInputParser.Parse(null, out var error).ShouldBe(TipInput.None);
        error.ShouldBeNull();
        TipInputParser.Parse(new TipRequest("None", null), out _).ShouldBe(TipInput.None);
    }

    // P26 (syntax part): negative, three decimals, non-numeric.
    [Theory]
    [InlineData("-1")]
    [InlineData("1.234")]
    [InlineData("abc")]
    [InlineData("1e3")]
    [InlineData("4,50")]
    public void TipInputParser_rejects_negative_3dp_and_nonnumeric(string? value)
    {
        TipInputParser.Parse(new TipRequest("Amount", value), out var error);

        error.ShouldBe(new FieldError("tip.value", "TIP_INVALID", "Tip must be zero or more, with at most two decimal places."));
    }

    [Fact]
    public void Unknown_mode_is_invalid()
    {
        TipInputParser.Parse(new TipRequest("Generous", "5"), out var error);

        error!.Path.ShouldBe("tip.mode");
        error.Code.ShouldBe("TIP_INVALID");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Missing_value_asks_for_one(string? value)
    {
        TipInputParser.Parse(new TipRequest("Amount", value), out var error);

        error.ShouldBe(new FieldError("tip.value", "TIP_INVALID", "Enter a tip, or choose No tip."));
    }

    // A value without a mode is an error rather than no tip, so a tip is never lost quietly.
    [Theory]
    [InlineData(null)]
    [InlineData("None")]
    public void Value_without_a_mode_is_invalid(string? mode)
    {
        TipInputParser.Parse(new TipRequest(mode, "5"), out var error).ShouldBe(TipInput.None);

        error.ShouldBe(new FieldError("tip.mode", "TIP_INVALID", "Choose Amount or Percent for the tip, or leave the tip empty."));
    }

    // Values too large for any limit are out of range, not an overflow and a 500 (a 28-digit decimal x 100 overflows).
    [Theory]
    [InlineData("Amount", "999999999999999999999")]
    [InlineData("Amount", "1000000000000000000000000000")]
    [InlineData("Amount", "79228162514264337593543950335")]
    [InlineData("Percent", "99999999999999999999999999.99")]
    public void Absurdly_large_tip_is_out_of_range(string mode, string value)
    {
        TipInputParser.Parse(new TipRequest(mode, value), out var error);

        error!.Code.ShouldBe("TIP_OUT_OF_RANGE");
    }
}
