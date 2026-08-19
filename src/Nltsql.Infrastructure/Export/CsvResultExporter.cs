using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Nltsql.Core.Results;

namespace Nltsql.Infrastructure.Export;

/// <summary>
/// Writes a result set as CSV.
/// </summary>
/// <remarks>
/// Defaults target the tool these exports actually get opened in:
/// Excel with German regional settings. That means a semicolon
/// delimiter, decimal commas and a UTF-8 BOM — without the BOM Excel
/// reads the file as ANSI and mangles every umlaut.
/// <para>
/// Percentage measures arrive from Cube as a 0..1 ratio. They are
/// exported as the percentage value with a "(%)" suffix on the header,
/// so the number in the file matches the number on screen instead of
/// silently differing by two orders of magnitude.
/// </para>
/// </remarks>
public static class CsvResultExporter
{
    public static async Task WriteAsync(
        QueryResultSet result,
        Stream destination,
        CsvExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(destination);

        var settings = options ?? CsvExportOptions.GermanExcel;
        var culture = CultureInfo.GetCultureInfo(settings.CultureName);

        var configuration = new CsvConfiguration(culture)
        {
            Delimiter = settings.Delimiter,
        };

        // leaveOpen: the caller owns the stream (an HTTP response body,
        // typically) and closing it here would truncate the download.
        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: settings.IncludeByteOrderMark),
            leaveOpen: true);

        await using var csv = new CsvWriter(writer, configuration);

        foreach (var column in result.Columns)
        {
            csv.WriteField(HeaderFor(column));
        }

        await csv.NextRecordAsync().ConfigureAwait(false);

        foreach (var row in result.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var i = 0; i < result.Columns.Count; i++)
            {
                csv.WriteField(Format(row[i], result.Columns[i], culture));
            }

            await csv.NextRecordAsync().ConfigureAwait(false);
        }

        await csv.FlushAsync().ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string HeaderFor(ResultColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        return column.IsPercent ? $"{column.Title} (%)" : column.Title;
    }

    private static string Format(object? value, ResultColumn column, CultureInfo culture) => value switch
    {
        null => string.Empty,
        double number when column.IsPercent => (number * 100).ToString("0.##", culture),
        double number => number.ToString("0.####", culture),
        DateTime timestamp => timestamp.ToString(
            column.Kind == ResultColumnKind.TimeDimension ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture),
        bool flag => flag ? "ja" : "nein",
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Suggests a file name from the query title and the current date.</summary>
    public static string SuggestFileName(string title, DateTimeOffset timestamp)
    {
        var safe = new string((title ?? "export")
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_')
            .ToArray())
            .Trim('_');

        if (safe.Length == 0)
        {
            safe = "export";
        }

        return $"{safe}_{timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";
    }
}

public sealed record CsvExportOptions
{
    public required string Delimiter { get; init; }

    public required string CultureName { get; init; }

    public bool IncludeByteOrderMark { get; init; } = true;

    /// <summary>Semicolon and decimal comma: what Excel expects in DACH.</summary>
    public static CsvExportOptions GermanExcel { get; } = new()
    {
        Delimiter = ";",
        CultureName = "de-DE",
    };

    /// <summary>RFC 4180 style, for downstream tooling rather than Excel.</summary>
    public static CsvExportOptions Standard { get; } = new()
    {
        Delimiter = ",",
        CultureName = "en-US",
        IncludeByteOrderMark = false,
    };
}
