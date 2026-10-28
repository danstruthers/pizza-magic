using System.Text.RegularExpressions;

namespace PizzaMagic.Domain.Promotions;

/// <summary>
/// Step 1 of the engine (plan section 2.6): trim and upper-case, check the format, collapse duplicates,
/// then apply the per-order limit to the distinct survivors, all in entry order.
/// </summary>
public static class CodeNormalizer
{
    public const int PromoCodeMaxLength = 32;

    private static readonly Regex Format = new($"^[A-Z0-9-]{{1,{PromoCodeMaxLength}}}$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<NormalizedCode> Normalize(IReadOnlyList<string> entered, PromotionCatalog catalog, int maxCodes)
    {
        var rows = catalog.Codes.ToDictionary(c => c.Code, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = 0;
        var result = new List<NormalizedCode>(entered.Count);

        foreach (var raw in entered)
        {
            var code = (raw ?? "").Trim().ToUpperInvariant();
            if (!Format.IsMatch(code))
            {
                result.Add(new NormalizedCode(raw ?? "", null, null, PromoStatus.InvalidFormat));
                continue;
            }

            rows.TryGetValue(code, out var row);
            // A repeated code, or a second code for the same promotion, collapses into the first.
            var key = row is null ? "code:" + code : "promotion:" + row.PromotionId;
            if (!seen.Add(key))
            {
                result.Add(new NormalizedCode(raw!, code, row, PromoStatus.Duplicate));
                continue;
            }

            distinct++;
            result.Add(new NormalizedCode(raw!, code, row, distinct > maxCodes ? PromoStatus.LimitExceeded : null));
        }
        return result;
    }
}

/// <summary>An entered code after normalization. <see cref="DroppedAs"/> is set when it goes no further.</summary>
public sealed record NormalizedCode(string Entered, string? Code, PromotionCodeRow? Row, PromoStatus? DroppedAs);
