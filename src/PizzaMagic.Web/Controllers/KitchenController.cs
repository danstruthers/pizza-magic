using Microsoft.AspNetCore.Mvc;

namespace PizzaMagic.Web.Controllers;

/// <summary>The simulated restaurant side: active orders and a button to move each one on. Red stub.</summary>
public sealed class KitchenController : Controller
{
    [HttpGet("/Kitchen")]
    public Task<IActionResult> Index() => throw new NotImplementedException();

    [HttpPost("/Kitchen/{id:guid}/Advance")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Advance(Guid id, [FromForm] string? expectedStatus) => throw new NotImplementedException();
}
