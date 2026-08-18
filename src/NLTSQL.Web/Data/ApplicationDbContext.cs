using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NLTSQL.Data;
using NLTSQL.Data.Entities;

namespace NLTSQL.Web.Data;

/// <summary>
/// The application's own store: Identity plus dashboards, saved queries and the audit trail.
/// </summary>
/// <remarks>
/// One SQLite file rather than two. The application's data is small and write-light, and a single
/// file means one backup, one migration history and no distributed transaction between two stores
/// that always change together. The target databases are untouched by any of this — they only ever
/// see read traffic.
/// </remarks>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), INltsqlDataContext
{
    /// <inheritdoc/>
    public DbSet<SavedQuery> SavedQueries => this.Set<SavedQuery>();

    /// <inheritdoc/>
    public DbSet<Dashboard> Dashboards => this.Set<Dashboard>();

    /// <inheritdoc/>
    public DbSet<DashboardTile> DashboardTiles => this.Set<DashboardTile>();

    /// <inheritdoc/>
    public DbSet<QueryRun> QueryRuns => this.Set<QueryRun>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureNltsql();
    }
}
