using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PizzaMagic.Contracts.Api;

namespace PizzaMagic.Web.Models.Checkout;

/// <summary>
/// What the Review page showed, sealed into a hidden form field (plan section 6.2): the server's normalized draft, its
/// total, the submission key and, when changing an order, its id and version. Place submits exactly this, never the
/// Session cart, so the customer is charged what they saw.
/// </summary>
public sealed record ReviewTicket(
    NormalizedDraftResponse Draft,
    long ExpectedTotalCents,
    string SubmissionKey,
    long Revision,
    Guid? OrderId,
    int? Version);

/// <summary>Seals and opens review tickets: tamper-evident and time-limited.</summary>
public sealed class ReviewTicketProtector(IDataProtectionProvider provider)
{
    /// <summary>As long as the Session's idle timeout: a ticket never outlives the cart it was built from.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ITimeLimitedDataProtector protector =
        provider.CreateProtector("PizzaMagic.Web.ReviewTicket.v1").ToTimeLimitedDataProtector();

    public string Protect(ReviewTicket ticket) => protector.Protect(JsonSerializer.Serialize(ticket, Json), Lifetime);

    /// <summary>The ticket, or null when it was altered, has expired or isn't a ticket.</summary>
    public ReviewTicket? TryUnprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<ReviewTicket>(protector.Unprotect(value), Json);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
