using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tax;
using PizzaMagic.Domain.Time;

namespace PizzaMagic.Domain.Pricing;

/// <summary>One restaurant's order limits and tip presets (its ordering_policy row); the menu shows them too.</summary>
public sealed record OrderingPolicy(
    int MaxLines,
    int MaxLineQuantity,
    int MaxToppingsPerPizza,
    int MaxPromoCodes,
    long MaxTipAmountCents,
    int MaxTipPercentPpm)
{
    /// <summary>Suggested percent tips shown as buttons, in ppm (e.g. 15%, 18%, 20%).</summary>
    public IReadOnlyList<int> TipPresetsPpm { get; init; } = [];
}

/// <summary>The restaurant a catalog belongs to.</summary>
public sealed record RestaurantInfo(long Id, string Code, string Name, bool IsActive);

/// <summary>
/// Everything one restaurant prices with, loaded once per request: its effective menu, the promotions,
/// its tax jurisdiction chain (outermost first), its ordering policy and its time zone.
/// </summary>
public sealed record PricingCatalog(
    EffectiveMenu Menu,
    PromotionCatalog Promotions,
    IReadOnlyList<JurisdictionNode> Chain,
    OrderingPolicy Policy,
    TimeZonePeriods Zone)
{
    /// <summary>The restaurant's identity, for display (set by the loader).</summary>
    public RestaurantInfo? Restaurant { get; init; }

    /// <summary>Tax class codes by id, for tax line display and snapshots (set by the loader).</summary>
    public IReadOnlyDictionary<long, string> TaxClassCodes { get; init; } = new Dictionary<long, string>();
}

/// <summary>The instant the order is priced at (from the injected clock).</summary>
public sealed record PricingContext(DateTimeOffset NowUtc);
