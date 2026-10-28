namespace PizzaMagic.Domain.Time;

/// <summary>A time zone and its offset periods, loaded from the database with the catalog (UQ15).</summary>
public sealed record TimeZonePeriods(long Id, string IanaId, IReadOnlyList<OffsetPeriod> Periods);
