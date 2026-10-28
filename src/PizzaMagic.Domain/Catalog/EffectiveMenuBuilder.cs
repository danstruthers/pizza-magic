using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Catalog;

/// <summary>
/// Resolves the global catalog plus one restaurant's overrides into its <see cref="EffectiveMenu"/>
/// (plan section 2.4). Pure: no I/O, no clock.
/// </summary>
public static class EffectiveMenuBuilder
{
    public static EffectiveMenu Build(CatalogSnapshot catalog, RestaurantOverrides overrides, PricingMethodRegistry registry)
    {
        // Resolve every tier's method up front, so unknown methods fail when the menu is built, not mid-quote.
        var tierRules = catalog.Tiers.ToDictionary(t => t.Id, t => (t.Rule, registry.Get(t.Rule.Method)));

        var activeTiers = catalog.Tiers.Where(t => t.IsActive).Select(t => t.Id).ToHashSet();
        var activeCategories = catalog.Categories.Where(c => c.IsActive).Select(c => c.Id).ToHashSet();
        var activeTaxClasses = catalog.TaxClasses.Where(c => c.IsActive).Select(c => c.Id).ToHashSet();
        var productOverrides = overrides.Products.ToDictionary(o => o.ProductId);

        var available = new List<EffectiveProduct>();
        foreach (var product in catalog.Products)
        {
            productOverrides.TryGetValue(product.Id, out var productOverride);

            // Global deactivation always wins: a restaurant cannot re-enable a retired product, tier or category.
            var isAvailable = overrides.RestaurantIsActive
                && product.IsActive
                && activeTaxClasses.Contains(product.TaxClassId)
                && (product.Type != ProductType.Topping || (product.ToppingTierId is { } tier && activeTiers.Contains(tier)))
                && (product.CategoryId is not { } category || activeCategories.Contains(category))
                && (productOverride?.IsAvailable ?? true);
            if (!isAvailable)
            {
                continue;
            }

            var restaurantPrice = productOverride?.PriceCents;
            var priceCents = restaurantPrice ?? product.PriceCents;
            available.Add(new EffectiveProduct(
                product.Id,
                product.Type,
                product.Code,
                product.DisplayName,
                priceCents is { } cents ? new Money(cents) : null,
                restaurantPrice is null ? PriceSource.Global : PriceSource.Restaurant,
                product.TaxClassId,
                product.ToppingTierId,
                product.SortOrder,
                product.MaxMultiplier));
        }

        var globalDefaults = catalog.Products.Where(p => p.IsDefault).Select(p => p.Id).ToHashSet();
        var catalogTypes = catalog.Products.ToDictionary(p => p.Code, p => p.Type);
        var tiers = catalog.Tiers
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .Select(t => new ToppingTierInfo(t.Id, t.Code, t.DisplayName, t.SortOrder))
            .ToList();
        return new EffectiveMenu(overrides.RestaurantId, available, catalogTypes, globalDefaults, tierRules, tiers);
    }
}
