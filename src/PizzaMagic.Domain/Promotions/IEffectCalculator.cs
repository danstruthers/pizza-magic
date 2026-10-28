using System.Text.Json;
using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Promotions;

/// <summary>
/// One kind of order-level discount. It sees only what remains of the order after earlier promos,
/// and must return between zero and that amount.
/// </summary>
public interface IEffectCalculator
{
    string Type { get; }

    /// <summary>Validates and parses the JSON parameters. Throws <see cref="PromotionDataException"/>.</summary>
    object Parse(JsonElement parameters);

    Money Apply(object parsed, Money remaining);
}
