using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nltsql.Core.Abstractions;
using Nltsql.Core.Charts;
using Nltsql.Core.Sql;
using Nltsql.Infrastructure.Configuration;

namespace Nltsql.Infrastructure.Metabase;

/// <summary>
/// Publishes semantic queries to Metabase as native questions.
/// </summary>
/// <remarks>
/// The card's SQL targets Cube's SQL API, not the warehouse. That is the
/// point of the integration: a user who opens the question in Metabase
/// and keeps filtering still gets metrics as the semantic layer defines
/// them, instead of a second, divergent definition of OEE.
/// </remarks>
public sealed class MetabaseChartGateway(
    HttpClient httpClient,
    MetabaseEmbedTokenFactory tokenFactory,
    ISemanticLayer semanticLayer,
    IOptions<MetabaseOptions> options,
    ILogger<MetabaseChartGateway> logger) : IChartGateway
{
    /// <summary>Metabase lays dashboards out on a 24-column grid.</summary>
    private const int DashboardGridWidth = 24;

    private const int DefaultCardWidth = 12;
    private const int DefaultCardHeight = 8;

    private readonly MetabaseOptions _options = options.Value;

    public bool IsAvailable => _options.IsConfigured;

    public async Task<ChartPreview> PublishAsync(
        ChartPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();

        var model = await semanticLayer.GetModelAsync(cancellationToken).ConfigureAwait(false);
        var view = model.FindView(request.Query.View)
            ?? throw new InvalidOperationException(
                $"Der Datenbereich \"{request.Query.View}\" existiert im semantischen Modell nicht.");

        var sql = CubeSqlRenderer.Render(request.Query, view);

        var payload = new JsonObject
        {
            ["name"] = request.Title,

            // Metabase 0.63 replaced the old boolean `dataset` flag with a
            // card type; "question" is the ordinary saved question.
            ["type"] = "question",

            // The create schema types this as a *non-blank* string when
            // present, so an empty description is a 400, not a no-op.
            ["description"] = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description,
            ["display"] = request.ChartType.ToMetabaseDisplay(),
            ["visualization_settings"] = new JsonObject(),
            ["dataset_query"] = new JsonObject
            {
                ["type"] = "native",
                ["database"] = _options.CubeDatabaseId!.Value,
                ["native"] = new JsonObject
                {
                    ["query"] = sql,
                    ["template-tags"] = new JsonObject(),
                },
            },
        };

        if (_options.CollectionId is { } collectionId)
        {
            payload["collection_id"] = collectionId;
        }

        var cardId = request.ExistingCardId is { } existing
            ? await UpdateCardAsync(existing, payload, cancellationToken).ConfigureAwait(false)
            : await CreateCardAsync(payload, cancellationToken).ConfigureAwait(false);

        // Static embedding is opt-in per question.
        await EnableEmbeddingAsync(cardId, cancellationToken).ConfigureAwait(false);

        return new ChartPreview
        {
            CardId = cardId,
            EmbedUrl = tokenFactory.CreateQuestionEmbedUrl(cardId),
            QuestionUrl = $"{_options.EffectivePublicUrl.TrimEnd('/')}/question/{cardId}",
            GeneratedSql = sql,
        };
    }

    public string? CreateEmbedUrl(int cardId) =>
        IsAvailable ? tokenFactory.CreateQuestionEmbedUrl(cardId) : null;

    private async Task<int> CreateCardAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/card", payload, cancellationToken)
            .ConfigureAwait(false);

        var card = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var id = card?["id"]?.GetValue<int>()
            ?? throw new MetabaseApiException("Metabase lieferte beim Anlegen der Frage keine Id zurück.");

        MetabaseLog.CardCreated(logger, id);
        return id;
    }

    private async Task<int> UpdateCardAsync(int cardId, JsonObject payload, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, $"/api/card/{cardId}", payload, cancellationToken)
            .ConfigureAwait(false);

        _ = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);

        MetabaseLog.CardUpdated(logger, cardId);
        return cardId;
    }

    /// <summary>Opts this one question into static embedding.</summary>
    /// <remarks>
    /// Metabase requires a superuser for this — the API key must belong to
    /// the Administrators group — and rejects it outright unless static
    /// embedding is enabled instance-wide. <c>infra/metabase/bootstrap.py</c>
    /// arranges both.
    ///
    /// <c>embedding_params</c> is sent explicitly and empty: no parameter of
    /// this card is exposed to the viewer. The tenant filter does not belong
    /// here anyway — Cube applies it on the connection Metabase reads
    /// through, where a viewer cannot reach it.
    /// </remarks>
    private async Task EnableEmbeddingAsync(int cardId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["enable_embedding"] = true,
            ["embedding_params"] = new JsonObject(),
        };

        using var response = await SendAsync(HttpMethod.Put, $"/api/card/{cardId}", payload, cancellationToken)
            .ConfigureAwait(false);

        _ = response;
    }

    public async Task<IReadOnlyList<MetabaseDashboard>> GetDashboardsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // The search endpoint is the stable way to enumerate dashboards;
        // the older collection-listing routes have moved between versions.
        //
        // `offset` is not optional here even though it looks like it:
        // Metabase validates the two paging parameters as a pair and
        // answers "When including a limit, an offset must also be
        // included." with a 400 if only one of them arrives.
        using var response = await SendAsync(
            HttpMethod.Get,
            "/api/search?models=dashboard&limit=100&offset=0",
            content: null,
            cancellationToken)
            .ConfigureAwait(false);

        var body = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var items = body?["data"]?.AsArray();

        if (items is null)
        {
            return [];
        }

        var dashboards = new List<MetabaseDashboard>();

        foreach (var item in items)
        {
            var id = item?["id"]?.GetValue<int>();
            var name = item?["name"]?.GetValue<string>();

            if (id is { } dashboardId && !string.IsNullOrWhiteSpace(name))
            {
                dashboards.Add(new MetabaseDashboard(dashboardId, name));
            }
        }

        return dashboards.OrderBy(d => d.Name, StringComparer.CurrentCulture).ToList();
    }

    public async Task AddCardToDashboardAsync(
        int dashboardId,
        int cardId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Metabase replaces the whole dashcard collection on update, so
        // the existing entries are read back and re-sent untouched. They
        // are kept as raw JSON nodes on purpose: deserialising into a
        // typed model would silently drop fields this app does not know
        // about, such as click behaviour or parameter mappings.
        using var getResponse = await SendAsync(
            HttpMethod.Get, $"/api/dashboard/{dashboardId}", content: null, cancellationToken)
            .ConfigureAwait(false);

        var dashboard = await ReadJsonAsync(getResponse, cancellationToken).ConfigureAwait(false)
            ?? throw new MetabaseApiException($"Dashboard {dashboardId} konnte nicht gelesen werden.");

        var dashcards = dashboard["dashcards"]?.AsArray() ?? [];
        var existing = dashcards.Select(node => node?.DeepClone()).ToArray();

        var updated = new JsonArray(existing);
        updated.Add(NewDashcard(cardId, NextRow(existing)));

        var payload = new JsonObject { ["dashcards"] = updated };

        using var putResponse = await SendAsync(
            HttpMethod.Put, $"/api/dashboard/{dashboardId}", payload, cancellationToken)
            .ConfigureAwait(false);

        _ = putResponse;

        MetabaseLog.CardAddedToDashboard(logger, cardId, dashboardId);
    }

    /// <summary>Places the new tile below everything already on the dashboard.</summary>
    private static int NextRow(IReadOnlyCollection<JsonNode?> dashcards)
    {
        var bottom = 0;

        foreach (var card in dashcards)
        {
            var row = card?["row"]?.GetValue<int>() ?? 0;
            var height = card?["size_y"]?.GetValue<int>() ?? 0;

            bottom = Math.Max(bottom, row + height);
        }

        return bottom;
    }

    private static JsonObject NewDashcard(int cardId, int row) => new()
    {
        // Metabase treats a negative id as "this tile is new".
        ["id"] = -1,
        ["card_id"] = cardId,
        ["row"] = row,
        ["col"] = 0,
        ["size_x"] = Math.Min(DefaultCardWidth, DashboardGridWidth),
        ["size_y"] = DefaultCardHeight,
        ["series"] = new JsonArray(),
        ["parameter_mappings"] = new JsonArray(),
        ["visualization_settings"] = new JsonObject(),
    };

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        JsonNode? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);

        // API keys are the supported server-to-server credential; a
        // session token would have to be refreshed and would tie the
        // app's access to a human account.
        request.Headers.Add("x-api-key", _options.ApiKey);

        if (content is not null)
        {
            request.Content = JsonContent.Create(content);
        }

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.Dispose();

        throw new MetabaseApiException(
            $"Metabase antwortete auf {method} {path} mit {(int)response.StatusCode}: {Truncate(body)}");
    }

    private static async Task<JsonNode?> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private void EnsureConfigured()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "Die Metabase-Anbindung ist nicht konfiguriert. Erforderlich sind Metabase:ApiKey, " +
                "Metabase:EmbeddingSecretKey und Metabase:CubeDatabaseId.");
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value : value[..1000] + "…";
}

public sealed class MetabaseApiException(string message) : Exception(message);
