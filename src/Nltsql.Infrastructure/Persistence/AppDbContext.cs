using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

        ApplyDateTimeOffsetConversion(modelBuilder);
        ApplyClientGeneratedKeys(modelBuilder);

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

    /// <summary>
    /// Declares Guid keys as assigned by the application.
    /// </summary>
    /// <remarks>
    /// The entities set their own <c>Guid.NewGuid()</c> in a property
    /// initializer, but EF's convention marks a Guid key as
    /// store-generated. Change tracking then reads a non-default key on
    /// a brand-new entity as "this row already exists" and emits an
    /// UPDATE instead of an INSERT — which fails silently as a
    /// zero-rows-affected concurrency error the first time an entity is
    /// added through a navigation collection rather than through Add().
    /// </remarks>
    private static void ApplyClientGeneratedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();

            if (key?.Properties is [{ ClrType: var clrType } property] && clrType == typeof(Guid))
            {
                property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }

    /// <summary>
    /// Stores <see cref="DateTimeOffset"/> in a form SQLite can order by.
    /// </summary>
    /// <remarks>
    /// SQLite has no native offset-aware timestamp, so EF refuses to
    /// translate an ORDER BY over one — which is exactly what the saved
    /// query list does. The binary converter keeps the offset and encodes
    /// the value so that byte order matches chronological order.
    /// <para>
    /// Applied by convention rather than per property, so a timestamp
    /// added to a future entity is covered without anyone remembering.
    /// </para>
    /// </remarks>
    private static void ApplyDateTimeOffsetConversion(ModelBuilder modelBuilder)
    {
        var converter = new DateTimeOffsetToBinaryConverter();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(converter);
                }
            }
        }
    }
}
