using System.Text.Json;

namespace PizzaMagic.Domain.Promotions;

/// <summary>Strict readers for promotion JSON parameters: money and rates must be integers (A67).</summary>
internal static class JsonParams
{
    public static long RequiredNonNegativeInt64(JsonElement parameters, string name) =>
        OptionalNonNegativeInt64(parameters, name) ?? throw new PromotionDataException($"'{name}' is required.");

    public static long? OptionalNonNegativeInt64(JsonElement parameters, string name)
    {
        if (!TryGet(parameters, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number < 0)
        {
            throw new PromotionDataException($"'{name}' must be a non-negative integer.");
        }
        return number;
    }

    public static bool OptionalBool(JsonElement parameters, string name, bool defaultValue)
    {
        if (!TryGet(parameters, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new PromotionDataException($"'{name}' must be true or false."),
        };
    }

    private static bool TryGet(JsonElement parameters, string name, out JsonElement value)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new PromotionDataException("Parameters must be a JSON object.");
        }
        return parameters.TryGetProperty(name, out value);
    }
}
