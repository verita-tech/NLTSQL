using System.ComponentModel.DataAnnotations;

namespace Nltsql.Infrastructure.Configuration;

public sealed class CubeOptions
{
    public const string SectionName = "Cube";

    /// <summary>Internal base URL of the Cube API, e.g. <c>http://cube:4000</c>.</summary>
    [Required]
    public string BaseUrl { get; set; } = "http://localhost:4000";

    /// <summary>
    /// Shared secret matching <c>CUBEJS_API_SECRET</c>. Used to sign the
    /// per-request JWT that carries the tenant security context.
    /// </summary>
    [Required]
    [MinLength(32, ErrorMessage = "Cube:ApiSecret muss mindestens 32 Zeichen lang sein.")]
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>Lifetime of the minted token. Short by design — it is created per request.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Overall budget for one query, including Cube's long-poll waits.</summary>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long the semantic model is cached before Cube is asked again.</summary>
    public TimeSpan ModelCacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}
