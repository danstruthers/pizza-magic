using System.Diagnostics.CodeAnalysis;
using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Catalog;

/// <summary>
/// The menu one restaurant sells, with its overrides applied (plan section 2.4). Built by
/// <see cref="EffectiveMenuBuilder"/>; used by both the menu endpoint and the pricing engine,
/// so the price shown and the price charged come from the same place.
/// </summary>
public sealed class EffectiveMenu
{
    private readonly Dictionary<(ProductType, string), EffectiveProduct> _available;
    private readonly Dictionary<long, (PricingRule Rule, IToppingPricingMethod Method)> _tierRules;

    internal EffectiveMenu(
        long restaurantId,
        IEnumerable<EffectiveProduct> available,
        IReadOnlySet<long> globalDefaultIds,
        Dictionary<long, (PricingRule, IToppingPricingMethod)> tierRules)
    {
        RestaurantId = restaurantId;
        _available = available.ToDictionary(p => (p.Type, p.Code));
        _tierRules = tierRules;
        DefaultCrust = DefaultOf(ProductType.Crust, globalDefaultIds);
        DefaultSauce = DefaultOf(ProductType.Sauce, globalDefaultIds);
    }

    public long RestaurantId { get; }

    /// <summary>The crust used when a pizza line omits one; null if no crust is available here.</summary>
    public EffectiveProduct? DefaultCrust { get; }

    /// <summary>The sauce used when a pizza line omits one; null if no sauce is available here.</summary>
    public EffectiveProduct? DefaultSauce { get; }

    public bool TryGetAvailable(string code, ProductType type, [MaybeNullWhen(false)] out EffectiveProduct product) =>
        _available.TryGetValue((type, code), out product);

    /// <summary>Available products of one type, in display order.</summary>
    public IReadOnlyList<EffectiveProduct> Available(ProductType type) =>
        _available.Values.Where(p => p.Type == type).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToList();

    /// <summary>The topping's price on a pizza whose effective base price is <paramref name="effectiveBase"/> (RP1).</summary>
    public ToppingPrice ToppingUnitPrice(EffectiveProduct topping, Money effectiveBase)
    {
        if (topping.Type != ProductType.Topping || topping.ToppingTierId is not { } tierId || !_tierRules.TryGetValue(tierId, out var tier))
        {
            throw new InvalidOperationException($"'{topping.Code}' is not a priced topping.");
        }

        var unitPrice = tier.Method.UnitPrice(tier.Rule, effectiveBase);
        var rate = tier.Rule.RatePpm is { } ppm ? new Rate(ppm) : (Rate?)null;
        return new ToppingPrice(unitPrice, tier.Rule.Method, rate, PriceSource.Global);
    }

    /// <summary>The global default of the type if it is available here, else the first available by (sort order, id).</summary>
    private EffectiveProduct? DefaultOf(ProductType type, IReadOnlySet<long> globalDefaultIds)
    {
        var candidates = Available(type);
        return candidates.FirstOrDefault(p => globalDefaultIds.Contains(p.Id)) ?? candidates.FirstOrDefault();
    }
}
