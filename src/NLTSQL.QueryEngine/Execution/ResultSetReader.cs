using System.Data.Common;
using NLTSQL.QueryEngine.Sql;

namespace NLTSQL.QueryEngine.Execution;

/// <summary>
/// Turns a data reader into rows.
/// </summary>
/// <remarks>
/// Split out from <see cref="QueryExecutor"/> so the part with actual logic — null handling, the
/// row cap, reporting truncation — can be tested against a stub reader without a database. What is
/// left in the executor is connection plumbing with no branches worth asserting on.
/// </remarks>
public static class ResultSetReader
{
    /// <summary>Reads at most <paramref name="maxRows"/> rows.</summary>
    /// <param name="reader">An open reader positioned before the first row.</param>
    /// <param name="columns">Expected columns, in select-list order.</param>
    /// <param name="maxRows">Row cap.</param>
    /// <param name="cancellationToken">Cancels a long read.</param>
    /// <returns>
    /// The rows, and whether the cap was reached. "Reached" rather than "exceeded": the statement
    /// itself carries the same limit, so a result of exactly <paramref name="maxRows"/> rows cannot
    /// be told apart from one the limit cut short. Reporting the boundary case as truncated errs
    /// towards warning the user needlessly, which is the harmless direction — a chart built from a
    /// silently shortened result is a wrong chart that looks entirely convincing.
    /// </returns>
    /// <exception cref="InvalidOperationException">The reader's shape does not match <paramref name="columns"/>.</exception>
    public static async Task<(IReadOnlyList<IReadOnlyList<object?>> Rows, bool Truncated)> ReadAsync(
        DbDataReader reader,
        IReadOnlyList<ResultColumn> columns,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);

        if (reader.FieldCount != columns.Count)
        {
            // The compiler produced both the statement and the column description, so a mismatch
            // means they have drifted apart. Reading positionally past that point would quietly
            // label values with the wrong headings.
            throw new InvalidOperationException(
                $"The statement returned {reader.FieldCount} columns but {columns.Count} were described.");
        }

        var rows = new List<IReadOnlyList<object?>>();

        // The cap is enforced here as well as in the statement. Belt and braces: a future code path
        // that compiles without a limit must not be able to pull an unbounded result into memory.
        while (rows.Count < maxRows && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                values[i] = await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetValue(i);
            }

            rows.Add(values);
        }

        return (rows, rows.Count >= maxRows);
    }
}
