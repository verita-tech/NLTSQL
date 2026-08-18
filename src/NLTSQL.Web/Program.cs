using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.FluentUI.AspNetCore.Components;
using NLTSQL.Ai;
using NLTSQL.Data;
using NLTSQL.Web;
using NLTSQL.Web.Endpoints;
using NLTSQL.Web.Components;
using NLTSQL.Web.Components.Account;
using NLTSQL.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
// SQLite serialises writers. Without write-ahead logging a single dashboard save blocks every
// reader on the file, which on a page that opens several tiles at once is immediately visible.
// The busy timeout turns the remaining brief contention into a short wait instead of an error.
builder.Services.AddSingleton<SqliteConfigurator>();

// Only the factory is registered. AddDbContext would additionally register DbContextOptions as
// scoped, which a singleton factory cannot consume — the two together fail at startup.
//
// Identity wants a scoped context, so that one is produced from the same factory. Everything else
// takes a context per operation, because a Blazor Server scope lives as long as the browser tab:
// a scoped context would accumulate tracked entities and be shared by overlapping component work.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped(provider =>
    provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddSingleton<INltsqlDataContextFactory, NltsqlDataContextFactory>();
builder.Services.AddSingleton<DashboardStore>();

builder.Services.AddOptions<NLTSQL.Web.Endpoints.CsvExportOptions>()
    .BindConfiguration(NLTSQL.Web.Endpoints.CsvExportOptions.SectionName);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddFluentUIComponents();

// Brings in the semantic model registry, the query engine and the planning pipeline. Options are
// bound with validation on start, so a misconfigured data source or model directory stops the
// application from starting rather than surfacing on the first question somebody asks.
builder.Services.AddNltsqlAi();

// The configured semantics path is relative to the application, not to whatever directory the
// process happened to be started from — otherwise `dotnet run` from the repository root and from
// the project folder load different models, or none.
builder.Services.PostConfigure<NLTSQL.Semantics.SemanticModelOptions>(options =>
{
    if (!Path.IsPathRooted(options.Directory))
    {
        options.Directory = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, options.Directory));
    }
});

var app = builder.Build();

await app.Services.GetRequiredService<SqliteConfigurator>().ApplyAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.MapCsvExport();

app.Run();
