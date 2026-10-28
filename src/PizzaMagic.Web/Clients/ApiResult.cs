using System.Text.Json;
using PizzaMagic.Contracts.Api;

namespace PizzaMagic.Web.Clients;

public enum ApiResultKind
{
    Success,
    Problem,
    Unavailable,
}

/// <summary>
/// The outcome of a call to the order service. Expected failures never throw: a business refusal is a
/// <see cref="ApiResultKind.Problem"/> (RFC 9457), and anything the network or server couldn't answer after the retry
/// budget is <see cref="ApiResultKind.Unavailable"/>.
/// </summary>
public sealed record ApiResult<T>(
    ApiResultKind Kind,
    T? Value = default,
    string? ETag = null,
    bool Replayed = false,
    ApiProblem? Problem = null,
    string? UnavailableReason = null)
{
    public bool IsSuccess => Kind == ApiResultKind.Success;

    public static ApiResult<T> Success(T value, string? etag, bool replayed) => new(ApiResultKind.Success, value, etag, replayed);

    public static ApiResult<T> Failed(ApiProblem problem) => new(ApiResultKind.Problem, Problem: problem);

    public static ApiResult<T> Unavailable(string reason) => new(ApiResultKind.Unavailable, UnavailableReason: reason);
}

/// <summary>
/// A problem response: its stable <see cref="Type"/> (switch on this, never on titles), field errors by JSON path,
/// and the raw body for extensions such as currentQuote, currentStatus or currentVersion.
/// </summary>
public sealed record ApiProblem(
    string Type,
    int Status,
    string? Title,
    string? Detail,
    IReadOnlyDictionary<string, IReadOnlyList<FieldErrorResponse>> Errors,
    JsonElement Body)
{
    public JsonElement? Extension(string name) =>
        Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
}
