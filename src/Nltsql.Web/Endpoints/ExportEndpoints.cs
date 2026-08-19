using Nltsql.Core.Abstractions;
using Nltsql.Infrastructure.Export;
using Nltsql.Infrastructure.Persistence;
using Nltsql.Web.Services;

namespace Nltsql.Web.Endpoints;

/// <summary>CSV download endpoints.</summary>
/// <remarks>
/// Exports re-execute the query rather than serialising whatever the
/// browser happens to be showing: the file then matches the definitions
/// in the semantic layer, and a large export is not bounded by what fits
/// in a rendered table.
/// </remarks>
public static class ExportEndpoints
{
    private const string CsvContentType = "text/csv";

    public static void MapExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/export");

        group.MapGet("/ticket/{ticket}", DownloadByTicketAsync);
        group.MapGet("/saved/{id:guid}", DownloadSavedAsync);
    }

    private static async Task<IResult> DownloadByTicketAsync(
        string ticket,
        ExportTicketStore tickets,
        ITenantContext tenant,
        ISemanticLayer semanticLayer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var entry = tickets.Redeem(ticket, tenant.TenantId);

        // Also the response for another tenant's ticket: an existence
        // oracle would leak that the export happened at all.
        if (entry is null)
        {
            return Results.NotFound();
        }

        return await StreamCsvAsync(
            semanticLayer, entry.Query, entry.Title, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> DownloadSavedAsync(
        Guid id,
        WorkspaceStore store,
        ISemanticLayer semanticLayer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // The store scopes by tenant, so an id from another tenant simply
        // does not resolve.
        var saved = await store.GetSavedQueryAsync(id, cancellationToken).ConfigureAwait(false);
        var query = saved?.ToQuery();

        if (saved is null || query is null)
        {
            return Results.NotFound();
        }

        return await StreamCsvAsync(
            semanticLayer, query, saved.Title, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> StreamCsvAsync(
        ISemanticLayer semanticLayer,
        Core.Queries.SemanticQuery query,
        string title,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var result = await semanticLayer.ExecuteAsync(query, cancellationToken).ConfigureAwait(false);
        var fileName = CsvResultExporter.SuggestFileName(title, timeProvider.GetUtcNow());

        // Buffered rather than streamed: the row cap keeps exports to a
        // size that fits comfortably in memory, and buffering means a
        // mid-write failure surfaces as an error page instead of a
        // truncated file the user cannot tell apart from a complete one.
        using var buffer = new MemoryStream();
        await CsvResultExporter.WriteAsync(result, buffer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Results.File(buffer.ToArray(), CsvContentType, fileName);
    }
}
