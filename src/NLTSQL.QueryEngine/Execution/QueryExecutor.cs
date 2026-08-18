using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using NLTSQL.QueryEngine.Sql;

namespace NLTSQL.QueryEngine.Execution;

/// <summary>Runs a compiled query against its data source.</summary>
public interface IQueryExecutor
{
    /// <summary>Executes <paramref name="query"/> against the named data source.</summary>
    Task<QueryResult> ExecuteAsync(
        CompiledQuery query,
        string dataSourceName,
        int maxRows,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes compiled queries under a read-only transaction with a bounded runtime.
/// </summary>
/// <remarks>
/// <para>
/// Three independent things stop a question from harming the target database, and the platform
/// relies on all three rather than any one: the account it connects with has only read access, the
/// transaction is declared read-only, and the statement carries both a row cap and a timeout.
/// </para>
/// <para>
/// The read-only declaration is worth its two lines even though the account should already be
/// read-only. It is the layer that still holds when someone points a data source at an
/// over-privileged account by mistake, which is the realistic failure — a misconfigured connection
/// string is far likelier than a compiler that emits DML it has no code to produce.
/// </para>
/// </remarks>
public sealed class QueryExecutor(
    IDataSourceRegistry registry,
    IOptions<QueryLimits> limits,
    TimeProvider timeProvider) : IQueryExecutor
{
    private readonly QueryLimits limits = limits?.Value ?? throw new ArgumentNullException(nameof(limits));

    /// <inheritdoc/>
    public async Task<QueryResult> ExecuteAsync(
        CompiledQuery query,
        string dataSourceName,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);

        var source = registry.Resolve(dataSourceName);

        if (!string.Equals(source.Dialect.Name, query.Dialect, StringComparison.Ordinal))
        {
            // Refusing here rather than letting the engine reject the syntax: the statement might
            // well be accepted and mean something subtly different, which is the worse outcome.
            throw new InvalidOperationException(
                $"Query was compiled for '{query.Dialect}' but data source '{dataSourceName}' speaks '{source.Dialect.Name}'.");
        }

        var started = timeProvider.GetTimestamp();

        await using var connection = source.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        await DeclareReadOnlyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query.Sql;
        command.CommandTimeout = (int)this.limits.CommandTimeout.TotalSeconds;

        foreach (var parameter in query.Parameters)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = parameter.Name;
            dbParameter.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(dbParameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var (rows, truncated) = await ResultSetReader
            .ReadAsync(reader, query.Columns, maxRows, cancellationToken)
            .ConfigureAwait(false);

        // Nothing was written, so the rollback is only about not leaving a transaction open.
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

        return new QueryResult(
            query.Columns,
            rows,
            truncated,
            timeProvider.GetElapsedTime(started));
    }

    private static async Task DeclareReadOnlyAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        // Oracle and PostgreSQL happen to spell this identically, which is why it is here rather
        // than on ISqlDialect. Should a third engine ever disagree, it moves.
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SET TRANSACTION READ ONLY";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
