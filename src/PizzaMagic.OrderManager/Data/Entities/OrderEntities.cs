namespace PizzaMagic.OrderManager.Data.Entities;

// Order snapshot rows (plan section 4.3, "Orders"). Everything needed to show, describe and re-derive an order is
// copied here at submit time, so a placed order never reads the live catalog.

public sealed class CustomerOrderEntity
{
    public string Id { get; set; } = "";
    public long RestaurantId { get; set; }
    public string ChannelCode { get; set; } = "";
    public string Status { get; set; } = "";
    public int Version { get; set; }
    public string? CustomerName { get; set; }
    public long TaxJurisdictionId { get; set; }
    public long TimeZoneId { get; set; }
    public DateTime PricedAtUtc { get; set; }
    public long SubtotalCents { get; set; }
    public long DiscountCents { get; set; }
    public long TaxableCents { get; set; }
    public long TaxCents { get; set; }
    public string TaxRoundingMethod { get; set; } = "";
    public string TaxRoundingScope { get; set; } = "";
    public string TipMode { get; set; } = "None";
    public string? TipInput { get; set; }
    public int? TipRatePpm { get; set; }
    public long TipCents { get; set; }
    public long TotalCents { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime StatusChangedAtUtc { get; set; }

    public List<OrderLineEntity> Lines { get; set; } = [];
    public List<OrderPromotionEntity> Promotions { get; set; } = [];
    public List<OrderTaxLineEntity> TaxLines { get; set; } = [];
    public List<OrderStatusHistoryEntity> History { get; set; } = [];
}

public sealed class OrderLineEntity
{
    public long Id { get; set; }
    public string OrderId { get; set; } = "";
    public int LineNumber { get; set; }
    public string LineKind { get; set; } = "PIZZA";
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public long BasePriceCents { get; set; }
    public string BasePriceSource { get; set; } = "";
    public long TaxClassId { get; set; }
    public long? CrustProductId { get; set; }
    public string? CrustCode { get; set; }
    public string? CrustName { get; set; }
    public bool? CrustIsDefault { get; set; }
    public long? CrustPriceCents { get; set; }
    public long? CrustTaxClassId { get; set; }
    public long? SauceProductId { get; set; }
    public string? SauceCode { get; set; }
    public string? SauceName { get; set; }
    public bool? SauceIsDefault { get; set; }
    public long? SaucePriceCents { get; set; }
    public long? SauceTaxClassId { get; set; }
    public int Quantity { get; set; }
    public long UnitPriceCents { get; set; }
    public long LineTotalCents { get; set; }
    public long DiscountCents { get; set; }

    public List<OrderLineToppingEntity> Toppings { get; set; } = [];
}

public sealed class OrderLineToppingEntity
{
    public long Id { get; set; }
    public long OrderLineId { get; set; }
    public int SortOrder { get; set; }
    public long ToppingProductId { get; set; }
    public string ToppingCode { get; set; } = "";
    public string ToppingName { get; set; } = "";
    public string TierCode { get; set; } = "";
    public string PortionCode { get; set; } = "";
    public string PortionLabel { get; set; } = "";
    public int CoverageMask { get; set; }
    public string CoveragePhrase { get; set; } = "";
    public string PortionTypeCode { get; set; } = "";
    public int Multiplier { get; set; }
    public string PriceSource { get; set; } = "";
    public string PricingMethod { get; set; } = "";
    public int? RatePpm { get; set; }
    public long ToppingUnitPriceCents { get; set; }
    public int CoverageFactorPpm { get; set; }
    public string CoverageFactorSource { get; set; } = "";
    public long PlacementUnitChargeCents { get; set; }
    public long PlacementChargeCents { get; set; }
    public long TaxClassId { get; set; }
}

public sealed class OrderPromotionEntity
{
    public long Id { get; set; }
    public string OrderId { get; set; } = "";
    public int Sequence { get; set; }
    public string Code { get; set; } = "";
    public long PromotionId { get; set; }
    public string EffectType { get; set; } = "";
    public string Phase { get; set; } = "Order";
    public long DiscountCents { get; set; }
    public string Message { get; set; } = "";
    public string DefinitionSnapshot { get; set; } = "{}";

    public List<OrderDiscountAllocationEntity> Allocations { get; set; } = [];
}

public sealed class OrderDiscountAllocationEntity
{
    public long Id { get; set; }
    public long OrderPromotionId { get; set; }
    public long OrderLineId { get; set; }
    public string ComponentKind { get; set; } = "";
    public long? OrderLineToppingId { get; set; }
    public long TaxClassId { get; set; }
    public long AmountCents { get; set; }

    public OrderLineEntity? OrderLine { get; set; }
    public OrderLineToppingEntity? OrderLineTopping { get; set; }
}

public sealed class OrderTaxLineEntity
{
    public long Id { get; set; }
    public string OrderId { get; set; } = "";
    public long TaxJurisdictionId { get; set; }
    public string JurisdictionCode { get; set; } = "";
    public string JurisdictionName { get; set; } = "";
    public int JurisdictionDepth { get; set; }
    public long TaxClassId { get; set; }
    public string TaxClassCode { get; set; } = "";
    public long GrossCents { get; set; }
    public long DiscountCents { get; set; }
    public long TaxableBaseCents { get; set; }
    public int RatePpm { get; set; }
    public string RateSource { get; set; } = "Default";
    public long TaxCents { get; set; }
}

public sealed class OrderStatusHistoryEntity
{
    public long Id { get; set; }
    public string OrderId { get; set; } = "";
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Actor { get; set; } = "";
    public int OrderVersion { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class IdempotencyRecordEntity
{
    public string IdempotencyKey { get; set; } = "";
    public string Operation { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int ResponseStatus { get; set; }
    public string ResponseBody { get; set; } = "";
    public string? ResponseEtag { get; set; }
    public string OrderId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
