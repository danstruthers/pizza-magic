namespace PizzaMagic.Domain.Pricing;

public enum TipMode
{
    None,
    Amount,
    Percent,
}

/// <summary>
/// A tip already parsed at the API boundary: whole cents for <see cref="TipMode.Amount"/>, parts per million
/// for <see cref="TipMode.Percent"/>. Ranges are checked by the validator against the ordering policy.
/// </summary>
public sealed record TipInput(TipMode Mode, long Value)
{
    public static TipInput None { get; } = new(TipMode.None, 0);
}
