using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PizzaMagic.Web.Clients;

namespace PizzaMagic.Web.Tests.Builder;

/// <summary>The order builder in-process, with a scripted order service in place of the real one.</summary>
public sealed partial class WebFactory : WebApplicationFactory<WebMarker>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FakeOrderManagerClient OrderManager { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOrderManagerClient>();
            services.AddSingleton<IOrderManagerClient>(OrderManager);
        });

    /// <summary>A browser-like session: cookies kept, the antiforgery token taken from the builder page.</summary>
    public async Task<(HttpClient Client, string Token)> BrowserAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/");
        return (client, Token(html));
    }

    /// <summary>A builder draft of one pizza with Pepperoni, as the page posts it.</summary>
    public static object Draft(long revision, string size, params string[] codes) => new
    {
        revision,
        lineItems = new[] { new { kind = "Pizza", sizeCode = size, quantity = 1, toppings = new[] { new { code = "PEPPERONI" } } } },
        promoCodes = codes,
        tip = (object?)null,
    };

    /// <summary>The antiforgery token rendered in a page form.</summary>
    public static string Token(string html)
    {
        var token = TokenField().Match(html).Groups[1].Value;
        token.ShouldNotBeNullOrEmpty();
        return token;
    }

    /// <summary>The sealed review ticket in a page form, or null when the page offers none.</summary>
    public static string? Ticket(string html) =>
        TicketField().Match(html) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>A page as text: HTML entities decoded, so assertions read like what the customer sees.</summary>
    public static string Text(string html) => System.Net.WebUtility.HtmlDecode(html);

    public static async Task<(string Html, string? Ticket, string Token)> ReviewAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Checkout");
        return (Text(html), Ticket(html), Token(html));
    }

    public static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, string token, params (string Name, string Value)[] fields) =>
        client.PostAsync(url, new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", token), .. fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value))]));

    public static Task<HttpResponseMessage> PlaceAsync(HttpClient client, string token, string ticket) =>
        PostFormAsync(client, "/Checkout/Place", token, ("ticket", ticket));

    public static Task<HttpResponseMessage> PostQuoteAsync(HttpClient client, string token, object draft)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Builder/Quote") { Content = JsonContent.Create(draft, options: Json) };
        request.Headers.Add("RequestVerificationToken", token);
        return client.SendAsync(request);
    }

    /// <summary>The cart the page was rendered with (the builder's data-cart attribute).</summary>
    public static JsonElement RenderedCart(string html)
    {
        var encoded = CartAttribute().Match(html).Groups[1].Value;
        return JsonDocument.Parse(System.Net.WebUtility.HtmlDecode(encoded)).RootElement;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenField();

    [GeneratedRegex("name=\"ticket\" value=\"([^\"]+)\"")]
    private static partial Regex TicketField();

    [GeneratedRegex("data-cart=\"([^\"]*)\"")]
    private static partial Regex CartAttribute();
}
