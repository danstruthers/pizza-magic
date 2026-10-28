namespace PizzaMagic.Domain.Ordering;

/// <summary>
/// How a topping reads in a description (A40): "Pepperoni", or "Pepperoni 2x" for extra layers (UQ29). Quotes and the
/// stored order snapshot both use this, so an order describes itself exactly as its quote did.
/// </summary>
public static class ToppingPhrase
{
    public static string Of(string name, int multiplier) => multiplier == 1 ? name : $"{name} {multiplier}x";
}
