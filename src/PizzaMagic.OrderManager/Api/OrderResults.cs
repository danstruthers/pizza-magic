using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.OrderManager.Services;

namespace PizzaMagic.OrderManager.Api;

/// <summary>The one place an <see cref="OrderResult"/> becomes an HTTP response (stable problem types, section 5.1).</summary>
public static class OrderResults
{
    private const string ReplayedHeader = "Idempotent-Replayed";

    /// <param name="successStatus">201 for create, 200 otherwise.</param>
    /// <param name="location">Set on create (and its replays).</param>
    public static IActionResult ToHttp(this ControllerBase controller, OrderResult result, int successStatus, bool location = false)
    {
        var headers = controller.Response.Headers;
        switch (result.Outcome)
        {
            case OrderOutcome.Ok:
            case OrderOutcome.Replayed:
                if (location)
                {
                    headers.Location = $"/api/v1/orders/{result.OrderId}";
                }
                if (result.ETag is not null)
                {
                    headers.ETag = result.ETag;
                }
                if (result.Outcome == OrderOutcome.Replayed)
                {
                    headers[ReplayedHeader] = "true";
                }
                // A stored body is returned byte for byte, so a replay is identical to the original response.
                return result.Body is not null
                    ? new ContentResult { StatusCode = successStatus, ContentType = "application/json", Content = result.Body }
                    : new ObjectResult(result.Payload) { StatusCode = successStatus };

            case OrderOutcome.KeyMissing:
                return Problems.Of(ProblemTypes.IdempotencyKeyMissing, StatusCodes.Status400BadRequest, "An Idempotency-Key header is required.");
            case OrderOutcome.KeyInvalid:
                return Problems.Of(ProblemTypes.IdempotencyKeyInvalid, StatusCodes.Status400BadRequest,
                    "The Idempotency-Key must be 1 to 64 letters, digits or dashes.");
            case OrderOutcome.KeyReused:
                return Problems.Of(ProblemTypes.IdempotencyKeyReused, StatusCodes.Status422UnprocessableEntity,
                    "This Idempotency-Key was already used with a different request.");
            case OrderOutcome.BadRequest:
                return Problems.Validation(result.Errors!, StatusCodes.Status400BadRequest);
            case OrderOutcome.PreconditionRequired:
                return Problems.Of(ProblemTypes.PreconditionRequired, StatusCodes.Status428PreconditionRequired,
                    "An If-Match header with the order's version is required.");
            case OrderOutcome.NotFound:
                return Problems.Of(ProblemTypes.OrderNotFound, StatusCodes.Status404NotFound, "Order not found.");
            case OrderOutcome.NotModifiable:
                return WithStatus(Problems.Of(ProblemTypes.OrderNotModifiable, StatusCodes.Status409Conflict, "The order can no longer be changed.", result.Detail), result);
            case OrderOutcome.NotCancellable:
                return WithStatus(Problems.Of(ProblemTypes.OrderNotCancellable, StatusCodes.Status409Conflict, "The order can no longer be cancelled.", result.Detail), result);
            case OrderOutcome.InvalidTransition:
                return WithStatus(Problems.Of(ProblemTypes.InvalidTransition, StatusCodes.Status409Conflict, "The order can't move to that status.", result.Detail), result);
            case OrderOutcome.VersionMismatch:
                var stale = Problems.Of(ProblemTypes.VersionMismatch, StatusCodes.Status412PreconditionFailed,
                    "The order has changed since you loaded it.", "Reload the order and try again.");
                ((ProblemDetails)stale.Value!).Extensions["currentVersion"] = result.Current!.Version;
                return stale;
            case OrderOutcome.Invalid:
                return Problems.Validation(result.Errors!);
            case OrderOutcome.PromotionNotApplied:
                return Problems.WithQuote(Problems.Validation(result.Errors!), ProblemTypes.PromotionNotApplied,
                    "A promo code can't be applied to this order.", result.CurrentQuote!);
            case OrderOutcome.PriceChanged:
                return Problems.WithQuote(Problems.Of(ProblemTypes.PriceChanged, StatusCodes.Status409Conflict,
                    "The price changed since you reviewed the order."), ProblemTypes.PriceChanged,
                    "The price changed since you reviewed the order.", result.CurrentQuote!);
            default:
                throw new InvalidOperationException($"Unhandled order outcome {result.Outcome}.");
        }
    }

    /// <summary>Conflicts name the current status and when it changed (UTC, plus the local display companion).</summary>
    private static ObjectResult WithStatus(ObjectResult result, OrderResult order)
    {
        var problem = (ProblemDetails)result.Value!;
        problem.Extensions["currentStatus"] = order.Current!.Status;
        problem.Extensions["statusChangedAt"] = UtcTimestampConverter.Write(order.Current.StatusChangedAtUtc);
        problem.Extensions["statusChangedAtLocal"] = OrderView.Local(order.Zone!, order.Current.StatusChangedAtUtc);
        return result;
    }
}
