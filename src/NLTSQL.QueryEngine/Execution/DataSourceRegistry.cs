using System.Data.Common;
using Microsoft.Extensions.Options;
using Npgsql;
using NLTSQL.QueryEngine.Sql;
using Oracle.ManagedDataAccess.Client;

namespace NLTSQL.QueryEngine.Execution;

/// <summary>A target database, resolved to a connection factory and the dialect that matches it.</summary>
/// <param name="Name">The name semantic models refer to.</param>
/// <param name="Provider">Which engine this is.</param>
/// <param name="Dialect">The dialect the compiler must use for this source.</param>
public sealed record ResolvedDataSource(string Name, DataSourceProvider Provider, ISqlDialect Dialect)
{
    /// <summary>Creates a new, unopened connection.</summary>
    public required Func<DbConnection> CreateConnection { get; init; }
}

/// <summary>Resolves a data source name to a connection factory and its dialect.</summary>
public interface IDataSourceRegistry
{
    /// <summary>Names of every configured source.</summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>Resolves <paramref name="name"/>.</summary>
    /// <exception cref="InvalidOperationException">No such data source is configured.</exception>
    ResolvedDataSource Resolve(string name);
}

/// <summary>
/// Binds configured data sources to the driver and dialect for their engine.
/// </summary>
/// <remarks>
/// Pairing the dialect with the connection here, rather than letting callers choose one, removes a
/// whole class of mistake: it is not possible to compile Oracle SQL and run it against PostgreSQL
/// because the two come from the same lookup.
/// </remarks>
public sealed class DataSourceRegistry(IOptions<DataSourceOptions> options) : IDataSourceRegistry
{
    private readonly DataSourceOptions options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Names => this.options.Sources.Keys;

    /// <inheritdoc/>
    public ResolvedDataSource Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!this.options.Sources.TryGetValue(name, out var definition))
        {
            throw new InvalidOperationException(
                $"No data source named '{name}' is configured. Configured: " +
                (this.options.Sources.Count == 0 ? "(none)" : string.Join(", ", this.options.Sources.Keys.Order(StringComparer.Ordinal))) +
                ".");
        }

        var connectionString = definition.ConnectionString;

        return definition.Provider switch
        {
            DataSourceProvider.PostgreSql => new ResolvedDataSource(name, definition.Provider, PostgreSqlDialect.Instance)
            {
                CreateConnection = () => new NpgsqlConnection(connectionString),
            },
            DataSourceProvider.Oracle => new ResolvedDataSource(name, definition.Provider, OracleDialect.Instance)
            {
                CreateConnection = () => new OracleConnection(connectionString),
            },
            _ => throw new InvalidOperationException($"Data source '{name}' has an unsupported provider '{definition.Provider}'."),
        };
    }
}
