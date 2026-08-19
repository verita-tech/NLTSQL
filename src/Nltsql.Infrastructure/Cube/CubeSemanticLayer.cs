using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nltsql.Core.Abstractions;
using Nltsql.Core.Queries;
using Nltsql.Core.Results;
using Nltsql.Core.Semantics;
using Nltsql.Infrastructure.Configuration;
using Nltsql.Infrastructure.Security;

namespace Nltsql.Infrastructure.Cube;

/// <summary>
/// <see cref="ISemanticLayer"/> backed by Cube's REST API.
/// </summary>
public sealed class CubeSemanticLayer(
    HttpClient httpClient,
    CubeTokenFactory tokenFactory,
    ITenantContext tenant,
    IMemoryCache cache,
    IOptions<CubeOptions> options,
    TimeProvider timeProvider,
    ILogger<CubeSemanticLayer> logger) : ISemanticLayer
{
    private const string MetaPath = "/cubejs-api/v1/meta";
    private const string LoadPath = "/cubejs-api/v1/load";

    /// <summary>
    /// Cube answers a still-running query with HTTP 200 and this body
    /// rather than blocking the connection. It is a signal to ask again,
    /// not an error.
    /// </summary>
    private const string ContinueWait = "Continue wait";

    private readonly CubeOptions _options = options.Value;

    public async Task<SemanticModel> GetModelAsync(CancellationToken cancellationToken = default)
    {
        // Keyed per tenant: what a caller may see is part of the answer.
        var cacheKey = $"cube-meta:{tenant.TenantId}";

        if (cache.TryGetValue(cacheKey, out SemanticModel? cached) && cached is not null)
        {
            return cached;
        }

        using var request = CreateRequest(HttpMethod.Get, $"{MetaPath}?extended=true");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        using var document = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var model = CubeMetaMapper.Map(document.RootElement);

        CubeLog.ModelLoaded(logger, model.Views.Count, tenant.TenantId);

        cache.Set(cacheKey, model, _options.ModelCacheDuration);
        return model;
    }

    public async Task<QueryResultSet> ExecuteAsync(
        SemanticQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Re-validate at the boundary. Callers are expected to have
        // validated already, but a saved query may predate a model change.
        var model = await GetModelAsync(cancellationToken).ConfigureAwait(false);
        var validation = SemanticQueryValidator.Validate(query, model);
        if (!validation.IsValid)
        {
            throw new SemanticQueryException(validation);
        }

        var view = model.FindView(query.View)!;
        var payload = new JsonObject { ["query"] = CubeQueryTranslator.ToCubeQuery(query) };

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.QueryTimeout);

        var started = Stopwatch.GetTimestamp();
        var document = await LoadWithContinueWaitAsync(payload, budget.Token).ConfigureAwait(false);

        using (document)
        {
            return CubeResultMapper.Map(
                document.RootElement,
                query,
                view,
                Stopwatch.GetElapsedTime(started));
        }
    }

    /// <summary>
    /// Issues the load request, re-asking while Cube reports
    /// <c>Continue wait</c> until the query budget is spent.
    /// </summary>
    private async Task<JsonDocument> LoadWithContinueWaitAsync(
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = CreateRequest(HttpMethod.Post, LoadPath);
            request.Content = JsonContent.Create(payload);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!IsContinueWait(document.RootElement))
            {
                return document;
            }

            document.Dispose();
            attempt++;

            CubeLog.ContinueWait(logger, attempt);

            // Cube is already long-polling server-side; a short pause is
            // enough to avoid hammering it on repeated waits.
            await Task.Delay(TimeSpan.FromMilliseconds(500), timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool IsContinueWait(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("error", out var error)
        && error.ValueKind == JsonValueKind.String
        && string.Equals(error.GetString(), ContinueWait, StringComparison.OrdinalIgnoreCase);

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);

        // A fresh token per request: lifetimes are minutes, and the
        // security context can change between requests.
        request.Headers.Authorization = new AuthenticationHeaderValue(tokenFactory.Create(tenant));

        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // Cube reports modelling and permission problems in the body;
        // losing it would leave the user with a bare status code.
        throw new CubeApiException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Cube antwortete mit {(int)response.StatusCode} ({response.ReasonPhrase}): {Truncate(body)}"),
            response.StatusCode);
    }

    private static string Truncate(string value) =>
        value.Length <= 2000 ? value : value[..2000] + "…";
}
