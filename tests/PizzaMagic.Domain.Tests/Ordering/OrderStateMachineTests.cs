using PizzaMagic.Domain.Ordering;
using static PizzaMagic.Domain.Ordering.OrderStatus;

namespace PizzaMagic.Domain.Tests.Ordering;

public class OrderStateMachineTests
{
    /// <summary>Plan section 2.10: exactly three forward kitchen transitions plus the customer's cancel from NotStarted.</summary>
    private static readonly HashSet<(OrderStatus From, OrderStatus To, OrderActor Actor)> Allowed =
    [
        (NotStarted, Started, OrderActor.Kitchen),
        (Started, ReadyForPickup, OrderActor.Kitchen),
        (ReadyForPickup, Delivered, OrderActor.Kitchen),
        (NotStarted, Started, OrderActor.Simulator),
        (Started, ReadyForPickup, OrderActor.Simulator),
        (ReadyForPickup, Delivered, OrderActor.Simulator),
        (NotStarted, Cancelled, OrderActor.Customer),
    ];

    public static IEnumerable<object[]> AllPairsAndActors() =>
        from source in Enum.GetValues<OrderStatus>()
        from target in Enum.GetValues<OrderStatus>()
        from actor in Enum.GetValues<OrderActor>()
        select new object[] { source, target, actor };

    // All 25 (from, to) pairs for every actor.
    [Theory]
    [MemberData(nameof(AllPairsAndActors))]
    public void Only_documented_transitions_allowed(OrderStatus from, OrderStatus to, OrderActor actor)
    {
        OrderStateMachine.CanTransition(from, to, actor).ShouldBe(Allowed.Contains((from, to, actor)));
    }

    [Fact]
    public void There_are_exactly_five_statuses_and_three_actors()
    {
        Enum.GetValues<OrderStatus>().Length.ShouldBe(5);
        Enum.GetValues<OrderActor>().Length.ShouldBe(3);
    }

    [Theory]
    [InlineData(Delivered)]
    [InlineData(Cancelled)]
    public void Terminal_states_have_no_exits(OrderStatus terminal)
    {
        OrderStateMachine.IsTerminal(terminal).ShouldBeTrue();
        foreach (var to in Enum.GetValues<OrderStatus>())
        {
            foreach (var actor in Enum.GetValues<OrderActor>())
            {
                OrderStateMachine.CanTransition(terminal, to, actor).ShouldBeFalse();
            }
        }
        OrderStateMachine.NextKitchenStatus(terminal).ShouldBeNull();
    }

    [Theory]
    [InlineData(NotStarted, false)]
    [InlineData(Started, false)]
    [InlineData(ReadyForPickup, false)]
    public void Non_terminal_states_are_not_terminal(OrderStatus status, bool expected)
    {
        OrderStateMachine.IsTerminal(status).ShouldBe(expected);
    }

    [Fact]
    public void Next_kitchen_status_sequence()
    {
        OrderStateMachine.NextKitchenStatus(NotStarted).ShouldBe(Started);
        OrderStateMachine.NextKitchenStatus(Started).ShouldBe(ReadyForPickup);
        OrderStateMachine.NextKitchenStatus(ReadyForPickup).ShouldBe(Delivered);
        OrderStateMachine.NextKitchenStatus(Delivered).ShouldBeNull();
        OrderStateMachine.NextKitchenStatus(Cancelled).ShouldBeNull();
    }

    [Fact]
    public void Customer_modifiable_only_NotStarted()
    {
        Enum.GetValues<OrderStatus>().Where(OrderStateMachine.IsCustomerModifiable).ShouldBe([NotStarted]);
    }

    [Fact]
    public void Statuses_have_customer_facing_names()
    {
        Enum.GetValues<OrderStatus>().Select(OrderStateMachine.DisplayName)
            .ShouldBe(["Not Started", "Started", "Ready for Pickup", "Delivered", "Cancelled"]);
    }
}
