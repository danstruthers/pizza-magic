using System.Text.Json;

namespace PizzaMagic.Domain.Promotions;

/// <summary>
/// One kind of promotion condition. A new kind is one class plus one registration; promos that use
/// existing kinds are pure data.
/// </summary>
public interface IConditionEvaluator
{
    string Type { get; }

    /// <summary>Validates and parses the JSON parameters. Throws <see cref="PromotionDataException"/>.</summary>
    object Parse(JsonElement parameters);

    ConditionResult Evaluate(object parsed, PromotionContext context, string code);
}

public sealed record ConditionResult(bool Passed, PromoMessage? Failure)
{
    public static ConditionResult Pass { get; } = new(true, null);

    public static ConditionResult Fail(PromoMessage failure) => new(false, failure);
}
