namespace PizzaMagic.Domain.Promotions;

/// <summary>Registered condition and effect kinds by type key. A duplicate key fails at startup.</summary>
public sealed class PromotionRegistry
{
    private readonly Dictionary<string, IConditionEvaluator> _conditions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IEffectCalculator> _effects = new(StringComparer.Ordinal);

    public PromotionRegistry(IEnumerable<IConditionEvaluator> conditions, IEnumerable<IEffectCalculator> effects)
    {
        foreach (var condition in conditions)
        {
            if (!_conditions.TryAdd(condition.Type, condition))
            {
                throw new ArgumentException($"Condition kind '{condition.Type}' is registered twice.", nameof(conditions));
            }
        }
        foreach (var effect in effects)
        {
            if (!_effects.TryAdd(effect.Type, effect))
            {
                throw new ArgumentException($"Effect kind '{effect.Type}' is registered twice.", nameof(effects));
            }
        }
    }

    public static PromotionRegistry Default { get; } = new([new MinSubtotal()], [new PercentOffOrder(), new FixedOffOrder()]);

    public IConditionEvaluator Condition(string type) =>
        _conditions.TryGetValue(type, out var condition) ? condition : throw new PromotionDataException($"Unknown condition kind '{type}'.");

    public IEffectCalculator Effect(string type) =>
        _effects.TryGetValue(type, out var effect) ? effect : throw new PromotionDataException($"Unknown effect kind '{type}'.");
}
