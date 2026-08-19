using System.Text.Json;
using Nltsql.Core.Queries;
using Nltsql.Core.Results;
using Nltsql.Infrastructure.Cube;
using Shouldly;

namespace Nltsql.Tests.Cube;

public sealed class CubeResultMapperTests
{
    private static QueryResultSet Map(string json, SemanticQuery query)
    {
        using var document = JsonDocument.Parse(json);
        return CubeResultMapper.Map(document.RootElement, query, TestModel.View, TimeSpan.FromMilliseconds(42));
    }

    [Fact]
    public void Orders_columns_the_way_the_query_was_built()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Dimensions = ["machine_name"],
        };

        var result = Map("""
        {
          "data": [ { "fertigung.machine_name": "Presse 1", "fertigung.oee": 0.75 } ],
          "annotation": { "measures": {}, "dimensions": {} }
        }
        """, query);

        // Breakdowns first, measures last — regardless of JSON key order.
        result.Columns.Select(c => c.Key).ShouldBe(["machine_name", "oee"]);
    }

    [Fact]
    public void Reads_a_bucketed_time_dimension_under_its_granularity_key()
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

        // Cube keys a bucketed axis as "<member>.<granularity>".
        var result = Map("""
        {
          "data": [ { "fertigung.started_at.month": "2026-05-01T00:00:00.000", "fertigung.oee": 0.8 } ]
        }
        """, query);

        result.Rows[0][0].ShouldBeOfType<DateTime>();
    }

    [Fact]
    public void Falls_back_to_the_bare_member_key_when_no_granularity_is_used()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            TimeDimension = new QueryTimeDimension { Dimension = "started_at" },
        };

        var result = Map("""
        { "data": [ { "fertigung.started_at": "2026-05-01T00:00:00.000", "fertigung.oee": 0.8 } ] }
        """, query);

        result.Rows[0][0].ShouldBeOfType<DateTime>();
    }

    [Fact]
    public void Parses_measures_that_arrive_as_JSON_strings()
    {
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["scrap_qty"] };

        // Postgres numeric and bigint come back quoted.
        var result = Map("""{ "data": [ { "fertigung.scrap_qty": "1234.5" } ] }""", query);

        result.Rows[0][0].ShouldBe(1234.5);
    }

    [Fact]
    public void Takes_the_format_from_the_annotation()
    {
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["oee"] };

        var result = Map("""
        {
          "data": [ { "fertigung.oee": 0.75 } ],
          "annotation": { "measures": { "fertigung.oee": { "shortTitle": "OEE", "format": "percent" } } }
        }
        """, query);

        result.Columns[0].IsPercent.ShouldBeTrue();
    }

    [Fact]
    public void Unwraps_a_results_array_payload()
    {
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["oee"] };

        // Cube returns either shape depending on version and query type.
        var result = Map("""{ "results": [ { "data": [ { "fertigung.oee": 0.5 } ] } ] }""", query);

        result.RowCount.ShouldBe(1);
    }

    [Fact]
    public void Reports_when_a_pre_aggregation_answered_the_query()
    {
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["oee"] };

        var result = Map("""
        {
          "data": [ { "fertigung.oee": 0.5 } ],
          "usedPreAggregations": { "fertigung.daily_rollup": {} }
        }
        """, query);

        result.FromPreAggregation.ShouldBeTrue();
    }

    [Fact]
    public void Flags_a_result_that_hit_the_row_limit()
    {
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["oee"], Limit = 2 };

        var result = Map("""
        { "data": [ { "fertigung.oee": 0.5 }, { "fertigung.oee": 0.6 } ] }
        """, query);

        // Silently showing a truncated answer is worse than saying so.
        result.IsTruncated.ShouldBeTrue();
    }

    [Fact]
    public void Yields_null_for_a_member_missing_from_a_row()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Dimensions = ["machine_name"],
        };

        var result = Map("""{ "data": [ { "fertigung.machine_name": "Presse 1" } ] }""", query);

        result.Rows[0][1].ShouldBeNull();
    }
}

public sealed class CubeQueryTranslatorTests
{
    [Fact]
    public void Qualifies_members_with_the_view_name()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Dimensions = ["machine_name"],
        };

        var json = CubeQueryTranslator.ToCubeQuery(query).ToJsonString();

        json.ShouldContain("fertigung.oee");
        json.ShouldContain("fertigung.machine_name");
    }

    [Fact]
    public void Emits_order_as_an_array_to_preserve_precedence()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee", "scrap_qty"],
            Order =
            [
                new QueryOrder { Member = "oee", Direction = Nltsql.Core.Queries.SortDirection.Descending },
                new QueryOrder { Member = "scrap_qty", Direction = Nltsql.Core.Queries.SortDirection.Ascending },
            ],
        };

        var json = CubeQueryTranslator.ToCubeQuery(query).ToJsonString();

        json.ShouldContain("""["fertigung.oee","desc"]""");
        json.ShouldContain("""["fertigung.scrap_qty","asc"]""");
    }

    [Fact]
    public void Omits_values_for_unary_operators()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Filters = [new QueryFilter { Member = "machine_name", Operator = FilterOperator.Set }],
        };

        var json = CubeQueryTranslator.ToCubeQuery(query).ToJsonString();

        json.ShouldContain("\"operator\":\"set\"");
        json.ShouldNotContain("\"values\"");
    }

    [Fact]
    public void Translates_a_rolling_range_to_Cubes_shorthand()
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

        var json = CubeQueryTranslator.ToCubeQuery(query).ToJsonString();

        json.ShouldContain("\"dateRange\":\"last 30 days\"");
    }
}
