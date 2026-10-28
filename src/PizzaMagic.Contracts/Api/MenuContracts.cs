namespace PizzaMagic.Contracts.Api;

/// <summary>
/// GET /api/v1/menu: one restaurant's effective menu with a server-computed topping price grid, so the order
/// builder never computes a price (preview = charge). Built scope: only WHOLE prices and no portion list (A69).
/// </summary>
public sealed record MenuResponse(
    long RestaurantId,
    string RestaurantName,
    IReadOnlyList<MenuSizeResponse> Sizes,
    IReadOnlyList<MenuChoiceResponse> Crusts,
    IReadOnlyList<MenuChoiceResponse> Sauces,
    IReadOnlyList<MenuAddOnCategoryResponse> AddOnCategories,
    IReadOnlyList<MenuTierResponse> ToppingTiers,
    MenuPolicyResponse Policy);

public sealed record MenuSizeResponse(string Code, string Name, long PriceCents);

public sealed record MenuChoiceResponse(string Code, string Name, long PriceCents, bool IsDefault);

/// <summary>Designed, not built (X2): always empty in the built scope.</summary>
public sealed record MenuAddOnCategoryResponse(string Code, string Name);

public sealed record MenuTierResponse(string Code, string Name, IReadOnlyList<MenuToppingResponse> Toppings);

/// <summary>
/// A topping. Prices by size code, then portion type code: prices["LARGE"]["WHOLE"] = 96. It may go on one pizza
/// 1x..<c>MaxMultiplier</c> times (UQ29).
/// </summary>
public sealed record MenuToppingResponse(
    string Code,
    string Name,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Prices,
    int MaxMultiplier = 1);

public sealed record MenuPolicyResponse(
    int MaxLines,
    int MaxLineQuantity,
    int MaxToppingsPerPizza,
    int MaxPromoCodes,
    long MaxTipAmountCents,
    int MaxTipPercentPpm,
    IReadOnlyList<int> TipPresetsPpm);
