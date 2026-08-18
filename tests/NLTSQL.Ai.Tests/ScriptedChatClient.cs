using Microsoft.Extensions.AI;

namespace NLTSQL.Ai.Tests;

/// <summary>
/// A chat client that returns prepared answers in order.
/// </summary>
/// <remarks>
/// Lets the planner's repair loop be tested without Ollama running. The recorded conversation is
/// kept so a test can assert that the repair prompt actually carried the resolver's complaint back
/// to the model — which is the whole mechanism, and easy to break without noticing.
/// </remarks>
internal sealed class ScriptedChatClient(params string[] replies) : IChatClient
{
    private int index;

    /// <summary>Every message the planner sent, across all turns.</summary>
    public List<ChatMessage> Sent { get; } = [];

    /// <summary>How many times the planner called the model.</summary>
    public int CallCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.Sent.Clear();
        this.Sent.AddRange(messages);
        this.CallCount++;

        var reply = replies[Math.Min(this.index++, replies.Length - 1)];
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Nothing to release.
    }
}
