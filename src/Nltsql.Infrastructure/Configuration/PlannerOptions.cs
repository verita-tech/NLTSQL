namespace Nltsql.Infrastructure.Configuration;

/// <summary>
/// Settings for the local language model used to turn questions into
/// semantic queries.
/// </summary>
/// <remarks>
/// The planner runs against Ollama on the customer's own machines. That
/// is a deliberate constraint rather than a cost decision: the prompt
/// carries the complete semantic catalogue — every measure, dimension
/// and business description of the domain — plus the user's question.
/// None of that leaves the network.
/// </remarks>
public sealed class PlannerOptions
{
    public const string SectionName = "Planner";

    /// <summary>Base URL of the Ollama server.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// API key sent with every request, for an Ollama that sits behind a
    /// gateway rather than on the loopback interface.
    /// </summary>
    /// <remarks>
    /// Empty means the header is omitted entirely — an empty credential
    /// header is worse than none, because a gateway may accept it as a
    /// present-but-blank value.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>Header carrying <see cref="ApiKey"/>.</summary>
    /// <remarks>
    /// Configurable because gateways disagree. For a bearer scheme, set
    /// this to <c>Authorization</c> and put <c>Bearer …</c> in the value.
    /// </remarks>
    public string ApiKeyHeader { get; set; } = "X-Api-Key";

    /// <summary>Token identifying the calling user or service.</summary>
    public string? UserToken { get; set; }

    /// <summary>Header carrying <see cref="UserToken"/>.</summary>
    public string UserTokenHeader { get; set; } = "X-User-Token";

    /// <summary>
    /// Further fixed headers sent with every request, for anything the
    /// two named ones do not cover.
    /// </summary>
    public IDictionary<string, string> DefaultHeaders { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Certificate handling for the connection to Ollama. Relevant only
    /// when <see cref="BaseUrl"/> is HTTPS.
    /// </summary>
    public TlsOptions Tls { get; set; } = new();

    /// <summary>
    /// Model tag, exactly as <c>ollama list</c> reports it.
    /// </summary>
    /// <remarks>
    /// Needs to be an instruction-tuned model that honours a JSON schema.
    /// The 7B class is the smallest that reliably picks the right member
    /// out of a domain catalogue; below that, plans get rejected by
    /// validation often enough to be annoying.
    /// </remarks>
    public string Model { get; set; } = "qwen2.5:7b-instruct";

    /// <summary>
    /// Turns the natural-language box on. Off by default so the app does
    /// not appear broken when no Ollama server is running.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Context window in tokens for the request.
    /// </summary>
    /// <remarks>
    /// This has to be set explicitly and is the single most damaging
    /// thing to get wrong. Ollama defaults a request to a small context
    /// (2048 tokens on most builds) and silently discards whatever does
    /// not fit — from the front, which is exactly where the catalogue
    /// and the rules live. The symptom is not an error but a planner
    /// that invents member names, because it never saw the list.
    /// <para>
    /// It must comfortably exceed the generated prompt. Check the real
    /// size on the "Datenmodell" page; a large model with many measures
    /// may need 16384.
    /// </para>
    /// </remarks>
    public int ContextTokens { get; set; } = 8192;

    /// <summary>
    /// How long Ollama keeps the model in memory after a request.
    /// </summary>
    /// <remarks>
    /// Without this, Ollama unloads after five minutes and the next
    /// question pays the load time again — several seconds for a 7B
    /// model, and far worse on a cold page cache.
    /// </remarks>
    public string KeepAlive { get; set; } = "30m";

    /// <summary>
    /// Extra business context handed to the planner alongside the
    /// semantic model: house rules, defaults and vocabulary that are not
    /// expressible as member metadata.
    /// </summary>
    public string? DomainBriefing { get; set; }

    /// <summary>
    /// How many times a rejected plan is sent back with its validation
    /// errors.
    /// </summary>
    /// <remarks>
    /// Two by default rather than one: a local 7B model misses a member
    /// name more often than a frontier model, and the retry is cheap
    /// because it is answered by hardware the customer already paid for.
    /// </remarks>
    public int MaxRepairAttempts { get; set; } = 2;

    /// <summary>
    /// Budget for one planning request, including model load time.
    /// </summary>
    /// <remarks>
    /// Generous on purpose: on CPU-only hardware a 7B model can take a
    /// minute for the first answer.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(180);

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);
}
