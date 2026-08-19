namespace Nltsql.Infrastructure.Configuration;

public sealed class PlannerOptions
{
    public const string SectionName = "Planner";

    /// <summary>Anthropic API key. Empty disables natural-language input.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    public string Model { get; set; } = "claude-opus-5";

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

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
