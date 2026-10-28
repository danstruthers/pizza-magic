using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Time;
using PizzaMagic.OrderManager.Data.Entities;

namespace PizzaMagic.OrderManager.Services;

/// <summary>Every way an order operation can end; <c>OrderResults</c> maps each to exactly one HTTP response.</summary>
public enum OrderOutcome
{
    /// <summary>201 (create) or 200 (update, cancel, advance), with Body or Payload.</summary>
    Ok,
    /// <summary>The stored response of an earlier request with the same idempotency key and body.</summary>
    Replayed,
    KeyMissing,
    KeyInvalid,
    KeyReused,
    /// <summary>400: the request itself can't be understood (bad header, missing expectedStatus).</summary>
    BadRequest,
    /// <summary>428: PUT without If-Match.</summary>
    PreconditionRequired,
    NotFound,
    NotModifiable,
    NotCancellable,
    InvalidTransition,
    /// <summary>412: If-Match names an older version.</summary>
    VersionMismatch,
    /// <summary>422 validation.</summary>
    Invalid,
    PromotionNotApplied,
    PriceChanged,
}

/// <summary>
/// The result of an order operation. A stored response (create, update, replays) travels as <see cref="Body"/> so a
/// replay is byte-identical; other successes carry a <see cref="Payload"/>. Conflicts carry the current order so the
/// client can say what happened and when.
/// </summary>
public sealed record OrderResult(
    OrderOutcome Outcome,
    string? OrderId = null,
    string? Body = null,
    string? ETag = null,
    object? Payload = null,
    IReadOnlyList<FieldError>? Errors = null,
    QuoteResponse? CurrentQuote = null,
    CustomerOrderEntity? Current = null,
    TimeZonePeriods? Zone = null,
    string? Detail = null);
