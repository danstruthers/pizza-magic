using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Promotions;

/// <summary>
/// The result for one entered code, in entry order. <see cref="ConflictsWith"/> lists every held code
/// a rejected code conflicts with (empty unless the status is <see cref="PromoStatus.Rejected"/>).
/// </summary>
public sealed record CodeOutcome(
    string EnteredCode,
    string? Code,
    long? PromotionId,
    PromoStatus Status,
    Money Discount,
    PromoMessage? Message,
    IReadOnlyList<string> ConflictsWith);

/// <summary>
/// Every code's outcome, the total discount, and the held codes (applied or not yet eligible) in entry
/// order, which the builder keeps as its promo list.
/// </summary>
public sealed record PromotionResult(IReadOnlyList<CodeOutcome> Outcomes, Money TotalDiscount, IReadOnlyList<string> NormalizedPromoCodes);
