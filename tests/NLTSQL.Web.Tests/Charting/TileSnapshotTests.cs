using NLTSQL.Core.Query;
using NLTSQL.Core.Serialization;
using NLTSQL.QueryEngine.Execution;
using NLTSQL.QueryEngine.Sql;
using NLTSQL.Semantics.Model;
using NLTSQL.Web.Charting;

namespace NLTSQL.Web.Tests.Charting;

/// <summary>
/// A snapshot tile is read back weeks after it was written, so what matters is the round trip.
/// </summary>
public sealed class TileSnapshotTests
{
    private static readonly IReadOnlyList<ResultColumn> Columns =
    [
        new("bestelldatum", "Bestelldatum", ResultColumnKind.Grouping, DataType.Date, TimeGrain.Month),
        new("status", "Status", ResultColumnKind.Grouping, DataType.String),
        new("umsatz", "Umsatz", ResultColumnKind.Measure, Format: new ValueFormat(ValueFormatKind.Currency, 2, "EUR")),
        new("anzahl", "Anzahl", ResultColumnKind.Measure, Format: new ValueFormat(ValueFormatKind.Integer, 0)),
    ];

    [Fact]
    public void NumbersSurviveAsNumbers_SoASnapshotCanStillBeCharted()
    {
        // Storing the rendered strings would have been shorter and would have turned every frozen
        // tile into a picture of a table.
        var restored = RoundTrip([[new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "offen", 1234.56m, 42L]]);

        restored.Rows[0][2].ShouldBe(1234.56m);
        ResultFormatter.Format(restored.Rows[0][2], Columns[2]).ShouldBe("1.234,56 EUR");
    }

    [Fact]
    public void GroupedDatesComeBackAsDates_NotAsStrings()
    {
        // JSON has no date type, so this is the one lossy spot and the one worth a test: without
        // re-parsing, a monthly axis would silently turn into raw ISO text.
        var restored = RoundTrip([[new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "offen", 1m, 1L]]);

        restored.Rows[0][0].ShouldBeOfType<DateTime>();
        ResultFormatter.Format(restored.Rows[0][0], Columns[0]).ShouldBe("03.2026");
    }

    [Fact]
    public void TextThatLooksLikeADate_InANonTemporalColumn_StaysText()
    {
        // Parsing every string would turn a product code such as "2026-03" into a timestamp.
        var restored = RoundTrip([[new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "2026-03", 1m, 1L]]);

        restored.Rows[0][1].ShouldBe("2026-03");
    }

    [Fact]
    public void NullsStayNull()
    {
        var restored = RoundTrip([[null, null, null, null]]);

        restored.Rows[0].ShouldAllBe(value => value == null);
        ResultFormatter.Format(restored.Rows[0][2], Columns[2]).ShouldBe("—");
    }

    [Fact]
    public void ColumnMetadataSurvives_SoFormattingIsUnchanged()
    {
        var restored = RoundTrip([[new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "offen", 1m, 1L]]);

        restored.Columns.Select(c => c.Alias).ShouldBe(["bestelldatum", "status", "umsatz", "anzahl"]);
        restored.Columns[0].Grain.ShouldBe(TimeGrain.Month);
        restored.Columns[2].Format!.Currency.ShouldBe("EUR");
    }

    [Fact]
    public void TruncationFlagSurvives_SoAFrozenPartialResultStillSaysSo()
    {
        var source = new QueryResult(Columns, [[null, "a", 1m, 1L]], Truncated: true, TimeSpan.FromSeconds(1));

        var restored = TileSnapshot.From(source).ToResult();

        restored.Truncated.ShouldBeTrue();
    }

    [Fact]
    public void TheSnapshotRecordsWhenItWasTaken()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var snapshot = TileSnapshot.From(new QueryResult(Columns, [], false, TimeSpan.Zero));

        snapshot.TakenAt.ShouldBeGreaterThan(before);
    }

    private static QueryResult RoundTrip(IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var source = new QueryResult(Columns, rows, Truncated: false, TimeSpan.FromMilliseconds(12));

        // Through the same serializer the database column uses, not an in-memory shortcut.
        var json = NltsqlJson.Serialize(TileSnapshot.From(source));
        var snapshot = NltsqlJson.Deserialize<TileSnapshot>(json);

        return snapshot.ShouldNotBeNull().ToResult();
    }
}
