namespace PizzaMagic.Domain.Pricing;

/// <summary>The priced parts of a pizza, in canonical order. Discounts and tax are allocated per component.</summary>
public enum ComponentKind
{
    Base,
    Crust,
    Sauce,
    Topping,
}
