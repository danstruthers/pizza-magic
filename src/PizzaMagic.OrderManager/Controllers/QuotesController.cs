using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.OrderManager.Api;
using PizzaMagic.OrderManager.Services;

namespace PizzaMagic.OrderManager.Controllers;

/// <summary>
/// POST /api/v1/quotes: prices a draft statelessly. Promo problems never fail a quote (each code gets an outcome);
/// only field errors are a 422.
/// </summary>
[ApiController]
[Route("api/v1/quotes")]
public sealed class QuotesController(OrderPricer pricer) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<QuoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Post([FromBody] QuoteRequest request, CancellationToken cancellationToken)
    {
        var outcome = await pricer.PriceAsync(request, cancellationToken);
        return outcome.Errors.Count > 0
            ? Problems.Validation(outcome.Errors)
            : Ok(ResponseMapper.ToQuote(outcome));
    }
}
