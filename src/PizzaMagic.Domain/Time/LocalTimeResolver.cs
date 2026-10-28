namespace PizzaMagic.Domain.Time;

/// <summary>
/// Converts instants to a restaurant's local time using its offset periods from the database.
/// No TimeZoneInfo and no OS time zone data (UQ15), so results are identical on every machine.
/// </summary>
public static class LocalTimeResolver
{
    /// <summary>
    /// The local clock at <paramref name="instant"/>, or null when the instant is outside the zone's
    /// seeded coverage (callers fail closed). Always unambiguous: each UTC instant is in exactly one period.
    /// </summary>
    public static LocalClock? ToLocal(TimeZonePeriods zone, DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        foreach (var period in zone.Periods)
        {
            if (period.ValidFromUtc <= utc && utc < period.ValidToUtc)
            {
                var local = DateTime.SpecifyKind(utc.AddMinutes(period.UtcOffsetMinutes), DateTimeKind.Unspecified);
                return new LocalClock(local, period.UtcOffsetMinutes, period.Abbreviation, period.IsDst);
            }
        }
        return null;
    }

    /// <summary>
    /// The earliest UTC instant at which the local clock reads <paramref name="local"/> or later (plan section 2.6.1).
    /// An ambiguous local time (fall-back repeated hour) resolves to its first occurrence; a nonexistent one
    /// (spring-forward gap) resolves to the first instant after the gap. Null when outside the zone's coverage.
    /// </summary>
    public static DateTimeOffset? Boundary(TimeZonePeriods zone, DateTime local)
    {
        if (zone.Periods.Count == 0)
        {
            return null;
        }

        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Utc);
        DateTime? earliest = null;
        foreach (var period in zone.Periods)
        {
            var atThisOffset = wallClock.AddMinutes(-period.UtcOffsetMinutes);
            var candidate = atThisOffset < period.ValidFromUtc ? period.ValidFromUtc : atThisOffset;
            if (candidate < period.ValidToUtc && (earliest is null || candidate < earliest))
            {
                earliest = candidate;
            }
        }

        // If the clock would read the value before the first covered instant, the true answer is unknown.
        var first = zone.Periods.MinBy(p => p.ValidFromUtc)!;
        if (earliest is null || wallClock.AddMinutes(-first.UtcOffsetMinutes) < first.ValidFromUtc)
        {
            return null;
        }
        return new DateTimeOffset(earliest.Value, TimeSpan.Zero);
    }
}
