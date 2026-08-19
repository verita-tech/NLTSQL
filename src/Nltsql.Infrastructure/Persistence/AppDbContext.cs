using Microsoft.EntityFrameworkCore;

namespace Nltsql.Infrastructure.Persistence;

/// <summary>
/// Application store for saved queries and dashboards.
/// </summary>
/// <remarks>
/// This database holds only the app's own objects. No warehouse data
/// ever lands here — results are fetched from the semantic layer on
/// every view, which is what keeps a saved tile honest.
/// </remarks>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SavedQuery> SavedQueries => Set<SavedQuery>();

    public DbSet<Dashboard> Dashboards => Set<Dashboard>();

    public DbSet<DashboardTile> DashboardTiles => Set<DashboardTile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SavedQuery>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Every read is tenant-scoped; nothing is ever fetched by id alone.
            entity.HasIndex(e => new { e.TenantId, e.UpdatedAt });
            entity.Property(e => e.ChartType).HasConversion<string>();
        });

        modelBuilder.Entity<Dashboard>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Name });
        });

        modelBuilder.Entity<DashboardTile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DashboardId, e.Position });

            entity.HasOne(e => e.Dashboard)
                  .WithMany(d => d.Tiles)
                  .HasForeignKey(e => e.DashboardId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Removing a saved query must not leave a tile pointing at
            // nothing, so the tile goes with it.
            entity.HasOne(e => e.SavedQuery)
                  .WithMany(q => q.Tiles)
                  .HasForeignKey(e => e.SavedQueryId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
