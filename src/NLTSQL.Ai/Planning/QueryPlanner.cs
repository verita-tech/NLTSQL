using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NLTSQL.Ai.Prompting;
using NLTSQL.Core.Query;
using NLTSQL.QueryEngine;
using NLTSQL.Semantics.Model;
using NLTSQL.Semantics.Validation;

namespace NLTSQL.Ai.Planning;

/// <summary>The outcome of planning a question.</summary>
/// <param name="Query">The resolved query, or <see langword="null"/> when planning failed.</param>
/// <param name="Spec">The last spec the model produced, kept even on failure so the UI can show it.</param>
/// <param name="Issues">Why it failed, if it did.</param>
/// <param name="Attempts">How many model calls it took.</param>
public sealed record QueryPlanResult(
    ResolvedQuery? Query,
    QuerySpec? Spec,
    IReadOnlyList<ValidationIssue> Issues,
    int Attempts)
{
    /// <summary>Whether a runnable query came out.</summary>
    public bool Success => this.Query is not null;
}

/// <summary>
/// Turns a question into a resolved query, repairing the model's output when it does not resolve.
/// </summary>
/// <remarks>
/// <para>
/// The repair loop is what makes a locally hosted model workable. A first attempt that names a
/// measure slightly wrong is the common failure, not an exotic one, and the resolver already knows
/// both what was wrong and what was available. Feeding that back is far more effective than a
/// larger prompt or a bigger model, because it turns an open-ended guess into a correction against
/// a list.
/// </para>
/// <para>
/// Attempts are capped. Past two repairs the model is not converging, and returning the resolver's
/// message to the user — who can rephrase with knowledge the model does not have — beats burning
/// more time on it.
/// </para>
/// </remarks>
public sealed class QueryPlanner(
    IChatClient chatClient,
    QuerySpecResolver resolver,
    ILogger<QueryPlanner> logger)
{
    /// <summary>How many repair rounds follow the first attempt.</summary>
    private const int MaxRepairAttempts = 2;

    private static readonly ChatOptions Options = new()
    {
        // Planning is not a creative task: the same question against the same model should produce
        // the same query, or dashboards built on it stop being reproducible.
        Temperature = 0f,
        ResponseFormat = ChatResponseFormat.Json,
    };

    /// <summary>Plans <paramref name="question"/> against <paramref name="model"/>.</summary>
    public async Task<QueryPlanResult> PlanAsync(
        string question,
        SemanticModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(model);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt(model)),
            new(ChatRole.User, question),
        };

        QuerySpec? lastSpec = null;
        List<ValidationIssue> issues = [];

        for (var attempt = 1; attempt <= MaxRepairAttempts + 1; attempt++)
        {
            var response = await chatClient.GetResponseAsync(messages, Options, cancellationToken).ConfigureAwait(false);
            var text = response.Text ?? string.Empty;

            issues = [];
            var draft = TryParse(text, issues);

            if (draft is not null)
            {
                lastSpec = QuerySpecDraftMapper.Map(draft, issues);

                if (lastSpec is not null && !issues.HasErrors())
                {
                    var resolution = resolver.Resolve(lastSpec, model);
                    if (resolution.Success)
                    {
                        return new QueryPlanResult(resolution.Query, lastSpec, [], attempt);
                    }

                    issues = [.. resolution.Issues];
                }
            }

            if (attempt > MaxRepairAttempts)
            {
                break;
            }

            logger.PlanAttemptFailed(attempt, issues.Count);

            messages.Add(new ChatMessage(ChatRole.Assistant, text));
            messages.Add(new ChatMessage(ChatRole.User, RepairPrompt(issues)));
        }

        return new QueryPlanResult(null, lastSpec, issues, MaxRepairAttempts + 1);
    }

    private static QuerySpecDraft? TryParse(string text, List<ValidationIssue> issues)
    {
        var json = ExtractJsonObject(text);

        if (json is null)
        {
            issues.Add(ValidationIssue.Error(
                "draft.json.missing",
                "response",
                "Die Antwort enthaelt kein JSON-Objekt. Antworte ausschliesslich mit dem JSON-Objekt, ohne erklaerenden Text."));
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<QuerySpecDraft>(json);
        }
        catch (JsonException exception)
        {
            issues.Add(ValidationIssue.Error("draft.json.invalid", "response", $"Das JSON ist fehlerhaft: {exception.Message}"));
            return null;
        }
    }

    /// <summary>
    /// Pulls the JSON object out of a response that may be wrapped in prose or a code fence.
    /// </summary>
    /// <remarks>
    /// Small models regularly ignore "reply with JSON only", and a leading "Hier ist die Abfrage:"
    /// is not a reason to spend a repair round. Scanning from the first brace to its matching close
    /// costs nothing and removes the most common cause of a wasted attempt.
    /// </remarks>
    internal static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c is '\\')
                {
                    escaped = true;
                }
                else if (c is '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..(i + 1)];
                    }

                    break;
                default:
                    break;
            }
        }

        return null;
    }

    private static string RepairPrompt(IReadOnlyList<ValidationIssue> issues)
    {
        var text = new StringBuilder();
        text.Append("Die Abfrage konnte nicht ausgefuehrt werden:\n");

        foreach (var issue in issues.Where(i => i.Severity is IssueSeverity.Error))
        {
            text.Append(CultureInfo.InvariantCulture, $"- {issue.Path}: {issue.Message}\n");
        }

        text.Append("\nKorrigiere das JSON-Objekt und antworte erneut ausschliesslich mit dem vollstaendigen JSON.");
        return text.ToString();
    }

    private static string SystemPrompt(SemanticModel model) =>
        $$"""
        Du uebersetzt Fragen von Fachanwendern in eine strukturierte Abfrage. Du schreibst kein SQL.

        Antworte ausschliesslich mit einem JSON-Objekt in genau diesem Format:

        {
          "entity": "name_einer_entitaet",
          "measures": ["name_einer_kennzahl"],
          "group_by": [{"field": "name_eines_merkmals", "grain": "month"}],
          "filters": [{"field": "name_eines_merkmals", "operator": "equals", "values": ["wert"]}],
          "order_by": [{"field": "name_einer_kennzahl", "direction": "desc"}],
          "limit": 10
        }

        Regeln:
        - Verwende ausschliesslich Namen, die im Datenmodell unten stehen. Erfinde nichts.
        - Alle Namen exakt so schreiben wie im Modell, in Kleinbuchstaben mit Unterstrichen.
        - "grain" nur bei Zeitmerkmalen angeben: day, week, month, quarter oder year.
        - Operatoren: equals, not_equals, in, not_in, greater_than, greater_or_equal, less_than,
          less_or_equal, between, is_null, is_not_null, contains, starts_with, ends_with,
          in_last_days, in_last_months, in_last_years.
        - Bei in_last_days, in_last_months und in_last_years steht in "values" genau eine Zahl,
          zum Beispiel ["12"] fuer die letzten zwoelf Monate.
        - "values" ist immer eine Liste von Zeichenketten.
        - Felder, die nicht gebraucht werden, weglassen oder leer lassen.
        - "limit" nur setzen, wenn die Frage nach einer bestimmten Anzahl verlangt, etwa "die
          fuenf groessten".
        - Nach einer Kennzahl sortieren heisst, sie auch unter "measures" aufzufuehren.

        {{ModelExcerpt.Render(model)}}
        """;
}

/// <summary>Source-generated logging for the planner.</summary>
internal static partial class QueryPlannerLog
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Query planning attempt {Attempt} did not resolve ({IssueCount} issue(s)); repairing.")]
    public static partial void PlanAttemptFailed(this ILogger logger, int attempt, int issueCount);
}
