using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
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
/// among catalogued members, its reply is constrained to a JSON schema
/// by Ollama's structured output, and the result is validated against
/// the live semantic model before it may be executed.
/// <para>
/// That validation matters more here than it would behind a frontier
/// model: a 7B model picks the wrong member name more often. The design
/// absorbs that instead of depending on the model being right — a
/// rejected plan goes back with the concrete errors, and a plan that
/// still does not validate is reported rather than run.
/// </para>
/// </remarks>
public sealed class OllamaQueryPlanner(
    OllamaChatClient client,
    IOptions<PlannerOptions> options,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<OllamaQueryPlanner> logger) : IQueryPlanner
{
    private const string ModelCheckCacheKey = "ollama-model-present";

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

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.Timeout);

        var preflight = await CheckModelAsync(budget.Token).ConfigureAwait(false);
        if (preflight is not null)
        {
            return QueryPlan.Failed(preflight);
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var systemPrompt = PlannerPrompt.BuildSystemPrompt(model, _options.DomainBriefing, today);

        var messages = new List<OllamaMessage>
        {
            OllamaMessage.System(systemPrompt),
            OllamaMessage.User(question),
        };

        var schema = PlanSchema.Create();
        ValidationResult? lastValidation = null;

        for (var attempt = 1; attempt <= _options.MaxRepairAttempts + 1; attempt++)
        {
            string json;

            try
            {
                json = await client.CompleteAsync(messages, schema, budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return QueryPlan.Failed(
                    $"Das lokale Modell hat nicht innerhalb von {_options.Timeout.TotalSeconds:N0} Sekunden " +
                    "geantwortet. Bei knapper Hardware hilft ein kleineres Modell oder ein höheres Zeitlimit " +
                    "(Planner:Timeout).",
                    attempts: attempt);
            }

            var contract = Deserialize(json);
            if (contract is null)
            {
                return QueryPlan.Failed(
                    "Die Antwort des lokalen Modells war kein verwertbares JSON.", attempts: attempt);
            }

            if (!contract.Answerable)
            {
                PlannerLog.NotAnswerable(logger, contract.Reason ?? "(ohne Begründung)");

                return QueryPlan.Failed(
                    contract.Reason ?? "Die Frage lässt sich mit den verfügbaren Daten nicht beantworten.",
                    attempts: attempt);
            }

            var query = PlanMapper.ToQuery(contract);
            if (query is null)
            {
                return QueryPlan.Failed("Der Plan enthielt keinen auswertbaren Datenbereich.", attempts: attempt);
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

            // Hand back the concrete errors. Naming the offending member
            // and the suggested replacement is what makes the retry a
            // correction rather than a re-roll of the same mistake.
            messages.Add(OllamaMessage.Assistant(json));
            messages.Add(OllamaMessage.User(
                "Die Abfrage ist ungültig:\n"
                + string.Join('\n', lastValidation.Errors.Select(e => $"- {e.Message}"))
                + "\nKorrigiere sie und verwende ausschließlich Namen aus dem Katalog."));
        }

        return QueryPlan.Failed(
            "Die Frage konnte nicht in eine gültige Abfrage übersetzt werden. "
            + "Formulieren Sie sie anders oder stellen Sie die Abfrage im Editor zusammen.",
            lastValidation?.Errors,
            _options.MaxRepairAttempts + 1);
    }

    /// <summary>
    /// Verifies the server is reachable and the model is pulled.
    /// </summary>
    /// <remarks>
    /// Without this the two most common setup mistakes both surface as
    /// an opaque HTTP error. The result is cached briefly: it is a fixed
    /// fact between deployments, but re-checked often enough that pulling
    /// the model fixes the app without a restart.
    /// </remarks>
    /// <returns>An error message, or <c>null</c> when everything is in place.</returns>
    private async Task<string?> CheckModelAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(ModelCheckCacheKey, out bool present) && present)
        {
            return null;
        }

        IReadOnlyList<string> installed;

        try
        {
            installed = await client.GetInstalledModelsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or OllamaException)
        {
            PlannerLog.ServerUnreachable(logger, _options.BaseUrl, exception.Message);

            return $"Der lokale Modellserver unter {_options.BaseUrl} ist nicht erreichbar. "
                 + "Läuft Ollama? (`ollama serve` bzw. `docker compose up -d ollama`)";
        }

        if (!OllamaChatClient.IsInstalled(_options.Model, installed))
        {
            var available = installed.Count == 0 ? "keine" : string.Join(", ", installed);

            return $"Das Modell \"{_options.Model}\" ist auf dem Ollama-Server nicht vorhanden "
                 + $"(installiert: {available}). Holen Sie es mit `ollama pull {_options.Model}`.";
        }

        cache.Set(ModelCheckCacheKey, true, TimeSpan.FromMinutes(5));
        return null;
    }

    private PlanContract? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PlanContract>(json);
        }
        catch (JsonException exception)
        {
            PlannerLog.UnreadableResponse(logger, exception.Message);
            return null;
        }
    }
}
