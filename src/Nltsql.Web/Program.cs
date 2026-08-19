using Microsoft.FluentUI.AspNetCore.Components;
using Nltsql.Core.Abstractions;
using Nltsql.Infrastructure;
using Nltsql.Infrastructure.Persistence;
using Nltsql.Web.Components;
using Nltsql.Web.Endpoints;
using Nltsql.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

builder.Services.AddNltsqlInfrastructure(builder.Configuration);

builder.Services.Configure<TenantOptions>(builder.Configuration.GetSection(TenantOptions.SectionName));
builder.Services.AddScoped<ITenantContext, ConfiguredTenantContext>();
builder.Services.AddSingleton<ExportTicketStore>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var app = builder.Build();

await EnsureDatabaseAsync(app).ConfigureAwait(false);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapExportEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync().ConfigureAwait(false);

// Creates the SQLite file on first run.
//
// EnsureCreated suits a prototype whose store holds only its own objects.
// Moving to a shared deployment means switching this to MigrateAsync and
// adding a first migration: EnsureCreated cannot evolve a schema that
// already exists.
static async Task EnsureDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
}
