namespace PizzaMagic.Domain.Tax;

/// <summary>How a jurisdiction rounds tax: DB 'HALF_UP' / 'TRUNCATE'.</summary>
public enum TaxRoundingMethod
{
    HalfUp,
    Truncate,
}

/// <summary>Whether tax is rounded once over every level, or once per level: DB 'COMBINED' / 'PER_LEVEL'.</summary>
public enum TaxRoundingScope
{
    Combined,
    PerLevel,
}

/// <summary>
/// The rounding rule in force for a restaurant, resolved nearest-wins from its jurisdiction chain and
/// snapshotted on each order. The seed (Washington, RCW 82.08.054) is HALF_UP + COMBINED.
/// </summary>
public sealed record TaxRoundingSettings(TaxRoundingMethod Method, TaxRoundingScope Scope)
{
    /// <summary>
    /// For each setting, the value of the jurisdiction nearest the restaurant that sets it. The root
    /// (outermost) jurisdiction must set both, so resolution always succeeds for valid data.
    /// </summary>
    public static TaxRoundingSettings Resolve(IReadOnlyList<JurisdictionNode> chainOutermostFirst)
    {
        TaxRoundingMethod? method = null;
        TaxRoundingScope? scope = null;
        for (var i = chainOutermostFirst.Count - 1; i >= 0; i--)
        {
            method ??= chainOutermostFirst[i].RoundingMethod;
            scope ??= chainOutermostFirst[i].RoundingScope;
        }

        if (method is null || scope is null)
        {
            var root = chainOutermostFirst[0].Code;
            throw new InvalidOperationException($"Root tax jurisdiction {root} must set tax_rounding_method and tax_rounding_scope.");
        }
        return new TaxRoundingSettings(method.Value, scope.Value);
    }
}
