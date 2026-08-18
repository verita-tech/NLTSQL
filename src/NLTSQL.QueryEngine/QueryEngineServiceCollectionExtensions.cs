using Microsoft.Extensions.DependencyInjection;
using NLTSQL.QueryEngine.Execution;

namespace NLTSQL.QueryEngine;

/// <summary>Registers the query engine.</summary>
public static class QueryEngineServiceCollectionExtensions
{
    /// <summary>
    /// Adds the resolver, the data source registry and the executor.
    /// </summary>
    /// <remarks>
    /// <see cref="QueryLimits"/> and <see cref="DataSourceOptions"/> are bound with validation on
    /// start, so a misconfigured deployment fails while starting rather than on the first question
    /// a customer asks.
    /// </remarks>
    public static IServiceCollection AddNltsqlQueryEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptionsWithValidateOnStart<QueryLimits>()
            .BindConfiguration(QueryLimits.SectionName)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<DataSourceOptions>()
            .BindConfiguration(DataSourceOptions.SectionName)
            .ValidateDataAnnotations();

        services.TryAddSingletonTimeProvider();
        services.AddSingleton<IDataSourceRegistry, DataSourceRegistry>();
        services.AddSingleton<QuerySpecResolver>();
        services.AddSingleton<IQueryExecutor, QueryExecutor>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
