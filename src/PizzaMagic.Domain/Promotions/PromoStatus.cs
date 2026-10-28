namespace PizzaMagic.Domain.Promotions;

/// <summary>The outcome of one entered promo code (plan section 2.6).</summary>
public enum PromoStatus
{
    Applied,
    NotEligible,
    Rejected,
    Duplicate,
    InvalidFormat,
    LimitExceeded,
    Unknown,
    Inactive,
    Expired,
    NotYetActive,
    NotAvailableHere,
    Misconfigured,
}
