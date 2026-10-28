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

/// <summary>The order lifecycle (plan section 2.10). Pure and table-driven.</summary>
public static class OrderStateMachine
{
    public static bool CanTransition(OrderStatus from, OrderStatus to, OrderActor actor) => throw new NotImplementedException();

    public static OrderStatus? NextKitchenStatus(OrderStatus from) => throw new NotImplementedException();

    public static bool IsCustomerModifiable(OrderStatus status) => throw new NotImplementedException();

    public static bool IsTerminal(OrderStatus status) => throw new NotImplementedException();

    public static string DisplayName(OrderStatus status) => throw new NotImplementedException();
}
