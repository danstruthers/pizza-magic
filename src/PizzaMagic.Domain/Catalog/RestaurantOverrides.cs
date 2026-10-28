namespace PizzaMagic.Domain.Catalog;

/// <summary>
/// One restaurant's sparse override rows. No row means the global catalog value applies;
/// the default restaurant has none.
/// </summary>
public sealed record RestaurantOverrides(long RestaurantId, bool RestaurantIsActive, IReadOnlyList<ProductOverrideRow> Products)
{
    public static RestaurantOverrides None(long restaurantId) => new(restaurantId, RestaurantIsActive: true, []);
}

/// <summary>A restaurant's override of one product: availability and/or price (NULL = use the global value).</summary>
public sealed record ProductOverrideRow(long ProductId, bool? IsAvailable, long? PriceCents);
