using System.Text.Json;
using PizzaMagic.Contracts;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Models.Session;

namespace PizzaMagic.Web.Models;

/// <summary>The builder page: the restaurant's menu (null when the service is unavailable) and the Session cart.</summary>
public sealed record BuilderViewModel(MenuResponse? Menu, CartState Cart)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string MenuJson => JsonSerializer.Serialize(Menu, Json);

    public string CartJson => CartStore.ToJson(Cart);

    public static string Money(long cents) => MoneyFormatter.Format(cents);

    /// <summary>A topping's whole-pizza price for each size, for the page to show as the size changes (display only).</summary>
    public static string WholePricesJson(MenuToppingResponse topping) =>
        JsonSerializer.Serialize(topping.Prices.ToDictionary(p => p.Key, p => p.Value.TryGetValue("WHOLE", out var cents) ? cents : 0), Json);

    public static string Percent(int ppm) => PercentFormatter.FormatBasisPointsCompact(ppm / 100);
}
