using System.ComponentModel.DataAnnotations;

namespace NLTSQL.QueryEngine.Execution;

/// <summary>The database engines a data source can point at.</summary>
public enum DataSourceProvider
{
    /// <summary>PostgreSQL, via Npgsql.</summary>
    PostgreSql,

    /// <summary>Oracle, via ODP.NET.</summary>
    Oracle,
}

/// <summary>One configured target database.</summary>
public sealed class DataSourceDefinition
{
    /// <summary>Which engine this points at.</summary>
    [Required]
    public DataSourceProvider Provider { get; set; }

    /// <summary>
    /// How to connect.
    /// </summary>
    /// <remarks>
    /// This must name an account with read access and nothing more. The engine's own permissions
    /// are the outer boundary; everything the platform does — parameterised statements, row
    /// policies, read-only transactions — sits inside it. A connection string with write rights
    /// makes all of that a matter of the platform having no bugs, which is a far weaker promise.
    /// </remarks>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Human-facing name shown in the admin UI.</summary>
    public string? Label { get; set; }
}

/// <summary>The configured target databases, keyed by the name models refer to.</summary>
public sealed class DataSourceOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Nltsql:DataSources";

    /// <summary>Data source name to definition. A semantic model's <c>data_source</c> names one of these.</summary>
    public Dictionary<string, DataSourceDefinition> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);
}
