namespace PizzaMagic.Contracts.Api;

/// <summary>
/// Stable RFC 9457 problem "type" URIs (plan section 5.1). The order builder switches on these values, never on
/// titles or messages.
/// </summary>
public static class ProblemTypes
{
    public const string Base = "https://pizzamagic.local/problems/";
    public const string Validation = Base + "validation";
    public const string PromotionNotApplied = Base + "promotion-not-applied";
    public const string IdempotencyKeyMissing = Base + "idempotency-key-missing";
    public const string IdempotencyKeyInvalid = Base + "idempotency-key-invalid";
    public const string IdempotencyKeyReused = Base + "idempotency-key-reused";
    public const string PriceChanged = Base + "price-changed";
    public const string OrderNotFound = Base + "order-not-found";
    public const string OrderNotModifiable = Base + "order-not-modifiable";
    public const string OrderNotCancellable = Base + "order-not-cancellable";
    public const string VersionMismatch = Base + "version-mismatch";
    public const string PreconditionRequired = Base + "precondition-required";
    public const string InvalidTransition = Base + "invalid-transition";
    public const string RestaurantNotFound = Base + "restaurant-not-found";
}

/// <summary>One field error in a validation problem's "errors" map, keyed by JSON path.</summary>
public sealed record FieldErrorResponse(string Code, string Message);
