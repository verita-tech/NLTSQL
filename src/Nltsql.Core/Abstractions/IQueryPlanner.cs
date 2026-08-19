using Nltsql.Core.Queries;
using Nltsql.Core.Semantics;

namespace Nltsql.Core.Abstractions;

/// <summary>Turns a natural-language question into a semantic query.</summary>
public interface IQueryPlanner
{
    /// <summary>False when no model is configured, so the UI can hide the input.</summary>
    bool IsAvailable { get; }

    Task<QueryPlan> PlanAsync(string question, SemanticModel model, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of planning. A plan is only ever returned once it has passed
/// <see cref="SemanticQueryValidator"/> against the live model.
/// </summary>
public sealed record QueryPlan
{
    public SemanticQuery? Query { get; init; }

    /// <summary>
    /// Plain-language restatement of what the query actually asks, shown
    /// above the result so the user can tell whether they were understood
    /// before they trust the number.
    /// </summary>
    public string? Interpretation { get; init; }

    /// <summary>Set when the question could not be answered from the model.</summary>
    public string? Failure { get; init; }

    public IReadOnlyList<ValidationError> Errors { get; init; } = [];

    /// <summary>How many model round trips it took, for diagnostics.</summary>
    public int Attempts { get; init; }

    public bool IsSuccess => Query is not null;

    public static QueryPlan Failed(string reason, IReadOnlyList<ValidationError>? errors = null, int attempts = 0) =>
        new() { Failure = reason, Errors = errors ?? [], Attempts = attempts };
}
