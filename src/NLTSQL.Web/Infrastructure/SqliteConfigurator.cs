using Microsoft.EntityFrameworkCore;
using NLTSQL.Web.Data;

namespace NLTSQL.Web;

/// <summary>
/// Brings the SQLite file up to date and puts it in the mode this application needs.
/// </summary>
/// <remarks>
/// <para>
/// Write-ahead logging is not a tuning preference here. SQLite serialises writers, and in the
/// default rollback journal a writer also blocks every reader — so one dashboard being saved would
/// stall every tile another user is loading. WAL lets readers carry on during a write, which is
/// exactly the shape of this workload: many small reads, occasional small writes.
/// </para>
/// <para>
/// The busy timeout covers what WAL does not: two writers still cannot overlap, and without a
/// timeout the second one fails immediately rather than waiting the few milliseconds the first
/// needs.
/// </para>
/// </remarks>
public sealed class SqliteConfigurator
{
    /// <summary>Applies migrations and sets the connection pragmas.</summary>
    public async Task ApplyAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.MigrateAsync().ConfigureAwait(false);

        // journal_mode is a property of the database file and survives; busy_timeout is per
        // connection, so it is also set through the connection string for pooled connections.
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;").ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;").ConfigureAwait(false);
    }
}
