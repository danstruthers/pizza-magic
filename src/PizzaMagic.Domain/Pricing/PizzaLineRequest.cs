namespace PizzaMagic.Domain.Pricing;

/// <summary>
/// One pizza line as the customer asked for it: whole toppings, one of each (the built scope, A69).
/// Omitted crust or sauce means the restaurant's default.
/// </summary>
public sealed record PizzaLineRequest(
    string SizeCode,
    int Quantity,
    IReadOnlyList<string> ToppingCodes,
    string? CrustCode = null,
    string? SauceCode = null);
