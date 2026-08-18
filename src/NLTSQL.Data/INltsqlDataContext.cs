using Microsoft.EntityFrameworkCore;
using NLTSQL.Data.Entities;

namespace NLTSQL.Data;

/// <summary>
/// The platform's own tables.
/// </summary>
/// <remarks>
/// Declared as an interface so the stores do not depend on the concrete context, which in this
/// application also carries the Identity schema. Keeping the two apart at the type level means the
/// dashboard code cannot accidentally reach into user records.
/// </remarks>
public interface INltsqlDataContext : IAsyncDisposable
{
    /// <summary>Saved queries.</summary>
    DbSet<SavedQuery> SavedQueries { get; }

    /// <summary>Dashboards.</summary>
    DbSet<Dashboard> Dashboards { get; }

    /// <summary>Tiles.</summary>
    DbSet<DashboardTile> DashboardTiles { get; }

    /// <summary>Recorded executions.</summary>
    DbSet<QueryRun> QueryRuns { get; }

    /// <summary>Persists pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates a short-lived context for a single operation.
/// </summary>
/// <remarks>
/// Blazor Server scopes a service to the circuit, not to a request, so an injected context would
/// live as long as the user's browser tab — accumulating tracked entities and, worse, being shared
/// by any two component operations that overlap. A context per operation is the documented way out,
/// and it costs nothing here because the work is short.
/// </remarks>
public interface INltsqlDataContextFactory
{
    /// <summary>Creates a context. The caller disposes it.</summary>
    INltsqlDataContext CreateContext();
}
