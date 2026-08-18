using System.Text.Json;
using NLTSQL.Ai.Planning;
using NLTSQL.Core.Query;
using NLTSQL.Semantics.Validation;

// Shouldly ships its own SortDirection; ours is the one this file means.
using SortDirection = NLTSQL.Core.Query.SortDirection;

namespace NLTSQL.Ai.Tests;

/// <summary>
/// The mapper's tolerance is what stops a repair round being spent on spelling.
/// </summary>
public sealed class QuerySpecDraftMapperTests
{
    [Theory]
    [InlineData("equals", FilterOperator.Equals)]
    [InlineData("=", FilterOperator.Equals)]
    [InlineData("EQ", FilterOperator.Equals)]
    [InlineData("!=", FilterOperator.NotEquals)]
    [InlineData("<>", FilterOperator.NotEquals)]
    [InlineData(">=", FilterOperator.GreaterOrEqual)]
    [InlineData("greater_than_or_equal", FilterOperator.GreaterOrEqual)]
    [InlineData("in_last_months", FilterOperator.InLastMonths)]
    [InlineData("last_months", FilterOperator.InLastMonths)]
    public void OperatorSpellings_WithOneSensibleReading_AreAccepted(string spelling, FilterOperator expected)
    {
        var spec = Map($$"""{"entity":"e","filters":[{"field":"f","operator":"{{spelling}}","values":["x"]}]}""");

        spec!.Filters.ShouldHaveSingleItem().Operator.ShouldBe(expected);
    }

    [Theory]
    [InlineData("month", TimeGrain.Month)]
    [InlineData("Monthly", TimeGrain.Month)]
    [InlineData("monat", TimeGrain.Month)]
    [InlineData("quartal", TimeGrain.Quarter)]
    [InlineData("YEAR", TimeGrain.Year)]
    public void GrainSpellings_InEitherLanguage_AreAccepted(string spelling, TimeGrain expected)
    {
        var spec = Map($$"""{"entity":"e","group_by":[{"field":"d","grain":"{{spelling}}"}]}""");

        spec!.GroupBy.ShouldHaveSingleItem().Grain.ShouldBe(expected);
    }

    [Fact]
    public void UnrecognisedOperator_IsReportedRatherThanGuessedAt()
    {
        // Tolerance covers spelling, not meaning. Inventing an interpretation here would produce a
        // confident wrong answer instead of a question the user can rephrase.
        var issues = new List<ValidationIssue>();
        Map("""{"entity":"e","filters":[{"field":"f","operator":"ungefaehr","values":["x"]}]}""", issues);

        var issue = issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("draft.filter.operator_unknown");
        issue.Message.ShouldContain("equals");
    }

    [Fact]
    public void MissingOperatorWithASingleValue_MeansEquals()
    {
        var spec = Map("""{"entity":"e","filters":[{"field":"f","values":["offen"]}]}""");

        spec!.Filters.ShouldHaveSingleItem().Operator.ShouldBe(FilterOperator.Equals);
    }

    [Fact]
    public void MissingOperatorWithSeveralValues_IsAmbiguousAndReported()
    {
        var issues = new List<ValidationIssue>();
        Map("""{"entity":"e","filters":[{"field":"f","values":["a","b"]}]}""", issues);

        issues.ShouldContain(i => i.Code == "draft.filter.operator_missing");
    }

    [Fact]
    public void UnquotedNumbers_AreAccepted()
    {
        // Asked for the last twelve months, a model will sooner or later write 12 rather than "12".
        var spec = Map("""{"entity":"e","filters":[{"field":"d","operator":"in_last_months","values":[12]}]}""");

        spec!.Filters.ShouldHaveSingleItem().Values.ShouldBe(["12"]);
    }

    [Fact]
    public void ASingleValueWhereAListWasExpected_IsAccepted()
    {
        var spec = Map("""{"entity":"e","filters":[{"field":"f","operator":"equals","values":"offen"}]}""");

        spec!.Filters.ShouldHaveSingleItem().Values.ShouldBe(["offen"]);
    }

    [Fact]
    public void MissingEntity_IsFatalBecauseNothingCanBeResolvedWithoutIt()
    {
        var issues = new List<ValidationIssue>();
        var spec = Map("""{"measures":["umsatz"]}""", issues);

        spec.ShouldBeNull();
        issues.ShouldContain(i => i.Code == "draft.entity.missing");
    }

    [Fact]
    public void OmittedSections_BecomeEmptyLists_NotNulls()
    {
        var spec = Map("""{"entity":"e"}""");

        spec!.Measures.ShouldBeEmpty();
        spec.GroupBy.ShouldBeEmpty();
        spec.Filters.ShouldBeEmpty();
        spec.OrderBy.ShouldBeEmpty();
        spec.Limit.ShouldBeNull();
    }

    [Fact]
    public void SortDirection_DefaultsToDescending_WhichIsWhatRankingQuestionsMean()
    {
        var spec = Map("""{"entity":"e","order_by":[{"field":"umsatz"}]}""");

        spec!.OrderBy.ShouldHaveSingleItem().Direction.ShouldBe(SortDirection.Descending);
    }

    private static QuerySpec? Map(string json, List<ValidationIssue>? issues = null)
    {
        var draft = JsonSerializer.Deserialize<QuerySpecDraft>(json)!;
        return QuerySpecDraftMapper.Map(draft, issues ?? []);
    }
}
