using NLTSQL.Core.Query;
using NLTSQL.Semantics.Validation;

using SortDirection = NLTSQL.Core.Query.SortDirection;

namespace NLTSQL.QueryEngine.Tests;

/// <summary>
/// The resolver is the gate between the language model and the database.
/// </summary>
/// <remarks>
/// Two things are being checked here, and the second matters as much as the first: that bad specs
/// are refused, and that the refusal says what was available instead. The message is fed straight
/// back to the model as the repair prompt, so a message that only says "unknown" leaves it to
/// guess again, whereas one that lists the real names usually gets a correct spec next attempt.
/// </remarks>
public sealed class QuerySpecResolverTests
{
    [Fact]
    public void UnknownEntity_IsRefusedAndTheAvailableOnesAreNamed()
    {
        var issue = ResolveExpectingFailure(new QuerySpec { Entity = "rechnung", Measures = ["umsatz"] });

        issue.Code.ShouldBe("query.entity.unknown");
        issue.Message.ShouldContain("auftrag");
    }

    [Fact]
    public void UnknownMeasure_IsRefusedAndTheAvailableOnesAreNamed()
    {
        var issue = ResolveExpectingFailure(new QuerySpec { Entity = "auftrag", Measures = ["gewinn"] });

        issue.Code.ShouldBe("query.measure.unknown");
        issue.Message.ShouldContain("umsatz");
        issue.Message.ShouldContain("durchschnittswert");
    }

    [Fact]
    public void UnknownGroupingField_IsRefusedAndTheAvailableOnesAreNamed()
    {
        var issue = ResolveExpectingFailure(new QuerySpec
        {
            Entity = "auftrag",
            Measures = ["umsatz"],
            GroupBy = [new GroupByItem("land")],
        });

        issue.Code.ShouldBe("query.group_by.unknown");
        issue.Message.ShouldContain("vertriebskanal");
    }

    [Fact]
    public void HiddenFields_AreNotQueryable()
    {
        // nettobetrag_wert exists only so the umsatz measure can name its column. It is not part
        // of the surface offered to users, and the model excerpt never shows it to the LLM either.
        var issue = ResolveExpectingFailure(new QuerySpec
        {
            Entity = "auftrag",
            Measures = ["umsatz"],
            GroupBy = [new GroupByItem("nettobetrag_wert")],
        });

        issue.Code.ShouldBe("query.group_by.hidden");
    }

    [Fact]
    public void GrainTheModelDoesNotAllow_IsRefused()
    {
        var model = QueryEngineFixture.Model;
        var restricted = QueryEngineFixture.ModelYaml.Replace(
            "granularities: [day, week, month, quarter, year]",
            "granularities: [month, year]",
            StringComparison.Ordinal);
        restricted.ShouldNotBe(QueryEngineFixture.ModelYaml);

        var loaded = Semantics.Yaml.SemanticModelLoader.Load(restricted, null).Model!;

        var result = QueryEngineFixture.Resolver().Resolve(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["umsatz"],
                GroupBy = [new GroupByItem("bestelldatum", TimeGrain.Day)],
            },
            loaded);

