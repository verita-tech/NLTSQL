using Nltsql.Core.Charts;
using Nltsql.Core.Queries;

namespace Nltsql.Core.Abstractions;

/// <summary>
/// Publishes a semantic query to Metabase so it can be previewed inline
/// and handed over for further self-service analysis.
/// </summary>
public interface IChartGateway
{
    /// <summary>False when Metabase is not configured; the app degrades to its own table.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Creates or updates the Metabase question backing a query and
    /// returns a signed embed URL for the inline preview.
    /// </summary>
    Task<ChartPreview> PublishAsync(
        ChartPublishRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MetabaseDashboard>> GetDashboardsAsync(CancellationToken cancellationToken = default);

    /// <summary>Appends an existing card to a Metabase dashboard.</summary>
    Task AddCardToDashboardAsync(
        int dashboardId,
        int cardId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Signed embed URL for a card that already exists, or
    /// <see langword="null"/> when Metabase is not configured.
    /// </summary>
    /// <remarks>
    /// Synchronous because signing a token is local work: a saved
    /// dashboard tile can render its chart without any call to Metabase.
    /// </remarks>
    string? CreateEmbedUrl(int cardId);
}

public sealed record ChartPublishRequest
{
    public required SemanticQuery Query { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public ChartType ChartType { get; init; } = ChartType.Table;

    /// <summary>Set to update the existing card instead of creating a new one.</summary>
    public int? ExistingCardId { get; init; }
}

public sealed record ChartPreview
{
    public required int CardId { get; init; }

    /// <summary>Signed static-embed URL for the preview iframe.</summary>
    public required string EmbedUrl { get; init; }

    /// <summary>Link that opens the question in Metabase for further drilling.</summary>
    public required string QuestionUrl { get; init; }

    /// <summary>The SQL handed to Metabase, shown in the UI for transparency.</summary>
    public string? GeneratedSql { get; init; }
}

public sealed record MetabaseDashboard(int Id, string Name);
