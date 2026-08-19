using System.ComponentModel.DataAnnotations;
using Nltsql.Core.Charts;
using Nltsql.Core.Queries;

namespace Nltsql.Infrastructure.Persistence;

/// <summary>
/// A query a user chose to keep.
/// </summary>
/// <remarks>
/// What is stored is the semantic query, not its result. Opening a saved
/// query re-executes it, so a dashboard always shows current data — and
/// stays correct when the metric definition behind it changes.
/// </remarks>
public sealed class SavedQuery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>The original question, when the query came from the NL box.</summary>
    [MaxLength(1000)]
    public string? Question { get; set; }

    /// <summary>Serialised <see cref="SemanticQuery"/>.</summary>
    public string QueryJson { get; set; } = string.Empty;

    public ChartType ChartType { get; set; } = ChartType.Table;

    /// <summary>Metabase question backing this query, once published.</summary>
    public int? MetabaseCardId { get; set; }

    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<DashboardTile> Tiles { get; set; } = [];

    public SemanticQuery? ToQuery() => SemanticQuerySerializer.Deserialize(QueryJson);
}

/// <summary>A named collection of saved queries.</summary>
public sealed class Dashboard
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<DashboardTile> Tiles { get; set; } = [];
}

public sealed class DashboardTile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DashboardId { get; set; }

    public Dashboard? Dashboard { get; set; }

    public Guid SavedQueryId { get; set; }

    public SavedQuery? SavedQuery { get; set; }

    /// <summary>Sort order within the dashboard.</summary>
    public int Position { get; set; }

    /// <summary>Tile width in twelfths, so tiles can sit side by side.</summary>
    public int Width { get; set; } = 6;
}
