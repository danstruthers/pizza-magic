using Microsoft.EntityFrameworkCore;
using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tax;
using PizzaMagic.Domain.Time;
using PizzaMagic.OrderManager.Data.Entities;

namespace PizzaMagic.OrderManager.Data;

/// <summary>
/// Loads everything one restaurant prices with into a Domain <see cref="PricingCatalog"/> (plan section 4.6): a
/// handful of no-tracking queries, no cache, so a data change takes effect on the next request.
/// </summary>
public sealed class CatalogLoader(PizzaMagicDbContext db, PricingMethodRegistry? pricingMethods = null)
{
    private readonly PricingMethodRegistry _pricingMethods = pricingMethods ?? PricingMethodRegistry.Default;

    /// <summary>Loads the given restaurant, or the default restaurant when <paramref name="restaurantId"/> is null.</summary>
    public async Task<PricingCatalog> LoadAsync(long? restaurantId, CancellationToken cancellationToken = default)
    {
        var restaurant = restaurantId is { } id
            ? await db.Restaurants.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            : await db.Restaurants.AsNoTracking().SingleOrDefaultAsync(r => r.IsDefault, cancellationToken);
        if (restaurant is null)
        {
            throw new InvalidOperationException(restaurantId is null ? "There is no default restaurant." : $"Restaurant {restaurantId} does not exist.");
        }

        var snapshot = new CatalogSnapshot(
            (await db.Products.AsNoTracking().ToListAsync(cancellationToken)).Select(ToRow).ToList(),
            (await db.ToppingTiers.AsNoTracking().ToListAsync(cancellationToken))
                .Select(t => new ToppingTierRow(t.Id, t.Code, t.DisplayName, new PricingRule(t.PricingMethod, t.RatePpm, t.AmountCents), t.SortOrder, t.IsActive))
                .ToList(),
            (await db.ProductCategories.AsNoTracking().ToListAsync(cancellationToken)).Select(c => new ProductCategoryRow(c.Id, c.Code, c.IsActive)).ToList(),
            (await db.TaxClasses.AsNoTracking().ToListAsync(cancellationToken)).Select(c => new TaxClassRow(c.Id, c.Code, c.IsActive)).ToList());

        var overrides = new RestaurantOverrides(
            restaurant.Id,
            restaurant.IsActive,
            (await db.RestaurantProductOverrides.AsNoTracking().Where(o => o.RestaurantId == restaurant.Id).ToListAsync(cancellationToken))
                .Select(o => new ProductOverrideRow(o.ProductId, o.IsAvailable, o.PriceCents))
                .ToList());

        var jurisdictions = (await db.TaxJurisdictions.AsNoTracking().ToListAsync(cancellationToken)).Select(ToNode).ToList();
        var policy = await db.OrderingPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.RestaurantId == restaurant.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Restaurant {restaurant.Code} has no ordering_policy row.");

        return new PricingCatalog(
            EffectiveMenuBuilder.Build(snapshot, overrides, _pricingMethods),
            await LoadPromotionsAsync(cancellationToken),
            JurisdictionChainResolver.Resolve(jurisdictions, restaurant.TaxJurisdictionId),
            new OrderingPolicy(policy.MaxLines, policy.MaxLineQuantity, policy.MaxToppingsPerPizza, policy.MaxPromoCodes, policy.MaxTipAmountCents, policy.MaxTipPercentPpm),
            await LoadZoneAsync(restaurant.TimeZoneId, cancellationToken));
    }

    public async Task<TimeZonePeriods> LoadZoneAsync(long timeZoneId, CancellationToken cancellationToken = default)
    {
        var zone = await db.TimeZones.AsNoTracking().SingleAsync(z => z.Id == timeZoneId, cancellationToken);
        var periods = await db.TimeZoneOffsetPeriods.AsNoTracking()
            .Where(p => p.TimeZoneId == timeZoneId)
            .OrderBy(p => p.ValidFromUtc)
            .Select(p => new OffsetPeriod(p.ValidFromUtc, p.ValidToUtc, p.UtcOffsetMinutes, p.Abbreviation, p.IsDst))
            .ToListAsync(cancellationToken);
        return new TimeZonePeriods(zone.Id, zone.IanaId, periods);
    }

    private async Task<PromotionCatalog> LoadPromotionsAsync(CancellationToken cancellationToken)
    {
        var conditions = (await db.PromotionConditions.AsNoTracking().ToListAsync(cancellationToken))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToLookup(c => c.PromotionId);
        var restaurants = (await db.PromotionRestaurants.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(r => r.PromotionId);

        var promotions = (await db.Promotions.AsNoTracking().ToListAsync(cancellationToken))
            .Select(p => new PromotionDefinition(
                p.Id, p.Name, p.EffectType, p.EffectParams, p.Priority, p.IsExclusive, p.ExclusionGroup, p.ValidFromLocal, p.ValidToLocal, p.IsActive)
            {
                Conditions = conditions[p.Id].Select(c => new ConditionDefinition(c.ConditionType, c.Params)).ToList(),
                RestaurantIds = restaurants[p.Id].Select(r => r.RestaurantId).ToList(),
            })
            .ToList();
        var codes = (await db.PromotionCodes.AsNoTracking().ToListAsync(cancellationToken))
            .Select(c => new PromotionCodeRow(c.Code, c.PromotionId, c.IsActive))
            .ToList();
        var pairs = (await db.PromotionIncompatibilities.AsNoTracking().ToListAsync(cancellationToken))
            .Select(p => new IncompatiblePair(p.PromotionIdLow, p.PromotionIdHigh))
            .ToList();
        return new PromotionCatalog(promotions, codes, pairs);
    }

    internal static ProductRow ToRow(ProductEntity p) => new(
        p.Id, ToProductType(p.ProductTypeCode), p.Code, p.DisplayName, p.PriceCents, p.ToppingTierId, p.CategoryId, p.TaxClassId, p.IsDefault, p.SortOrder, p.IsActive);

    internal static ProductType ToProductType(string code) => code switch
    {
        "PIZZA_SIZE" => ProductType.PizzaSize,
        "TOPPING" => ProductType.Topping,
        "CRUST" => ProductType.Crust,
        "SAUCE" => ProductType.Sauce,
        "ADDON" => ProductType.AddOn,
        _ => throw new InvalidOperationException($"Unknown product type '{code}'."),
    };

    internal static JurisdictionNode ToNode(TaxJurisdictionEntity j) => new(
        j.Id,
        j.Code,
        j.Name,
        j.ParentJurisdictionId,
        j.DefaultRatePpm,
        j.TaxRoundingMethod switch { "HALF_UP" => TaxRoundingMethod.HalfUp, "TRUNCATE" => TaxRoundingMethod.Truncate, _ => null },
        j.TaxRoundingScope switch { "COMBINED" => TaxRoundingScope.Combined, "PER_LEVEL" => TaxRoundingScope.PerLevel, _ => null });
}
