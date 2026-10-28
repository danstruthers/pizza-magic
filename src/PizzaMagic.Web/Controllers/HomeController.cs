using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Web.Models;

namespace PizzaMagic.Web.Controllers;

/// <summary>The error page. The home page is the order builder (<see cref="BuilderController"/>).</summary>
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
