using System.Net;
using System.Text;

namespace Nltsql.Tests.Planning;

/// <summary>
/// Serves canned responses and records what was sent.
/// </summary>
/// <remarks>
/// Ollama cannot be run in CI, so the wire contract is pinned here
/// instead: these tests prove what this app sends and how it reads the
/// reply. They do not prove how a given model behaves.
/// </remarks>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<string> _responses = new();

    public List<string> RequestBodies { get; } = [];

    public List<string> RequestPaths { get; } = [];

    public void Enqueue(string json) => _responses.Enqueue(json);

    /// <summary>Simulates a server that is not running at all.</summary>
    public bool RefuseConnections { get; set; }

    /// <summary>Queues an Ollama chat reply carrying the given plan JSON.</summary>
    public void EnqueueChatReply(string planJson)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(planJson);

        Enqueue($$"""{"model":"test","message":{"role":"assistant","content":{{escaped}}},"done":true}""");
    }

    public void EnqueueInstalledModels(params string[] names)
    {
        var models = string.Join(",", names.Select(n => $$"""{"name":"{{n}}"}"""));

        Enqueue($$"""{"models":[{{models}}]}""");
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestPaths.Add(request.RequestUri!.AbsolutePath);

        if (RefuseConnections)
        {
            throw new HttpRequestException("Connection refused");
        }

        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var body = _responses.Count > 0 ? _responses.Dequeue() : "{}";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
