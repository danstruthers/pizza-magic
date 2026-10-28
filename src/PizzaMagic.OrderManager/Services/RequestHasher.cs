using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Pricing;

namespace PizzaMagic.OrderManager.Services;

/// <summary>
/// The idempotency request hash (plan section 2.11): SHA-256 over a canonical form of the raw request after
/// syntactic normalization only. Codes are trimmed and upper-cased, omitted portion/multiplier are made explicit,
/// lines and toppings keep entry order, and the tip text is kept as sent. No catalog data is used, so the hash is
/// computed before the catalog loads; two bodies that differ only in how toppings were written are different requests.
/// </summary>
public static class RequestHasher
{
    public const string CreateOperation = "CreateOrder";

    private sealed record CanonicalTopping(string Code, string Portion, int Multiplier);

    private sealed record CanonicalLine(string Kind, string Size, int Quantity, string? Crust, string? Sauce, string? Product, IReadOnlyList<CanonicalTopping> Toppings);

    private sealed record Canonical(
        string Operation,
        long? RestaurantId,
        string? Channel,
        IReadOnlyList<CanonicalLine> Lines,
        IReadOnlyList<string> PromoCodes,
        string? TipMode,
        string? TipValue,
        long? ExpectedTotalCents,
        string? CustomerName);

    public static string HashCreate(SubmitOrderRequest request) => Hash(new Canonical(
        CreateOperation,
        request.RestaurantId,
        Code(request.Channel),
        (request.LineItems ?? []).Select(l => new CanonicalLine(
            Code(l.Kind) ?? "",
            Code(l.SizeCode) ?? "",
            l.Quantity,
            Code(l.CrustCode),
            Code(l.SauceCode),
            Code(l.ProductCode),
            (l.Toppings ?? []).Select(t => new CanonicalTopping(
                Code(t.Code) ?? "",
                Code(t.Portion) ?? DraftValidator.WholePortionCode,
                t.Multiplier ?? 1)).ToList())).ToList(),
        (request.PromoCodes ?? []).Select(c => Code(c) ?? "").ToList(),
        Code(request.Tip?.Mode),
        request.Tip?.Value,
        request.ExpectedTotalCents,
        request.CustomerName?.Trim()));

    private static string? Code(string? value) => value?.Trim().ToUpperInvariant();

    private static string Hash(Canonical canonical)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
