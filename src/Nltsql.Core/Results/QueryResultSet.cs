namespace Nltsql.Core.Results;

/// <summary>Tabular outcome of executing a semantic query.</summary>
/// <remarks>
/// Rows are positional arrays rather than dictionaries: a result set can
/// reach tens of thousands of rows, and one dictionary per row costs far
/// more than the column list it would repeat.
/// </remarks>
public sealed record QueryResultSet
{
    public required IReadOnlyList<ResultColumn> Columns { get; init; }

    public required IReadOnlyList<object?[]> Rows { get; init; }

    /// <summary>Server-side execution time, shown so slow queries are visible.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>True when Cube answered from a pre-aggregation.</summary>
    public bool FromPreAggregation { get; init; }

    /// <summary>
    /// True when the row cap truncated the result, so the UI can say so
    /// instead of silently showing a partial answer.
    /// </summary>
    public bool IsTruncated { get; init; }

    public static QueryResultSet Empty { get; } = new() { Columns = [], Rows = [] };

    public int RowCount => Rows.Count;
}

public sealed record ResultColumn(
    string Key,
    string Title,
    ResultColumnKind Kind,
    SemanticValueType ValueType,
    string? Format = null)
{
    /// <summary>Percent-formatted measures arrive from Cube as a 0..1 ratio.</summary>
    public bool IsPercent => string.Equals(Format, "percent", StringComparison.OrdinalIgnoreCase);
}

public enum ResultColumnKind
{
    Dimension,
    TimeDimension,
    Measure,
}

public enum SemanticValueType
{
    String,
    Number,
    Time,
    Boolean,
}
