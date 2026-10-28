using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Pricing;

namespace PizzaMagic.OrderManager.Api;

/// <summary>RFC 9457 problem responses with the stable types from <see cref="ProblemTypes"/>.</summary>
public static class Problems
{
    public const string ProblemJson = "application/problem+json";

    /// <summary>Field errors keyed by JSON path: 422 (business rules) or 400 (the request can't be understood).</summary>
    public static ObjectResult Validation(IEnumerable<FieldError> errors, int status = StatusCodes.Status422UnprocessableEntity)
    {
        var problem = new ProblemDetails
        {
            Type = ProblemTypes.Validation,
            Title = "The order has invalid fields.",
            Status = status,
        };
        problem.Extensions["errors"] = errors
            .GroupBy(e => e.Path)
            .ToDictionary(g => g.Key, g => g.Select(e => new FieldErrorResponse(e.Code, e.Message)).ToList());
        return Result(problem);
    }

    /// <summary>Re-types a problem and attaches the fresh quote, so the client can show the new price or promo state.</summary>
    public static ObjectResult WithQuote(ObjectResult result, string type, string title, QuoteResponse currentQuote)
    {
        var problem = (ProblemDetails)result.Value!;
        problem.Type = type;
        problem.Title = title;
        problem.Extensions["currentQuote"] = currentQuote;
        return result;
    }

    public static ObjectResult Of(string type, int status, string title, string? detail = null) =>
        Result(new ProblemDetails { Type = type, Title = title, Status = status, Detail = detail });

    private static ObjectResult Result(ProblemDetails problem) =>
        new(problem) { StatusCode = problem.Status, ContentTypes = { ProblemJson } };
}
