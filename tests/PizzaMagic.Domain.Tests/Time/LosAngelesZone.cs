using PizzaMagic.Domain.Time;

namespace PizzaMagic.Domain.Tests.Time;

/// <summary>
/// America/Los_Angeles offset periods for [2020-01-01, 2041-01-01) UTC, built from the US rule in force
/// since 2007 (DST from the second Sunday of March 02:00 local to the first Sunday of November 02:00 local).
/// Stands in for data/seed/0000-time-zones.sql until M8, whose seed test checks the real file against tzdb.
/// </summary>
public static class LosAngelesZone
{
    public static readonly DateTime CoverageStart = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime CoverageEnd = new(2041, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static TimeZonePeriods Periods()
    {
        var periods = new List<OffsetPeriod>();
        var from = CoverageStart;
        for (var year = 2020; year <= 2040; year++)
        {
            var dstStart = NthSunday(year, 3, 2).AddHours(10);  // 02:00 PST = 10:00 UTC
            var dstEnd = NthSunday(year, 11, 1).AddHours(9);    // 02:00 PDT = 09:00 UTC
            periods.Add(new OffsetPeriod(from, dstStart, -480, "PST", IsDst: false));
            periods.Add(new OffsetPeriod(dstStart, dstEnd, -420, "PDT", IsDst: true));
            from = dstEnd;
        }
        periods.Add(new OffsetPeriod(from, CoverageEnd, -480, "PST", IsDst: false));
        return new TimeZonePeriods(1, "America/Los_Angeles", periods);
    }

    private static DateTime NthSunday(int year, int month, int n)
    {
        var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var offset = ((int)DayOfWeek.Sunday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (n - 1));
    }
}
