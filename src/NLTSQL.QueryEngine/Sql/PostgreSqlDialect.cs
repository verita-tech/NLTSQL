using System.Globalization;
using System.Text;
using NLTSQL.Core.Query;

namespace NLTSQL.QueryEngine.Sql;

/// <summary>PostgreSQL rendering rules.</summary>
public sealed class PostgreSqlDialect : ISqlDialect
{
    /// <summary>A shared instance; the dialect holds no state.</summary>
    public static PostgreSqlDialect Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "postgresql";

    /// <inheritdoc/>
    public string QuoteIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        // Identifiers reach here only from the semantic model, never from a user or a language
        // model, but doubling embedded quotes costs nothing and removes the question entirely.
        return '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    /// <inheritdoc/>
    public string ParameterReference(string name) => "@" + name;

    /// <inheritdoc/>
    public string TruncateToGrain(string expression, TimeGrain grain)
    {
        var unit = grain switch
        {
            TimeGrain.Day => "day",
            TimeGrain.Week => "week",
            TimeGrain.Month => "month",
            TimeGrain.Quarter => "quarter",
            TimeGrain.Year => "year",
            _ => null,
        };

        return unit is null ? expression : $"date_trunc('{unit}', {expression})";
    }

    /// <inheritdoc/>
    public void AppendRowLimit(StringBuilder sql, int limit)
    {
        ArgumentNullException.ThrowIfNull(sql);
        sql.Append(CultureInfo.InvariantCulture, $"\nLIMIT {limit}");
    }
}
