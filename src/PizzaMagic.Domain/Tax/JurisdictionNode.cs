namespace PizzaMagic.Domain.Tax;

/// <summary>
/// One tax_jurisdiction row. A null parent is the outermost jurisdiction. Null rounding settings
/// inherit from the nearest ancestor that sets them.
/// </summary>
public sealed record JurisdictionNode(
    long Id,
    string Code,
    string Name,
    long? ParentId,
    int DefaultRatePpm,
    TaxRoundingMethod? RoundingMethod,
    TaxRoundingScope? RoundingScope);
