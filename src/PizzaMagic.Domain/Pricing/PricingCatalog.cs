using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tax;
using PizzaMagic.Domain.Time;

namespace PizzaMagic.Domain.Pricing;

/// <summary>Menu-facing limits for one restaurant (ordering_policy row).</summary>
public sealed record OrderingPolicy(
    int MaxLines,
    int MaxLineQuantity,
    int MaxToppingsPerPizza,
    int MaxPromoCodes,
    long MaxTipAmountCents,
    int MaxTipPercentPpm);

/// <summary>
/// Everything one restaurant prices with, loaded once per request: its effective menu, the promotions,
/// its tax jurisdiction chain (outermost first), its ordering policy and its time zone.
/// </summary>
public sealed record PricingCatalog(
    EffectiveMenu Menu,
    PromotionCatalog Promotions,
    IReadOnlyList<JurisdictionNode> Chain,
    OrderingPolicy Policy,
    TimeZonePeriods Zone);

/// <summary>The instant the order is priced at (from the injected clock).</summary>
public sealed record PricingContext(DateTimeOffset NowUtc);
