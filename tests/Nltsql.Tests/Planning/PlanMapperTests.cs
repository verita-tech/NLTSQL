using System.Text.Json;
using Nltsql.Core.Queries;
using Nltsql.Infrastructure.Planning;
using Shouldly;

namespace Nltsql.Tests.Planning;

/// <summary>
/// The mapper is deliberately forgiving, so that the validator downstream
/// reports the errors that matter — unknown members — instead of noise a
/// local model produces on the way there.
/// </summary>
public sealed class PlanMapperTests
{
    [Fact]
    public void Rejects_a_plan_without_a_view()
    {
        Map("""{"answerable": true, "measures": ["oee"]}""").ShouldBeNull();
    }

    [Fact]
    public void Drops_filters_and_orders_without_a_member()
    {
        var query = Map("""
            {
              "view": "fertigung",
              "measures": ["oee"],
              "filters": [{"member": "", "operator": "Equals", "values": ["x"]},
                          {"member": "machine_name", "operator": "Equals", "values": ["CNC 2"]}],
              "order": [{"member": "  ", "direction": "Ascending"}]
            }
            """);

        query.ShouldNotBeNull();
        query.Filters.ShouldHaveSingleItem().Member.ShouldBe("machine_name");
        query.Order.ShouldBeEmpty();
    }

    [Fact]
    public void Falls_back_on_an_unknown_operator_or_direction()
    {
        var query = Map("""
            {
              "view": "fertigung",
              "measures": ["oee"],
              "filters": [{"member": "machine_name", "operator": "SoundsLike", "values": ["x"]}],
              "order": [{"member": "oee", "direction": "Sideways"}]
            }
            """);

        query.ShouldNotBeNull();
        query.Filters[0].Operator.ShouldBe(FilterOperator.Equals);
        query.Order[0].Direction.ShouldBe(Nltsql.Core.Queries.SortDirection.Descending);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Clamps_a_limit_below_the_floor(int limit)
    {
        var query = Map($$"""{"view": "fertigung", "measures": ["oee"], "limit": {{limit}}}""");

        query.ShouldNotBeNull();
        query.Limit.ShouldBe(1);
    }

    [Fact]
    public void Clamps_a_limit_above_the_ceiling()
    {
        var query = Map("""{"view": "fertigung", "measures": ["oee"], "limit": 999999}""");

        query.ShouldNotBeNull();
        query.Limit.ShouldBe(QueryLimits.MaxRows);
    }

    [Fact]
    public void Uses_the_default_limit_when_none_is_given()
    {
        var query = Map("""{"view": "fertigung", "measures": ["oee"]}""");

        query.ShouldNotBeNull();
        query.Limit.ShouldBe(QueryLimits.DefaultRows);
    }

    [Fact]
    public void Prefers_a_relative_range_over_fixed_dates()
    {
        // A rolling window has to stay symbolic: pinning it to dates here
        // would quietly freeze a saved dashboard tile in time.
        var query = Map("""
            {
              "view": "fertigung",
              "measures": ["oee"],
              "time_dimension": {
                "dimension": "started_at",
                "granularity": "Month",
                "relative_range": "Last30Days",
                "from": "2024-01-01",
                "to": "2024-06-30"
              }
            }
            """);

        query.ShouldNotBeNull();
        query.TimeDimension!.Granularity.ShouldBe(TimeGranularity.Month);
        query.TimeDimension.DateRange!.Relative.ShouldBe(RelativeDateRange.Last30Days);
    }

    [Fact]
    public void Reads_a_fixed_range_when_there_is_no_relative_one()
    {
        var query = Map("""
            {
              "view": "fertigung",
              "measures": ["oee"],
              "time_dimension": {"dimension": "started_at", "from": "2024-01-01", "to": "2024-06-30"}
            }
            """);

        query.ShouldNotBeNull();
        query.TimeDimension!.DateRange!.From.ShouldBe(new DateOnly(2024, 1, 1));
        query.TimeDimension.DateRange.To.ShouldBe(new DateOnly(2024, 6, 30));
    }

    [Fact]
    public void Ignores_a_time_dimension_without_a_dimension_name()
    {
        var query = Map("""
            {"view": "fertigung", "measures": ["oee"], "time_dimension": {"granularity": "Month"}}
            """);

        query.ShouldNotBeNull();
        query.TimeDimension.ShouldBeNull();
    }

    [Fact]
    public void Ignores_an_unparseable_granularity()
    {
        var query = Map("""
            {
              "view": "fertigung",
              "measures": ["oee"],
              "time_dimension": {"dimension": "started_at", "granularity": "Fortnight"}
            }
            """);

        query.ShouldNotBeNull();
        query.TimeDimension!.Granularity.ShouldBeNull();
    }

    private static SemanticQuery? Map(string json) =>
        PlanMapper.ToQuery(JsonSerializer.Deserialize<PlanContract>(json)!);
}
