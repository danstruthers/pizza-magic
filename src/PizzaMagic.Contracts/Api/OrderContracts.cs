namespace PizzaMagic.Contracts.Api;

/// <summary>
/// POST /api/v1/orders (requires an Idempotency-Key header). The draft fields are the quote's; expectedTotalCents is
/// the total the customer saw and confirmed, so the order is never accepted at a different price (409 otherwise).
/// </summary>
public sealed record SubmitOrderRequest(
    long? RestaurantId,
    string? Channel,
    IReadOnlyList<LineItemRequest>? LineItems,
    IReadOnlyList<string>? PromoCodes,
    TipRequest? Tip,
    long? ExpectedTotalCents,
    string? CustomerName);

/// <summary>
/// PUT /api/v1/orders/{id} (requires If-Match: "version"; optional Idempotency-Key). Replaces the order's content
/// while it is Not Started. There is no restaurantId or channel: those are fixed at creation (400 if sent).
/// </summary>
public sealed record UpdateOrderRequest(
    IReadOnlyList<LineItemRequest>? LineItems,
    IReadOnlyList<string>? PromoCodes,
    TipRequest? Tip,
    long? ExpectedTotalCents,
    string? CustomerName);

/// <summary>POST /api/v1/kitchen/orders/{id}/advance: the status the kitchen believes the order is in (required).</summary>
public sealed record AdvanceOrderRequest(string? ExpectedStatus);

/// <summary>GET /api/v1/kitchen/orders: one restaurant's active orders (not yet Delivered or Cancelled), oldest first.</summary>
public sealed record KitchenQueueResponse(long RestaurantId, IReadOnlyList<KitchenOrderResponse> Orders);

/// <summary>
/// One active order as the kitchen sees it: what to make (one description per line), when it was placed, and the
/// status it moves to next (send <c>Status</c> as <c>expectedStatus</c> to advance it).
/// </summary>
public sealed record KitchenOrderResponse(
    string OrderId,
    string Status,
    string StatusDisplay,
    int Version,
    string CreatedAt,
    LocalTimeResponse? CreatedAtLocal,
    string? CustomerName,
    IReadOnlyList<string> Items,
    long TotalCents,
    string NextStatus,
    string NextStatusDisplay);

/// <summary>
/// The kitchen's result: the new status, whether the call was a repeat (alreadyApplied), and the content the kitchen
/// committed to, read in the same transaction as the status change.
/// </summary>
public sealed record AdvanceOrderResponse(
    string OrderId,
    string PreviousStatus,
    string Status,
    int Version,
    bool AlreadyApplied,
    IReadOnlyList<QuoteLineResponse> LineItems,
    string Description);

/// <summary>A stored order, rendered only from its snapshot (never re-priced).</summary>
public sealed record OrderResponse(
    string OrderId,
    long RestaurantId,
    string Channel,
    string? CustomerName,
    string Status,
    string StatusDisplay,
    int Version,
    string CreatedAt,
    string StatusChangedAt,
    LocalTimeResponse? CreatedAtLocal,
    LocalTimeResponse? StatusChangedAtLocal,
    IReadOnlyList<string> AllowedActions,
    OrderPricingResponse Pricing,
    string Description);

/// <summary>The priced snapshot: the same shape as a quote's pricing fields.</summary>
public sealed record OrderPricingResponse(
    IReadOnlyList<QuoteLineResponse> LineItems,
    long SubtotalCents,
    IReadOnlyList<PromotionOutcomeResponse> Promotions,
    long DiscountCents,
    long TaxableCents,
    TaxRoundingResponse TaxRounding,
    IReadOnlyList<TaxLineResponse> TaxLines,
    long TaxCents,
    long BillCents,
    TipResponse Tip,
    long TotalCents);

/// <summary>GET /api/v1/orders/{id}/status: the light polling payload.</summary>
public sealed record OrderStatusResponse(
    string OrderId,
    string Status,
    string StatusDisplay,
    int Version,
    string StatusChangedAt,
    LocalTimeResponse? StatusChangedAtLocal,
    IReadOnlyList<string> AllowedActions);

/// <summary>
/// A server-computed, never-stored display companion for a UTC instant, in the order's restaurant zone (UQ15).
/// Null when the instant is outside the zone's seeded coverage; the client then shows UTC.
/// </summary>
public sealed record LocalTimeResponse(string LocalTime, int UtcOffsetMinutes, string Abbreviation);
