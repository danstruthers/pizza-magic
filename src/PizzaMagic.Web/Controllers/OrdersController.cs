using Microsoft.AspNetCore.Mvc;

namespace PizzaMagic.Web.Controllers;

/// <summary>Order details, status polling, edit, cancel, recent orders and lookup (plan section 6.1). M14 red stub.</summary>
public sealed class OrdersController : Controller
{
    [HttpGet("/Orders/{id:guid}")]
    public Task<IActionResult> Details(Guid id) => throw new NotImplementedException();

    [HttpGet("/Orders/{id:guid}/Status")]
    public Task<IActionResult> Status(Guid id) => throw new NotImplementedException();

    [HttpPost("/Orders/{id:guid}/Edit")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Edit(Guid id) => throw new NotImplementedException();

    [HttpPost("/Orders/{id:guid}/Cancel")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(Guid id) => throw new NotImplementedException();

    [HttpGet("/Orders")]
    public IActionResult Recent() => throw new NotImplementedException();

    [HttpGet("/Orders/Lookup")]
    public IActionResult Lookup(string? orderId) => throw new NotImplementedException();
}
