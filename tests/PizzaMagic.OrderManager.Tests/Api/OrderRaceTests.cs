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

    /// <summary>
    /// The naive variant (test project only): read the status, check it, then write unconditionally. With both racers
    /// held after their read, both "succeed", which is the bug the conditional UPDATE prevents. This proves the race
    /// tests above are able to detect it.
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
