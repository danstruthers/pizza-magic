using System.Net;
using System.Text;

namespace PizzaMagic.Web.Tests.Clients;

/// <summary>
/// The last handler in the pipeline: answers from a script and records every request it received (method, path,
/// headers and the exact body bytes), so tests can see what each retry actually sent.
/// </summary>
public sealed class StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] script) : HttpMessageHandler
{
    private int _calls;

    public List<RecordedRequest> Requests { get; } = [];

    public static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode status, string? json = null, string mediaType = "application/json",
        Action<HttpResponseMessage>? configure = null) => _ =>
    {
        var response = new HttpResponseMessage(status);
        if (json is not null)
        {
            response.Content = new StringContent(json, Encoding.UTF8, mediaType);
        }
        configure?.Invoke(response);
        return response;
    };

    public static Func<HttpRequestMessage, HttpResponseMessage> Problem(HttpStatusCode status, string type, string extraJson = "") =>
        Respond(status, $$"""{ "type": "https://pizzamagic.local/problems/{{type}}", "title": "{{type}}", "status": {{(int)status}}{{extraJson}} }""",
            "application/problem+json");

    public static Func<HttpRequestMessage, HttpResponseMessage> Throw(Exception exception) => _ => throw exception;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!.PathAndQuery,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
            body));
        var step = script[Math.Min(_calls, script.Length - 1)];
        _calls++;
        return step(request);
    }
}

public sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, IReadOnlyDictionary<string, string> Headers, string? Body);
