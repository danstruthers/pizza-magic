using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PizzaMagic.Web;
using PizzaMagic.Web.Clients;

namespace PizzaMagic.EndToEnd.Tests.Hosting;

/// <summary>
/// The real order builder in-process, wired to the in-process order service through its real typed client and
/// resilience pipeline. Only the network is replaced, by a <see cref="NetworkGate"/> that can drop requests or
/// responses.
/// </summary>
public sealed partial class WebHost(OrderManagerHost orderManager) : WebApplicationFactory<WebMarker>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public NetworkGate Network { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("OrderManager:BaseUrl", orderManager.Server.BaseAddress.ToString());
        builder.UseSetting("OrderManager:RetryBaseDelayMilliseconds", "1");
        builder.ConfigureTestServices(services =>
        {
            foreach (var name in new[] { OrderManagerClient.ClientName, OrderManagerClient.QuotesClientName })
            {
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => Network.Connect(orderManager.Server.CreateHandler()));
            }
        });
    }

    /// <summary>A browser: cookies kept, redirects not followed (so tests see each PRG step).</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    public static string Token(string html)
    {
        var token = TokenField().Match(html).Groups[1].Value;
        token.ShouldNotBeNullOrEmpty();
        return token;
    }

    public static string? Ticket(string html) => TicketField().Match(html) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>A page as the customer reads it: HTML entities decoded.</summary>
    public static string Text(string html) => System.Net.WebUtility.HtmlDecode(html);

    public static Task<HttpResponseMessage> PostQuoteAsync(HttpClient browser, string token, object draft)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Builder/Quote") { Content = JsonContent.Create(draft, options: Json) };
        request.Headers.Add("RequestVerificationToken", token);
        return browser.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PostFormAsync(HttpClient browser, string url, string token, params (string Name, string Value)[] fields) =>
        browser.PostAsync(url, new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", token), .. fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value))]));

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenField();

    [GeneratedRegex("name=\"ticket\" value=\"([^\"]+)\"")]
    private static partial Regex TicketField();
}

/// <summary>
/// The network between the two apps. <see cref="Down"/> fails every request before it arrives;
/// <see cref="DropResponses"/> lets the order service do the work and then loses its answer (a phone losing signal).
/// </summary>
public sealed class NetworkGate
{
    public bool Down { get; set; }

    public bool DropResponses { get; set; }

    public int Requests;

    public HttpMessageHandler Connect(HttpMessageHandler server) => new Gate(this) { InnerHandler = server };

    private sealed class Gate(NetworkGate network) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref network.Requests);
            if (network.Down)
            {
                throw new HttpRequestException("Network down (test).");
            }
            var response = await base.SendAsync(request, cancellationToken);
            if (network.DropResponses)
            {
                response.Dispose();
                throw new HttpRequestException("Response lost (test).");
            }
            return response;
        }
    }
}
