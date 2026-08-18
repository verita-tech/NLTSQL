using Microsoft.EntityFrameworkCore;
using NLTSQL.Data;
using NLTSQL.Data.Entities;

namespace NLTSQL.Data.Tests;

/// <summary>
/// Exercises the store against real SQLite.
/// </summary>
/// <remarks>
/// In-process, against a temporary file — no container and no server. It is here because the two
/// things most likely to break are things only a real engine shows: SQLite refuses to ORDER BY a
/// DateTimeOffset, and the delete behaviour on the tile relationships is enforced by the database
/// rather than by EF. An in-memory provider would have accepted both and told us nothing.
/// </remarks>
public sealed class DashboardStoreTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"nltsql-test-{Guid.NewGuid():N}.db");
    private TestContextFactory factory = null!;

    public async Task InitializeAsync()
    {
        this.factory = new TestContextFactory($"Data Source={this.databasePath}");

        await using var context = this.factory.CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var path in new[] { this.databasePath, this.databasePath + "-wal", this.databasePath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task DashboardsComeBackNewestFirst()
    {
        // This is the query that failed against real SQLite when the timestamps were
        // DateTimeOffset. An in-memory provider sorts them happily.
        var store = new DashboardStore(this.factory);

        await store.CreateDashboardAsync("Erstes", "tester");
        await Task.Delay(10);
        await store.CreateDashboardAsync("Zweites", "tester");

        var dashboards = await store.ListDashboardsAsync();

        dashboards.Select(d => d.Title).ShouldBe(["Zweites", "Erstes"]);
    }

    [Fact]
    public async Task ATileRoundTripsWithItsSavedQuery()
    {
        var store = new DashboardStore(this.factory);
        var dashboard = await store.CreateDashboardAsync("Vertrieb", "tester");

        await store.AddTileAsync(dashboard.Id, NewQuery("Umsatz pro Monat"), TileMode.Live, null);

        var loaded = await store.GetDashboardAsync(dashboard.Id);

        var tile = loaded.ShouldNotBeNull().Tiles.ShouldHaveSingleItem();
        tile.Mode.ShouldBe(TileMode.Live);
        tile.SavedQuery.ShouldNotBeNull().Title.ShouldBe("Umsatz pro Monat");
        tile.SnapshotTakenAt.ShouldBeNull();
    }

    [Fact]
    public async Task TilesKeepTheOrderTheyWereAddedIn()
    {
        var store = new DashboardStore(this.factory);
        var dashboard = await store.CreateDashboardAsync("Vertrieb", "tester");

        foreach (var title in new[] { "Eins", "Zwei", "Drei" })
        {
            await store.AddTileAsync(dashboard.Id, NewQuery(title), TileMode.Live, null);
        }

        var loaded = await store.GetDashboardAsync(dashboard.Id);

        loaded!.Tiles.Select(t => t.SavedQuery!.Title).ShouldBe(["Eins", "Zwei", "Drei"]);
    }

    [Fact]
    public async Task ASnapshotTileKeepsItsFrozenResultAndItsDate()
    {
        var store = new DashboardStore(this.factory);
        var dashboard = await store.CreateDashboardAsync("Vertrieb", "tester");

        await store.AddTileAsync(dashboard.Id, NewQuery("Umsatz"), TileMode.Snapshot, "{\"rows\":[]}");

        var tile = (await store.GetDashboardAsync(dashboard.Id))!.Tiles.Single();
        tile.SnapshotJson.ShouldBe("{\"rows\":[]}");
        tile.SnapshotTakenAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ASnapshotTileWithoutAResultIsRefused()
    {
        // Without this the tile would render as permanently empty and look like missing data.
        var store = new DashboardStore(this.factory);
        var dashboard = await store.CreateDashboardAsync("Vertrieb", "tester");

        await Should.ThrowAsync<ArgumentException>(
            () => store.AddTileAsync(dashboard.Id, NewQuery("Umsatz"), TileMode.Snapshot, null));
    }

    [Fact]
    public async Task DeletingADashboardTakesItsTilesButLeavesTheSavedQueries()
    {
        // The placement of a tile is cheap to redo; the work of phrasing a question is not.
        var store = new DashboardStore(this.factory);
        var dashboard = await store.CreateDashboardAsync("Vertrieb", "tester");
        await store.AddTileAsync(dashboard.Id, NewQuery("Umsatz"), TileMode.Live, null);

        await store.DeleteDashboardAsync(dashboard.Id);

        await using var context = this.factory.CreateDbContext();
        (await context.DashboardTiles.CountAsync()).ShouldBe(0);
        (await context.SavedQueries.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task ARecordedRunCanBeFoundAgain_BecauseItsIdIsTheExportHandle()
    {
        var store = new DashboardStore(this.factory);

        var run = await store.RecordRunAsync(new QueryRun
        {
            ModelName = "vertrieb",
            Question = "Umsatz pro Monat",
            QuerySpecJson = "{}",
            Success = true,
            RowCount = 12,
        });

        var found = await store.GetRunAsync(run.Id);

        found.ShouldNotBeNull().Question.ShouldBe("Umsatz pro Monat");
        found.RowCount.ShouldBe(12);
    }

    [Fact]
    public async Task FailedRunsAreRecordedToo()
    {
        // A run of questions the platform could not answer is the most direct signal of what the
        // semantic model is missing, and keeping only successes throws it away.
        var store = new DashboardStore(this.factory);

        var run = await store.RecordRunAsync(new QueryRun
        {
            ModelName = "vertrieb",
            Question = "Umsatz nach Land",
            Success = false,
            Error = "Unbekanntes Merkmal 'land'.",
        });

        (await store.GetRunAsync(run.Id))!.Error!.ShouldContain("land");
    }

    private static SavedQuery NewQuery(string title) => new()
    {
        Title = title,
        Question = title,
        ModelName = "vertrieb",
        ModelVersion = 1,
        QuerySpecJson = "{\"entity\":\"auftrag\"}",
    };

    /// <summary>A context over a temporary SQLite file, with only the platform's own tables.</summary>
    private sealed class TestDbContext(DbContextOptions<TestDbContext> options)
        : DbContext(options), INltsqlDataContext
    {
        public DbSet<SavedQuery> SavedQueries => this.Set<SavedQuery>();

        public DbSet<Dashboard> Dashboards => this.Set<Dashboard>();

        public DbSet<DashboardTile> DashboardTiles => this.Set<DashboardTile>();

        public DbSet<QueryRun> QueryRuns => this.Set<QueryRun>();

        protected override void OnModelCreating(ModelBuilder builder) => builder.ConfigureNltsql();
    }

    private sealed class TestContextFactory(string connectionString) : INltsqlDataContextFactory
    {
        public INltsqlDataContext CreateContext() => this.CreateDbContext();

        public TestDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options);
    }
}
