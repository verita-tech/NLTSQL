using Microsoft.EntityFrameworkCore;
using NLTSQL.Data.Entities;

namespace NLTSQL.Data;

/// <summary>Maps the platform's tables.</summary>
public static class NltsqlModelBuilderExtensions
{
    /// <summary>Configures the dashboard and audit tables.</summary>
    public static ModelBuilder ConfigureNltsql(this ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Entity<SavedQuery>(entity =>
        {
            entity.ToTable("saved_query");
            entity.HasIndex(q => q.CreatedAt);
        });

        builder.Entity<Dashboard>(entity =>
        {
            entity.ToTable("dashboard");
            entity.HasIndex(d => d.Title);

            entity.HasMany(d => d.Tiles)
                .WithOne(t => t.Dashboard!)
                .HasForeignKey(t => t.DashboardId)
                // Deleting a dashboard takes its tiles with it: a tile has no meaning on its own.
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DashboardTile>(entity =>
        {
            entity.ToTable("dashboard_tile");
            entity.HasIndex(t => new { t.DashboardId, t.Position });

            entity.HasOne(t => t.SavedQuery!)
                .WithMany()
                .HasForeignKey(t => t.SavedQueryId)
                // A saved query still referenced by a tile must not vanish underneath it — that
                // would leave a dashboard with a hole nobody can explain.
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(t => t.Mode).HasConversion<string>();
        });

        builder.Entity<QueryRun>(entity =>
        {
            entity.ToTable("query_run");
            entity.HasIndex(r => r.StartedAt);
            entity.HasIndex(r => r.UserId);
        });

        return builder;
    }
}
