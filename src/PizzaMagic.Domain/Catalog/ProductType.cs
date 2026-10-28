namespace PizzaMagic.Domain.Catalog;

/// <summary>
/// Structural product types: each decides a line's shape, so adding a type is a code change.
/// Mirrored by the product_type lookup table. Products themselves are data.
/// </summary>
public enum ProductType
{
    PizzaSize,
    Topping,
    Crust,
    Sauce,
    AddOn,
}
