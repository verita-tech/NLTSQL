namespace Nltsql.Infrastructure.Configuration;

public sealed class PlannerOptions
{
    public const string SectionName = "Planner";

    /// <summary>
    /// Turns the natural-language box on or off.
    /// </summary>
    /// <remarks>
    /// A local model has no API key, so availability cannot be inferred
    /// from a secret being present the way it could with a hosted API.
    /// This is the explicit switch that replaces it.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Base address of the Ollama server.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model tag, as it appears in <c>ollama list</c>.</summary>
    public string Model { get; set; } = "qwen2.5:14b-instruct";

    /// <summary>
    /// Context window handed to Ollama, in tokens.
    /// </summary>
    /// <remarks>
    /// Ollama silently truncates the prompt to the model's default window
    /// rather than failing, and the generated catalogue is the bulk of the
    /// prompt — a truncated catalogue produces plausible-looking plans that
    /// name members the model never saw. Raise this when the semantic model
    /// grows.
    /// </remarks>
    public int ContextTokens { get; set; } = 8192;

    /// <summary>
    /// How long Ollama keeps the model resident after a request.
    /// </summary>
    /// <remarks>
    /// Loading a 14B model costs seconds; keeping it warm turns the second
    /// question of a session into a fast one.
    /// </remarks>
    public string KeepAlive { get; set; } = "10m";

    /// <summary>
    /// Extra business context handed to the planner alongside the
    /// semantic model: house rules, defaults and vocabulary that are not
    /// expressible as member metadata.
    /// </summary>
    public string? DomainBriefing { get; set; }

    /// <summary>
    /// How many times a rejected plan is sent back with its validation
    /// errors. One retry fixes nearly every miss; more mostly burns time.
    /// </summary>
    public int MaxRepairAttempts { get; set; } = 1;

    /// <summary>
    /// Budget for the whole planning call, repair round included.
    /// </summary>
    /// <remarks>
    /// Generous on purpose: a 14B model answering on CPU takes far longer
    /// than a hosted API did, and the alternative to waiting is an error
    /// message for a question that would have succeeded.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(180);

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);
}
