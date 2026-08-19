using Nltsql.Core.Charts;
using Nltsql.Core.Queries;
using Shouldly;

namespace Nltsql.Tests.Queries;

public sealed class SemanticQuerySerializerTests
{
    [Fact]
    public void Round_trips_a_full_query()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Dimensions = ["machine_name"],
            TimeDimension = new QueryTimeDimension
            {
                Dimension = "started_at",
                Granularity = TimeGranularity.Month,
                DateRange = QueryDateRange.Of(RelativeDateRange.Last12Months),
            },
            Filters = [new QueryFilter
            {
                Member = "machine_name",
                Operator = FilterOperator.Contains,
                Values = ["Presse"],
            }],
            Order = [new QueryOrder { Member = "oee", Direction = Nltsql.Core.Queries.SortDirection.Ascending }],
            Limit = 250,
        };

        var json = SemanticQuerySerializer.Serialize(query);
        var restored = SemanticQuerySerializer.Deserialize(json).ShouldNotBeNull();

        // Compared field by field rather than with record equality:
        // SemanticQuery holds IReadOnlyList members, and the compiler's
        // generated Equals compares those by reference, so two queries
        // with identical contents are never "equal".
        restored.View.ShouldBe(query.View);
        restored.Measures.ShouldBe(query.Measures);
        restored.Dimensions.ShouldBe(query.Dimensions);
        restored.TimeDimension.ShouldBe(query.TimeDimension);
        restored.Filters[0].Member.ShouldBe("machine_name");
        restored.Filters[0].Operator.ShouldBe(FilterOperator.Contains);
        restored.Filters[0].Values.ShouldBe(["Presse"]);
        restored.Order[0].Direction.ShouldBe(Nltsql.Core.Queries.SortDirection.Ascending);
        restored.Limit.ShouldBe(250);

        // Re-serialising must reproduce the same document, which is what
        // keeps a stored query stable across save/load cycles.
        SemanticQuerySerializer.Serialize(restored).ShouldBe(json);
    }

    [Fact]
    public void Writes_enums_as_names_so_stored_queries_survive_reordering()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            TimeDimension = new QueryTimeDimension
            {
                Dimension = "started_at",
                DateRange = QueryDateRange.Of(RelativeDateRange.Last30Days),
            },
        };

        var json = SemanticQuerySerializer.Serialize(query);

        // A numeric ordinal here would turn a "Last30Days" tile into a
        // different range the moment the enum gains a member.
        json.ShouldContain("Last30Days");
    }

    [Fact]
    public void Returns_null_for_empty_input()
    {
        SemanticQuerySerializer.Deserialize("  ").ShouldBeNull();
    }
}

public sealed class ChartTypeSelectorTests
{
    [Fact]
    public void Suggests_a_single_number_for_one_measure_without_breakdown()
    {
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["oee"] };

        ChartTypeSelector.Suggest(query).ShouldBe(ChartType.Scalar);
    }

    [Fact]
    public void Suggests_a_line_for_a_measure_over_time()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            TimeDimension = new QueryTimeDimension
            {
                Dimension = "started_at",
                Granularity = TimeGranularity.Month,
            },
        };

        ChartTypeSelector.Suggest(query).ShouldBe(ChartType.Line);
    }

    [Fact]
    public void Suggests_bars_for_one_measure_broken_down_once()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Dimensions = ["machine_name"],
        };

        ChartTypeSelector.Suggest(query).ShouldBe(ChartType.Bar);
    }

    [Fact]
    public void Falls_back_to_a_table_when_no_chart_would_read_well()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee", "scrap_qty"],
            Dimensions = ["machine_name"],
            TimeDimension = new QueryTimeDimension
            {
                Dimension = "started_at",
                Granularity = TimeGranularity.Day,
            },
        };

        ChartTypeSelector.Suggest(query).ShouldBe(ChartType.Table);
    }
}
