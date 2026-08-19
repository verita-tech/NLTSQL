using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Nltsql.Infrastructure.Configuration;

namespace Nltsql.Infrastructure.Planning;

/// <summary>
/// Minimal client for the Ollama HTTP API.
/// </summary>
/// <remarks>
/// Only the two endpoints the planner needs: <c>/api/chat</c> for a
/// single non-streamed completion, and <c>/api/tags</c> to tell "model
/// not pulled" apart from "server not running" — two failures that look
/// identical to a user but need opposite fixes.
/// </remarks>
public sealed class OllamaChatClient(HttpClient httpClient, IOptions<PlannerOptions> options)
{
    private readonly PlannerOptions _options = options.Value;

    /// <summary>
    /// Sends one chat completion constrained to a JSON schema.
    /// </summary>
    /// <returns>The assistant's message content, which is the JSON document.</returns>
    public async Task<string> CompleteAsync(
        IReadOnlyList<OllamaMessage> messages,
        Dictionary<string, JsonElement> jsonSchema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(jsonSchema);

        var payload = new JsonObject
        {
            ["model"] = _options.Model,

            // The UI waits for a whole plan; streaming would only add
            // reassembly work for no benefit.
            ["stream"] = false,

            // Structured outputs: Ollama constrains decoding to the
            // schema, so the reply cannot come back as prose or as JSON
            // wrapped in a markdown fence — the two things small models
            // do most often when merely asked for JSON.
            ["format"] = ToJsonNode(jsonSchema),

            ["keep_alive"] = _options.KeepAlive,

            ["options"] = new JsonObject
            {
                // Picking members out of a catalogue is extraction, not
                // composition. Sampling only invents member names.
                ["temperature"] = 0,

                // Must be set: Ollama otherwise applies a small default
                // and truncates the prompt from the front, silently
                // discarding the catalogue this whole design rests on.
                ["num_ctx"] = _options.ContextTokens,
            },

            ["messages"] = new JsonArray(messages.Select(m => (JsonNode?)new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content,
            }).ToArray()),
        };

        using var response = await httpClient
            .PostAsJsonAsync("/api/chat", payload, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync<JsonNode>(cancellationToken)
            .ConfigureAwait(false);

        return body?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
    }

    /// <summary>Model tags the server currently has pulled.</summary>
    public async Task<IReadOnlyList<string>> GetInstalledModelsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/api/tags", cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync<JsonNode>(cancellationToken)
            .ConfigureAwait(false);

        var models = body?["models"]?.AsArray();
        if (models is null)
        {
            return [];
        }

        return models
            .Select(m => m?["name"]?.GetValue<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
    }

    /// <summary>
    /// Matches a configured model against what is installed.
    /// </summary>
    /// <remarks>
    /// Ollama reports tags fully qualified, so a model configured as
    /// <c>qwen2.5:7b-instruct</c> may be listed under exactly that name,
    /// while one configured as <c>qwen2.5</c> is listed as
    /// <c>qwen2.5:latest</c>. Both are the same model to the user.
    /// </remarks>
    public static bool IsInstalled(string configuredModel, IReadOnlyList<string> installed)
    {
        ArgumentNullException.ThrowIfNull(installed);

        var qualified = configuredModel.Contains(':', StringComparison.Ordinal)
            ? configuredModel
            : $"{configuredModel}:latest";

        return installed.Any(name => string.Equals(name, qualified, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonObject ToJsonNode(Dictionary<string, JsonElement> schema)
    {
        var node = new JsonObject();

        foreach (var (key, value) in schema)
        {
            node[key] = JsonSerializer.Deserialize<JsonNode>(value);
        }

        return node;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        throw new OllamaException(
            $"Ollama antwortete mit {(int)response.StatusCode} ({response.ReasonPhrase}): {Truncate(body)}");
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}

public sealed record OllamaMessage(string Role, string Content)
{
    public static OllamaMessage System(string content) => new("system", content);

    public static OllamaMessage User(string content) => new("user", content);

    public static OllamaMessage Assistant(string content) => new("assistant", content);
}

public sealed class OllamaException(string message) : Exception(message);
