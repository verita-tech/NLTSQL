namespace Nltsql.Infrastructure.Configuration;

public sealed class MetabaseOptions
{
    public const string SectionName = "Metabase";

    /// <summary>URL the server uses to call the Metabase API.</summary>
    public string BaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>
    /// URL a browser uses to load the embed iframe. Differs from
    /// <see cref="BaseUrl"/> whenever the app talks to Metabase over an
    /// internal network name.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>Server-to-server API key (<c>x-api-key</c> header).</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Must equal Metabase's <c>MB_EMBEDDING_SECRET_KEY</c>; embed tokens
    /// signed with anything else are rejected.
    /// </summary>
    public string? EmbeddingSecretKey { get; set; }

    /// <summary>Id of the Metabase database entry pointing at Cube's SQL API.</summary>
    public int? CubeDatabaseId { get; set; }

    /// <summary>Metabase collection new questions are filed under.</summary>
    public int? CollectionId { get; set; }

    /// <summary>
    /// Certificate handling for the server-to-server calls to Metabase.
    /// </summary>
    /// <remarks>
    /// Applies to this app's API calls only. The chart preview is loaded
    /// by the browser straight from <see cref="PublicUrl"/>, so a
    /// certificate the browser distrusts still shows as a blocked or
    /// warned-about iframe no matter what is configured here.
    /// </remarks>
    public TlsOptions Tls { get; set; } = new();

    /// <summary>Lifetime of a signed embed token.</summary>
    public TimeSpan EmbedTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The integration is optional: without these the app still queries,
    /// tables and exports — only the chart preview is unavailable.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(EmbeddingSecretKey)
        && CubeDatabaseId is > 0;

    public string EffectivePublicUrl =>
        string.IsNullOrWhiteSpace(PublicUrl) ? BaseUrl : PublicUrl;
}
