namespace PizzaMagic.Contracts.Api;

// Wire contract for /api/v1 (plan section 5): camelCase JSON, money in integer cents, codes are catalog codes.
// Request fields are nullable so the server's validator, not model binding, reports every problem with its path.

/// <summary>A draft to price (POST /api/v1/quotes). Submit and update reuse the same shape in M10.</summary>
public sealed record QuoteRequest(
    long? RestaurantId,
    string? Channel,
    IReadOnlyList<LineItemRequest>? LineItems,
    IReadOnlyList<string>? PromoCodes,
    TipRequest? Tip);

/// <summary>
/// One line. <c>kind</c> is "Pizza" (built) or "AddOn" (designed, not built). <c>portion</c> and <c>multiplier</c>
/// on toppings exist so placements can be added without a contract change; the built scope accepts WHOLE only, 1x up to
/// the topping's max multiplier (A69, UQ29).
/// </summary>
public sealed record LineItemRequest(
    string? Kind,
    string? SizeCode,
    int Quantity,
    IReadOnlyList<ToppingRequest>? Toppings,
    string? CrustCode = null,
    string? SauceCode = null,
    string? ProductCode = null);

public sealed record ToppingRequest(string? Code, string? Portion = null, int? Multiplier = null);

/// <summary>A tip as the customer typed it: mode "Amount" ("4.50" dollars) or "Percent" ("17.5"); at most two decimals.</summary>
public sealed record TipRequest(string? Mode, string? Value);

/// <summary>The priced draft: every intermediate value, the normalized draft the server will accept, and canSubmit.</summary>
public sealed record QuoteResponse(
    long RestaurantId,
    string Channel,
    IReadOnlyList<QuoteLineResponse> LineItems,
    long SubtotalCents,
    IReadOnlyList<PromotionOutcomeResponse> Promotions,
    IReadOnlyList<string> NormalizedPromoCodes,
    long DiscountCents,
    long TaxableCents,
    TaxRoundingResponse TaxRounding,
    IReadOnlyList<TaxLineResponse> TaxLines,
    long TaxCents,
    long BillCents,
    TipResponse Tip,
    long TotalCents,
    IReadOnlyList<IssueResponse> Issues,
    NormalizedDraftResponse NormalizedDraft,
    bool CanSubmit,
    string Description);

public sealed record QuoteLineResponse(
    int Index,
    int LineNumber,
    string Kind,
    int Quantity,
    PricedProductResponse Size,
    PricedChoiceResponse Crust,
    PricedChoiceResponse Sauce,
    IReadOnlyList<PricedToppingResponse> Toppings,
    long UnitPriceCents,
    long LineTotalCents,
    long DiscountCents,
    IReadOnlyList<string> NormalizationNotes,
    string Description);

public sealed record PricedProductResponse(string Code, string Name, long PriceCents, string PriceSource);

public sealed record PricedChoiceResponse(string Code, string Name, long PriceCents, bool IsDefault);

public sealed record PricedToppingResponse(
    string Code,
    string Name,
    string Tier,
    string Portion,
    string PortionLabel,
    int Multiplier,
    string PriceSource,
    long ToppingUnitPriceCents,
    int CoverageFactorPpm,
    long PlacementUnitChargeCents,
    long PlacementChargeCents);

/// <summary>One entered promo code's outcome; money in the message is already formatted.</summary>
public sealed record PromotionOutcomeResponse(
    string Code,
    string Status,
    long DiscountCents,
    string? Message,
    IReadOnlyList<string> ConflictsWith,
    IReadOnlyList<AllocationResponse> Allocations);

public sealed record AllocationResponse(int LineNumber, string Component, string? ToppingCode, string? Portion, long AmountCents);

public sealed record TaxRoundingResponse(string Method, string Scope);

public sealed record TaxLineResponse(
    string JurisdictionCode,
    int Depth,
    string TaxClassCode,
    long GrossCents,
    long DiscountCents,
    long TaxableBaseCents,
    int RatePpm,
    string RateSource,
    long TaxCents);

/// <summary>
/// The tip: mode, the text entered, the percent as ppm (Percent mode), the amount, and for a dollar tip its
/// effective rate of the bill in basis points (UQ22; format with PercentFormatter.FormatBasisPoints).
/// </summary>
public sealed record TipResponse(string Mode, string? Input, int? RatePpm, long TipCents, long? EffectiveRateBasisPoints);

public sealed record IssueResponse(string Code, string Message);

/// <summary>The draft exactly as the server will accept it: canonical codes, explicit crust and sauce, held promo codes.</summary>
public sealed record NormalizedDraftResponse(
    string Channel,
    IReadOnlyList<LineItemRequest> LineItems,
    IReadOnlyList<string> PromoCodes,
    TipRequest? Tip);
