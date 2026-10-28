using PizzaMagic.OrderManager.Data;

namespace PizzaMagic.OrderManager.Tests.Persistence;

public class ConverterTests
{
    private static readonly UtcTimestampConverter Utc = new();
    private static readonly LocalWallClockConverter Local = new();

    [Fact]
    public void Utc_converter_writes_fixed_width_text_and_reads_utc()
    {
        var instant = new DateTime(2026, 10, 27, 19, 0, 0, 5, DateTimeKind.Utc);

        var text = (string)Utc.ConvertToProvider(instant)!;
        text.ShouldBe("2026-10-27T19:00:00.005Z");

        var back = (DateTime)Utc.ConvertFromProvider(text)!;
        back.ShouldBe(instant);
        back.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Utc_converter_rejects_local_and_unspecified_kinds(DateTimeKind kind)
    {
        Should.Throw<ArgumentException>(() => Utc.ConvertToProvider(new DateTime(2026, 10, 27, 12, 0, 0, kind)));
    }

    // Reading never depends on the machine's time zone: the text is parsed as UTC and returned as UTC.
    [Fact]
    public void Utc_timestamp_round_trips_on_non_utc_machine_zone()
    {
        var parsed = (DateTime)Utc.ConvertFromProvider("2026-11-01T09:30:00.000Z")!;

        parsed.Kind.ShouldBe(DateTimeKind.Utc);
        parsed.Ticks.ShouldBe(new DateTime(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc).Ticks);
        Utc.ConvertToProvider(parsed).ShouldBe("2026-11-01T09:30:00.000Z");
    }

    [Fact]
    public void Local_converter_writes_offset_free_text()
    {
        var local = new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified);

        Local.ConvertToProvider(local).ShouldBe("2026-11-01T01:30:00");
        var back = (DateTime)Local.ConvertFromProvider("2026-11-01T01:30:00")!;
        back.ShouldBe(local);
        back.Kind.ShouldBe(DateTimeKind.Unspecified);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void Local_converter_rejects_utc_and_local_kinds(DateTimeKind kind)
    {
        Should.Throw<ArgumentException>(() => Local.ConvertToProvider(new DateTime(2026, 11, 1, 1, 30, 0, kind)));
    }
}
