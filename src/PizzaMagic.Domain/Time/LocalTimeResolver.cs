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
}
