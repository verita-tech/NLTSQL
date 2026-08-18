using System.Globalization;
using Microsoft.Extensions.Options;
using NLTSQL.QueryEngine.Sql;

namespace NLTSQL.QueryEngine.Execution;

/// <summary>Supplies the values row policies are bound to.</summary>
public interface IQueryContextProvider
{
    /// <summary>The context for the caller the current request belongs to.</summary>
    QueryExecutionContext Current { get; }
}

/// <summary>Row-policy values taken from configuration.</summary>
/// <remarks>
/// A prototype stand-in. The values belong to the authenticated principal, and the seam exists so
/// that swapping this implementation is the only change needed — nothing downstream knows where a
/// policy value came from, and the compiler still refuses to run a query whose policy has no value.
/// </remarks>
public sealed class QueryContextOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Nltsql:QueryContext";

    /// <summary>Policy parameter name to value, as written in configuration.</summary>
    public Dictionary<string, string> PolicyValues { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <inheritdoc cref="QueryContextOptions"/>
public sealed class ConfiguredQueryContextProvider : IQueryContextProvider
{
    private readonly QueryExecutionContext context;

    /// <summary>Creates a provider over the configured values.</summary>
    public ConfiguredQueryContextProvider(IOptions<QueryContextOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, raw) in options.Value.PolicyValues)
        {
            values[key] = Convert(raw);
        }

        this.context = new QueryExecutionContext(values);
    }

    /// <inheritdoc/>
    public QueryExecutionContext Current => this.context;

    /// <summary>
    /// Turns configuration text into the CLR type the column expects.
    /// </summary>
    /// <remarks>
    /// Configuration has no types, but the database does: binding the string "1" against an integer
    /// tenant column fails on both engines. Narrowing to the most specific type that fits is what
    /// makes a tenant id written as <c>"1"</c> in appsettings actually work.
    /// </remarks>
    private static object Convert(string raw)
    {
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        return bool.TryParse(raw, out var flag) ? flag : raw;
    }
}
