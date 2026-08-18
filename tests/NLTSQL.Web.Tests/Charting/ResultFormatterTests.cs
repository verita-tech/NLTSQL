using NLTSQL.Core.Query;
using NLTSQL.QueryEngine.Sql;
using NLTSQL.Semantics.Model;
using NLTSQL.Web.Charting;

namespace NLTSQL.Web.Tests.Charting;

/// <summary>
/// Formatting comes from the semantic model, and the same rules apply on screen and in an export —
/// a CSV that disagrees with the table it came from is why people stop trusting exports.
/// </summary>
public sealed class ResultFormatterTests
{
    [Fact]
    public void CurrencyUsesGermanSeparatorsAndTheModelsCurrencyCode()
    {
        var column = new ResultColumn("umsatz", "Umsatz", ResultColumnKind.Measure,
            Format: new ValueFormat(ValueFormatKind.Currency, 2, "EUR"));

        ResultFormatter.Format(1234567.891m, column).ShouldBe("1.234.567,89 EUR");
    }

    [Fact]
    public void PercentIsScaled_BecauseTheModelStoresARatio()
    {
        // A rabattquote of 0.075 means 7,5 %. Showing "0,08 %" would be wrong by two orders of
        // magnitude and entirely plausible-looking.
        var column = new ResultColumn("quote", "Rabattquote", ResultColumnKind.Measure,
            Format: new ValueFormat(ValueFormatKind.Percent, 1));

        ResultFormatter.Format(0.075m, column).ShouldBe("7,5 %");
    }

    [Fact]
    public void IntegersCarryNoDecimals()
    {
        var column = new ResultColumn("anzahl", "Anzahl", ResultColumnKind.Measure,
            Format: new ValueFormat(ValueFormatKind.Integer, 0));

        ResultFormatter.Format(6000L, column).ShouldBe("6.000");
    }

    [Theory]
    [InlineData(TimeGrain.Day, "15.03.2026")]
    [InlineData(TimeGrain.Month, "03.2026")]
    [InlineData(TimeGrain.Quarter, "Q1 2026")]
    [InlineData(TimeGrain.Year, "2026")]
    public void TimeGroupingsAreFormattedByTheirBucket(TimeGrain grain, string expected)
    {
        // A monthly figure shown as 01.03.2026 invites being read as a single day's number.
        var column = new ResultColumn("d", "Datum", ResultColumnKind.Grouping, DataType.Date, grain);

        ResultFormatter.Format(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), column).ShouldBe(expected);
    }

    [Fact]
    public void WeeksUseIsoWeekNumbers_MatchingTheSqlThatBucketedThem()
    {
        var column = new ResultColumn("d", "Datum", ResultColumnKind.Grouping, DataType.Date, TimeGrain.Week);

        ResultFormatter.Format(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), column).ShouldBe("KW 02 2026");
    }

    [Fact]
    public void MissingValuesReadAsDeliberate_NotAsARenderingFailure()
    {
        var column = new ResultColumn("umsatz", "Umsatz", ResultColumnKind.Measure);

        ResultFormatter.Format(null, column).ShouldBe("—");
    }

    [Theory]
    [InlineData(42)]
    [InlineData(42L)]
    [InlineData(42.0)]
    public void NumbersAreAcceptedInWhateverTypeTheDriverReturned(object value)
    {
        // Npgsql and ODP.NET disagree about which CLR type a NUMERIC comes back as.
        ResultFormatter.ToNumber(value).ShouldBe(42m);
    }
}
