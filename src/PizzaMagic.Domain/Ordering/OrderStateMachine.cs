namespace PizzaMagic.Domain.Ordering;

/// <summary>Order statuses (spec: Not Started, Started, Ready for Pickup, Delivered; plus Cancelled, A26).</summary>
public enum OrderStatus
{
    NotStarted,
    Started,
    ReadyForPickup,
    Delivered,
    Cancelled,
}

/// <summary>Who asks for a status change.</summary>
public enum OrderActor
{
    Customer,
    Kitchen,
    Simulator,
}

/// <summary>
/// The order lifecycle (plan section 2.10). Pure and table-driven: the kitchen (or the simulator) moves an order
/// forward one step at a time, and only the customer cancels, only while the order is NotStarted. There is no
/// other transition; content edits are not transitions and are allowed only while NotStarted.
/// </summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus> KitchenSteps = new()
    {
        [OrderStatus.NotStarted] = OrderStatus.Started,
        [OrderStatus.Started] = OrderStatus.ReadyForPickup,
        [OrderStatus.ReadyForPickup] = OrderStatus.Delivered,
    };

    private static readonly Dictionary<OrderStatus, string> DisplayNames = new()
    {
        [OrderStatus.NotStarted] = "Not Started",
        [OrderStatus.Started] = "Started",
        [OrderStatus.ReadyForPickup] = "Ready for Pickup",
        [OrderStatus.Delivered] = "Delivered",
        [OrderStatus.Cancelled] = "Cancelled",
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to, OrderActor actor) => actor switch
    {
        OrderActor.Kitchen or OrderActor.Simulator => NextKitchenStatus(from) == to,
        OrderActor.Customer => from == OrderStatus.NotStarted && to == OrderStatus.Cancelled,
        _ => false,
    };

    /// <summary>The kitchen's next status, or null when the order cannot move forward (Delivered, Cancelled).</summary>
    public static OrderStatus? NextKitchenStatus(OrderStatus from) =>
        KitchenSteps.TryGetValue(from, out var next) ? next : null;

    /// <summary>The customer may update or cancel only while the kitchen has not started the order.</summary>
    public static bool IsCustomerModifiable(OrderStatus status) => status == OrderStatus.NotStarted;

    public static bool IsTerminal(OrderStatus status) => status is OrderStatus.Delivered or OrderStatus.Cancelled;

    public static string DisplayName(OrderStatus status) => DisplayNames[status];
}
