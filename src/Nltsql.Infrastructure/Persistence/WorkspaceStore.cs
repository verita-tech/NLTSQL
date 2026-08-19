using Microsoft.EntityFrameworkCore;
using Nltsql.Core.Abstractions;
using Nltsql.Core.Charts;
using Nltsql.Core.Queries;

namespace Nltsql.Infrastructure.Persistence;

/// <summary>
/// Reads and writes the user's saved queries and dashboards.
/// </summary>
/// <remarks>
/// Every query in here is filtered by tenant. The filter is applied in
/// this class rather than left to callers, so a missing <c>where</c> in
/// a page cannot leak another customer's dashboard.
/// </remarks>
public sealed class WorkspaceStore(AppDbContext db, ITenantContext tenant, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<SavedQuery>> GetSavedQueriesAsync(CancellationToken cancellationToken = default) =>
        await db.SavedQueries
            .Where(q => q.TenantId == tenant.TenantId)
            .OrderByDescending(q => q.UpdatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<SavedQuery?> GetSavedQueryAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.SavedQueries
            .Where(q => q.TenantId == tenant.TenantId && q.Id == id)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<SavedQuery> SaveQueryAsync(
        SaveQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = timeProvider.GetUtcNow();

        var entity = request.Id is { } id
            ? await db.SavedQueries
                .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == tenant.TenantId, cancellationToken)
                .ConfigureAwait(false)
            : null;

        if (entity is null)
        {
            entity = new SavedQuery
            {
                TenantId = tenant.TenantId,
                CreatedBy = tenant.UserName,
                CreatedAt = now,
            };

            db.SavedQueries.Add(entity);
        }

        entity.Title = request.Title;
        entity.Description = request.Description;
        entity.Question = request.Question;
        entity.QueryJson = SemanticQuerySerializer.Serialize(request.Query);
        entity.ChartType = request.ChartType;
        entity.MetabaseCardId = request.MetabaseCardId ?? entity.MetabaseCardId;
        entity.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entity;
    }

    public async Task DeleteSavedQueryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await db.SavedQueries
            .Where(q => q.Id == id && q.TenantId == tenant.TenantId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Dashboard>> GetDashboardsAsync(CancellationToken cancellationToken = default) =>
        await db.Dashboards
            .Where(d => d.TenantId == tenant.TenantId)
            .OrderBy(d => d.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Loads a dashboard with its tiles and their saved queries.</summary>
    public async Task<Dashboard?> GetDashboardAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Dashboards
            .Where(d => d.Id == id && d.TenantId == tenant.TenantId)
            .Include(d => d.Tiles.OrderBy(t => t.Position))
                .ThenInclude(t => t.SavedQuery)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<Dashboard> CreateDashboardAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var dashboard = new Dashboard
        {
            TenantId = tenant.TenantId,
            Name = name,
            Description = description,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        db.Dashboards.Add(dashboard);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return dashboard;
    }

    /// <summary>Appends a saved query to a dashboard, ignoring duplicates.</summary>
    public async Task AddTileAsync(Guid dashboardId, Guid savedQueryId, CancellationToken cancellationToken = default)
    {
        var dashboard = await db.Dashboards
            .Include(d => d.Tiles)
            .FirstOrDefaultAsync(d => d.Id == dashboardId && d.TenantId == tenant.TenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Das Dashboard wurde nicht gefunden.");

        if (dashboard.Tiles.Any(t => t.SavedQueryId == savedQueryId))
        {
            return;
        }

        // Added through the set rather than the navigation collection, so
        // the intent to insert is explicit rather than inferred.
        db.DashboardTiles.Add(new DashboardTile
        {
            DashboardId = dashboardId,
            SavedQueryId = savedQueryId,
            Position = dashboard.Tiles.Count == 0 ? 0 : dashboard.Tiles.Max(t => t.Position) + 1,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveTileAsync(Guid tileId, CancellationToken cancellationToken = default)
    {
        await db.DashboardTiles
            .Where(t => t.Id == tileId && t.Dashboard!.TenantId == tenant.TenantId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteDashboardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await db.Dashboards
            .Where(d => d.Id == id && d.TenantId == tenant.TenantId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed record SaveQueryRequest
{
    /// <summary>Set to update an existing saved query.</summary>
    public Guid? Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public string? Question { get; init; }

    public required SemanticQuery Query { get; init; }

    public ChartType ChartType { get; init; } = ChartType.Table;

    public int? MetabaseCardId { get; init; }
}
