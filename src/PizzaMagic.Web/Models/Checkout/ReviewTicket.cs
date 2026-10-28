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
#pragma warning disable CS9113 // M14 red stub
public sealed class ReviewTicketProtector(IDataProtectionProvider provider)
#pragma warning restore CS9113
{
    public string Protect(ReviewTicket ticket) => throw new NotImplementedException();

    /// <summary>The ticket, or null when it was altered, has expired or isn't a ticket.</summary>
    public ReviewTicket? TryUnprotect(string? value) => throw new NotImplementedException();
}
