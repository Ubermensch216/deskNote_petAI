using System.Net;
using System.Text;

namespace DeskNote.Ai.Tests;

/// <summary>
/// A stand-in for the Ollama daemon: canned responses per path, and a record of what was sent.
/// </summary>
/// <remarks>
/// The tests that matter here are about the contract with the daemon — which endpoint is called,
/// what the prompt looks like, how a failure is classified — and none of that needs a 7GB model.
/// </remarks>
internal sealed class FakeOllama : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    internal List<string> RequestBodies { get; } = [];

    internal List<string> RequestPaths { get; } = [];

    internal FakeOllama Route(string path, HttpStatusCode status, string body, string contentType = "application/json")
    {
        _routes[path] = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };

        return this;
    }

    internal FakeOllama Fails(string path, Exception exception)
    {
        _routes[path] = _ => throw exception;
        return this;
    }

    internal FakeOllama WithModels(params string[] names)
    {
        var models = string.Join(",", names.Select(n => $$"""{"name":"{{n}}","size":7200000000}"""));
        return Route("/api/tags", HttpStatusCode.OK, $$"""{"models":[{{models}}]}""");
    }

    /// <summary>A non-streaming /api/chat reply carrying exactly this assistant content.</summary>
    internal FakeOllama WithChatReply(string content)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(content);
        return Route(
            "/api/chat",
            HttpStatusCode.OK,
            $$"""{"message":{"role":"assistant","content":{{escaped}}},"done":true}""");
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        RequestPaths.Add(path);

        if (request.Content is not null)
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        return _routes.TryGetValue(path, out var route)
            ? route(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    internal HttpClient Client() => new(this) { BaseAddress = new Uri(OllamaOptions.DefaultEndpoint) };
}
