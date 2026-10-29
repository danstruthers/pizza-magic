using System.Text.Json;
using PizzaMagic.Contracts.Api;

namespace PizzaMagic.Web.Models.Session;

/// <summary>
/// The customer's cart, kept in the ASP.NET Core Session (plan section 6.2). The page increments
/// <see cref="Revision"/> on every edit, so an older draft never overwrites a newer one (best effort: Place submits a
/// review ticket, never this cart). <see cref="PendingTicket"/> locks the cart while an order waits for confirmation.
/// An order being changed is priced at its own restaurant and channel; <see cref="LastPlacedOrderId"/> keeps the last
/// order findable even if the response that placed it never reached the browser.
/// </summary>
public sealed record CartState(
    long Revision,
    IReadOnlyList<LineItemRequest> LineItems,
    IReadOnlyList<string> PromoCodes,
    TipRequest? Tip,
    string SubmissionKey,
    long KeyRevision,
    string? PendingTicket = null,
    Guid? EditingOrderId = null,
    int? EditingVersion = null,
    long? EditingRestaurantId = null,
    string? EditingChannel = null,
    Guid? LastPlacedOrderId = null)
{
    public static CartState Empty(Guid? lastPlacedOrderId = null) =>
        new(0, [], [], null, Guid.NewGuid().ToString(), 0, LastPlacedOrderId: lastPlacedOrderId);

    /// <summary>The restaurant to price at: the changed order's own, else the one this web app sells for.</summary>
    public long? RestaurantId(IConfiguration configuration) => EditingRestaurantId ?? Clients.ConfiguredRestaurant.Id(configuration);
}

/// <summary>Reads and writes the cart as JSON in the Session.</summary>
public static class CartStore
{
    private const string Key = "cart";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static CartState Load(ISession session) =>
        session.GetString(Key) is { } json ? JsonSerializer.Deserialize<CartState>(json, Json) ?? CartState.Empty() : CartState.Empty();

    public static void Save(ISession session, CartState cart) => session.SetString(Key, JsonSerializer.Serialize(cart, Json));

    public static string ToJson(CartState cart) => JsonSerializer.Serialize(cart, Json);
}

/// <summary>What the builder page posts to the quote proxy on every edit.</summary>
public sealed record BuilderDraft(
    long Revision,
    IReadOnlyList<LineItemRequest>? LineItems,
    IReadOnlyList<string>? PromoCodes,
    TipRequest? Tip);
