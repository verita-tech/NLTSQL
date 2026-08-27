using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nltsql.Core.Abstractions;
using Nltsql.Core.Queries;
using Nltsql.Core.Semantics;
using Nltsql.Infrastructure.Configuration;

namespace Nltsql.Infrastructure.Planning;

/// <summary>
/// Plans semantic queries from natural-language questions using a local
/// model served by Ollama.
/// </summary>
/// <remarks>
/// The model never writes SQL and never sees the warehouse. It chooses
/// among catalogued members, its answer is constrained to a JSON schema,
/// and the result is validated against the live semantic model before it
/// is allowed anywhere near execution. A plan that fails validation is
/// sent back once with the concrete errors, which fixes the common case
/// of a near-miss member name.
///
/// Running the model locally means the catalogue — which is the customer's
/// business vocabulary — never leaves their machine. The price is that the
/// schema is enforced by a sampling grammar rather than a validating API,
/// so the response needs the extra care in <see cref="JsonPayload"/>.
/// </remarks>
public sealed class OllamaQueryPlanner(
    HttpClient httpClient,
    IOptions<PlannerOptions> options,
    TimeProvider timeProvider,
    ILogger<OllamaQueryPlanner> logger) : IQueryPlanner
{
    private const string ChatPath = "/api/chat";

    private readonly PlannerOptions _options = options.Value;

    public bool IsAvailable => _options.IsConfigured;

    public async Task<QueryPlan> PlanAsync(
        string question,
        SemanticModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(model);

        if (!IsAvailable)
        {
            return QueryPlan.Failed(
                "Die Frageeingabe in natürlicher Sprache ist nicht aktiviert (Planner:Enabled).");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var systemPrompt = PlannerPrompt.BuildSystemPrompt(model, _options.DomainBriefing, today);

        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = question },
        };

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.Timeout);

        ValidationResult? lastValidation = null;

        for (var attempt = 1; attempt <= _options.MaxRepairAttempts + 1; attempt++)
        {
            PlanContract? contract;
            string rawJson;

            try
            {
                (contract, rawJson) = await RequestPlanAsync(messages, budget.Token).ConfigureAwait(false);
            }
            catch (OllamaUnavailableException exception)
            {
                PlannerLog.TransportFailed(logger, exception.Message);
                return QueryPlan.Failed(exception.Message, attempts: attempt);
            }

            if (contract is null)
            {
                return QueryPlan.Failed(
                    "Die Antwort des Sprachmodells konnte nicht gelesen werden.", attempts: attempt);
            }

            if (!contract.Answerable)
            {
                PlannerLog.NotAnswerable(logger, contract.Reason ?? "(ohne Begründung)");

                return QueryPlan.Failed(
                    contract.Reason
                    ?? "Die Frage lässt sich mit den verfügbaren Daten nicht beantworten.",
                    attempts: attempt);
            }

            var query = PlanMapper.ToQuery(contract);
            if (query is null)
            {
                return QueryPlan.Failed(
                    "Der Plan enthielt keinen auswertbaren Datenbereich.", attempts: attempt);
            }

            lastValidation = SemanticQueryValidator.Validate(query, model);
            if (lastValidation.IsValid)
            {
                PlannerLog.Planned(logger, query.View, attempt);

                return new QueryPlan
                {
                    Query = query,
                    Interpretation = contract.Interpretation,
                    Attempts = attempt,
                };
            }

            PlannerLog.ValidationFailed(logger, attempt, lastValidation.Summary);

            if (attempt > _options.MaxRepairAttempts)
            {
                break;
            }

            // Feed the concrete errors back. Naming the offending member
            // and the suggested replacement is what makes the retry
            // worthwhile rather than a re-roll of the same mistake.
            messages.Add(new OllamaMessage { Role = "assistant", Content = rawJson });
            messages.Add(new OllamaMessage
            {
                Role = "user",
                Content =
                    "Die Abfrage ist ungültig:\n"
                    + string.Join('\n', lastValidation.Errors.Select(e => $"- {e.Message}"))
                    + "\nKorrigiere sie und verwende ausschließlich Namen aus dem Katalog.",
            });
        }

        return QueryPlan.Failed(
            "Die Frage konnte nicht in eine gültige Abfrage übersetzt werden.",
            lastValidation?.Errors,
            _options.MaxRepairAttempts + 1);
    }

    private async Task<(PlanContract? Contract, string RawJson)> RequestPlanAsync(
        List<OllamaMessage> messages,
        CancellationToken cancellationToken)
    {
        var request = new OllamaChatRequest
        {
            Model = _options.Model,
            Messages = messages,
            Stream = false,
            Format = PlanSchema.Create(),
            KeepAlive = _options.KeepAlive,
            Options = new OllamaRuntimeOptions
            {
                // Translating a question into a fixed schema is not a
                // creative task; sampling variety only costs accuracy.
                Temperature = 0,
                ContextTokens = _options.ContextTokens,
            },
        };

        HttpResponseMessage response;

        try
        {
            response = await httpClient.PostAsJsonAsync(ChatPath, request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new OllamaUnavailableException(
                $"Ollama ist unter {_options.BaseUrl} nicht erreichbar. Läuft der Dienst? "
                + "(`ollama serve` bzw. `docker compose --profile ollama up -d`)",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new OllamaUnavailableException(DescribeFailure(response.StatusCode, body));
            }

            var payload = await response.Content
                .ReadFromJsonAsync<OllamaChatResponse>(cancellationToken)
                .ConfigureAwait(false);

            var json = JsonPayload.Extract(payload?.Message?.Content);

            if (json is null)
            {
                PlannerLog.UnreadableResponse(logger, "Die Antwort enthielt kein JSON-Objekt.");
                return (null, payload?.Message?.Content ?? string.Empty);
            }

            try
            {
                return (JsonSerializer.Deserialize<PlanContract>(json), json);
            }
            catch (JsonException exception)
            {
                PlannerLog.UnreadableResponse(logger, exception.Message);
                return (null, json);
            }
        }
    }

    /// <summary>
    /// Turns Ollama's own error into something the person who asked the
    /// question can act on.
    /// </summary>
    /// <remarks>
    /// A missing model is by far the most common first-run failure, and
    /// "404 Not Found" tells the user nothing about the one command that
    /// fixes it.
    /// </remarks>
    private string DescribeFailure(HttpStatusCode statusCode, string body)
    {
        if (statusCode == HttpStatusCode.NotFound
            && body.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return $"Das Modell \"{_options.Model}\" ist in Ollama nicht vorhanden. "
                + $"Einmalig `ollama pull {_options.Model}` ausführen.";
        }

        var detail = body.Length > 500 ? body[..500] : body;

        return $"Ollama hat die Anfrage mit {(int)statusCode} abgelehnt: {detail}";
    }
}

/// <summary>Ollama could not be reached or refused the request.</summary>
internal sealed class OllamaUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed record OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OllamaMessage> Messages { get; init; }

    /// <summary>Always false: the plan is only useful complete.</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    /// <summary>JSON Schema the sampler is constrained to.</summary>
    [JsonPropertyName("format")]
    public JsonNode? Format { get; init; }

    [JsonPropertyName("keep_alive")]
    public string? KeepAlive { get; init; }

    [JsonPropertyName("options")]
    public OllamaRuntimeOptions? Options { get; init; }
}

internal sealed record OllamaRuntimeOptions
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }

    [JsonPropertyName("num_ctx")]
    public int ContextTokens { get; init; }
}

internal sealed record OllamaMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

internal sealed record OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; init; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }
}
