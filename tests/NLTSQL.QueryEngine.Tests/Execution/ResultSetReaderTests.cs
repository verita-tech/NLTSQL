using NLTSQL.QueryEngine.Execution;
using NLTSQL.QueryEngine.Sql;

namespace NLTSQL.QueryEngine.Tests.Execution;

public sealed class ResultSetReaderTests
{
    private static readonly IReadOnlyList<ResultColumn> TwoColumns =
    [
        new("status", "Auftragsstatus", ResultColumnKind.Grouping),
        new("umsatz", "Umsatz", ResultColumnKind.Measure),
    ];

    [Fact]
    public async Task RowsAreReadInColumnOrder()
    {
        var reader = new StubDataReader(2, [["offen", 100m], ["versendet", 250m]]);

        var (rows, truncated) = await ResultSetReader.ReadAsync(reader, TwoColumns, maxRows: 10);

        rows.Count.ShouldBe(2);
        rows[0].ShouldBe(["offen", 100m]);
        rows[1].ShouldBe(["versendet", 250m]);
        truncated.ShouldBeFalse();
    }

    [Fact]
    public async Task DatabaseNullsBecomeClrNulls()
    {
        // Leaving DBNull.Value in the result would push a database-specific sentinel all the way
        // into charting and CSV export, each of which would then have to know about it.
        var reader = new StubDataReader(2, [[null, 100m]]);

        var (rows, _) = await ResultSetReader.ReadAsync(reader, TwoColumns, maxRows: 10);

        rows[0][0].ShouldBeNull();
    }

    [Fact]
    public async Task ReadingStopsAtTheRowCap()
    {
        var reader = new StubDataReader(2, [["a", 1m], ["b", 2m], ["c", 3m]]);

        var (rows, truncated) = await ResultSetReader.ReadAsync(reader, TwoColumns, maxRows: 2);

        rows.Count.ShouldBe(2);
        truncated.ShouldBeTrue();
    }

    [Fact]
    public async Task ExactlyReachingTheCapIsReportedAsTruncated()
    {
        // The statement carries the same limit, so a result of exactly maxRows cannot be told apart
        // from one the limit cut short. Warning needlessly is the harmless direction.
        var reader = new StubDataReader(2, [["a", 1m], ["b", 2m]]);

        var (_, truncated) = await ResultSetReader.ReadAsync(reader, TwoColumns, maxRows: 2);

        truncated.ShouldBeTrue();
    }

    [Fact]
    public async Task EmptyResultIsNotTruncated()
    {
        var reader = new StubDataReader(2, []);

        var (rows, truncated) = await ResultSetReader.ReadAsync(reader, TwoColumns, maxRows: 10);

        rows.ShouldBeEmpty();
        truncated.ShouldBeFalse();
    }

    [Fact]
    public async Task ShapeMismatchIsRefusedRatherThanMislabelled()
    {
        // Reading positionally past a mismatch would put values under the wrong headings, which
        // looks like a plausible answer rather than an error.
        var reader = new StubDataReader(3, [["a", 1m, "x"]]);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => ResultSetReader.ReadAsync(reader, TwoColumns, maxRows: 10));

        exception.Message.ShouldContain("3 columns");
        exception.Message.ShouldContain("2 were described");
    }

    [Fact]
    public async Task CancellationIsHonoured()
    {
        var reader = new StubDataReader(2, [["a", 1m]]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => ResultSetReader.ReadAsync(reader, TwoColumns, maxRows: 10, cancellation.Token));
    }
}
