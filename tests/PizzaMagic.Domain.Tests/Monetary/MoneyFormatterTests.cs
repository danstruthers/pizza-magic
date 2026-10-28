using PizzaMagic.Contracts;

namespace PizzaMagic.Domain.Tests.Monetary;

public class MoneyFormatterTests
{
    // M10: invariant en-US formatting, never the machine's current culture.
    [Theory]
    [InlineData(-290, "-$2.90")]
    [InlineData(123456, "$1,234.56")]
    [InlineData(0, "$0.00")]
    [InlineData(5, "$0.05")]
    [InlineData(533, "$5.33")]
    [InlineData(100_000_000, "$1,000,000.00")]
    [InlineData(long.MinValue, "-$92,233,720,368,547,758.08")]
    public void MoneyFormatter_negative_and_thousands(long cents, string expected)
    {
        MoneyFormatter.Format(cents).ShouldBe(expected);
    }

    [Fact]
    public void MoneyFormatter_ignores_current_culture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            MoneyFormatter.Format(123456).ShouldBe("$1,234.56");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
