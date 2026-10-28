using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PizzaMagic.Contracts;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.OrderManager.Data.Entities;

namespace PizzaMagic.OrderManager.Services;

public enum SubmitOutcome
{
    Created,
    Replayed,
    KeyMissing,
    KeyInvalid,
    KeyReused,
    Invalid,
    PromotionNotApplied,
    PriceChanged,
}

/// <summary>M11 red-phase stub.</summary>
public enum OrderOutcome
{
    Ok,
    NotCancellable,
    InvalidTransition,
    NotModifiable,
}

/// <summary>M11 red-phase stub.</summary>
public sealed record OrderResult(OrderOutcome Outcome, object? Payload = null);

/// <summary>The result of a submit; the controller turns it into the HTTP response.</summary>
public sealed record SubmitResult(
    SubmitOutcome Outcome,
    string? OrderId = null,
    string? Body = null,
    string? ETag = null,
    IReadOnlyList<FieldError>? Errors = null,
    QuoteResponse? CurrentQuote = null);

/// <summary>
/// Order submission and reads (plan section 2.11, create algorithm). Pricing happens outside the writer lock; the
/// IMMEDIATE transaction holds only the idempotency re-check and the writes. At most one order per key.
/// </summary>
public sealed partial class OrderService(
    PizzaMagicDbContext db, OrderPricer pricer, CatalogLoader loader, TimeProvider clock, JsonSerializerOptions json, IConfiguration configuration)
{
    private const int MaxCustomerNameLength = 50;
    private const int SqlitePrimaryKeyViolation = 1555;

    private TimeSpan IdempotencyTtl => TimeSpan.FromHours(configuration.GetValue("Ordering:IdempotencyTtlHours", 24));

    public async Task<SubmitResult> SubmitAsync(SubmitOrderRequest request, string? key, CancellationToken cancellationToken = default)
    {
        // 1. Headers.
        if (string.IsNullOrEmpty(key))
        {
            return new SubmitResult(SubmitOutcome.KeyMissing);
        }
        if (!ValidKey().IsMatch(key))
        {
            return new SubmitResult(SubmitOutcome.KeyInvalid);
        }

        // 2. Idempotency lookup, before any pricing (the hash needs no catalog data).
        var hash = RequestHasher.HashCreate(request);
        var now = clock.GetUtcNow().UtcDateTime;
        if (await LiveRecordAsync(key, now, cancellationToken) is { } existing)
        {
            return Replay(existing, hash);
        }

        // 3. Price outside the lock; refusals are not stored, so the client can fix the request and retry with the key.
        var errors = new List<FieldError>();
        if (request.ExpectedTotalCents is null)
        {
            errors.Add(new FieldError("expectedTotalCents", "REQUIRED", "The total the customer confirmed is required."));
        }
        var customerName = request.CustomerName?.Trim();
        if (request.CustomerName is not null
            && (customerName!.Length is 0 or > MaxCustomerNameLength || customerName.Any(char.IsControl)))
        {
            errors.Add(new FieldError("customerName", "CUSTOMER_NAME_INVALID", $"The name must be 1 to {MaxCustomerNameLength} characters."));
        }

        var outcome = await pricer.PriceAsync(
            new QuoteRequest(request.RestaurantId, request.Channel, request.LineItems, request.PromoCodes, request.Tip), cancellationToken);
        errors.AddRange(outcome.Errors);
        if (outcome.Breakdown is { Lines.Count: 0 })
        {
            errors.Add(new FieldError("lineItems", "ORDER_EMPTY", "Add at least one pizza to your order."));
        }
        if (errors.Count > 0)
        {
            return new SubmitResult(SubmitOutcome.Invalid, Errors: errors);
        }

        var breakdown = outcome.Breakdown!;
        var promotionErrors = breakdown.Promotions.Outcomes
            .Select((o, i) => (o, i))
            .Where(x => x.o.Status is not (PromoStatus.Applied or PromoStatus.Duplicate))
            .Select(x => new FieldError($"promoCodes[{x.i}]", x.o.Status.ToString(), x.o.Message?.Render(MoneyFormatter.Format) ?? x.o.Status.ToString()))
            .ToList();
        if (promotionErrors.Count > 0)
        {
            return new SubmitResult(SubmitOutcome.PromotionNotApplied, Errors: promotionErrors, CurrentQuote: ResponseMapper.ToQuote(outcome));
        }
        if (breakdown.Total.Cents != request.ExpectedTotalCents)
        {
            return new SubmitResult(SubmitOutcome.PriceChanged, CurrentQuote: ResponseMapper.ToQuote(outcome));
        }

        // 4-7. The order is charged at the prices just computed, which the customer confirmed.
        var orderId = Guid.NewGuid().ToString();
        var order = OrderSnapshot.Build(orderId, outcome, customerName, now);
        var zone = await loader.LoadZoneAsync(order.TimeZoneId, cancellationToken);
        var body = JsonSerializer.Serialize(OrderView.ToResponse(order, zone), json);
        var etag = ETag(order.Version);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Re-check inside the writer lock: a concurrent duplicate may have won.
            var raced = await db.IdempotencyRecords.SingleOrDefaultAsync(
                r => r.IdempotencyKey == key && r.Operation == RequestHasher.CreateOperation, cancellationToken);
            if (raced is not null && raced.ExpiresAtUtc > now)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Replay(raced, hash);
            }
            if (raced is not null)
            {
                db.IdempotencyRecords.Remove(raced);   // expired: free the key for this new order
            }

            db.Orders.Add(order);
            db.IdempotencyRecords.Add(new IdempotencyRecordEntity
            {
                IdempotencyKey = key,
                Operation = RequestHasher.CreateOperation,
                RequestHash = hash,
                ResponseStatus = StatusCodes.Status201Created,
                ResponseBody = body,
                ResponseEtag = etag,
                OrderId = orderId,
                CreatedAtUtc = now,
                ExpiresAtUtc = now + IdempotencyTtl,
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SubmitResult(SubmitOutcome.Created, orderId, body, etag);
        }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteExtendedErrorCode: SqlitePrimaryKeyViolation })
        {
            // Backstop for multi-process deployments: another writer stored the key first; replay it.
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return Replay((await LiveRecordAsync(key, now, cancellationToken))!, hash);
        }
    }

    public async Task<CustomerOrderEntity?> FindAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var id = orderId.ToString();
        return await db.Orders.AsNoTrackingWithIdentityResolution()
            .Include(o => o.Lines).ThenInclude(l => l.Toppings)
            .Include(o => o.Promotions).ThenInclude(p => p.Allocations).ThenInclude(a => a.OrderLine)
            .Include(o => o.Promotions).ThenInclude(p => p.Allocations).ThenInclude(a => a.OrderLineTopping)
            .Include(o => o.TaxLines)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public Task<OrderResult> UpdateAsync(Guid orderId, UpdateOrderRequest request, string? ifMatch, string? key, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<OrderResult> CancelAsync(Guid orderId, string? ifMatch, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<OrderResult> AdvanceAsync(Guid orderId, AdvanceOrderRequest request, Domain.Ordering.OrderActor actor, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<Domain.Time.TimeZonePeriods> ZoneAsync(CustomerOrderEntity order, CancellationToken cancellationToken = default) =>
        loader.LoadZoneAsync(order.TimeZoneId, cancellationToken);

    public static string ETag(int version) => $"\"{version}\"";

    private async Task<IdempotencyRecordEntity?> LiveRecordAsync(string key, DateTime now, CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(
            r => r.IdempotencyKey == key && r.Operation == RequestHasher.CreateOperation, cancellationToken);
        return record is not null && record.ExpiresAtUtc > now ? record : null;
    }

    private static SubmitResult Replay(IdempotencyRecordEntity record, string hash) =>
        record.RequestHash == hash
            ? new SubmitResult(SubmitOutcome.Replayed, record.OrderId, record.ResponseBody, record.ResponseEtag)
            : new SubmitResult(SubmitOutcome.KeyReused);

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex ValidKey();
}
