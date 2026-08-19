using Nltsql.Core.Abstractions;
using Nltsql.Core.Semantics;

namespace Nltsql.Infrastructure.Planning;

/// <summary>
/// Stand-in used when the local model is not enabled.
/// </summary>
/// <remarks>
/// The natural-language box is an accelerator, not the product: without
/// it the structured builder still answers every question the semantic
/// model supports. Registering a null object keeps that path free of
/// conditional wiring.
/// </remarks>
public sealed class DisabledQueryPlanner : IQueryPlanner
{
    public bool IsAvailable => false;

    public Task<QueryPlan> PlanAsync(
        string question,
        SemanticModel model,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(QueryPlan.Failed(
            "Die Eingabe in natürlicher Sprache ist nicht aktiviert. " +
            "Setzen Sie Planner:Enabled auf true und starten Sie Ollama, " +
            "oder nutzen Sie den Abfrage-Editor."));
}
