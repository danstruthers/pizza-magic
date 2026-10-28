namespace PizzaMagic.Domain.Catalog;

/// <summary>Topping pricing methods by code.</summary>
public sealed class PricingMethodRegistry
{
    private readonly Dictionary<string, IToppingPricingMethod> _methods = new(StringComparer.Ordinal);

    public PricingMethodRegistry(IEnumerable<IToppingPricingMethod> methods)
    {
        foreach (var method in methods)
        {
            if (!_methods.TryAdd(method.Code, method))
            {
                throw new ArgumentException($"Pricing method '{method.Code}' is registered twice.", nameof(methods));
            }
        }
    }

    public static PricingMethodRegistry Default { get; } = new([new PercentOfBaseMethod(), new FixedAmountMethod()]);

    public IToppingPricingMethod Get(string code) =>
        _methods.TryGetValue(code, out var method)
            ? method
            : throw new InvalidOperationException($"Unknown topping pricing method '{code}'.");
}
