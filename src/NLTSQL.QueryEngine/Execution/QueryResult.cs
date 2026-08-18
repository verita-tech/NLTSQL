using NLTSQL.QueryEngine.Sql;

namespace NLTSQL.QueryEngine.Execution;

/// <summary>The answer to a question.</summary>
/// <param name="Columns">Description of each column, carried over from the compiled query.</param>
/// <param name="Rows">The rows, each holding one value per column in <paramref name="Columns"/> order.</param>
/// <param name="Truncated">
/// Whether the row cap cut the result short. Shown to the user rather than kept internal: a chart
/// built from a silently truncated result is a wrong chart that looks entirely convincing.
/// </param>
/// <param name="Duration">How long the statement took, for the audit record.</param>
public sealed record QueryResult(
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    bool Truncated,
    TimeSpan Duration)
{
    /// <summary>Number of rows returned.</summary>
    public int RowCount => this.Rows.Count;

    /// <summary>Whether the result has exactly one row and one column.</summary>
    /// <remarks>Used to decide whether an answer is a single figure rather than a table or chart.</remarks>
    public bool IsScalar => this.Rows.Count == 1 && this.Columns.Count == 1;
}
