using NLTSQL.Core.Query;
using NLTSQL.Semantics.Model;

namespace NLTSQL.QueryEngine;

/// <summary>A value produced in the SELECT list.</summary>
/// <param name="Name">Machine name, also the column alias.</param>
/// <param name="Label">Human-facing heading.</param>
/// <param name="Format">How to render it.</param>
public abstract record SelectedField(string Name, string Label, ValueFormat Format);

/// <summary>An aggregate over the queried entity.</summary>
public sealed record SelectedMeasure(Measure Measure)
    : SelectedField(Measure.Name, Measure.Label, Measure.Format);

/// <summary>Arithmetic over already-aggregated measures.</summary>
/// <param name="Metric">The metric definition.</param>
/// <param name="Dependencies">
/// The measures the metric's expression names, resolved. They are computed as part of the same
/// aggregation whether or not the question asked for them, because the metric is arithmetic over
/// their aggregates and cannot be evaluated row by row.
/// </param>
public sealed record SelectedMetric(Metric Metric, IReadOnlyList<Measure> Dependencies)
    : SelectedField(Metric.Name, Metric.Label, Metric.Format);

/// <summary>A grouping column.</summary>
/// <param name="Name">Machine name, also the column alias.</param>
/// <param name="Label">Human-facing heading.</param>
/// <param name="Column">Physical column.</param>
/// <param name="DataType">Logical type of the column.</param>
/// <param name="Grain">Time bucket, or <see cref="TimeGrain.None"/> for a plain dimension.</param>
public sealed record SelectedGrouping(string Name, string Label, string Column, DataType DataType, TimeGrain Grain);

/// <summary>A filter with its operands already converted to CLR values.</summary>
/// <param name="Column">Physical column.</param>
/// <param name="DataType">Logical type of the column.</param>
/// <param name="Operator">The comparison.</param>
/// <param name="Values">
/// Converted operands. Every one is bound as a parameter; none is ever rendered into SQL text.
/// </param>
public sealed record ResolvedFilter(string Column, DataType DataType, FilterOperator Operator, IReadOnlyList<object?> Values);

/// <summary>
/// A row policy applied to this query.
/// </summary>
/// <remarks>
/// Carried separately from ordinary filters so it cannot be confused with one. The value is not
/// held here at all — it is supplied by the server at execution time from the authenticated
/// principal, never from the query spec and never from the language model.
/// </remarks>
/// <param name="Column">Physical discriminator column.</param>
/// <param name="Parameter">Name of the runtime-supplied value.</param>
/// <param name="MultiValued">Whether the value is a set of permitted values.</param>
public sealed record PolicyFilter(string Column, string Parameter, bool MultiValued);

/// <summary>A sort key, expressed against an output alias.</summary>
/// <param name="Alias">Alias of a selected field or grouping.</param>
/// <param name="Direction">Sort direction.</param>
public sealed record ResolvedOrderBy(string Alias, SortDirection Direction);

/// <summary>
/// A query spec with every name resolved against the semantic model.
/// </summary>
/// <remarks>
/// Reaching this type means every name existed, every operand converted, and every applicable row
/// policy was attached. The compiler that turns this into SQL therefore has no decisions left that
/// could fail — it only has to render, which is what keeps the dialect implementations small
/// enough to be trustworthy.
/// </remarks>
public sealed record ResolvedQuery
{
    /// <summary>The model this query was resolved against.</summary>
    public required SemanticModel Model { get; init; }

    /// <summary>The queried entity.</summary>
    public required Entity Entity { get; init; }

    /// <summary>Values to compute.</summary>
    public required IReadOnlyList<SelectedField> Fields { get; init; }

    /// <summary>Grouping columns.</summary>
    public IReadOnlyList<SelectedGrouping> Groupings { get; init; } = [];

    /// <summary>Conditions from the question, combined with AND.</summary>
    public IReadOnlyList<ResolvedFilter> Filters { get; init; } = [];

    /// <summary>Conditions imposed by the platform, also ANDed in.</summary>
    public IReadOnlyList<PolicyFilter> PolicyFilters { get; init; } = [];

    /// <summary>Sort keys.</summary>
    public IReadOnlyList<ResolvedOrderBy> OrderBy { get; init; } = [];

    /// <summary>Effective row cap, already clamped to the configured maximum.</summary>
    public required int Limit { get; init; }

    /// <summary>
    /// The instant relative time filters are measured back from.
    /// </summary>
    /// <remarks>
    /// Captured once per query rather than read from the clock while rendering, so that "the last
    /// twelve months" means the same thing in the generated SQL, in the audit record and in a test.
    /// </remarks>
    public required DateTimeOffset ReferenceTime { get; init; }
}