        var issue = result.Issues.First(i => i.Code == "query.grain.unsupported");
        issue.Message.ShouldContain("month");
        model.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(FilterOperator.Equals, 0)]
    [InlineData(FilterOperator.Equals, 2)]
    [InlineData(FilterOperator.Between, 1)]
    [InlineData(FilterOperator.IsNull, 1)]
    [InlineData(FilterOperator.In, 0)]
    public void FilterWithTheWrongNumberOfValues_IsRefused(FilterOperator op, int valueCount)
    {
        var values = Enumerable.Range(0, valueCount).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();

        var issue = ResolveExpectingFailure(new QuerySpec
        {
            Entity = "auftrag",
            Measures = ["umsatz"],
            Filters = [new FilterItem("status", op, values)],
        });

        issue.Code.ShouldBe("query.filter.arity");
    }

    [Fact]
    public void FilterValueThatDoesNotConvert_IsRefusedRatherThanPassedThrough()
    {
        var issue = ResolveExpectingFailure(new QuerySpec
        {
            Entity = "auftrag",
            Measures = ["umsatz"],
            Filters = [new FilterItem("bestelldatum", FilterOperator.Equals, ["irgendwann"])],
        });

        issue.Code.ShouldBe("query.filter.value_invalid");
    }

    [Fact]
    public void TextOperatorOnANonTextField_IsRefused()
    {
        var issue = ResolveExpectingFailure(new QuerySpec
        {
            Entity = "auftrag",
            Measures = ["umsatz"],
            Filters = [new FilterItem("bestelldatum", FilterOperator.Contains, ["2026"])],
        });

        issue.Code.ShouldBe("query.filter.operator_type");
    }

    [Fact]
    public void RelativeTimeOperatorOnANonDateField_IsRefused()
    {
        var issue = ResolveExpectingFailure(new QuerySpec
        {
            Entity = "auftrag",
            Measures = ["umsatz"],
            Filters = [new FilterItem("status", FilterOperator.InLastMonths, ["3"])],
        });

        issue.Code.ShouldBe("query.filter.operator_type");
    }

    [Fact]
    public void RelativeTimeWithANonPositiveCount_IsRefused()
    {
        var issue = ResolveExpectingFailure(new QuerySpec
        {
            Entity = "auftrag",
            Measures = ["umsatz"],
            Filters = [new FilterItem("bestelldatum", FilterOperator.InLastMonths, ["0"])],
        });

        issue.Code.ShouldBe("query.filter.value_invalid");
    }

    [Fact]
    public void SortingOnSomethingTheQueryDoesNotSelect_IsRefused()
    {
        // Valid SQL, but the column cannot be shown, so the answer would be sorted by something
        // invisible — which reads as an arbitrary order.
        var issue = ResolveExpectingFailure(new QuerySpec
        {
            Entity = "auftrag",
            Measures = ["umsatz"],
            OrderBy = [new OrderByItem("anzahl", SortDirection.Descending)],
        });

        issue.Code.ShouldBe("query.order_by.unknown");
        issue.Message.ShouldContain("umsatz");
    }

    [Fact]
    public void QuerySelectingNothing_IsRefused()
    {
        var issue = ResolveExpectingFailure(new QuerySpec { Entity = "auftrag" });

        issue.Code.ShouldBe("query.empty");
    }

    [Fact]
    public void MetricPullsInTheMeasuresItNeeds_WithoutThemBeingAsked_For()
    {
        var result = QueryEngineFixture.Resolver().Resolve(
            new QuerySpec { Entity = "auftrag", Measures = ["durchschnittswert"] },
            QueryEngineFixture.Model);

        var metric = result.Query!.Fields.OfType<SelectedMetric>().ShouldHaveSingleItem();
        metric.Dependencies.Select(d => d.Name).ShouldBe(["umsatz", "anzahl"], ignoreOrder: true);
    }

    [Fact]
    public void RowPoliciesAreAttachedAutomatically_NotRequestedByTheSpec()
    {
        var result = QueryEngineFixture.Resolver().Resolve(
            new QuerySpec { Entity = "auftrag", Measures = ["umsatz"] },
            QueryEngineFixture.Model);

        var policy = result.Query!.PolicyFilters.ShouldHaveSingleItem();
        policy.Column.ShouldBe("mandant_id");
        policy.Parameter.ShouldBe("tenant_id");
    }

    [Fact]
    public void LimitAboveTheCeiling_IsClampedRatherThanRefused()
    {
        // The user never chose the ceiling, so erroring on it would be unhelpful; they get an
        // answer, capped.
        var limits = new QueryLimits { MaxRowLimit = 100 };

        var result = QueryEngineFixture.Resolver(limits).Resolve(
            new QuerySpec { Entity = "auftrag", Measures = ["umsatz"], Limit = 10_000 },
            QueryEngineFixture.Model);

        result.Query!.Limit.ShouldBe(100);
    }

    [Fact]
    public void NoLimitRequested_UsesTheDefault()
    {
        var result = QueryEngineFixture.Resolver(new QueryLimits { DefaultRowLimit = 250 }).Resolve(
            new QuerySpec { Entity = "auftrag", Measures = ["umsatz"] },
            QueryEngineFixture.Model);

        result.Query!.Limit.ShouldBe(250);
    }

    [Fact]
    public void TooManyGroupings_AreRefused()
    {
        var issue = ResolveExpectingFailure(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["umsatz"],
                GroupBy = [new GroupByItem("status"), new GroupByItem("vertriebskanal")],
            },
            new QueryLimits { MaxGroupings = 1 });

        issue.Code.ShouldBe("query.group_by.too_many");
    }

    [Fact]
    public void ReferenceTimeIsCapturedOnce_SoTheAnswerIsReproducible()
    {
        var result = QueryEngineFixture.Resolver().Resolve(
            new QuerySpec { Entity = "auftrag", Measures = ["umsatz"] },
            QueryEngineFixture.Model);

        result.Query!.ReferenceTime.ShouldBe(QueryEngineFixture.Now);
    }

    private static ValidationIssue ResolveExpectingFailure(QuerySpec spec, QueryLimits? limits = null)
    {
        var result = QueryEngineFixture.Resolver(limits).Resolve(spec, QueryEngineFixture.Model);

        result.Success.ShouldBeFalse();
        return result.Issues.First(i => i.Severity is IssueSeverity.Error);
    }
}
