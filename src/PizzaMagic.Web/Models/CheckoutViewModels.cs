using System.Globalization;
using PizzaMagic.Contracts;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;

namespace PizzaMagic.Web.Models;

/// <summary>
/// The Review page in its three forms: a priced quote (with a sealed ticket when it can be placed), a refusal or
/// outage message, or an order waiting for confirmation (<see cref="PendingTotalCents"/> set; Try again re-posts the
/// same ticket).
/// </summary>
public sealed record CheckoutViewModel(
    QuoteResponse? Quote,
    string? Ticket,
    bool IsUpdate,
    string? Banner,
    IReadOnlyList<string> Messages,
    long? PendingTotalCents = null)
{
    public bool IsPending => PendingTotalCents is not null;

    public static CheckoutViewModel Message(string message) => new(null, null, false, null, [message]);
}

/// <summary>The order details page: the stored order and a one-time notice (cancelled, kitchen started, …).</summary>
public sealed record OrderDetailsViewModel(OrderResponse Order, string? Notice)
{
    public bool CanUpdate => Order.AllowedActions.Contains("update");

    public bool CanCancel => Order.AllowedActions.Contains("cancel");
}

/// <summary>Recent orders from the cookie (newest first), each with its stored order, plus the lookup form's message.</summary>
public sealed record RecentOrdersViewModel(IReadOnlyList<RecentOrderRow> Orders, string? LookupMessage);

/// <summary>One recent order: the order as the service returned it, or why it couldn't be read (<see cref="Problem"/>).</summary>
public sealed record RecentOrderRow(Guid OrderId, OrderResponse? Order, string? Problem);

/// <summary>The kitchen page: the active orders (null when the service is unavailable), a one-time notice, and the outage message.</summary>
public sealed record KitchenViewModel(IReadOnlyList<KitchenOrderResponse>? Orders, string? Notice, string? Unavailable)
{
    /// <summary>The button for an order's next step.</summary>
    public static string ActionLabel(string nextStatus) => nextStatus switch
    {
        "Started" => "Start",
        "ReadyForPickup" => "Mark ready for pickup",
        "Delivered" => "Mark delivered",
        _ => $"Move to {Display.StatusName(nextStatus)}",
    };
}

/// <summary>A page that only says something (order not found, service unavailable).</summary>
public sealed record MessageViewModel(string Title, string Text);

public static class Display
{
    public static string Money(long cents) => MoneyFormatter.Format(cents);

    /// <summary>"$3.00 (10.96%)" for a dollar tip (UQ22); the amount alone otherwise.</summary>
    public static string Tip(TipResponse tip) =>
        tip.EffectiveRateBasisPoints is { } basisPoints
            ? $"{Money(tip.TipCents)} ({PercentFormatter.FormatBasisPoints(basisPoints)})"
            : Money(tip.TipCents);

    /// <summary>"Oct 28, 2026, 12:00 PM PDT" in the restaurant's zone, or in UTC when the service had no local time.</summary>
    public static string DateAndTime(LocalTimeResponse? local, string utc) =>
        local is not null && DateTime.TryParse(local.LocalTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock)
            ? $"{wallClock.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture)} {local.Abbreviation}"
            : DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var instant)
                ? $"{instant.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture)} UTC"
                : utc;

    /// <summary>A status code as people read it: "NotStarted" → "Not Started", "ReadyForPickup" → "Ready for Pickup".</summary>
    public static string StatusName(string status) => status switch
    {
        "NotStarted" => "Not Started",
        "ReadyForPickup" => "Ready for Pickup",
        _ => status,
    };

    /// <summary>A status badge colour for the order lists: grey, amber, blue, green, or dark when cancelled.</summary>
    public static string StatusBadge(string status) => status switch
    {
        "Started" => "text-bg-warning",
        "ReadyForPickup" => "text-bg-info",
        "Delivered" => "text-bg-success",
        "Cancelled" => "text-bg-dark",
        _ => "text-bg-secondary",
    };

    /// <summary>"12:05 PM PDT" in the restaurant's zone, or the UTC time when the service had no local time.</summary>
    public static string Time(LocalTimeResponse? local, string utc) =>
        local is not null && DateTime.TryParse(local.LocalTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock)
            ? $"{wallClock.ToString("h:mm tt", CultureInfo.InvariantCulture)} {local.Abbreviation}"
            : DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var instant)
                ? $"{instant.ToString("h:mm tt", CultureInfo.InvariantCulture)} UTC"
                : utc;

    /// <summary>Field errors as sentences: "Pizza 1: Large is not available here."</summary>
    public static IReadOnlyList<string> FieldErrors(ApiProblem problem) =>
        problem.Errors.SelectMany(e => e.Value.Select(error => $"{FieldLabel(e.Key)}{error.Message}")).ToList();

    private static string FieldLabel(string path)
    {
        if (path.StartsWith("lineItems[", StringComparison.Ordinal) && path.IndexOf(']') is var end and > 10
            && int.TryParse(path.AsSpan(10, end - 10), out var index))
        {
            return $"Pizza {index + 1}: ";
        }
        return path switch
        {
            _ when path.StartsWith("promoCodes", StringComparison.Ordinal) => "Promo code: ",
            _ when path.StartsWith("tip", StringComparison.Ordinal) => "Tip: ",
            _ => "",
        };
    }
}
