using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PizzaMagic.Contracts;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Ordering;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Time;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.OrderManager.Data.Entities;

namespace PizzaMagic.OrderManager.Services;

/// <summary>
/// Order submission, update, cancel and kitchen advance (plan sections 2.10 and 2.11).
/// <list type="bullet">
/// <item>Create and update price outside the writer lock; the IMMEDIATE transaction holds only the idempotency
/// re-check and the writes.</item>
/// <item>Every status change and content edit is one conditional UPDATE whose WHERE clause carries the precondition;
/// the affected-row count decides the outcome, and a 0 is diagnosed by re-reading.</item>
/// </list>
/// </summary>
public sealed partial class OrderService(
    PizzaMagicDbContext db, OrderPricer pricer, CatalogLoader loader, TimeProvider clock, JsonSerializerOptions json, IConfiguration configuration)
{
    private const int MaxCustomerNameLength = 50;
    private const int SqlitePrimaryKeyViolation = 1555;
    private static readonly string NotStarted = nameof(OrderStatus.NotStarted);

    private TimeSpan IdempotencyTtl => TimeSpan.FromHours(configuration.GetValue("Ordering:IdempotencyTtlHours", 24));

    // ---------------------------------------------------------------- create

    public async Task<OrderResult> SubmitAsync(SubmitOrderRequest request, string? key, CancellationToken cancellationToken = default)
    {
        // 1. Headers.
        if (string.IsNullOrEmpty(key))
        {
            return new OrderResult(OrderOutcome.KeyMissing);
        }
        if (!ValidKey().IsMatch(key))
        {
            return new OrderResult(OrderOutcome.KeyInvalid);
        }

        // 2. Idempotency lookup, before any pricing (the hash needs no catalog data).
        var hash = RequestHasher.HashCreate(request);
        var now = clock.GetUtcNow().UtcDateTime;
        if (await LiveRecordAsync(key, RequestHasher.CreateOperation, now, cancellationToken) is { } existing)
        {
            return Replay(existing, hash);
        }

        // 3. Price outside the lock; refusals are not stored, so the client can fix the request and retry with the key.
        var (refusal, outcome, customerName) = await PriceForWriteAsync(
            new QuoteRequest(request.RestaurantId, request.Channel, request.LineItems, request.PromoCodes, request.Tip),
            request.ExpectedTotalCents, request.CustomerName, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        // 4-7. The order is charged at the prices just computed, which the customer confirmed.
        var orderId = Guid.NewGuid().ToString();
        var order = OrderSnapshot.Build(orderId, outcome!, customerName, now);
        var body = JsonSerializer.Serialize(OrderView.ToResponse(order, await loader.LoadZoneAsync(order.TimeZoneId, cancellationToken)), json);
        var etag = ETag(order.Version);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Re-check inside the writer lock: a concurrent duplicate may have won.
            if (await ClaimKeyAsync(key, RequestHasher.CreateOperation, now, cancellationToken) is { } raced)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Replay(raced, hash);
            }

            db.Orders.Add(order);
            AddRecord(key, RequestHasher.CreateOperation, hash, StatusCodes.Status201Created, body, etag, orderId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OrderResult(OrderOutcome.Ok, orderId, body, etag);
        }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteExtendedErrorCode: SqlitePrimaryKeyViolation })
        {
            // Backstop for multi-process deployments: another writer stored the key first; replay it.
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return Replay((await LiveRecordAsync(key, RequestHasher.CreateOperation, now, cancellationToken))!, hash);
        }
    }

    // ---------------------------------------------------------------- update

    /// <summary>Replace the content while NotStarted (section 2.11 PUT algorithm).</summary>
    public async Task<OrderResult> UpdateAsync(
        Guid orderId, UpdateOrderRequest request, string? ifMatch, string? key, CancellationToken cancellationToken = default)
    {
        // 1. Headers.
        if (ifMatch is null)
        {
            return new OrderResult(OrderOutcome.PreconditionRequired);
        }
        if (!TryParseVersion(ifMatch, out var version))
        {
            return BadIfMatch();
        }
        if (key is not null && !ValidKey().IsMatch(key))
        {
            return new OrderResult(OrderOutcome.KeyInvalid);
        }

        // 2. Idempotency lookup: a retry after a lost response replays the 200 instead of getting 412.
        var hash = RequestHasher.HashUpdate(orderId, ifMatch, request);
        var now = clock.GetUtcNow().UtcDateTime;
        if (key is not null && await LiveRecordAsync(key, RequestHasher.UpdateOperation, now, cancellationToken) is { } existing)
        {
            return Replay(existing, hash);
        }

        // 3. Fast pre-checks; the conditional UPDATE below is authoritative.
        var id = orderId.ToString();
        var stored = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (await DiagnoseEditAsync(stored, version, cancellationToken) is { } preCheck)
        {
            return preCheck;
        }

        // 4. Price with the stored restaurant and channel at current catalog data.
        var (refusal, outcome, customerName) = await PriceForWriteAsync(
            new QuoteRequest(stored!.RestaurantId, stored.ChannelCode, request.LineItems, request.PromoCodes, request.Tip),
            request.ExpectedTotalCents, request.CustomerName, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var updated = OrderSnapshot.Build(id, outcome!, customerName, now);
        updated.Version = version + 1;
        updated.CreatedAtUtc = stored.CreatedAtUtc;
        updated.StatusChangedAtUtc = stored.StatusChangedAtUtc;
        updated.History.Clear();
        var body = JsonSerializer.Serialize(OrderView.ToResponse(updated, await loader.LoadZoneAsync(updated.TimeZoneId, cancellationToken)), json);
        var etag = ETag(updated.Version);

        // 5-7. One IMMEDIATE transaction: re-check the key, conditional UPDATE, replace the children.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (key is not null && await ClaimKeyAsync(key, RequestHasher.UpdateOperation, now, cancellationToken) is { } raced)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Replay(raced, hash);
        }

        var rows = await db.Orders
            .Where(o => o.Id == id && o.Status == NotStarted && o.Version == version)
            .ExecuteUpdateAsync(set => set
                .SetProperty(o => o.Version, o => o.Version + 1)
                .SetProperty(o => o.CustomerName, updated.CustomerName)
                .SetProperty(o => o.PricedAtUtc, now)
                .SetProperty(o => o.SubtotalCents, updated.SubtotalCents)
                .SetProperty(o => o.DiscountCents, updated.DiscountCents)
                .SetProperty(o => o.TaxableCents, updated.TaxableCents)
                .SetProperty(o => o.TaxCents, updated.TaxCents)
                .SetProperty(o => o.TaxJurisdictionId, updated.TaxJurisdictionId)
                .SetProperty(o => o.TaxRoundingMethod, updated.TaxRoundingMethod)
                .SetProperty(o => o.TaxRoundingScope, updated.TaxRoundingScope)
                .SetProperty(o => o.TipMode, updated.TipMode)
                .SetProperty(o => o.TipInput, updated.TipInput)
                .SetProperty(o => o.TipRatePpm, updated.TipRatePpm)
                .SetProperty(o => o.TipCents, updated.TipCents)
                .SetProperty(o => o.TotalCents, updated.TotalCents)
                .SetProperty(o => o.UpdatedAtUtc, now),
                cancellationToken);
        if (rows == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var current = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
            return (await DiagnoseEditAsync(current, version, cancellationToken))!;
        }

        // The order row itself is never tracked; its children are replaced as new rows.
        await db.OrderPromotions.Where(p => p.OrderId == id).ExecuteDeleteAsync(cancellationToken);
        await db.OrderLines.Where(l => l.OrderId == id).ExecuteDeleteAsync(cancellationToken);
        await db.OrderTaxLines.Where(t => t.OrderId == id).ExecuteDeleteAsync(cancellationToken);
        db.OrderLines.AddRange(updated.Lines);
        db.OrderPromotions.AddRange(updated.Promotions);
        db.OrderTaxLines.AddRange(updated.TaxLines);
        db.OrderStatusHistory.Add(History(id, NotStarted, NotStarted, "Updated", OrderActor.Customer, updated.Version, now));
        if (key is not null)
        {
            AddRecord(key, RequestHasher.UpdateOperation, hash, StatusCodes.Status200OK, body, etag, id, now);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OrderResult(OrderOutcome.Ok, id, body, etag);
    }

    // ---------------------------------------------------------------- cancel

    /// <summary>Cancel while NotStarted. Write-first: the conditional UPDATE is the first statement (section 2.10).</summary>
    public async Task<OrderResult> CancelAsync(Guid orderId, string? ifMatch, CancellationToken cancellationToken = default)
    {
        int? version = null;
        if (ifMatch is not null)
        {
            if (!TryParseVersion(ifMatch, out var parsed))
            {
                return BadIfMatch();
            }
            version = parsed;
        }

        var id = orderId.ToString();
        var now = clock.GetUtcNow().UtcDateTime;
        var cancelled = nameof(OrderStatus.Cancelled);

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var rows = await db.Orders
                .Where(o => o.Id == id && o.Status == NotStarted && (version == null || o.Version == version))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(o => o.Status, cancelled)
                    .SetProperty(o => o.Version, o => o.Version + 1)
                    .SetProperty(o => o.StatusChangedAtUtc, now)
                    .SetProperty(o => o.UpdatedAtUtc, now),
                    cancellationToken);
            if (rows == 1)
            {
                var newVersion = await db.Orders.Where(o => o.Id == id).Select(o => o.Version).SingleAsync(cancellationToken);
                db.OrderStatusHistory.Add(History(id, NotStarted, cancelled, "Cancelled", OrderActor.Customer, newVersion, now));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return await OkWithOrderAsync(orderId, cancellationToken);
            }
            await transaction.RollbackAsync(cancellationToken);
        }

        // 0 rows: the outcome is already decided; say which.
        var current = await FindAsync(orderId, cancellationToken);
        if (current is null)
        {
            return new OrderResult(OrderOutcome.NotFound);
        }
        if (current.Status == cancelled)
        {
            return await OkWithOrderAsync(orderId, cancellationToken);   // idempotent: no new history row
        }
        if (current.Status != NotStarted)
        {
            return new OrderResult(OrderOutcome.NotCancellable, Current: current, Zone: await ZoneAsync(current, cancellationToken),
                Detail: "The kitchen has started this order; it can no longer be cancelled.");
        }
        return new OrderResult(OrderOutcome.VersionMismatch, Current: current);
    }

    // ---------------------------------------------------------------- kitchen

    /// <summary>
    /// The restaurant side moves the order one step. <c>expectedStatus</c> makes a retry safe: a repeat finds the order
    /// already at the next status and reports alreadyApplied instead of advancing twice.
    /// </summary>
    public async Task<OrderResult> AdvanceAsync(Guid orderId, AdvanceOrderRequest request, OrderActor actor, CancellationToken cancellationToken = default)
    {
        if (request.ExpectedStatus is null)
        {
            return BadRequest("expectedStatus", "REQUIRED", "expectedStatus is required: the status the kitchen believes the order is in.");
        }
        var expected = Enum.GetValues<OrderStatus>().Cast<OrderStatus?>()
            .FirstOrDefault(s => s.ToString()!.Equals(request.ExpectedStatus, StringComparison.OrdinalIgnoreCase));
        if (expected is null)
        {
            return BadRequest("expectedStatus", "INVALID_STATUS", $"'{request.ExpectedStatus}' isn't an order status.");
        }

        var id = orderId.ToString();
        var now = clock.GetUtcNow().UtcDateTime;
        var next = OrderStateMachine.NextKitchenStatus(expected.Value);
        if (next is { } to && OrderStateMachine.CanTransition(expected.Value, to, actor))
        {
            var from = expected.Value.ToString();
            var target = to.ToString();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var rows = await db.Orders
                .Where(o => o.Id == id && o.Status == from)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(o => o.Status, target)
                    .SetProperty(o => o.Version, o => o.Version + 1)
                    .SetProperty(o => o.StatusChangedAtUtc, now)
                    .SetProperty(o => o.UpdatedAtUtc, now),
                    cancellationToken);
            if (rows == 1)
            {
                // Read the content the kitchen committed to, in the same transaction as the status change.
                var order = (await FindAsync(orderId, cancellationToken))!;
                db.OrderStatusHistory.Add(History(id, from, target, "StatusChanged", actor, order.Version, now));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new OrderResult(OrderOutcome.Ok, id, Payload: AdvanceResponse(order, from, alreadyApplied: false, await ZoneAsync(order, cancellationToken)));
            }
            await transaction.RollbackAsync(cancellationToken);
        }

        var current = await FindAsync(orderId, cancellationToken);
        if (current is null)
        {
            return new OrderResult(OrderOutcome.NotFound);
        }
        if (next is { } repeatTarget && current.Status == repeatTarget.ToString())
        {
            return new OrderResult(OrderOutcome.Ok, id,
                Payload: AdvanceResponse(current, expected.Value.ToString(), alreadyApplied: true, await ZoneAsync(current, cancellationToken)));
        }
        var detail = current.Status == nameof(OrderStatus.Cancelled)
            ? $"Order was cancelled at {UtcTimestampConverter.Write(current.StatusChangedAtUtc)}."
            : $"Order is {OrderStateMachine.DisplayName(Enum.Parse<OrderStatus>(current.Status))}, not {OrderStateMachine.DisplayName(expected.Value)}.";
        return new OrderResult(OrderOutcome.InvalidTransition, Current: current, Zone: await ZoneAsync(current, cancellationToken), Detail: detail);
    }

    // ---------------------------------------------------------------- reads

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

    /// <summary>
    /// The kitchen queue: a restaurant's orders that are not yet Delivered or Cancelled, oldest first (ties by id).
    /// Throws <see cref="RestaurantNotFoundException"/> for an unknown restaurant (or no default one).
    /// </summary>
    public async Task<KitchenQueueResponse> KitchenQueueAsync(long? restaurantId, CancellationToken cancellationToken = default)
    {
        var restaurant = restaurantId is { } requested
            ? await db.Restaurants.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requested, cancellationToken)
            : await db.Restaurants.AsNoTracking().SingleOrDefaultAsync(r => r.IsDefault, cancellationToken);
        if (restaurant is null)
        {
            throw new RestaurantNotFoundException(restaurantId is null ? "There is no default restaurant." : $"Restaurant {restaurantId} does not exist.");
        }

        var active = Enum.GetValues<OrderStatus>().Where(s => !OrderStateMachine.IsTerminal(s)).Select(s => s.ToString()).ToList();
        var orders = await db.Orders.AsNoTrackingWithIdentityResolution()
            .Where(o => o.RestaurantId == restaurant.Id && active.Contains(o.Status))
            .Include(o => o.Lines).ThenInclude(l => l.Toppings)
            .Include(o => o.Promotions)
            .Include(o => o.TaxLines)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var zone = await loader.LoadZoneAsync(restaurant.TimeZoneId, cancellationToken);
        var queue = orders
            .OrderBy(o => o.CreatedAtUtc).ThenBy(o => o.Id, StringComparer.Ordinal)
            .Select(order =>
            {
                var view = OrderView.ToResponse(order, zone);
                var next = OrderStateMachine.NextKitchenStatus(Enum.Parse<OrderStatus>(order.Status))!.Value;
                return new KitchenOrderResponse(
                    view.OrderId, view.Status, view.StatusDisplay, view.Version, view.CreatedAt, view.CreatedAtLocal, view.CustomerName,
                    view.Pricing.LineItems.Select(l => l.Description).ToList(), view.Pricing.TotalCents,
                    next.ToString(), OrderStateMachine.DisplayName(next));
            })
            .ToList();
        return new KitchenQueueResponse(restaurant.Id, queue);
    }

    public Task<TimeZonePeriods> ZoneAsync(CustomerOrderEntity order, CancellationToken cancellationToken = default) =>
        loader.LoadZoneAsync(order.TimeZoneId, cancellationToken);

    public static string ETag(int version) => $"\"{version}\"";

    // ---------------------------------------------------------------- shared steps

    /// <summary>
    /// Validate and price a create or update, then apply the remaining precedence steps: 422 validation, 422
    /// promotion-not-applied, 409 price-changed. Returns a refusal, or the outcome to write.
    /// </summary>
    private async Task<(OrderResult? Refusal, PricingOutcome? Outcome, string? CustomerName)> PriceForWriteAsync(
        QuoteRequest draft, long? expectedTotalCents, string? enteredName, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (expectedTotalCents is null)
        {
            errors.Add(new FieldError("expectedTotalCents", "REQUIRED", "The total the customer confirmed is required."));
        }
        var customerName = enteredName?.Trim();
        if (enteredName is not null && (customerName!.Length is 0 or > MaxCustomerNameLength || customerName.Any(char.IsControl)))
        {
            errors.Add(new FieldError("customerName", "CUSTOMER_NAME_INVALID", $"The name must be 1 to {MaxCustomerNameLength} characters."));
        }

        var outcome = await pricer.PriceAsync(draft, cancellationToken);
        errors.AddRange(outcome.Errors);
        if (outcome.Breakdown is { Lines.Count: 0 })
        {
            errors.Add(new FieldError("lineItems", "ORDER_EMPTY", "Add at least one pizza to your order."));
        }
        if (errors.Count > 0)
        {
            return (new OrderResult(OrderOutcome.Invalid, Errors: errors), null, null);
        }

        var breakdown = outcome.Breakdown!;
        var promotionErrors = breakdown.Promotions.Outcomes
            .Select((o, i) => (o, i))
            .Where(x => x.o.Status is not (PromoStatus.Applied or PromoStatus.Duplicate))
            .Select(x => new FieldError($"promoCodes[{x.i}]", x.o.Status.ToString(), x.o.Message?.Render(MoneyFormatter.Format) ?? x.o.Status.ToString()))
            .ToList();
        if (promotionErrors.Count > 0)
        {
            return (new OrderResult(OrderOutcome.PromotionNotApplied, Errors: promotionErrors, CurrentQuote: ResponseMapper.ToQuote(outcome)), null, null);
        }
        if (breakdown.Total.Cents != expectedTotalCents)
        {
            return (new OrderResult(OrderOutcome.PriceChanged, CurrentQuote: ResponseMapper.ToQuote(outcome)), null, null);
        }
        return (null, outcome, customerName);
    }

    /// <summary>Why an edit can't proceed (404, 409 not-modifiable, 412), or null if it can.</summary>
    private async Task<OrderResult?> DiagnoseEditAsync(CustomerOrderEntity? order, int version, CancellationToken cancellationToken)
    {
        if (order is null)
        {
            return new OrderResult(OrderOutcome.NotFound);
        }
        if (order.Status != NotStarted)
        {
            return new OrderResult(OrderOutcome.NotModifiable, Current: order, Zone: await ZoneAsync(order, cancellationToken),
                Detail: "The kitchen has started this order; it can no longer be changed.");
        }
        return order.Version != version ? new OrderResult(OrderOutcome.VersionMismatch, Current: order) : null;
    }

    private async Task<OrderResult> OkWithOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = (await FindAsync(orderId, cancellationToken))!;
        return new OrderResult(OrderOutcome.Ok, order.Id, ETag: ETag(order.Version),
            Payload: OrderView.ToResponse(order, await ZoneAsync(order, cancellationToken)));
    }

    private static AdvanceOrderResponse AdvanceResponse(CustomerOrderEntity order, string previousStatus, bool alreadyApplied, TimeZonePeriods zone)
    {
        var view = OrderView.ToResponse(order, zone);
        return new AdvanceOrderResponse(order.Id, previousStatus, order.Status, order.Version, alreadyApplied, view.Pricing.LineItems, view.Description);
    }

    /// <summary>A live record for (key, operation), or null. An expired record is ignored.</summary>
    private async Task<IdempotencyRecordEntity?> LiveRecordAsync(string key, string operation, DateTime now, CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == key && r.Operation == operation, cancellationToken);
        return record is not null && record.ExpiresAtUtc > now ? record : null;
    }

    /// <summary>
    /// Inside the writer lock: returns a live record that a concurrent request stored first, or frees the key by
    /// deleting an expired record (otherwise the primary key would block a legitimate reuse).
    /// </summary>
    private async Task<IdempotencyRecordEntity?> ClaimKeyAsync(string key, string operation, DateTime now, CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.SingleOrDefaultAsync(r => r.IdempotencyKey == key && r.Operation == operation, cancellationToken);
        if (record is not null && record.ExpiresAtUtc > now)
        {
            return record;
        }
        if (record is not null)
        {
            db.IdempotencyRecords.Remove(record);
        }
        return null;
    }

    private void AddRecord(string key, string operation, string hash, int status, string body, string etag, string orderId, DateTime now) =>
        db.IdempotencyRecords.Add(new IdempotencyRecordEntity
        {
            IdempotencyKey = key,
            Operation = operation,
            RequestHash = hash,
            ResponseStatus = status,
            ResponseBody = body,
            ResponseEtag = etag,
            OrderId = orderId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + IdempotencyTtl,
        });

    private static OrderStatusHistoryEntity History(string orderId, string? from, string to, string eventType, OrderActor actor, int version, DateTime now) => new()
    {
        OrderId = orderId,
        FromStatus = from,
        ToStatus = to,
        EventType = eventType,
        Actor = actor.ToString(),
        OrderVersion = version,
        OccurredAtUtc = now,
    };

    private static OrderResult Replay(IdempotencyRecordEntity record, string hash) =>
        record.RequestHash == hash
            ? new OrderResult(OrderOutcome.Replayed, record.OrderId, record.ResponseBody, record.ResponseEtag)
            : new OrderResult(OrderOutcome.KeyReused);

    private static OrderResult BadRequest(string path, string code, string message) =>
        new(OrderOutcome.BadRequest, Errors: [new FieldError(path, code, message)]);

    private static OrderResult BadIfMatch() =>
        BadRequest("If-Match", "INVALID_IF_MATCH", "If-Match must be a quoted version number, for example \"1\".");

    /// <summary>If-Match is a quoted integer: "3".</summary>
    private static bool TryParseVersion(string ifMatch, out int version)
    {
        var match = QuotedVersion().Match(ifMatch.Trim());
        version = 0;
        return match.Success && int.TryParse(match.Groups[1].Value, out version);
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex ValidKey();

    [GeneratedRegex("^\"([0-9]{1,9})\"$")]
    private static partial Regex QuotedVersion();
}
