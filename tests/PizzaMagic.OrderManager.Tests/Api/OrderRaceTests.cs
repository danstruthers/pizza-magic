using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Ordering;
using PizzaMagic.OrderManager.Services;
using static PizzaMagic.OrderManager.Tests.Api.OrderApiTests;
using static PizzaMagic.OrderManager.Tests.Api.OrderLifecycleTests;

namespace PizzaMagic.OrderManager.Tests.Api;

/// <summary>
/// Races on a real temp-file database (plan section 7.4): two requests released together by a Barrier, 50 times.
/// The conditional UPDATE decides; exactly one of {cancel, kitchen start} commits and the loser gets a definitive 409.
/// </summary>
public class OrderRaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int Iterations = 50;

    private static readonly SubmitOrderRequest SmallPizza = new(null, null, [Pizza("SMALL", 1)], null, null, 533, null);

    private OrderService NewService(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<OrderService>();

    private async Task<(string Status, long TransitionsOutOfNotStarted, int LineQuantity)> StateAsync(string orderId)
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT status,
                   (SELECT COUNT(*) FROM order_status_history WHERE order_id = o.id AND from_status = 'NotStarted' AND to_status <> 'NotStarted'),
                   (SELECT quantity FROM order_line WHERE order_id = o.id)
            FROM customer_order o WHERE id = '{orderId}'
            """;
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetInt64(1), reader.GetInt32(2));
    }

    private static async Task<(T1, T2)> RaceAsync<T1, T2>(Func<Task<T1>> first, Func<Task<T2>> second)
    {
        using var barrier = new Barrier(2);
        var a = Task.Run(async () => { barrier.SignalAndWait(); return await first(); });
        var b = Task.Run(async () => { barrier.SignalAndWait(); return await second(); });
        return (await a, await b);
    }

    [Fact]
    public async Task Cancel_and_kitchen_start_racing_exactly_one_wins()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < Iterations; i++)
        {
            var order = await CreateAsync(client, SmallPizza);
            var id = Guid.Parse(order.OrderId);
            using var cancelScope = factory.Services.CreateScope();
            using var kitchenScope = factory.Services.CreateScope();

            var (cancel, start) = await RaceAsync(
                () => NewService(cancelScope).CancelAsync(id, null),
                () => NewService(kitchenScope).AdvanceAsync(id, new AdvanceOrderRequest("NotStarted"), OrderActor.Kitchen));

            var cancelWon = cancel.Outcome == OrderOutcome.Ok;
            var startWon = start.Outcome == OrderOutcome.Ok;
            (cancelWon ^ startWon).ShouldBeTrue($"iteration {i}: cancel {cancel.Outcome}, start {start.Outcome}");
            (cancelWon ? start.Outcome : cancel.Outcome).ShouldBe(cancelWon ? OrderOutcome.InvalidTransition : OrderOutcome.NotCancellable);
            var state = await StateAsync(order.OrderId);
            state.Status.ShouldBe(cancelWon ? "Cancelled" : "Started");
            state.TransitionsOutOfNotStarted.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Cancel_and_kitchen_start_racing_over_HTTP_exactly_one_wins()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 20; i++)
        {
            var order = await CreateAsync(client, SmallPizza);

            var (cancel, start) = await RaceAsync(
                () => CancelAsync(client, order.OrderId),
                () => AdvanceAsync(client, order.OrderId, "NotStarted"));

            var statuses = new[] { cancel.StatusCode, start.StatusCode };
            statuses.Count(s => s == HttpStatusCode.OK).ShouldBe(1, $"iteration {i}: {cancel.StatusCode} / {start.StatusCode}");
            statuses.Count(s => s == HttpStatusCode.Conflict).ShouldBe(1);
            (await StateAsync(order.OrderId)).Status.ShouldBe(cancel.StatusCode == HttpStatusCode.OK ? "Cancelled" : "Started");
        }
    }

    // Either the edit committed before the kitchen started (the kitchen sees the edited content), or the edit
    // was refused with 409 and the content is unchanged. Never an edit on a started order.
    [Fact]
    public async Task Edit_and_kitchen_start_racing_never_edit_a_started_order()
    {
        var client = factory.CreateClient();
        var edited = new UpdateOrderRequest([Pizza("SMALL", 2)], null, null, 1065, null);
        for (var i = 0; i < Iterations; i++)
        {
            var order = await CreateAsync(client, SmallPizza);
            var id = Guid.Parse(order.OrderId);
            using var editScope = factory.Services.CreateScope();
            using var kitchenScope = factory.Services.CreateScope();

            var (edit, start) = await RaceAsync(
                () => NewService(editScope).UpdateAsync(id, edited, "\"1\"", null),
                () => NewService(kitchenScope).AdvanceAsync(id, new AdvanceOrderRequest("NotStarted"), OrderActor.Kitchen));

            start.Outcome.ShouldBe(OrderOutcome.Ok, $"iteration {i}");
            var startedWith = ((AdvanceOrderResponse)start.Payload!).LineItems.Single().Quantity;
            var state = await StateAsync(order.OrderId);
            if (edit.Outcome == OrderOutcome.Ok)
            {
                startedWith.ShouldBe(2, $"iteration {i}: the kitchen must see the edit that committed first");
                state.LineQuantity.ShouldBe(2);
            }
            else
            {
                edit.Outcome.ShouldBe(OrderOutcome.NotModifiable, $"iteration {i}");
                startedWith.ShouldBe(1);
                state.LineQuantity.ShouldBe(1);
            }
            state.Status.ShouldBe("Started");
        }
    }

    // Code review: the races below were untested. Each runs 50 times on a real database.

    private async Task<long> UpdatesAsync(string orderId)
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM order_status_history WHERE order_id = '{orderId}' AND event_type = 'Updated'";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    // Two edits made from the same version (two tabs): exactly one is applied; the other is told the order changed.
    [Fact]
    public async Task Two_updates_from_the_same_version_exactly_one_wins()
    {
        var client = factory.CreateClient();
        var two = new UpdateOrderRequest([Pizza("SMALL", 2)], null, null, 1065, null);
        var three = new UpdateOrderRequest([Pizza("SMALL", 3)], null, null, 1598, null);
        for (var i = 0; i < Iterations; i++)
        {
            var order = await CreateAsync(client, SmallPizza);
            var id = Guid.Parse(order.OrderId);
            using var firstScope = factory.Services.CreateScope();
            using var secondScope = factory.Services.CreateScope();

            var (first, second) = await RaceAsync(
                () => NewService(firstScope).UpdateAsync(id, two, "\"1\"", null),
                () => NewService(secondScope).UpdateAsync(id, three, "\"1\"", null));

            new[] { first.Outcome, second.Outcome }.Order().ToArray().ShouldBe([OrderOutcome.Ok, OrderOutcome.VersionMismatch], $"iteration {i}");
            (await StateAsync(order.OrderId)).LineQuantity.ShouldBe(first.Outcome == OrderOutcome.Ok ? 2 : 3);
            (await UpdatesAsync(order.OrderId)).ShouldBe(1);
        }
    }

    // A cancel and an edit at the same moment: either the edit lands first and the cancel then cancels the edited
    // order, or the cancel lands first and the edit is refused. The cancel always wins in the end; nothing is lost.
    [Fact]
    public async Task Cancel_and_update_racing_end_cancelled_and_consistent()
    {
        var client = factory.CreateClient();
        var two = new UpdateOrderRequest([Pizza("SMALL", 2)], null, null, 1065, null);
        for (var i = 0; i < Iterations; i++)
        {
            var order = await CreateAsync(client, SmallPizza);
            var id = Guid.Parse(order.OrderId);
            using var cancelScope = factory.Services.CreateScope();
            using var editScope = factory.Services.CreateScope();

            var (cancel, edit) = await RaceAsync(
                () => NewService(cancelScope).CancelAsync(id, null),
                () => NewService(editScope).UpdateAsync(id, two, "\"1\"", null));

            cancel.Outcome.ShouldBe(OrderOutcome.Ok, $"iteration {i}");
            var state = await StateAsync(order.OrderId);
            state.Status.ShouldBe("Cancelled");
            if (edit.Outcome == OrderOutcome.Ok)
            {
                state.LineQuantity.ShouldBe(2, $"iteration {i}: the cancelled order is the edited one");
            }
            else
            {
                edit.Outcome.ShouldBe(OrderOutcome.NotModifiable, $"iteration {i}");
                state.LineQuantity.ShouldBe(1);
            }
        }
    }

    // A retried edit (same key, same body) arriving many times at once is applied once, and every copy gets the
    // answer: never a 412 for the copies that lost the race to their own first attempt.
    [Fact]
    public async Task Parallel_retries_of_one_update_apply_once()
    {
        var client = factory.CreateClient();
        var two = new UpdateOrderRequest([Pizza("SMALL", 2)], null, null, 1065, null);
        for (var i = 0; i < 20; i++)
        {
            var order = await CreateAsync(client, SmallPizza);
            var id = Guid.Parse(order.OrderId);
            var key = Guid.NewGuid().ToString();
            using var barrier = new Barrier(10);
            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                using var scope = factory.Services.CreateScope();
                barrier.SignalAndWait();
                return await NewService(scope).UpdateAsync(id, two, "\"1\"", key);
            })));

            var outcomes = results.Select(r => r.Outcome).ToList();
            outcomes.Count(o => o == OrderOutcome.Ok).ShouldBe(1, $"iteration {i}: {string.Join(", ", outcomes)}");
            outcomes.Count(o => o == OrderOutcome.Replayed).ShouldBe(9, $"iteration {i}: {string.Join(", ", outcomes)}");
            (await UpdatesAsync(order.OrderId)).ShouldBe(1);
        }
    }

    /// <summary>
    /// The naive variant (test project only): read the status, check it, then write unconditionally. With both racers
    /// held after their read, both "succeed": the bug the conditional UPDATE prevents. (That the race tests above
    /// catch it was checked by removing the status condition from the real cancel; see docs/ai-log.md, M11.)
    /// </summary>
    [Fact]
    public async Task Naive_check_then_act_double_succeeds()
    {
        var client = factory.CreateClient();
        var order = await CreateAsync(client, SmallPizza);
        using var afterRead = new Barrier(2);

        async Task<bool> NaiveTransitionAsync(string to)
        {
            await using var connection = new SqliteConnection(factory.ConnectionString);
            await connection.OpenAsync();
            await using var read = connection.CreateCommand();
            read.CommandText = $"SELECT status FROM customer_order WHERE id = '{order.OrderId}'";
            var status = (string)(await read.ExecuteScalarAsync())!;
            afterRead.SignalAndWait();   // injected hook: both racers have read NotStarted
            if (status != "NotStarted")
            {
                return false;
            }
            await using var write = connection.CreateCommand();
            write.CommandText = $"UPDATE customer_order SET status = '{to}', version = version + 1 WHERE id = '{order.OrderId}'";
            await write.ExecuteNonQueryAsync();
            return true;
        }

        var results = await Task.WhenAll(
            Task.Run(() => NaiveTransitionAsync("Cancelled")),
            Task.Run(() => NaiveTransitionAsync("Started")));

        results.ShouldBe([true, true]);   // double success: exactly what the real implementation never does
    }
}
