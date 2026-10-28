namespace PizzaMagic.Domain.Pricing;

/// <summary>
/// An order as the customer builds it: a full replacement each time (removing a line means leaving it out).
/// Codes are as entered; the validator and engine normalize them.
/// </summary>
public sealed record OrderDraft(IReadOnlyList<PizzaDraftLine> LineItems, IReadOnlyList<string> PromoCodes, TipInput Tip);

/// <summary>A pizza line: size, quantity and toppings, with optional crust and sauce (defaults when omitted).</summary>
public sealed record PizzaDraftLine(
    string SizeCode,
    int Quantity,
    IReadOnlyList<DraftTopping> Toppings,
    string? CrustCode = null,
    string? SauceCode = null);

/// <summary>
/// One topping entry. The wire contract keeps <see cref="Portion"/> and <see cref="Multiplier"/> so placements
/// can be added later (X1) without a contract change; the built scope accepts only WHOLE x1 (A69).
/// </summary>
public sealed record DraftTopping(string Code, string? Portion = null, int? Multiplier = null);
