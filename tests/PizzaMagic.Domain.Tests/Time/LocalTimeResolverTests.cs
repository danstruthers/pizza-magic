using PizzaMagic.Domain.Time;

namespace PizzaMagic.Domain.Tests.Time;

public class LocalTimeResolverTests
{
    private static readonly TimeZonePeriods LosAngeles = LosAngelesZone.Periods();

    private static LocalClock? ToLocal(string utc) =>
        LocalTimeResolver.ToLocal(LosAngeles, DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

    private static DateTime Local(int year, int month, int day, int hour, int minute, int second, int millisecond = 0) =>
        new(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);

    [Fact]
    public void Fixture_has_43_contiguous_periods()
    {
        LosAngeles.Periods.Count.ShouldBe(43);
        for (var i = 1; i < LosAngeles.Periods.Count; i++)
        {
            LosAngeles.Periods[i].ValidFromUtc.ShouldBe(LosAngeles.Periods[i - 1].ValidToUtc);
        }
    }

    // TZ1-TZ4
    [Fact]
    public void Local_time_resolver_dst_boundaries()
    {
        ToLocal("2026-03-08T09:59:59.999Z").ShouldBe(new LocalClock(Local(2026, 3, 8, 1, 59, 59, 999), -480, "PST", IsDst: false));
        ToLocal("2026-03-08T10:00:00.000Z").ShouldBe(new LocalClock(Local(2026, 3, 8, 3, 0, 0), -420, "PDT", IsDst: true));
        ToLocal("2026-11-01T08:59:59.999Z").ShouldBe(new LocalClock(Local(2026, 11, 1, 1, 59, 59, 999), -420, "PDT", IsDst: true));
        ToLocal("2026-11-01T09:00:00.000Z").ShouldBe(new LocalClock(Local(2026, 11, 1, 1, 0, 0), -480, "PST", IsDst: false));
    }

    // TZ5: 01:30 local happens twice on 2026-11-01; each UTC instant maps to exactly one of them.
    [Fact]
    public void Local_time_resolver_repeated_hour_is_unambiguous()
    {
        var first = ToLocal("2026-11-01T08:30:00.000Z");
        var second = ToLocal("2026-11-01T09:30:00.000Z");

        first.ShouldBe(new LocalClock(Local(2026, 11, 1, 1, 30, 0), -420, "PDT", IsDst: true));
        second.ShouldBe(new LocalClock(Local(2026, 11, 1, 1, 30, 0), -480, "PST", IsDst: false));
    }

    // TZ6: no UTC instant maps into the skipped local hour 02:00-02:59:59.999 on 2026-03-08.
    [Fact]
    public void Local_time_resolver_spring_gap_never_produced()
    {
        var start = new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero);
        for (var utc = start; utc < start.AddHours(4); utc = utc.AddMinutes(1))
        {
            var local = LocalTimeResolver.ToLocal(LosAngeles, utc)!;
            (local.LocalTime.Date == new DateTime(2026, 3, 8) && local.LocalTime.Hour == 2).ShouldBeFalse($"{utc:O} -> {local.LocalTime:O}");
        }
    }

    // TZ7: outside the seeded coverage the resolver returns nothing, so callers fail closed.
    [Fact]
    public void Local_time_resolver_out_of_coverage_returns_none()
    {
        ToLocal("2041-01-01T00:00:00.000Z").ShouldBeNull();
        ToLocal("2019-12-31T23:59:59.999Z").ShouldBeNull();
        ToLocal("2020-01-01T00:00:00.000Z").ShouldNotBeNull();
    }

    [Fact]
    public void Input_offset_is_irrelevant_only_the_instant_matters()
    {
        var utc = LocalTimeResolver.ToLocal(LosAngeles, new DateTimeOffset(2026, 10, 27, 19, 0, 0, TimeSpan.Zero));
        var sameInstant = LocalTimeResolver.ToLocal(LosAngeles, new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(-7)));

        utc.ShouldBe(new LocalClock(Local(2026, 10, 27, 12, 0, 0), -420, "PDT", IsDst: true));
        sameInstant.ShouldBe(utc);
    }
}
