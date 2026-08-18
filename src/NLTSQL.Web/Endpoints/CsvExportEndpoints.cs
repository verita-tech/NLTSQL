using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;
using NLTSQL.Ai;
using NLTSQL.Core.Query;
using NLTSQL.Core.Serialization;
using NLTSQL.Data;
using NLTSQL.QueryEngine;
using NLTSQL.Web.Charting;

namespace NLTSQL.Web.Endpoints;

/// <summary>How exported files are written.</summary>
public sealed class CsvExportOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Nltsql:CsvExport";

    /// <summary>
    /// Column separator.
    /// </summary>
    /// <remarks>
    /// A semicolon by default. Excel in a German locale reads a comma-separated file as one column
    /// per row, which reliably produces a support ticket rather than a spreadsheet.
    /// </remarks>
    public string Delimiter { get; set; } = ";";

    /// <summary>
    /// Whether to write a UTF-8 byte order mark.
    /// </summary>
    /// <remarks>
    /// On by default for the same reason: without it Excel assumes the system code page and umlauts
    /// arrive mangled.
    /// </remarks>
    public bool WriteByteOrderMark { get; set; } = true;

    /// <summary>Culture used to format numbers and dates in the file.</summary>
    public string Culture { get; set; } = "de-DE";

    /// <summary>Row cap for an export, independent of what the screen showed.</summary>
    public int MaxRows { get; set; } = 100_000;
}

/// <summary>Serves query results as CSV.</summary>
public static class CsvExportEndpoints
{
    /// <summary>Maps the export endpoint.</summary>
    /// <remarks>
    /// Exports run from a recorded execution rather than from an opaque handle, so every file that
    /// leaves the system is traceable to the question that produced it — and re-running the stored
    /// spec means the export re-applies the row policies rather than dumping a cached result that
    /// might predate a permission change.
    /// </remarks>
    public static IEndpointRouteBuilder MapCsvExport(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/export/{runId:guid}.csv", async (
            Guid runId,
            DashboardStore store,
            SpecRunner runner,
            IOptions<CsvExportOptions> exportOptions,
            IOptions<QueryLimits> limits,
            CancellationToken cancellationToken) =>
        {
            var run = await store.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

            if (run?.QuerySpecJson is not { Length: > 0 } specJson)
            {
                return Results.NotFound();
            }

            var spec = NltsqlJson.Deserialize<QuerySpec>(specJson);
            if (spec is null)
            {
                return Results.NotFound();
            }

            var options = exportOptions.Value;
            var rowLimit = Math.Min(options.MaxRows, limits.Value.MaxRowLimit);

            var result = await runner
                .RunAsync(spec, run.ModelName, rowLimit, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                return Results.Problem(
                    string.Join(" ", result.Issues.Select(i => i.Message)),
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Die gespeicherte Abfrage laesst sich nicht mehr ausfuehren.");
            }

            var fileName = BuildFileName(run.Question, run.StartedAt);

            return Results.Stream(
                async stream =>
                {
                    // Streamed rather than buffered: an export is the one place this application
                    // deliberately produces a large payload, and holding it in memory first would
                    // make the row cap the only thing standing between a question and an outage.
                    await WriteCsvAsync(stream, result.Result!, options, cancellationToken).ConfigureAwait(false);
                },
                "text/csv",
                fileName);
        })
        .RequireAuthorization()
        .WithName("ExportQueryRunCsv");

        return endpoints;
    }

    private static async Task WriteCsvAsync(
        Stream stream,
        QueryEngine.Execution.QueryResult result,
        CsvExportOptions options,
        CancellationToken cancellationToken)
    {
        var encoding = new UTF8Encoding(options.WriteByteOrderMark);
        var culture = CultureInfo.GetCultureInfo(options.Culture);

        var configuration = new CsvConfiguration(culture) { Delimiter = options.Delimiter };

        await using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
        await using var csv = new CsvWriter(writer, configuration, leaveOpen: true);

        foreach (var column in result.Columns)
        {
            csv.WriteField(column.Label);
        }

        await csv.NextRecordAsync().ConfigureAwait(false);

        foreach (var row in result.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var i = 0; i < result.Columns.Count; i++)
            {
                // Written the way the screen shows it: an export that disagrees with the table it
                // came from is the reason people stop trusting exports.
                csv.WriteField(ResultFormatter.Format(row[i], result.Columns[i]));
            }

            await csv.NextRecordAsync().ConfigureAwait(false);
        }

        await csv.FlushAsync().ConfigureAwait(false);
    }

    private static string BuildFileName(string question, DateTimeOffset askedAt)
    {
        var slug = new string([.. question
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);

        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));

        if (slug.Length > 60)
        {
            slug = slug[..60].TrimEnd('-');
        }

        var stamp = askedAt.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);
        return $"{(slug.Length == 0 ? "abfrage" : slug)}-{stamp}.csv";
    }
}
