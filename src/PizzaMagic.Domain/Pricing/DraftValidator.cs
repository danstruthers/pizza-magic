using PizzaMagic.Domain.Catalog;

namespace PizzaMagic.Domain.Pricing;

/// <summary>
/// Pipeline step 0 (plan section 5.2): every field rule, collecting all errors with their JSON paths.
/// Limits come from the restaurant's ordering policy; products must be available at this restaurant.
/// Promo codes are never field errors here (they get per-code outcomes).
/// </summary>
public static class DraftValidator
{
    /// <summary>The only portion the built scope accepts (A69).</summary>
    public const string WholePortionCode = "WHOLE";

    public static IReadOnlyList<FieldError> Validate(OrderDraft draft, PricingCatalog catalog)
    {
        var policy = catalog.Policy;
        var menu = catalog.Menu;
        var errors = new List<FieldError>();

        if (draft.LineItems.Count > policy.MaxLines)
        {
            errors.Add(new FieldError("lineItems", "TOO_MANY_LINES", $"An order can have at most {Plural(policy.MaxLines, "line")}."));
        }

        for (var i = 0; i < draft.LineItems.Count; i++)
        {
            var line = draft.LineItems[i];
            var path = $"lineItems[{i}]";

            if (line.Quantity < 1 || line.Quantity > policy.MaxLineQuantity)
            {
                errors.Add(new FieldError($"{path}.quantity", "QUANTITY_OUT_OF_RANGE", $"Quantity must be between 1 and {policy.MaxLineQuantity}."));
            }

            CheckProduct(menu, line.SizeCode, ProductType.PizzaSize, $"{path}.sizeCode", "a pizza size", errors);
            CheckChoice(menu, line.CrustCode, ProductType.Crust, menu.DefaultCrust, $"{path}.crustCode", "crust", errors);
            CheckChoice(menu, line.SauceCode, ProductType.Sauce, menu.DefaultSauce, $"{path}.sauceCode", "sauce", errors);

            if (line.Toppings.Count > policy.MaxToppingsPerPizza)
            {
                errors.Add(new FieldError($"{path}.toppings", "TOO_MANY_TOPPINGS",
                    $"A pizza can have at most {Plural(policy.MaxToppingsPerPizza, "topping")}."));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var j = 0; j < line.Toppings.Count; j++)
            {
                var topping = line.Toppings[j];
                var toppingPath = $"{path}.toppings[{j}]";
                CheckProduct(menu, topping.Code, ProductType.Topping, $"{toppingPath}.code", "a topping", errors);

                var isWholeOnce = (topping.Portion is null || Canonical(topping.Portion) == WholePortionCode)
                    && (topping.Multiplier is null or 1);
                if (!isWholeOnce || !seen.Add(Canonical(topping.Code)))
                {
                    errors.Add(new FieldError(toppingPath, "PLACEMENT_NOT_SUPPORTED",
                        "Half, quarter and extra toppings are designed but not built in this version."));
                }
            }
        }

        var tipLimit = draft.Tip.Mode switch
        {
            TipMode.Amount => policy.MaxTipAmountCents,
            TipMode.Percent => policy.MaxTipPercentPpm,
            _ => (long?)null,
        };
        if (tipLimit is { } limit && (draft.Tip.Value < 0 || draft.Tip.Value > limit))
        {
            errors.Add(new FieldError("tip.value", "TIP_OUT_OF_RANGE", "The tip is outside the allowed range."));
        }

        return errors;
    }

    /// <summary>Codes are trimmed and case-insensitive; catalog codes are upper case.</summary>
    public static string Canonical(string? code) => (code ?? "").Trim().ToUpperInvariant();

    private static void CheckProduct(EffectiveMenu menu, string? code, ProductType type, string path, string kind, List<FieldError> errors)
    {
        var canonical = Canonical(code);
        switch (menu.Lookup(canonical, type, out _))
        {
            case ProductLookup.Unknown:
                errors.Add(new FieldError(path, "UNKNOWN_PRODUCT", $"'{code}' isn't on the menu."));
                break;
            case ProductLookup.WrongType:
                errors.Add(new FieldError(path, "WRONG_PRODUCT_TYPE", $"'{code}' isn't {kind}."));
                break;
            case ProductLookup.Unavailable:
                errors.Add(new FieldError(path, "PRODUCT_UNAVAILABLE", $"'{code}' isn't available at this restaurant."));
                break;
        }
    }

    private static void CheckChoice(
        EffectiveMenu menu, string? code, ProductType type, EffectiveProduct? fallback, string path, string kind, List<FieldError> errors)
    {
        if (code is not null)
        {
            CheckProduct(menu, code, type, path, $"a {kind}", errors);
        }
        else if (fallback is null)
        {
            var errorCode = type == ProductType.Crust ? "CRUST_REQUIRED" : "SAUCE_REQUIRED";
            errors.Add(new FieldError(path, errorCode, $"Choose a {kind}."));
        }
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
