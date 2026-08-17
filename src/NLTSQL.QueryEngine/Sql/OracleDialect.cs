using System.Globalization;
using System.Text;
using NLTSQL.Core.Query;

namespace NLTSQL.QueryEngine.Sql;

/// <summary>Oracle rendering rules.</summary>
public sealed class OracleDialect : ISqlDialect
{
    /// <summary>A shared instance; the dialect holds no state.</summary>
    public static OracleDialect Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "oracle";

    /// <inheritdoc/>
    public string QuoteIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    /// <inheritdoc/>
    public string ParameterReference(string name) => ":" + name;

    /// <inheritdoc/>
    public string TruncateToGrain(string expression, TimeGrain grain)
    {
        // TRUNC's format models are not the same words PostgreSQL uses, and 'IW' rather than 'W'
        // is what makes Oracle weeks ISO weeks — matching date_trunc('week', …), which starts on
        // Monday. Getting that wrong would shift every weekly figure by up to six days on one
        // engine only, which is exactly the kind of divergence nobody notices until a customer does.
        var model = grain switch
        {
            TimeGrain.Day => "'DDD'",
            TimeGrain.Week => "'IW'",
            TimeGrain.Month => "'MM'",
            TimeGrain.Quarter => "'Q'",
            TimeGrain.Year => "'YYYY'",
            _ => null,
        };

        return model is null ? expression : $"TRUNC({expression}, {model})";
    }

    /// <inheritdoc/>
    public void AppendRowLimit(StringBuilder sql, int limit)
    {
        ArgumentNullException.ThrowIfNull(sql);
        sql.Append(CultureInfo.InvariantCulture, $"\nFETCH FIRST {limit} ROWS ONLY");
    }
}
