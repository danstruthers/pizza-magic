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
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var html = await client.GetStringAsync("/");
        var token = TokenField().Match(html).Groups[1].Value;
        token.ShouldNotBeNullOrEmpty();
        return (client, token);
    }

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

    [GeneratedRegex("data-cart=\"([^\"]*)\"")]
    private static partial Regex CartAttribute();
}
