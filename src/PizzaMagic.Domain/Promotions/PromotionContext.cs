using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Time;

namespace PizzaMagic.Domain.Promotions;

/// <summary>What promotions are evaluated against: the restaurant, the instant, its time zone and the cart.</summary>
public sealed record PromotionContext(
    long RestaurantId,
    DateTimeOffset NowUtc,
    TimeZonePeriods Zone,
    Money GrossSubtotal,
    int MaxPromoCodes);
