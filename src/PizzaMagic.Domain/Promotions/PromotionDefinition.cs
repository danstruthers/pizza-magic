namespace PizzaMagic.Domain.Promotions;

/// <summary>
/// One promotion as stored in the database. Its effect and conditions are kinds plus JSON parameters,
/// so a new promo that uses existing kinds is pure data. Validity values are restaurant-local wall-clock
/// times (UQ16): each restaurant resolves them in its own time zone. Lower priority applies first.
/// </summary>
public sealed record PromotionDefinition(
    long Id,
    string Name,
    string EffectType,
    string EffectParams,
    int Priority = PromotionDefinition.DefaultPriority,
    bool IsExclusive = false,
    string? ExclusionGroup = null,
    DateTime? ValidFromLocal = null,
    DateTime? ValidToLocal = null,
    bool IsActive = true)
{
    /// <summary>The <c>promotion.priority</c> column default.</summary>
    public const int DefaultPriority = 100;

    public IReadOnlyList<ConditionDefinition> Conditions { get; init; } = [];

    /// <summary>Restaurants the promotion is limited to; empty means every restaurant.</summary>
    public IReadOnlyList<long> RestaurantIds { get; init; } = [];
}

/// <summary>One condition row: a registered kind plus its JSON parameters. All conditions must pass.</summary>
public sealed record ConditionDefinition(string Type, string Params);

/// <summary>A customer-facing code; many codes can map to one promotion.</summary>
public sealed record PromotionCodeRow(string Code, long PromotionId, bool IsActive = true);

/// <summary>An explicit, symmetric "cannot be combined" pair.</summary>
public sealed record IncompatiblePair(long PromotionIdLow, long PromotionIdHigh);

/// <summary>Every promotion, code and incompatibility pair, loaded with the catalog.</summary>
public sealed record PromotionCatalog(
    IReadOnlyList<PromotionDefinition> Promotions,
    IReadOnlyList<PromotionCodeRow> Codes,
    IReadOnlyList<IncompatiblePair> Incompatibilities);
