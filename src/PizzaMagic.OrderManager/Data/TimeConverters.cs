using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PizzaMagic.OrderManager.Data;

/// <summary>
/// UTC instants (*_utc columns) as fixed-width text 'yyyy-MM-ddTHH:mm:ss.fffZ', so text order equals time order.
/// Writing a Local or Unspecified DateTime throws instead of silently shifting it (A72). Reading never depends on
/// the machine's time zone.
/// </summary>
public sealed class UtcTimestampConverter() : ValueConverter<DateTime, string>(v => Write(v), s => Read(s))
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static string Write(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value.ToString(Format, CultureInfo.InvariantCulture)
            : throw new ArgumentException($"A *_utc column needs a UTC DateTime; got Kind {value.Kind}.", nameof(value));

    public static DateTime Read(string text) =>
        DateTime.ParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}

/// <summary>
/// Restaurant-local wall-clock values (*_local columns) as offset-free text 'yyyy-MM-ddTHH:mm:ss' (UQ16).
/// Only DateTimeKind.Unspecified is accepted: a local business value has no zone of its own.
/// </summary>
public sealed class LocalWallClockConverter() : ValueConverter<DateTime, string>(v => Write(v), s => Read(s))
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss";

    public static string Write(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? value.ToString(Format, CultureInfo.InvariantCulture)
            : throw new ArgumentException($"A *_local column needs an offset-free (Unspecified) DateTime; got Kind {value.Kind}.", nameof(value));

    public static DateTime Read(string text) =>
        DateTime.ParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None);
}
