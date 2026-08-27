using System.Net;
using System.Text;

namespace Nltsql.Tests.Planning;

/// <summary>
/// Answers each request from a queued list of replies and records what
/// was sent, so a test can assert on the conversation as well as the
/// outcome.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _replies = new();

    public List<string> Requests { get; } = [];

    /// <summary>Queues a successful Ollama chat reply carrying the given content.</summary>
    public StubHttpMessageHandler RespondingWith(string messageContent)
    {
        var body = $$"""
            {"model":"test","message":{"role":"assistant","content":{{JsonString(messageContent)}}},"done":true,"done_reason":"stop"}
            """;

        return Responding(HttpStatusCode.OK, body);
    }

    public StubHttpMessageHandler Responding(HttpStatusCode statusCode, string body)
    {
        _replies.Enqueue(() => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

        return this;
    }

    public StubHttpMessageHandler Throwing(Exception exception)
    {
        _replies.Enqueue(() => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (_replies.Count == 0)
        {
            throw new InvalidOperationException("Es wurden mehr Anfragen gestellt als Antworten hinterlegt.");
        }

        return _replies.Dequeue()();
    }

    private static string JsonString(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);
}
