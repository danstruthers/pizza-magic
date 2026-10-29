using PizzaMagic.Domain.Time;

namespace PizzaMagic.Domain.Tests.Time;

/// <summary>
/// US time zone offset periods for [2020-01-01, 2041-01-01) UTC, built from the US rule in force since 2007
/// (DST from the second Sunday of March 02:00 local to the first Sunday of November 02:00 local).
/// Stands in for data/seed/0000-time-zones.sql, which Domain tests don't read; the OrderManager seed tests check
/// the real file against tzdb.
/// </summary>
public static class UsTimeZones
{
    public static readonly DateTime CoverageStart = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime CoverageEnd = new(2041, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static TimeZonePeriods LosAngeles() => Build(1, "America/Los_Angeles", standardOffsetMinutes: -480, "PST", "PDT");

    public static TimeZonePeriods NewYork() => Build(2, "America/New_York", standardOffsetMinutes: -300, "EST", "EDT");

    private static TimeZonePeriods Build(long id, string ianaId, int standardOffsetMinutes, string standard, string daylight)
    {
        var periods = new List<OffsetPeriod>();
        var from = CoverageStart;
        for (var year = 2020; year <= 2040; year++)
        {
            // 02:00 local standard time = 02:00 - offset in UTC; 02:00 local daylight time = 02:00 - (offset + 60).
            var dstStart = NthSunday(year, 3, 2).AddMinutes(120 - standardOffsetMinutes);
            var dstEnd = NthSunday(year, 11, 1).AddMinutes(120 - (standardOffsetMinutes + 60));
            periods.Add(new OffsetPeriod(from, dstStart, standardOffsetMinutes, standard, IsDst: false));
            periods.Add(new OffsetPeriod(dstStart, dstEnd, standardOffsetMinutes + 60, daylight, IsDst: true));
            from = dstEnd;
        }
        periods.Add(new OffsetPeriod(from, CoverageEnd, standardOffsetMinutes, standard, IsDst: false));
        return new TimeZonePeriods(id, ianaId, periods);
    }

    private static DateTime NthSunday(int year, int month, int n)
    {
        var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var offset = ((int)DayOfWeek.Sunday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (n - 1));
    }
}
