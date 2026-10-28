namespace PizzaMagic.Domain.Time;

/// <summary>
/// One row of time_zone_offset_period: during [ValidFromUtc, ValidToUtc) local time is UTC + UtcOffsetMinutes.
/// </summary>
public sealed record OffsetPeriod(DateTime ValidFromUtc, DateTime ValidToUtc, int UtcOffsetMinutes, string Abbreviation, bool IsDst);
