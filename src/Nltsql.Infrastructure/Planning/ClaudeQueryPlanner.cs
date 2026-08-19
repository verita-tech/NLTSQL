using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nltsql.Core.Abstractions;
using Nltsql.Core.Queries;
using Nltsql.Core.Semantics;
using Nltsql.Infrastructure.Configuration;

namespace Nltsql.Infrastructure.Planning;

/// <summary>
/// Plans semantic queries from natural-language questions using Claude.
/// </summary>
/// <remarks>
/// The model never writes SQL and never sees the warehouse. It chooses
/// among catalogued members, its answer is constrained to a JSON schema,
/// and the result is validated against the live semantic model before it
/// is allowed anywhere near execution. A plan that fails validation is
/// sent back once with the concrete errors, which fixes the common case
/// of a near-miss member name.
/// </remarks>
public sealed class ClaudeQueryPlanner(
    AnthropicClient client,
    IOptions<PlannerOptions> options,
    TimeProvider timeProvider,
    ILogger<ClaudeQueryPlanner> logger) : IQueryPlanner
{
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
                "Die Frageeingabe in natürlicher Sprache ist nicht konfiguriert (Planner:ApiKey fehlt).");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var systemPrompt = PlannerPrompt.BuildSystemPrompt(model, _options.DomainBriefing, today);

        var messages = new List<MessageParam>
        {
            new() { Role = Role.User, Content = question },
        };

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.Timeout);

        ValidationResult? lastValidation = null;

        for (var attempt = 1; attempt <= _options.MaxRepairAttempts + 1; attempt++)
        {
            var (contract, rawJson) = await RequestPlanAsync(systemPrompt, messages, budget.Token)
                .ConfigureAwait(false);

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
            messages.Add(new MessageParam { Role = Role.Assistant, Content = rawJson });
            messages.Add(new MessageParam
            {
                Role = Role.User,
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
        string systemPrompt,
        List<MessageParam> messages,
        CancellationToken cancellationToken)
    {
        var response = await client.Messages.Create(
            new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = 4096,
                System = new List<TextBlockParam>
                {
                    new()
                    {
                        Text = systemPrompt,

                        // The catalogue is identical for every question a
                        // tenant asks, and it is the bulk of the prompt.
                        CacheControl = new CacheControlEphemeral(),
                    },
                },
                Messages = messages,
                OutputConfig = new OutputConfig
                {
                    Format = new JsonOutputFormat { Schema = PlanSchema.Create() },

                    // Picking members from a catalogue is a bounded task;
                    // the extra latency of a higher effort is not repaid.
                    Effort = Effort.Medium,
                },
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var json = string.Concat(
            response.Content.Select(block => block.Value).OfType<TextBlock>().Select(t => t.Text));

        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, string.Empty);
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

/// <summary>Maps the wire contract onto the domain query.</summary>
internal static class PlanMapper
{
    public static SemanticQuery? ToQuery(PlanContract contract)
    {
        if (string.IsNullOrWhiteSpace(contract.View))
        {
            return null;
        }

        return new SemanticQuery
        {
            View = contract.View,
            Measures = contract.Measures,
            Dimensions = contract.Dimensions,
            TimeDimension = ToTimeDimension(contract.TimeDimension),
            Filters = contract.Filters
                .Where(f => !string.IsNullOrWhiteSpace(f.Member))
                .Select(f => new QueryFilter
                {
                    Member = f.Member!,
                    Operator = Parse(f.Operator, FilterOperator.Equals),
                    Values = f.Values,
                })
                .ToList(),
            Order = contract.Order
                .Where(o => !string.IsNullOrWhiteSpace(o.Member))
                .Select(o => new QueryOrder
                {
                    Member = o.Member!,
                    Direction = Parse(o.Direction, SortDirection.Descending),
                })
                .ToList(),
            Limit = Math.Clamp(contract.Limit ?? QueryLimits.DefaultRows, 1, QueryLimits.MaxRows),
        };
    }

    private static QueryTimeDimension? ToTimeDimension(PlanTimeDimension? time)
    {
        if (time is null || string.IsNullOrWhiteSpace(time.Dimension))
        {
            return null;
        }

        return new QueryTimeDimension
        {
            Dimension = time.Dimension,
            Granularity = Enum.TryParse<TimeGranularity>(time.Granularity, ignoreCase: true, out var granularity)
                ? granularity
                : null,
            DateRange = ToDateRange(time),
        };
    }

    private static QueryDateRange? ToDateRange(PlanTimeDimension time)
    {
        if (Enum.TryParse<RelativeDateRange>(time.RelativeRange, ignoreCase: true, out var relative))
        {
            return QueryDateRange.Of(relative);
        }

        var from = ParseDate(time.From);
        var to = ParseDate(time.To);

        return from is null && to is null ? null : new QueryDateRange { From = from, To = to };
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static TEnum Parse<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
