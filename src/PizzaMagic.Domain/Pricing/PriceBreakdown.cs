using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tax;

namespace PizzaMagic.Domain.Pricing;

/// <summary>A field-level validation error at a JSON path, e.g. lineItems[0].quantity.</summary>
public sealed record FieldError(string Path, string Code, string Message);

/// <summary>An order-level issue that does not block a quote, e.g. ORDER_EMPTY.</summary>
public sealed record PricingIssue(string Code, string Message);

/// <summary>A priced line with its 1-based line number (the allocation tie-break ordinal).</summary>
public sealed record PricedLine(int LineNumber, PricedPizzaLine Line);

/// <summary>The part of one promotion's discount attributed to one component of one line (plan section 2.8).</summary>
public sealed record DiscountAllocation(
    string Code,
    long PromotionId,
    int LineNumber,
    ComponentKind Kind,
    string ProductCode,
    long TaxClassId,
    Money Amount);

/// <summary>
/// The full quote: S (subtotal), D (discount), tax, bill, tip and total, with every intermediate value.
/// <see cref="NormalizedDraft"/> is the draft as the server will accept it; <see cref="CanSubmit"/> is true
/// exactly when submitting it at this instant would succeed.
/// </summary>
public sealed record PriceBreakdown(
    IReadOnlyList<PricedLine> Lines,
    Money Subtotal,
    PromotionResult Promotions,
    Money Discount,
    IReadOnlyList<DiscountAllocation> Allocations,
    Money Taxable,
    TaxResult Tax,
    Money Bill,
    Money Tip,
    long? TipEffectiveRateBasisPoints,
    Money Total,
    IReadOnlyList<PricingIssue> Issues,
    OrderDraft NormalizedDraft,
    bool CanSubmit);

/// <summary>Either field errors (a 422 on every endpoint) or a breakdown.</summary>
public sealed record PriceResult(IReadOnlyList<FieldError> Errors, PriceBreakdown? Breakdown);
