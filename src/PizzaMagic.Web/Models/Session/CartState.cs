using System.Text.Json;
using PizzaMagic.Contracts.Api;

namespace PizzaMagic.Web.Models.Session;

/// <summary>
/// The customer's cart, kept in the ASP.NET Core Session (plan section 6.2). <see cref="Revision"/> is incremented by
/// the page on every edit, so an older draft never overwrites a newer one (best effort; Place uses a review ticket,
/// never this cart, M14). The submission key and edit fields are used by checkout (M14).
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
    int? EditingVersion = null)
{
    public static CartState Empty() => new(0, [], [], null, Guid.NewGuid().ToString(), 0);
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
