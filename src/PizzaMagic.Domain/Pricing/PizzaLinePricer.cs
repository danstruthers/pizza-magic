using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Pricing;

/// <summary>
/// Prices one pizza line against a restaurant's effective menu. Every component is already in whole
/// cents before it is multiplied by the quantity (decision J). Input must be validated first
/// (DraftValidator); anything that cannot be priced throws.
/// </summary>
public static class PizzaLinePricer
{
    public static PricedPizzaLine Price(EffectiveMenu menu, PizzaLineRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Quantity);

        var size = Require(menu, request.SizeCode, ProductType.PizzaSize);
        var crust = request.CrustCode is { } crustCode
            ? Require(menu, crustCode, ProductType.Crust)
            : menu.DefaultCrust ?? throw new InvalidOperationException("No crust is available at this restaurant.");
        var sauce = request.SauceCode is { } sauceCode
            ? Require(menu, sauceCode, ProductType.Sauce)
            : menu.DefaultSauce ?? throw new InvalidOperationException("No sauce is available at this restaurant.");
        var basePrice = PriceOf(size);

        var components = new List<PricedComponent>
        {
            new(ComponentKind.Base, size, basePrice, size.PriceSource, size.TaxClassId),
            new(ComponentKind.Crust, crust, PriceOf(crust), crust.PriceSource, crust.TaxClassId),
            new(ComponentKind.Sauce, sauce, PriceOf(sauce), sauce.PriceSource, sauce.TaxClassId),
        };
        var toppings = request.ToppingCodes
            .Select(code => Require(menu, code, ProductType.Topping))
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id);
        foreach (var topping in toppings)
        {
            var price = menu.ToppingUnitPrice(topping, basePrice);
            components.Add(new PricedComponent(ComponentKind.Topping, topping, price.UnitPrice, price.Source, topping.TaxClassId, price));
        }

        var unitPrice = components.Aggregate(Money.Zero, (sum, c) => sum + c.UnitCharge);
        return new PricedPizzaLine(size, crust, sauce, components, request.Quantity, unitPrice, unitPrice * request.Quantity);
    }

    private static EffectiveProduct Require(EffectiveMenu menu, string code, ProductType type) =>
        menu.TryGetAvailable(code, type, out var product)
            ? product
            : throw new InvalidOperationException($"{type} '{code}' is not available at restaurant {menu.RestaurantId}.");

    private static Money PriceOf(EffectiveProduct product) =>
        product.Price ?? throw new InvalidOperationException($"'{product.Code}' has no price.");
}
