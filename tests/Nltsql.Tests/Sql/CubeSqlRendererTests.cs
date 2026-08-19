using Nltsql.Core.Queries;
using Nltsql.Core.Sql;
using Shouldly;

namespace Nltsql.Tests.Sql;

/// <summary>
/// The rendered SQL is what Metabase stores and what a customer may edit
/// later, so these tests pin the properties that keep it correct rather
/// than the exact formatting.
/// </summary>
public sealed class CubeSqlRendererTests
{
    [Fact]
    public void Wraps_measures_in_MEASURE_so_the_semantic_layer_aggregates()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Dimensions = ["machine_name"],
        };

        var sql = CubeSqlRenderer.Render(query, TestModel.View);

        sql.ShouldContain("MEASURE(\"oee\")");
        sql.ShouldContain("FROM \"fertigung\"");
        sql.ShouldContain("GROUP BY 1");
    }

    [Fact]
    public void Buckets_the_time_axis_with_the_requested_granularity()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            TimeDimension = new QueryTimeDimension
            {
                Dimension = "started_at",
                Granularity = TimeGranularity.Month,
                DateRange = QueryDateRange.Of(RelativeDateRange.Last12Months),
            },
        };

        var sql = CubeSqlRenderer.Render(query, TestModel.View);

        sql.ShouldContain("DATE_TRUNC('month', \"started_at\")");
    }

    [Fact]
    public void Keeps_rolling_ranges_relative_so_the_card_stays_current()
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

        var sql = CubeSqlRenderer.Render(query, TestModel.View);

        // A resolved date here would freeze the Metabase card at the day
        // it was created.
        sql.ShouldContain("NOW()");
        sql.ShouldContain("INTERVAL '30 days'");
    }

    [Fact]
    public void Treats_a_fixed_end_date_as_inclusive()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            TimeDimension = new QueryTimeDimension
            {
                Dimension = "started_at",
                DateRange = QueryDateRange.Between(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            },
        };

        var sql = CubeSqlRenderer.Render(query, TestModel.View);

        // Exclusive upper bound of the following day, so timestamps
        // during 31 January are not silently dropped.
        sql.ShouldContain("< DATE '2026-02-01'");
    }

    [Fact]
    public void Puts_measure_filters_in_HAVING_and_dimension_filters_in_WHERE()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Dimensions = ["machine_name"],
            Filters =
            [
                new QueryFilter
                {
                    Member = "machine_name",
                    Operator = FilterOperator.Equals,
                    Values = ["Presse 1"],
                },
                new QueryFilter
                {
                    Member = "oee",
                    Operator = FilterOperator.LessThan,
                    Values = ["0.7"],
                },
            ],
        };

        var sql = CubeSqlRenderer.Render(query, TestModel.View);

        sql.ShouldContain("WHERE \"machine_name\" = 'Presse 1'");
        sql.ShouldContain("HAVING MEASURE(\"oee\") < 0.7");
    }

    [Fact]
    public void Renders_multiple_values_as_an_IN_list()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Filters = [new QueryFilter
            {
                Member = "machine_name",
                Operator = FilterOperator.Equals,
                Values = ["Presse 1", "Presse 2"],
            }],
        };

        var sql = CubeSqlRenderer.Render(query, TestModel.View);

        sql.ShouldContain("IN ('Presse 1', 'Presse 2')");
    }

    [Fact]
    public void Escapes_apostrophes_in_filter_values()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Filters = [new QueryFilter
            {
                Member = "machine_name",
                Operator = FilterOperator.Equals,
                Values = ["O'Brien' OR 1=1 --"],
            }],
        };

        var sql = CubeSqlRenderer.Render(query, TestModel.View);

        // The apostrophes are doubled, so the payload stays one literal
        // and never becomes syntax.
        sql.ShouldContain("'O''Brien'' OR 1=1 --'");
        sql.ShouldNotContain("= 'O'Brien'");
    }

    [Fact]
    public void Refuses_to_render_a_member_that_is_not_in_the_view()
    {
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["umsatz"] };

        Should.Throw<InvalidOperationException>(() => CubeSqlRenderer.Render(query, TestModel.View));
    }

    [Fact]
    public void Orders_by_ordinal_so_expressions_can_be_sorted()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Dimensions = ["machine_name"],
            Order = [new QueryOrder { Member = "oee", Direction = Nltsql.Core.Queries.SortDirection.Descending }],
        };

        var sql = CubeSqlRenderer.Render(query, TestModel.View);

        sql.ShouldContain("ORDER BY 2 DESC");
    }
}

public sealed class SqlTextTests
{
    [Fact]
    public void Doubles_embedded_quotes_in_identifiers()
    {
        SqlText.Identifier("we\"ird").ShouldBe("\"we\"\"ird\"");
    }

    [Fact]
    public void Rejects_control_characters_in_literals()
    {
        Should.Throw<ArgumentException>(() => SqlText.Literal("line\nbreak"));
    }
}
