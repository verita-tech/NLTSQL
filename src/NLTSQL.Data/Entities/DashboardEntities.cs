using System.ComponentModel.DataAnnotations;

namespace NLTSQL.Data.Entities;

// Timestamps are DateTime in UTC rather than DateTimeOffset. SQLite refuses to ORDER BY a
// DateTimeOffset, and since every timestamp here is produced by the server in UTC the offset was
// always zero — it carried no information and cost a whole class of query failure. Anything read
// back is UTC by construction; the UI marks the kind before converting for display.

/// <summary>How a dashboard tile gets its numbers.</summary>
public enum TileMode
{
    /// <summary>Re-run the query each time the dashboard is opened.</summary>
    Live,

    /// <summary>Show the result frozen at the moment the tile was saved.</summary>
    Snapshot,
}

/// <summary>A question worth keeping.</summary>
/// <remarks>
/// What is stored is the <em>query</em>, not its answer. A dashboard whose tiles hold last month's
/// numbers is worse than no dashboard, because it looks current. The original question is kept
/// alongside so a tile can still be read as the thing somebody actually asked, and the model
/// version is stamped on so a later change to the semantic model is traceable rather than silent.
/// </remarks>
public sealed class SavedQuery
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Heading shown on the tile.</summary>
    [MaxLength(200)]
    public required string Title { get; set; }

    /// <summary>The question as it was typed.</summary>
    [MaxLength(2000)]
    public required string Question { get; set; }

    /// <summary>Which semantic model it was asked against.</summary>
    [MaxLength(100)]
    public required string ModelName { get; set; }

    /// <summary>The model's version at the time, so a later change is traceable.</summary>
    public int ModelVersion { get; set; }

    /// <summary>The structured query, serialised.</summary>
    public required string QuerySpecJson { get; set; }

    /// <summary>The chosen presentation, serialised.</summary>
    public string? ChartSpecJson { get; set; }

    /// <summary>When it was saved, in UTC.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who saved it.</summary>
    [MaxLength(256)]
    public string? CreatedBy { get; set; }
}

/// <summary>A named collection of tiles.</summary>
public sealed class Dashboard
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Name shown in the list and as the page heading.</summary>
    [MaxLength(200)]
    public required string Title { get; set; }

    /// <summary>When it was created, in UTC.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who created it.</summary>
    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    /// <summary>The tiles, in display order.</summary>
    public ICollection<DashboardTile> Tiles { get; } = [];
}

/// <summary>One saved query placed on a dashboard.</summary>
public sealed class DashboardTile
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The dashboard this belongs to.</summary>
    public Guid DashboardId { get; set; }

    /// <summary>Navigation to the dashboard.</summary>
    public Dashboard? Dashboard { get; set; }

    /// <summary>The query this tile shows.</summary>
    public Guid SavedQueryId { get; set; }

    /// <summary>Navigation to the saved query.</summary>
    public SavedQuery? SavedQuery { get; set; }

    /// <summary>Display order within the dashboard.</summary>
    public int Position { get; set; }

    /// <summary>How wide the tile is, in grid columns.</summary>
    public int ColumnSpan { get; set; } = 1;

    /// <summary>Whether the tile re-runs or shows a frozen result.</summary>
    public TileMode Mode { get; set; } = TileMode.Live;

    /// <summary>
    /// The frozen result, for a snapshot tile.
    /// </summary>
    /// <remarks>
    /// Only set for <see cref="TileMode.Snapshot"/>. Reproducibility occasionally matters more than
    /// currency — a figure quoted in a report should keep saying what it said — but it is the
    /// exception, which is why live is the default and a snapshot always shows its date.
    /// </remarks>
    public string? SnapshotJson { get; set; }

    /// <summary>When the snapshot was taken, in UTC.</summary>
    public DateTime? SnapshotTakenAt { get; set; }
}

/// <summary>
/// One execution, recorded.
/// </summary>
/// <remarks>
/// Serves two purposes deliberately. It is the audit trail — who asked what, which SQL ran, how
/// long it took, whether it failed — and it is what CSV export re-runs from, so an export is
/// always traceable to a recorded question rather than to an opaque handle.
/// </remarks>
public sealed class QueryRun
{
    /// <summary>Primary key. Doubles as the export handle.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>When the question was asked, in UTC.</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who asked.</summary>
    [MaxLength(256)]
    public string? UserId { get; set; }

    /// <summary>Which semantic model was used.</summary>
    [MaxLength(100)]
    public required string ModelName { get; set; }

    /// <summary>The question as typed.</summary>
    [MaxLength(2000)]
    public required string Question { get; set; }

    /// <summary>The structured query, when one was produced.</summary>
    public string? QuerySpecJson { get; set; }

    /// <summary>The statement that ran, with parameters as placeholders.</summary>
    public string? Sql { get; set; }

    /// <summary>How many rows came back.</summary>
    public int? RowCount { get; set; }

    /// <summary>How long the statement took.</summary>
    public double DurationMs { get; set; }

    /// <summary>How many model calls it took to plan.</summary>
    public int Attempts { get; set; }

    /// <summary>Whether the question was answered.</summary>
    public bool Success { get; set; }

    /// <summary>Why it was not, if it was not.</summary>
    [MaxLength(4000)]
    public string? Error { get; set; }
}
