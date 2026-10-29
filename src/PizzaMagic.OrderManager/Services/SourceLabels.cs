using PizzaMagic.Domain.Catalog;

namespace PizzaMagic.OrderManager.Services;

/// <summary>
/// The "where did this price or rate come from" labels, shared by quotes and stored orders so they can't drift. In the
/// built scope a topping is always priced by its tier's rule (topping and tier overrides are refused at startup, A70)
/// and a tax rate is always its jurisdiction's default (class rates are refused too).
/// </summary>
public static class SourceLabels
{
    public const string DefaultRate = "Default";

    /// <summary>One of the schema's topping price sources: GlobalTier, or RestaurantTier for a store tier override.</summary>
    public static string Topping(ToppingPrice price) => price.Source == PriceSource.Restaurant ? "RestaurantTier" : "GlobalTier";
}
