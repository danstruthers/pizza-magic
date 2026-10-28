using Microsoft.AspNetCore.Mvc;

namespace PizzaMagic.Web.Controllers;

/// <summary>Review and Place (plan sections 6.1, 6.2 and 6.4). M14 red stub.</summary>
public sealed class CheckoutController : Controller
{
    [HttpGet("/Checkout")]
    public Task<IActionResult> Review() => throw new NotImplementedException();

    [HttpPost("/Checkout/Place")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Place([FromForm] string? ticket) => throw new NotImplementedException();
}
