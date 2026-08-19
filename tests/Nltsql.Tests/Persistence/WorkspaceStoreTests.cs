using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nltsql.Core.Abstractions;
using Nltsql.Core.Charts;
using Nltsql.Core.Queries;
using Nltsql.Infrastructure.Persistence;
using Shouldly;

namespace Nltsql.Tests.Persistence;

/// <summary>
/// Runs against a real SQLite connection rather than the in-memory
/// provider: the bug these were written for — SQLite refusing to order
/// by a DateTimeOffset — only exists in the SQLite translation layer and
/// is invisible to the in-memory provider.
/// </summary>
public sealed class WorkspaceStoreTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private WorkspaceStore _store = null!;
    private FakeTimeProvider _time = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);

        await _db.Database.EnsureCreatedAsync();

        _time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));
        _store = new WorkspaceStore(_db, new FakeTenant("kunde-a"), _time);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>Satisfies CA1001; the async path does the real work.</summary>
    public void Dispose()
    {
        _db?.Dispose();
        _connection?.Dispose();
    }

    private static SemanticQuery SampleQuery => new()
    {
        View = TestModel.ViewName,
        Measures = ["oee"],
        Dimensions = ["machine_name"],
    };

    private Task<SavedQuery> SaveAsync(string title) =>
        _store.SaveQueryAsync(new SaveQueryRequest
        {
            Title = title,
            Query = SampleQuery,
            ChartType = ChartType.Bar,
        });

    [Fact]
    public async Task Saves_and_reloads_a_query()
    {
        var saved = await SaveAsync("OEE je Maschine");

        var loaded = await _store.GetSavedQueryAsync(saved.Id);

        loaded.ShouldNotBeNull();
        loaded.Title.ShouldBe("OEE je Maschine");
        loaded.ChartType.ShouldBe(ChartType.Bar);

        // The stored form has to survive the round trip, not just the row.
        loaded.ToQuery()!.Measures.ShouldBe(["oee"]);
    }

    [Fact]
    public async Task Lists_saved_queries_newest_first()
    {
        await SaveAsync("Erste");
        _time.Advance(TimeSpan.FromMinutes(5));
        await SaveAsync("Zweite");

        // Regression: ordering by DateTimeOffset threw on SQLite until
        // the model applied a binary value conversion.
        var all = await _store.GetSavedQueriesAsync();

        all.Select(q => q.Title).ShouldBe(["Zweite", "Erste"]);
    }

    [Fact]
    public async Task Updates_in_place_instead_of_creating_a_duplicate()
    {
        var saved = await SaveAsync("Ursprünglich");

        await _store.SaveQueryAsync(new SaveQueryRequest
        {
            Id = saved.Id,
            Title = "Umbenannt",
            Query = SampleQuery,
            MetabaseCardId = 42,
        });

        var all = await _store.GetSavedQueriesAsync();

        all.ShouldHaveSingleItem().Title.ShouldBe("Umbenannt");
        all[0].MetabaseCardId.ShouldBe(42);
    }

    [Fact]
    public async Task Keeps_an_existing_Metabase_card_when_none_is_supplied()
    {
        var saved = await _store.SaveQueryAsync(new SaveQueryRequest
        {
            Title = "Mit Karte",
            Query = SampleQuery,
            MetabaseCardId = 7,
        });

        await _store.SaveQueryAsync(new SaveQueryRequest
        {
            Id = saved.Id,
            Title = "Ohne Kartenangabe",
            Query = SampleQuery,
        });

        (await _store.GetSavedQueryAsync(saved.Id))!.MetabaseCardId.ShouldBe(7);
    }

    [Fact]
    public async Task Hides_another_tenants_query()
    {
        var saved = await SaveAsync("Vertraulich");

        var otherTenant = new WorkspaceStore(_db, new FakeTenant("kunde-b"), _time);

        // Scoping lives in the store, so an id alone is not enough.
        (await otherTenant.GetSavedQueryAsync(saved.Id)).ShouldBeNull();
        (await otherTenant.GetSavedQueriesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Adds_a_tile_to_a_dashboard_and_loads_it_back()
    {
        var saved = await SaveAsync("Kachel");
        var dashboard = await _store.CreateDashboardAsync("Produktion", "Tagesüberblick");

        await _store.AddTileAsync(dashboard.Id, saved.Id);

        var loaded = await _store.GetDashboardAsync(dashboard.Id);

        loaded.ShouldNotBeNull();
        loaded.Tiles.ShouldHaveSingleItem().SavedQuery!.Title.ShouldBe("Kachel");
    }

    [Fact]
    public async Task Ignores_a_query_that_is_already_on_the_dashboard()
    {
        var saved = await SaveAsync("Kachel");
        var dashboard = await _store.CreateDashboardAsync("Produktion", null);

        await _store.AddTileAsync(dashboard.Id, saved.Id);
        await _store.AddTileAsync(dashboard.Id, saved.Id);

        (await _store.GetDashboardAsync(dashboard.Id))!.Tiles.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_saved_query_removes_its_tiles()
    {
        var saved = await SaveAsync("Kachel");
        var dashboard = await _store.CreateDashboardAsync("Produktion", null);
        await _store.AddTileAsync(dashboard.Id, saved.Id);

        await _store.DeleteSavedQueryAsync(saved.Id);

        // A tile pointing at nothing would break the whole dashboard.
        (await _store.GetDashboardAsync(dashboard.Id))!.Tiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_dashboard_keeps_the_saved_queries()
    {
        var saved = await SaveAsync("Bleibt");
        var dashboard = await _store.CreateDashboardAsync("Wegwerf", null);
        await _store.AddTileAsync(dashboard.Id, saved.Id);

        await _store.DeleteDashboardAsync(dashboard.Id);

        (await _store.GetDashboardsAsync()).ShouldBeEmpty();
        (await _store.GetSavedQueriesAsync()).ShouldHaveSingleItem();
    }

    private sealed class FakeTenant(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;

        public string UserName => "Testbenutzer";

        public IReadOnlyList<string> AllowedSites => [];
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
