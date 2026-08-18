using Microsoft.Extensions.Options;
using NLTSQL.Core.Query;
using NLTSQL.QueryEngine;
using NLTSQL.QueryEngine.Execution;
using NLTSQL.QueryEngine.Sql;
using NLTSQL.Semantics;
using NLTSQL.Semantics.Validation;

namespace NLTSQL.Ai;

/// <summary>What running a stored query produced.</summary>
/// <param name="Result">The rows, when it ran.</param>
/// <param name="Sql">The statement that ran.</param>
/// <param name="Issues">Why it did not run, if it did not.</param>
public sealed record SpecRunResult(QueryResult? Result, string? Sql, IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>Whether there is a result.</summary>
    public bool Success => this.Result is not null;
}

/// <summary>
/// Runs a query spec that already exists, without involving the language model.
/// </summary>
/// <remarks>
/// <para>
/// A dashboard tile and a CSV export both re-run a query somebody asked once. Sending the original
/// question back through the model each time would be slower, cost more, and — worse — could
/// quietly produce a different query than the one that was saved.
/// </para>
/// <para>
/// The spec is re-resolved against the current model rather than trusted as stored. A measure
/// removed from the semantic model since the tile was saved has to surface as a clear error, not
/// as SQL referring to something that no longer exists.
/// </para>
/// </remarks>
public sealed class SpecRunner(
    ISemanticModelRegistry models,
    IDataSourceRegistry dataSources,
    IQueryContextProvider contextProvider,
    IQueryExecutor executor,
    QuerySpecResolver resolver,
    IOptions<QueryLimits> limits)
{
    private readonly QueryLimits limits = limits?.Value ?? throw new ArgumentNullException(nameof(limits));

    /// <summary>Runs <paramref name="spec"/> against the named semantic model.</summary>
    /// <param name="spec">The stored query.</param>
    /// <param name="modelName">Which semantic model it belongs to.</param>
    /// <param name="rowLimitOverride">A different row cap, still clamped to the configured maximum.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public async Task<SpecRunResult> RunAsync(
        QuerySpec spec,
        string modelName,
        int? rowLimitOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        var model = models.GetModel(modelName);

        var effective = rowLimitOverride is null
            ? spec
            : spec with { Limit = Math.Min(rowLimitOverride.Value, this.limits.MaxRowLimit) };

        var resolution = resolver.Resolve(effective, model);
        if (!resolution.Success)
        {
            return new SpecRunResult(null, null, resolution.Issues);
        }

        var query = resolution.Query!;
        var source = dataSources.Resolve(model.DataSource);
        var compiled = new SqlCompiler(source.Dialect).Compile(query, contextProvider.Current);

        var result = await executor
            .ExecuteAsync(compiled, model.DataSource, query.Limit, cancellationToken)
            .ConfigureAwait(false);

        return new SpecRunResult(result, compiled.Sql, []);
    }
}
