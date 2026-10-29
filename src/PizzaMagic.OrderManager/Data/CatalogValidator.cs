using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tax;
using PizzaMagic.OrderManager.Data.Entities;

namespace PizzaMagic.OrderManager.Data;

/// <summary>
/// Cross-row and registry checks that SQL CHECKs cannot express (plan section 2.6 "Fail-safe data"). Runs at startup;
/// any error stops the service. Built scope (UQ7, A70): data that only unbuilt code would read is refused rather
/// than silently ignored.
/// </summary>
public sealed partial class CatalogValidator(
    PizzaMagicDbContext db, PromotionRegistry promotions, PricingMethodRegistry pricingMethods, TimeProvider clock)
{
    private const int CoverageYearsBack = 1;
    private const int CoverageYearsAhead = 5;
    private const string NotBuilt = "designed, not built (A70)";

    public async Task<IReadOnlyList<string>> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var restaurants = await db.Restaurants.AsNoTracking().OrderBy(r => r.Id).ToListAsync(cancellationToken);
        var products = await db.Products.AsNoTracking().OrderBy(p => p.Id).ToListAsync(cancellationToken);

        await CheckRestaurantsAsync(restaurants, errors, cancellationToken);
        await CheckTimeZonesAsync(restaurants, errors, cancellationToken);
        var tiersValid = await CheckTiersAsync(errors, cancellationToken);
        await CheckProductsAsync(products, errors, cancellationToken);
        await CheckOverridesAsync(restaurants, products, errors, cancellationToken);
        await CheckPromotionsAsync(errors, cancellationToken);
        await CheckUnbuiltFeaturesAsync(products, errors, cancellationToken);
        if (tiersValid)
        {
            await CheckMenusAsync(restaurants, errors, cancellationToken);
        }
        return errors;
    }

    private async Task CheckRestaurantsAsync(List<RestaurantEntity> restaurants, List<string> errors, CancellationToken cancellationToken)
    {
        var defaults = restaurants.Count(r => r.IsDefault);
        if (defaults != 1)
        {
            errors.Add($"Exactly one restaurant must be the default (found {defaults}).");
        }

        var policies = await db.OrderingPolicies.AsNoTracking().Select(p => p.RestaurantId).ToListAsync(cancellationToken);
        var jurisdictions = await db.TaxJurisdictions.AsNoTracking().ToListAsync(cancellationToken);
        var nodes = jurisdictions.Select(CatalogLoader.ToNode).ToList();
        foreach (var restaurant in restaurants.Where(r => r.IsActive))
        {
            if (!policies.Contains(restaurant.Id))
            {
                errors.Add($"Restaurant {restaurant.Code} has no ordering_policy row.");
            }

            var jurisdiction = jurisdictions.Single(j => j.Id == restaurant.TaxJurisdictionId);
            if (jurisdiction.ParentJurisdictionId is not null)
            {
                errors.Add($"Restaurant {restaurant.Code} is in jurisdiction {jurisdiction.Code}, which has a parent: multi-level tax is {NotBuilt}.");
            }
            try
            {
                TaxRoundingSettings.Resolve(JurisdictionChainResolver.Resolve(nodes, restaurant.TaxJurisdictionId));
            }
            catch (InvalidOperationException e)
            {
                errors.Add(e.Message);
            }
        }
    }

    /// <summary>Each zone an active restaurant uses has contiguous, non-overlapping periods covering [now - 1 y, now + 5 y].</summary>
    private async Task CheckTimeZonesAsync(List<RestaurantEntity> restaurants, List<string> errors, CancellationToken cancellationToken)
    {
        var zones = await db.TimeZones.AsNoTracking().ToListAsync(cancellationToken);
        var periods = (await db.TimeZoneOffsetPeriods.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(p => p.TimeZoneId);
        var now = clock.GetUtcNow().UtcDateTime;
        var needFrom = now.AddYears(-CoverageYearsBack);
        var needTo = now.AddYears(CoverageYearsAhead);

        foreach (var zone in zones.Where(z => restaurants.Any(r => r.IsActive && r.TimeZoneId == z.Id)))
        {
            if (!zone.IsActive)
            {
                errors.Add($"Time zone {zone.IanaId} is used by an active restaurant but is inactive.");
            }
            var ordered = periods[zone.Id].OrderBy(p => p.ValidFromUtc).ToList();
            if (ordered.Count == 0)
            {
                errors.Add($"Time zone {zone.IanaId} has no offset periods.");
                continue;
            }
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].ValidFromUtc > ordered[i - 1].ValidToUtc)
                {
                    errors.Add($"Time zone {zone.IanaId} has a gap at {Utc(ordered[i - 1].ValidToUtc)}.");
                }
                else if (ordered[i].ValidFromUtc < ordered[i - 1].ValidToUtc)
                {
                    errors.Add($"Time zone {zone.IanaId} has overlapping periods at {Utc(ordered[i].ValidFromUtc)}.");
                }
            }
            var coverage = $"Time zone {zone.IanaId} must cover {Utc(needFrom)} to {Utc(needTo)} (one year back, five years ahead)";
            if (ordered[0].ValidFromUtc > needFrom)
            {
                errors.Add($"{coverage}; it starts at {Utc(ordered[0].ValidFromUtc)}.");
            }
            if (ordered[^1].ValidToUtc < needTo)
            {
                errors.Add($"{coverage}; it ends at {Utc(ordered[^1].ValidToUtc)}.");
            }
        }
    }

    /// <summary>Every tier's pricing method is registered and has its parameters.</summary>
    private async Task<bool> CheckTiersAsync(List<string> errors, CancellationToken cancellationToken)
    {
        var valid = true;
        foreach (var tier in await db.ToppingTiers.AsNoTracking().OrderBy(t => t.Id).ToListAsync(cancellationToken))
        {
            try
            {
                pricingMethods.Get(tier.PricingMethod).UnitPrice(new PricingRule(tier.PricingMethod, tier.RatePpm, tier.AmountCents), new Money(1000));
            }
            catch (InvalidOperationException e)
            {
                errors.Add($"Topping tier {tier.Code}: {e.Message}");
                valid = false;
            }
        }
        return valid;
    }

    private async Task CheckProductsAsync(List<ProductEntity> products, List<string> errors, CancellationToken cancellationToken)
    {
        var classes = await db.TaxClasses.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        foreach (var product in products.Where(p => p.IsActive && !classes[p.TaxClassId].IsActive))
        {
            errors.Add($"Product {product.Code} is active but its tax class {classes[product.TaxClassId].Code} is not.");
        }
    }

    /// <summary>Override rows must match their product type (a join a CHECK cannot do).</summary>
    private async Task CheckOverridesAsync(
        List<RestaurantEntity> restaurants, List<ProductEntity> products, List<string> errors, CancellationToken cancellationToken)
    {
        var byId = products.ToDictionary(p => p.Id);
        foreach (var row in await db.RestaurantProductOverrides.AsNoTracking().ToListAsync(cancellationToken))
        {
            var restaurant = restaurants.Single(r => r.Id == row.RestaurantId).Code;
            var product = byId[row.ProductId];
            if (row.PriceCents is { } price && product.ProductTypeCode == "TOPPING")
            {
                errors.Add($"Restaurant {restaurant} overrides the price of {product.Code}, which is a topping (toppings are priced by rules).");
            }
            else if (row.PriceCents is { } sizePrice && product.ProductTypeCode == "PIZZA_SIZE" && sizePrice <= 0)
            {
                errors.Add($"Restaurant {restaurant} overrides the price of {product.Code} to {sizePrice}; a pizza size must cost more than zero.");
            }
            if (row.PricingMethod is not null && product.ProductTypeCode != "TOPPING")
            {
                errors.Add($"Restaurant {restaurant} sets a pricing rule for {product.Code}, which is not a topping.");
            }
        }
    }

    /// <summary>Every promotion's kinds are registered and their parameters parse; time-valued keys end in Local and carry no zone (UQ16).</summary>
    private async Task CheckPromotionsAsync(List<string> errors, CancellationToken cancellationToken)
    {
        var conditions = (await db.PromotionConditions.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(c => c.PromotionId);
        foreach (var promotion in await db.Promotions.AsNoTracking().OrderBy(p => p.Id).ToListAsync(cancellationToken))
        {
            var label = $"Promotion {promotion.Id} ({promotion.Name})";
            Check(label, promotion.EffectParams, json => promotions.Effect(promotion.EffectType).Parse(json), errors);
            foreach (var condition in conditions[promotion.Id].OrderBy(c => c.SortOrder).ThenBy(c => c.Id))
            {
                Check(label, condition.Params, json => promotions.Condition(condition.ConditionType).Parse(json), errors);
            }
        }
    }

    private static void Check(string label, string json, Func<JsonElement, object> parse, List<string> errors)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            parse(document.RootElement);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    var value = property.Value.GetString()!;
                    if (!TimeValue().IsMatch(value))
                    {
                        continue;
                    }
                    if (!property.Name.EndsWith("Local", StringComparison.Ordinal))
                    {
                        errors.Add($"{label}: time-valued parameter '{property.Name}' must use a key ending in 'Local'.");
                    }
                    else if (ZoneSuffix().IsMatch(value))
                    {
                        errors.Add($"{label}: parameter '{property.Name}' is a local value and must not carry Z or an offset.");
                    }
                }
            }
        }
        catch (Exception e) when (e is PromotionDataException or JsonException)
        {
            errors.Add($"{label}: {e.Message}");
        }
    }

    /// <summary>A70: rows that only designed-but-unbuilt code would read.</summary>
    private async Task CheckUnbuiltFeaturesAsync(List<ProductEntity> products, List<string> errors, CancellationToken cancellationToken)
    {
        foreach (var (table, feature) in new[]
        {
            ("restaurant_topping_tier_override", "restaurant tier overrides are"),
            ("restaurant_portion_type_override", "per-store portion factors are"),
            ("promotion_target", "item-scoped promotion targets are"),
            ("jurisdiction_tax_class_rate", "per-class tax rates are"),
        })
        {
            // The table names come from the fixed list above, never from input.
            var count = await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM " + table).SingleAsync(cancellationToken);
            if (count > 0)
            {
                errors.Add($"{table} has {count} row(s): {feature} {NotBuilt}.");
            }
        }

        foreach (var product in products.Where(p => p.PricingMethod is not null))
        {
            errors.Add($"Product {product.Code} has its own pricing rule: per-topping rules are {NotBuilt}.");
        }
        var overrideRules = await db.RestaurantProductOverrides.AsNoTracking().Where(o => o.PricingMethod != null).ToListAsync(cancellationToken);
        foreach (var row in overrideRules)
        {
            errors.Add($"Restaurant {row.RestaurantId} sets a pricing rule for {products.Single(p => p.Id == row.ProductId).Code}: per-topping rules are {NotBuilt}.");
        }

        var whole = await db.PortionTypes.AsNoTracking().SingleOrDefaultAsync(p => p.Code == "WHOLE", cancellationToken);
        if (whole is not null && whole.PriceFactorPpm != Rate.MaxPpm)
        {
            errors.Add($"Portion type WHOLE has factor {whole.PriceFactorPpm}; placement pricing is {NotBuilt}, so it must be {Rate.MaxPpm}.");
        }
        foreach (var product in products.Where(p => p.IsActive && p.ProductTypeCode == "ADDON"))
        {
            errors.Add($"Product {product.Code} is an add-on: add-on lines are {NotBuilt}.");
        }
        foreach (var product in products.Where(p => p.IsActive && !p.IsDefault && p.ProductTypeCode is "CRUST" or "SAUCE"))
        {
            errors.Add($"Product {product.Code} is a non-default crust or sauce: upgrades are {NotBuilt}.");
        }
    }

    /// <summary>Each active restaurant's menu builds and has a default crust and sauce.</summary>
    private async Task CheckMenusAsync(List<RestaurantEntity> restaurants, List<string> errors, CancellationToken cancellationToken)
    {
        var loader = new CatalogLoader(db, pricingMethods);
        foreach (var restaurant in restaurants.Where(r => r.IsActive))
        {
            try
            {
                var menu = (await loader.LoadAsync(restaurant.Id, cancellationToken)).Menu;
                if (menu.DefaultCrust is null)
                {
                    errors.Add($"Restaurant {restaurant.Code} has no available crust.");
                }
                if (menu.DefaultSauce is null)
                {
                    errors.Add($"Restaurant {restaurant.Code} has no available sauce.");
                }
            }
            catch (InvalidOperationException e)
            {
                errors.Add($"Restaurant {restaurant.Code}: {e.Message}");
            }
        }
    }

    private static string Utc(DateTime value) => UtcTimestampConverter.Write(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>A date, a time of day, or a date-time, optionally with Z or an offset.</summary>
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2}(\.\d+)?)?)?|\d{2}:\d{2}(:\d{2})?)(Z|[+-]\d{2}:?\d{2})?$")]
    private static partial Regex TimeValue();

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex ZoneSuffix();
}
