using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddHttpClient<ISemanticLayer, CubeSemanticLayer>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);

                // Generous: Cube's own long-poll budget sits inside this,
                // and the per-query deadline is enforced separately.
                client.Timeout = TimeSpan.FromMinutes(2);
            })
            .AddStandardResilienceHandler();
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

        if (!section.GetValue("Enabled", defaultValue: true))
        {
            // Switched off: the structured builder carries the app on its own.
            services.AddSingleton<IQueryPlanner, DisabledQueryPlanner>();
            return;
        }

        var baseUrl = section["BaseUrl"] ?? "http://localhost:11434";

        services.AddHttpClient<IQueryPlanner, OllamaQueryPlanner>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);

                // The per-request budget lives in the planner's own linked
                // token, so the client must not impose a shorter one.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler(options =>
            {
                // The defaults (10 s per attempt, 30 s total) are sized for
                // a web API and would abort every local inference. The three
                // values are validated against each other, so all of them
                // have to move together: total > attempt, and the circuit
                // breaker's sampling window >= 2 x attempt.
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(4);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(8);

                // A retry here costs a whole model response, and the
                // planner already has a second, better-informed attempt in
                // its repair round.
                options.Retry.MaxRetryAttempts = 1;
            });
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
