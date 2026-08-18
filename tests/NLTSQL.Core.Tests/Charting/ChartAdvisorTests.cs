using NLTSQL.Core.Charting;
using NLTSQL.Core.Query;

namespace NLTSQL.Core.Tests.Charting;

public sealed class ChartAdvisorTests
{
    [Fact]
    public void SingleValueWithNoGrouping_IsAKpi()
    {
        var chart = ChartAdvisor.Choose([Value("umsatz")], rowCount: 1);

        chart.Kind.ShouldBe(ChartKind.Kpi);
        chart.ValueColumns.ShouldBe(["umsatz"]);
    }

    [Fact]
    public void TimeGrouping_IsALine()
    {
        var chart = ChartAdvisor.Choose(
            [Grouping("bestelldatum", TimeGrain.Month), Value("umsatz")],
            rowCount: 12);

        chart.Kind.ShouldBe(ChartKind.Line);
        chart.CategoryColumn.ShouldBe("bestelldatum");
    }

    [Fact]
    public void CategoricalGrouping_IsBars()
    {
        var chart = ChartAdvisor.Choose([Grouping("status"), Value("umsatz")], rowCount: 4);

        chart.Kind.ShouldBe(ChartKind.Bar);
        chart.CategoryColumn.ShouldBe("status");
    }

    [Fact]
    public void SeveralMeasuresOverTime_ShareTheLineChart()
    {
        var chart = ChartAdvisor.Choose(
            [Grouping("bestelldatum", TimeGrain.Month), Value("umsatz"), Value("rabatt")],
            rowCount: 12);

        chart.Kind.ShouldBe(ChartKind.Line);
        chart.ValueColumns.ShouldBe(["umsatz", "rabatt"]);
    }

    [Fact]
    public void TooManyCategories_FallsBackToATable()
    {
        // A bar per customer across four hundred customers is not a chart anybody can read.
        var chart = ChartAdvisor.Choose([Grouping("kundennummer"), Value("umsatz")], rowCount: 400);

        chart.Kind.ShouldBe(ChartKind.Table);
        chart.Reason.ShouldContain("400");
    }

    [Fact]
    public void TwoGroupings_FallBackToATable_RatherThanCollapsingADimension()
    {
        var chart = ChartAdvisor.Choose(
            [Grouping("status"), Grouping("vertriebskanal"), Value("umsatz")],
            rowCount: 12);

        chart.Kind.ShouldBe(ChartKind.Table);
    }

    [Fact]
    public void SeveralMeasuresWithoutGrouping_AreATable()
    {
        var chart = ChartAdvisor.Choose([Value("umsatz"), Value("anzahl")], rowCount: 1);

        chart.Kind.ShouldBe(ChartKind.Table);
    }

    [Fact]
    public void EmptyResult_IsATable_NotAnEmptyChart()
    {
        var chart = ChartAdvisor.Choose([Grouping("status"), Value("umsatz")], rowCount: 0);

        chart.Kind.ShouldBe(ChartKind.Table);
    }

    [Fact]
    public void ResultWithNoMeasure_IsATable()
    {
        var chart = ChartAdvisor.Choose([Grouping("status")], rowCount: 3);

        chart.Kind.ShouldBe(ChartKind.Table);
    }

    [Fact]
    public void EveryChoiceExplainsItself()
    {
        // The reason is shown next to the chart, so a user is never left wondering why their
        // question produced a table.
        ChartAdvisor.Choose([Grouping("status"), Value("umsatz")], 4).Reason.ShouldNotBeNullOrWhiteSpace();
        ChartAdvisor.Choose([Value("umsatz")], 1).Reason.ShouldNotBeNullOrWhiteSpace();
        ChartAdvisor.Choose([], 0).Reason.ShouldNotBeNullOrWhiteSpace();
    }

    private static ChartColumn Grouping(string alias, TimeGrain grain = TimeGrain.None) => new(alias, true, grain);

    private static ChartColumn Value(string alias) => new(alias, false);
}
