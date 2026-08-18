using Microsoft.Extensions.Logging;
using NLTSQL.Ai.Planning;
using NLTSQL.Core.Charting;
using NLTSQL.Core.Query;
using NLTSQL.QueryEngine.Execution;
using NLTSQL.QueryEngine.Sql;
using NLTSQL.Semantics;
using NLTSQL.Semantics.Validation;

namespace NLTSQL.Ai;

/// <summary>Everything one question produced.</summary>
/// <param name="Question">What was asked.</param>
/// <param name="ModelName">Which semantic model it was asked against.</param>
/// <param name="ModelVersion">Which version of that model, so a saved query can be traced later.</param>
/// <param name="Spec">The structured query the model produced, shown so the answer can be checked.</param>
/// <param name="Sql">The generated statement, shown for the same reason.</param>
/// <param name="Result">The rows, when it ran.</param>
/// <param name="Chart">How to draw them.</param>
/// <param name="Issues">Why it did not run, if it did not.</param>
/// <param name="Attempts">How many model calls it took.</param>
public sealed record AskResult(
    string Question,
    string ModelName,
    int ModelVersion,
    QuerySpec? Spec,
    string? Sql,
    QueryResult? Result,
    ChartSpec? Chart,
    IReadOnlyList<ValidationIssue> Issues,
    int Attempts)
{
    /// <summary>Whether there is an answer to show.</summary>
    public bool Success => this.Result is not null;
}

/// <summary>
/// Answers a question end to end: plan, compile, run, choose a presentation.
/// </summary>
/// <remarks>
/// The spec and the SQL are returned even on success, and the UI shows them. That is not a debug
/// affordance — it is what lets a domain expert see that "revenue" meant the measure they think it
/// means. A number with no visible derivation is exactly the thing people are right not to trust.
/// </remarks>
public sealed class AskService(
    QueryPlanner planner,
    ISemanticModelRegistry models,
    SpecRunner runner,
    ILogger<AskService> logger)
{
    /// <summary>Answers <paramref name="question"/> against the named semantic model.</summary>
    public async Task<AskResult> AskAsync(
        string question,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        var model = models.GetModel(modelName);
        var plan = await planner.PlanAsync(question, model, cancellationToken).ConfigureAwait(false);

        if (!plan.Success)
        {
            return new AskResult(question, model.Name, model.Version, plan.Spec, null, null, null, plan.Issues, plan.Attempts);
        }

        // Execution goes through the same path a dashboard tile takes, so a saved tile cannot
        // diverge from the answer that was originally shown.
        var run = await runner.RunAsync(plan.Spec!, model.Name, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!run.Success)
        {
            return new AskResult(question, model.Name, model.Version, plan.Spec, run.Sql, null, null, run.Issues, plan.Attempts);
        }

        var result = run.Result!;
        logger.QueryAnswered(model.Name, plan.Attempts, result.RowCount, result.Duration.TotalMilliseconds);

        var chart = ChartAdvisor.Choose(
            [.. result.Columns.Select(c => new ChartColumn(c.Alias, c.Kind is ResultColumnKind.Grouping, c.Grain))],
            result.RowCount);

        return new AskResult(
            question,
            model.Name,
            model.Version,
            plan.Spec,
            run.Sql,
            result,
            chart,
            [],
            plan.Attempts);
    }
}

/// <summary>Source-generated logging for the ask pipeline.</summary>
internal static partial class AskServiceLog
{
    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Information,
        Message = "Answered a question against '{Model}' in {Attempts} attempt(s): {RowCount} row(s) in {ElapsedMs:F0} ms.")]
    public static partial void QueryAnswered(this ILogger logger, string model, int attempts, int rowCount, double elapsedMs);
}
