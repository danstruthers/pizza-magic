namespace PizzaMagic.Domain.Pricing;

/// <summary>
/// One pizza line as the customer asked for it: whole toppings, each listed once (the built scope, A69), each
/// 1x..max times (UQ29; a code missing from <see cref="ToppingMultipliers"/> is 1x). Omitted crust or sauce means
/// the restaurant's default.
/// </summary>
public sealed record PizzaLineRequest(
    string SizeCode,
    int Quantity,
    IReadOnlyList<string> ToppingCodes,
    string? CrustCode = null,
    string? SauceCode = null,
    IReadOnlyDictionary<string, int>? ToppingMultipliers = null);
