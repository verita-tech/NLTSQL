using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Nltsql.Core.Abstractions;
using Nltsql.Infrastructure.Configuration;
using Nltsql.Infrastructure.Cube;
using Nltsql.Infrastructure.Metabase;
using Nltsql.Infrastructure.Persistence;
using Nltsql.Infrastructure.Planning;
using Nltsql.Infrastructure.Security;

namespace Nltsql.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNltsqlInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddOptions(services, configuration);

        services.TryAddSingletonTimeProvider();
        services.AddMemoryCache();

        AddCube(services, configuration);
        AddMetabase(services, configuration);
        AddPlanner(services, configuration);
        AddPersistence(services, configuration);

        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart turns a missing secret into a startup failure
        // with a clear message, rather than a 500 on the first query.
        services.AddOptions<CubeOptions>()
            .Bind(configuration.GetSection(CubeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MetabaseOptions>()
            .Bind(configuration.GetSection(MetabaseOptions.SectionName));

        services.AddOptions<PlannerOptions>()
            .Bind(configuration.GetSection(PlannerOptions.SectionName));
    }

    private static void AddCube(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<CubeTokenFactory>();

        var baseUrl = configuration[$"{CubeOptions.SectionName}:BaseUrl"] ?? "http://localhost:4000";

        var queryTimeout = configuration.GetValue<TimeSpan?>($"{CubeOptions.SectionName}:QueryTimeout")
            ?? TimeSpan.FromSeconds(60);

        services.AddHttpClient<ISemanticLayer, CubeSemanticLayer>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);

                // Generous: Cube's own long-poll budget sits inside this,
                // and the per-query deadline is enforced separately.
                client.Timeout = TimeSpan.FromMinutes(5);
            })
            .AddStandardResilienceHandler(resilience =>
            {
                // The defaults (10s per attempt, 30s total) are tuned for
                // chatty service calls and would cut an analytical query
                // long before the configured budget was spent — with a
                // timeout that looks like Cube failing rather than a
                // client-side cap.
                ConfigureTimeouts(resilience, queryTimeout, retries: 2);
            });
    }

    private static void AddMetabase(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<MetabaseEmbedTokenFactory>();

        var baseUrl = configuration[$"{MetabaseOptions.SectionName}:BaseUrl"] ?? "http://localhost:3000";

        services.AddHttpClient<IChartGateway, MetabaseChartGateway>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler();
    }

    private static void AddPlanner(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PlannerOptions.SectionName);

        if (!section.GetValue("Enabled", false))
        {
            // Disabled: the structured builder carries the app on its own.
            services.AddSingleton<IQueryPlanner, DisabledQueryPlanner>();
            return;
        }

        var baseUrl = section["BaseUrl"] ?? "http://localhost:11434";
        var timeout = section.GetValue<TimeSpan?>("Timeout") ?? TimeSpan.FromSeconds(180);

        services.AddHttpClient<OllamaChatClient>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);

                // Outermost bound; the planner enforces its own budget.
                client.Timeout = timeout + TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler(resilience =>
            {
                // A local model on CPU can take a minute for the first
                // answer, and re-running a slow generation is expensive.
                // One retry covers a dropped connection; beyond that,
                // failing fast tells the user more than waiting does.
                ConfigureTimeouts(resilience, timeout, retries: 1);
            });

        services.AddScoped<IQueryPlanner, OllamaQueryPlanner>();
    }

    /// <summary>
    /// Widens the resilience pipeline to fit a genuinely slow call.
    /// </summary>
    /// <remarks>
    /// The pipeline validates its own options: the total timeout has to
    /// exceed one attempt, and the circuit breaker's sampling window has
    /// to be at least twice the attempt timeout. Deriving all three from
    /// one budget keeps those relationships true whatever the operator
    /// configures.
    /// </remarks>
    private static void ConfigureTimeouts(
        HttpStandardResilienceOptions resilience,
        TimeSpan budget,
        int retries)
    {
        resilience.AttemptTimeout.Timeout = budget;
        resilience.TotalRequestTimeout.Timeout = budget * (retries + 1) + TimeSpan.FromSeconds(10);
        resilience.CircuitBreaker.SamplingDuration = budget * 2;
        resilience.Retry.MaxRetryAttempts = retries;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AppDb") ?? "Data Source=nltsql.db";

        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<WorkspaceStore>();
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
