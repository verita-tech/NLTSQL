using Nltsql.Core.Queries;
using Shouldly;

namespace Nltsql.Tests.Queries;

/// <summary>
/// The validator is the boundary that makes planner output safe to run,
/// so these cases are written from the angle of "what could get through
/// that should not".
/// </summary>
public sealed class SemanticQueryValidatorTests
{
    [Fact]
    public void Accepts_a_well_formed_query()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Dimensions = ["machine_name"],
            Order = [new QueryOrder { Member = "oee" }],
        };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeTrue(result.Summary);
    }

    [Fact]
    public void Rejects_an_unknown_view()
    {
        var query = new SemanticQuery { View = "vertrieb", Measures = ["oee"] };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().Message.ShouldContain("vertrieb");
    }

    [Fact]
    public void Rejects_an_invented_measure()
    {
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["umsatz"] };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Suggests_the_intended_member_for_a_near_miss()
    {
        // The kind of mistake a planner actually makes: a real business
        // term that is not the technical name.
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["Anlageneffektivitaet"] };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeFalse();
        result.Summary.ShouldContain("oee");
    }

    [Fact]
    public void Explains_when_a_dimension_was_asked_for_as_a_measure()
    {
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["machine_name"] };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeFalse();
        result.Summary.ShouldContain("Merkmal");
    }

    [Fact]
    public void Rejects_a_non_time_dimension_as_the_time_axis()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            TimeDimension = new QueryTimeDimension
            {
                Dimension = "machine_name",
                DateRange = QueryDateRange.Of(RelativeDateRange.Last30Days),
            },
        };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeFalse();
        result.Summary.ShouldContain("Zeitmerkmal");
    }

    [Fact]
    public void Rejects_a_text_operator_on_a_numeric_member()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Filters = [new QueryFilter
            {
                Member = "oee",
                Operator = FilterOperator.Contains,
                Values = ["0,5"],
            }],
        };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_non_numeric_value_for_a_numeric_member()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Filters = [new QueryFilter
            {
                Member = "oee",
                Operator = FilterOperator.GreaterThan,
                Values = ["hoch"],
            }],
        };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Allows_filtering_on_a_measure()
    {
        // Cube turns this into a HAVING clause; it is a legitimate ask.
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Dimensions = ["machine_name"],
            Filters = [new QueryFilter
            {
                Member = "oee",
                Operator = FilterOperator.LessThan,
                Values = ["0.7"],
            }],
        };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeTrue(result.Summary);
    }

    [Fact]
    public void Rejects_sorting_by_a_member_that_is_not_selected()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            Order = [new QueryOrder { Member = "scrap_qty" }],
        };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_query_that_selects_nothing()
    {
        var result = SemanticQueryValidator.Validate(new SemanticQuery { View = TestModel.ViewName }, TestModel.Model);

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(QueryLimits.MaxRows + 1)]
    public void Rejects_a_row_limit_outside_the_allowed_range(int limit)
    {
        var query = new SemanticQuery { View = TestModel.ViewName, Measures = ["oee"], Limit = limit };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_date_range_that_ends_before_it_starts()
    {
        var query = new SemanticQuery
        {
            View = TestModel.ViewName,
            Measures = ["oee"],
            TimeDimension = new QueryTimeDimension
            {
                Dimension = "started_at",
                DateRange = QueryDateRange.Between(new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1)),
            },
        };

        var result = SemanticQueryValidator.Validate(query, TestModel.Model);

        result.IsValid.ShouldBeFalse();
    }
}
