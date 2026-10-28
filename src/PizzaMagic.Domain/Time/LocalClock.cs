namespace PizzaMagic.Domain.Time;

/// <summary>The restaurant's local wall-clock reading for one instant. Never stored.</summary>
public sealed record LocalClock(DateTime LocalTime, int UtcOffsetMinutes, string Abbreviation, bool IsDst);
