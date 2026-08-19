using System.Text;
using Nltsql.Core.Results;
using Nltsql.Infrastructure.Export;
using Shouldly;

namespace Nltsql.Tests.Export;

public sealed class CsvResultExporterTests
{
    private static readonly QueryResultSet Result = new()
    {
        Columns =
        [
            new ResultColumn("machine_name", "Maschine", ResultColumnKind.Dimension, SemanticValueType.String),
            new ResultColumn("oee", "OEE", ResultColumnKind.Measure, SemanticValueType.Number, "percent"),
            new ResultColumn("scrap_qty", "Ausschussmenge", ResultColumnKind.Measure, SemanticValueType.Number),
        ],
        Rows =
        [
            ["Presse 1", 0.7521, 1234.0],
            ["Gießerei; Süd", 0.6, 12.5],
        ],
    };

    [Fact]
    public async Task Writes_German_Excel_defaults()
    {
        var csv = await ExportAsync(CsvExportOptions.GermanExcel);

        // Semicolon delimiter and decimal comma: what Excel with German
        // regional settings reads without an import wizard.
        csv.ShouldContain("Maschine;OEE (%);Ausschussmenge");
        csv.ShouldContain("75,2");
    }

    [Fact]
    public async Task Converts_percentage_measures_to_percent_values()
    {
        var csv = await ExportAsync(CsvExportOptions.GermanExcel);

        // Cube reports a 0..1 ratio; exporting that raw would read as
        // 0,75 % and differ from what the screen showed.
        csv.ShouldContain("75,21");
        csv.ShouldContain("OEE (%)");
    }

    [Fact]
    public async Task Quotes_values_containing_the_delimiter()
    {
        var csv = await ExportAsync(CsvExportOptions.GermanExcel);

        csv.ShouldContain("\"Gießerei; Süd\"");
    }

    [Fact]
    public async Task Emits_a_byte_order_mark_for_Excel()
    {
        using var buffer = new MemoryStream();
        await CsvResultExporter.WriteAsync(Result, buffer, CsvExportOptions.GermanExcel);

        var bytes = buffer.ToArray();

        // Without the BOM Excel decodes the file as ANSI and every umlaut
        // in the data comes out wrong.
        bytes[0].ShouldBe((byte)0xEF);
        bytes[1].ShouldBe((byte)0xBB);
        bytes[2].ShouldBe((byte)0xBF);
    }

    [Fact]
    public async Task Supports_a_standard_profile_for_downstream_tooling()
    {
        var csv = await ExportAsync(CsvExportOptions.Standard);

        csv.ShouldContain("Maschine,OEE (%),Ausschussmenge");
        csv.ShouldContain("75.21");
    }

    [Fact]
    public async Task Leaves_the_destination_stream_open_for_the_caller()
    {
        using var buffer = new MemoryStream();
        await CsvResultExporter.WriteAsync(Result, buffer);

        // The HTTP response body is owned by ASP.NET Core; closing it
        // here would truncate the download.
        buffer.CanWrite.ShouldBeTrue();
    }

    [Theory]
    [InlineData("OEE je Maschine", "OEE_je_Maschine_2026-08-19.csv")]
    [InlineData("Ausschuss / Woche", "Ausschuss___Woche_2026-08-19.csv")]
    [InlineData("", "export_2026-08-19.csv")]
    public void Builds_a_safe_file_name(string title, string expected)
    {
        var name = CsvResultExporter.SuggestFileName(title, new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero));

        name.ShouldBe(expected);
    }

    private static async Task<string> ExportAsync(CsvExportOptions options)
    {
        using var buffer = new MemoryStream();
        await CsvResultExporter.WriteAsync(Result, buffer, options);

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
