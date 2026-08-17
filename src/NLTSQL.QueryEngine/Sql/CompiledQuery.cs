using NLTSQL.Core.Query;
using NLTSQL.Semantics.Model;

namespace NLTSQL.QueryEngine.Sql;

/// <summary>What a result column represents.</summary>
public enum ResultColumnKind
{
    /// <summary>A grouping key.</summary>
    Grouping,

    /// <summary>An aggregate.</summary>
    Measure,

    /// <summary>Arithmetic over aggregates.</summary>
    Metric,
}

/// <summary>Describes one column of a result set.</summary>
/// <param name="Alias">Column alias in the statement, and the key in each result row.</param>
/// <param name="Label">Human-facing heading.</param>
/// <param name="Kind">What the column represents.</param>
/// <param name="DataType">Logical type, for grouping columns.</param>
/// <param name="Grain">Time bucket applied, for time groupings.</param>
/// <param name="Format">How to render the value, for measures and metrics.</param>
/// <remarks>
/// Carried alongside the SQL so that formatting, chart selection and CSV export all read the same
/// description of the result rather than each re-deriving it from the raw reader.
/// </remarks>
public sealed record ResultColumn(
    string Alias,
    string Label,
    ResultColumnKind Kind,
    DataType? DataType = null,
    TimeGrain Grain = TimeGrain.None,
    ValueFormat? Format = null);

/// <summary>A value bound to the statement.</summary>
/// <param name="Name">Parameter name without any dialect prefix.</param>
/// <param name="Value">The value, already converted to a CLR type.</param>
public sealed record QueryParameter(string Name, object? Value);

/// <summary>A statement ready to execute.</summary>
/// <param name="Sql">The statement text. Contains no user or model-supplied values, only parameter references.</param>
/// <param name="Parameters">Every value the statement needs.</param>
/// <param name="Columns">Description of the result set.</param>
/// <param name="Dialect">Name of the dialect that produced it.</param>
public sealed record CompiledQuery(
    string Sql,
    IReadOnlyList<QueryParameter> Parameters,
    IReadOnlyList<ResultColumn> Columns,
    string Dialect);

/// <summary>
/// The values the platform supplies to row policies.
/// </summary>
/// <remarks>
/// These come from the authenticated principal. They are passed to the compiler separately from
/// the query so there is no code path by which a query spec — and therefore the language model —
/// could set or overwrite one.
/// </remarks>
/// <param name="PolicyValues">Policy parameter name to value.</param>
public sealed record QueryExecutionContext(IReadOnlyDictionary<string, object?> PolicyValues)
{
    /// <summary>An empty context, for models that declare no row policies.</summary>
    public static QueryExecutionContext Empty { get; } = new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
}
