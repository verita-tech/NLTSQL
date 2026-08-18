using Microsoft.EntityFrameworkCore;
using NLTSQL.Data.Entities;

namespace NLTSQL.Data;

/// <summary>
/// Reads and writes dashboards, saved queries and the audit trail.
/// </summary>
/// <remarks>
/// Each method opens and closes its own context. See <see cref="INltsqlDataContextFactory"/> for
/// why an injected one would be wrong in a Blazor Server circuit.
/// </remarks>
public sealed class DashboardStore(INltsqlDataContextFactory contexts)
{
    /// <summary>Every dashboard, newest first.</summary>
    public async Task<IReadOnlyList<Dashboard>> ListDashboardsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.CreateContext();

        return await context.Dashboards
            .AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>One dashboard with its tiles and their saved queries, in display order.</summary>
    public async Task<Dashboard?> GetDashboardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.CreateContext();

        return await context.Dashboards
            .AsNoTracking()
            .Include(d => d.Tiles.OrderBy(t => t.Position))
            .ThenInclude(t => t.SavedQuery)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Creates a dashboard.</summary>
    public async Task<Dashboard> CreateDashboardAsync(
        string title,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = contexts.CreateContext();

        var dashboard = new Dashboard { Title = title, CreatedBy = userId };
        context.Dashboards.Add(dashboard);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return dashboard;
    }

    /// <summary>Deletes a dashboard and its tiles. The saved queries survive.</summary>
    public async Task DeleteDashboardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.CreateContext();

        await context.Dashboards
            .Where(d => d.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Saves a query and places it on a dashboard.</summary>
    /// <param name="dashboardId">The dashboard to add to.</param>
    /// <param name="query">The query to save.</param>
    /// <param name="mode">Whether the tile re-runs or freezes its result.</param>
    /// <param name="snapshotJson">The frozen result, required for a snapshot tile.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task<DashboardTile> AddTileAsync(
        Guid dashboardId,
        SavedQuery query,
        TileMode mode,
        string? snapshotJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (mode is TileMode.Snapshot && string.IsNullOrWhiteSpace(snapshotJson))
        {
            throw new ArgumentException("A snapshot tile needs a frozen result.", nameof(snapshotJson));
        }

        await using var context = contexts.CreateContext();

        var nextPosition = await context.DashboardTiles
            .Where(t => t.DashboardId == dashboardId)
            .Select(t => (int?)t.Position)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? -1;

        var tile = new DashboardTile
        {
            DashboardId = dashboardId,
            SavedQueryId = query.Id,
            Position = nextPosition + 1,
            Mode = mode,
            SnapshotJson = mode is TileMode.Snapshot ? snapshotJson : null,
            SnapshotTakenAt = mode is TileMode.Snapshot ? DateTime.UtcNow : null,
        };

        context.SavedQueries.Add(query);
        context.DashboardTiles.Add(tile);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return tile;
    }

    /// <summary>Removes a tile. The saved query it pointed at is left alone.</summary>
    public async Task RemoveTileAsync(Guid tileId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.CreateContext();

        await context.DashboardTiles
            .Where(t => t.Id == tileId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Records an execution, and returns it so its id can serve as the export handle.</summary>
    public async Task<QueryRun> RecordRunAsync(QueryRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        // Written after the target-database query has finished, never around it. Holding a SQLite
        // write open across a call to another database would serialise every user's question
        // behind the slowest one.
        await using var context = contexts.CreateContext();

        context.QueryRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return run;
    }

    /// <summary>Looks up a recorded execution.</summary>
    public async Task<QueryRun?> GetRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.CreateContext();

        return await context.QueryRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }
}
