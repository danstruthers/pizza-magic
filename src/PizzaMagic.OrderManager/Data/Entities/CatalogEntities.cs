namespace PizzaMagic.OrderManager.Data.Entities;

// Catalog and configuration rows (plan section 4.3). Column names come from the snake_case convention;
// table names are set explicitly in PizzaMagicDbContext. Order tables are mapped in M10.

public sealed class TaxClassEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class TaxJurisdictionEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public long? ParentJurisdictionId { get; set; }
    public int DefaultRatePpm { get; set; }
    public string? TaxRoundingMethod { get; set; }
    public string? TaxRoundingScope { get; set; }
}

public sealed class TimeZoneEntity
{
    public long Id { get; set; }
    public string IanaId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class TimeZoneOffsetPeriodEntity
{
    public long Id { get; set; }
    public long TimeZoneId { get; set; }
    public DateTime ValidFromUtc { get; set; }
    public DateTime ValidToUtc { get; set; }
    public int UtcOffsetMinutes { get; set; }
    public string Abbreviation { get; set; } = "";
    public bool IsDst { get; set; }
}

public sealed class RestaurantEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long TimeZoneId { get; set; }
    public long TaxJurisdictionId { get; set; }
    public string CurrencyCode { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
}

public sealed class OrderingPolicyEntity
{
    public long RestaurantId { get; set; }
    public int MaxLines { get; set; }
    public int MaxLineQuantity { get; set; }
    public int MaxToppingsPerPizza { get; set; }
    public int MaxPromoCodes { get; set; }
    public long MaxTipAmountCents { get; set; }
    public int MaxTipPercentPpm { get; set; }
    public string TipPresetsPpm { get; set; } = "[]";
}

public sealed class ProductCategoryEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProductTypeCode { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ToppingTierEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PricingMethod { get; set; } = "";
    public int? RatePpm { get; set; }
    public long? AmountCents { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ProductEntity
{
    public long Id { get; set; }
    public string ProductTypeCode { get; set; } = "";
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long? CategoryId { get; set; }
    public long? ToppingTierId { get; set; }
    public long? PriceCents { get; set; }
    public string? PricingMethod { get; set; }
    public int? RatePpm { get; set; }
    public long? AmountCents { get; set; }
    public int? MaxMultiplier { get; set; }
    public long TaxClassId { get; set; }
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class PortionTypeEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceFactorPpm { get; set; }
}

public sealed class RestaurantProductOverrideEntity
{
    public long RestaurantId { get; set; }
    public long ProductId { get; set; }
    public bool? IsAvailable { get; set; }
    public long? PriceCents { get; set; }
    public string? PricingMethod { get; set; }
    public int? RatePpm { get; set; }
    public long? AmountCents { get; set; }
}

public sealed class PromotionEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string CustomerDescription { get; set; } = "";
    public string EffectType { get; set; } = "";
    public string EffectParams { get; set; } = "{}";
    public int Priority { get; set; }
    public bool IsExclusive { get; set; }
    public string? ExclusionGroup { get; set; }
    public DateTime? ValidFromLocal { get; set; }
    public DateTime? ValidToLocal { get; set; }
    public bool IsActive { get; set; }
}

public sealed class PromotionCodeEntity
{
    public string Code { get; set; } = "";
    public long PromotionId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class PromotionConditionEntity
{
    public long Id { get; set; }
    public long PromotionId { get; set; }
    public string ConditionType { get; set; } = "";
    public string Params { get; set; } = "{}";
    public string? FailureMessage { get; set; }
    public int SortOrder { get; set; }
}

public sealed class PromotionIncompatibilityEntity
{
    public long PromotionIdLow { get; set; }
    public long PromotionIdHigh { get; set; }
}

public sealed class PromotionRestaurantEntity
{
    public long PromotionId { get; set; }
    public long RestaurantId { get; set; }
}

public sealed class DataScriptLogEntity
{
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTime AppliedAtUtc { get; set; }
}
