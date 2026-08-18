using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NLTSQL.Ai.Planning;
using NLTSQL.QueryEngine;
using NLTSQL.QueryEngine.Execution;
using NLTSQL.Semantics;
using OllamaSharp;

namespace NLTSQL.Ai;

/// <summary>Which model to plan with, and where it runs.</summary>
public sealed class AiOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Nltsql:Ai";

    /// <summary>Base address of the Ollama server.</summary>
    [Required]
    public Uri Endpoint { get; set; } = new("http://localhost:11434");

    /// <summary>
    /// The model that plans queries.
    /// </summary>
    /// <remarks>
    /// Left to configuration rather than fixed in code because the right answer is measured, not
    /// argued: planning quality varies enough between local models that the only honest way to
    /// choose is to try a few against real questions.
    /// </remarks>
    [Required]
    public string ChatModel { get; set; } = "qwen3:14b";

    /// <summary>How long to wait for the model before giving up.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>Registers the planning pipeline.</summary>
public static class AiServiceCollectionExtensions
{
    /// <summary>Adds the chat client, the planner and the ask service.</summary>
    public static IServiceCollection AddNltsqlAi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptionsWithValidateOnStart<AiOptions>()
            .BindConfiguration(AiOptions.SectionName)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<SemanticModelOptions>()
            .BindConfiguration(SemanticModelOptions.SectionName);

        services.AddOptions<QueryContextOptions>()
            .BindConfiguration(QueryContextOptions.SectionName);

        services.AddSingleton<ISemanticModelRegistry, SemanticModelRegistry>();
        services.AddSingleton<IQueryContextProvider, ConfiguredQueryContextProvider>();

        services.AddNltsqlQueryEngine();

        services.AddChatClient(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;

            // A local model on a cold start can take a while to load; the default HttpClient
            // timeout is short enough to make that look like a failure rather than a wait.
            var http = new HttpClient { BaseAddress = options.Endpoint, Timeout = options.Timeout };
            return new OllamaApiClient(http, options.ChatModel);
        }).UseLogging();

        services.AddSingleton<QueryPlanner>();
        services.AddSingleton<AskService>();

        return services;
    }
}
