using NLTSQL.Core.Expressions;
using NLTSQL.Semantics.Model;
using NLTSQL.Semantics.Validation;
using NLTSQL.Semantics.Yaml;

namespace NLTSQL.Semantics.Tests;

public sealed class SemanticModelLoaderTests
{
    [Fact]
    public void ValidModel_LoadsWithoutErrors()
    {
        var result = SemanticModelLoader.Load(TestModels.Valid, null);

        result.Issues.Where(i => i.Severity is IssueSeverity.Error).ShouldBeEmpty();
        result.Model.ShouldNotBeNull();
        result.Model.Name.ShouldBe("vertrieb");
        result.Model.Version.ShouldBe(3);
        result.Model.DataSource.ShouldBe("pg_main");
        result.Model.Entities.Count.ShouldBe(2);
    }

    [Fact]
    public void MeasureWithColumnShorthand_BecomesAReferenceExpression()
    {
        var model = Load(TestModels.Valid);

        var umsatz = model.FindEntity("auftrag")!.FindMeasure("umsatz")!;
        umsatz.Aggregation.ShouldBe(Aggregation.Sum);
        umsatz.Expression.ShouldBeOfType<ReferenceExpression>().Name.ShouldBe("nettobetrag");
    }

    [Fact]
    public void CountMeasure_MayOmitItsSource()
    {
        var model = Load(TestModels.Valid);

        var anzahl = model.FindEntity("auftrag")!.FindMeasure("anzahl")!;
        anzahl.Aggregation.ShouldBe(Aggregation.Count);
        anzahl.Expression.ShouldBeNull();
    }

    [Fact]
    public void NonCountMeasure_WithoutSource_IsRejected()
    {
        var yaml = TestModels.SingleEntity("""
                measures:
                  - name: m
                    agg: sum
            """);

        ShouldReport(yaml, "measure.source.missing");
    }

    [Fact]
    public void MeasureWithBothColumnAndExpression_IsRejectedAsAmbiguous()
    {
        var yaml = TestModels.SingleEntity("""
                measures:
                  - name: m
                    agg: sum
                    column: a
                    expression: "{{a}}"
            """);

        ShouldReport(yaml, "measure.source.ambiguous");
    }

    [Fact]
    public void EntityWithoutSchema_IsRejected()
    {
        // A schema-less table resolves against the connection's search path, which means the same
        // model could read different tables depending on who connects.
        var yaml = """
            model: t
            version: 1
            data_source: ds
            entities:
              - name: e
                table:
                  name: t
            """;

        ShouldReport(yaml, "entity.table.missing");
    }

    [Fact]
    public void UnknownYamlKey_IsRejectedRatherThanIgnored()
    {
        // A typo such as "synonym:" for "synonyms:" must not load quietly and degrade retrieval.
        var yaml = TestModels.SingleEntity("""
                synonym: [bestellung]
            """);

        var result = SemanticModelLoader.Load(yaml, null);

        result.Issues.ShouldContain(i => i.Code == "model.yaml.invalid");
        result.Model.ShouldBeNull();
    }

    [Theory]
    [InlineData("model: vertrieb\n", "model.name.missing")]
    [InlineData("version: 3\n", "model.version.missing")]
    [InlineData("data_source: pg_main\n", "model.datasource.missing")]
    public void MissingHeaderField_IsReported(string line, string expectedCode)
    {
        var yaml = TestModels.Valid.Replace(line, string.Empty, StringComparison.Ordinal);
        yaml.ShouldNotBe(TestModels.Valid);

        var result = SemanticModelLoader.Load(yaml, null);

        result.Issues.ShouldContain(i => i.Code == expectedCode);
        result.Model.ShouldBeNull();
    }

    [Fact]
    public void UnparsableMetricExpression_IsReportedAtItsPath()
    {
        var yaml = TestModels.SingleEntity("""
                measures:
                  - name: umsatz
                    agg: count
                metrics:
                  - name: broken
                    expression: "{{umsatz}} / "
            """);

        var result = SemanticModelLoader.Load(yaml, null);

        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("metric.expression.invalid");
        issue.Path.ShouldBe("e.metrics.broken");
    }

    [Fact]
    public void InvalidAggregate_IsReportedWithTheAllowedSpellings()
    {
        var yaml = TestModels.SingleEntity("""
                measures:
                  - name: m
                    agg: median
                    column: a
            """);

        var issue = SemanticModelLoader.Load(yaml, null).Issues.First(i => i.Code == "measure.agg.invalid");

        issue.Message.ShouldContain("count_distinct");
    }

    [Theory]
    [InlineData("avg", Aggregation.Average)]
    [InlineData("average", Aggregation.Average)]
    [InlineData("mean", Aggregation.Average)]
    [InlineData("min", Aggregation.Minimum)]
    [InlineData("max", Aggregation.Maximum)]
    [InlineData("count_distinct", Aggregation.CountDistinct)]
    [InlineData("distinct_count", Aggregation.CountDistinct)]
    public void AggregateSpellings_AnAnalystWouldUse_AreAccepted(string spelling, Aggregation expected)
    {
        var yaml = TestModels.SingleEntity($"""
                measures:
                  - name: m
                    agg: {spelling}
                    column: a
            """);

        Load(yaml).FindEntity("e")!.FindMeasure("m")!.Aggregation.ShouldBe(expected);
    }

    [Fact]
    public void TimeDimension_WithNonTemporalType_IsRejected()
    {
        var yaml = TestModels.SingleEntity("""
                time_dimensions:
                  - name: d
                    column: c
                    type: string
            """);

        ShouldReport(yaml, "time_dimension.type.not_temporal");
    }

    [Fact]
    public void TimeDimension_WithoutGranularities_GetsThemAll()
    {
        var yaml = TestModels.SingleEntity("""
                time_dimensions:
                  - name: d
                    column: c
                    type: date
            """);

        Load(yaml).FindEntity("e")!.FindTimeDimension("d")!.Granularities.Count.ShouldBe(5);
    }

    [Fact]
    public void TimeDimension_WithUnknownGranularity_IsReported()
    {
        var yaml = TestModels.SingleEntity("""
                time_dimensions:
                  - name: d
                    column: c
                    type: date
                    granularities: [day, fortnight]
            """);

        ShouldReport(yaml, "time_dimension.granularity.invalid");
    }

    [Fact]
    public void CurrencyFormat_WithoutCurrencyCode_IsRejected()
    {
        var yaml = TestModels.SingleEntity("""
                measures:
                  - name: m
                    agg: sum
                    column: a
                    format:
                      kind: currency
            """);

        ShouldReport(yaml, "format.currency.missing");
    }

    [Fact]
    public void RelationshipName_IsDerivedWhenNotDeclared()
    {
        var model = Load(TestModels.Valid);

        var relationship = model.Relationships.ShouldHaveSingleItem();
        relationship.Name.ShouldBe("auftrag_kunde_id_to_kunde");
        relationship.From.ShouldBe(new EntityColumn("auftrag", "kunde_id"));
        relationship.To.ShouldBe(new EntityColumn("kunde", "id"));
        relationship.Cardinality.ShouldBe(Cardinality.ManyToOne);
    }

    [Fact]
    public void MalformedRelationshipSide_IsReported()
    {
        var yaml = TestModels.SingleEntity(
            """
                dimensions: []
            """,
            """
            relationships:
              - from: e
                to: e.id
            """);

        ShouldReport(yaml, "relationship.side.malformed");
    }

    [Fact]
    public void RowPolicy_IsLoadedStructurally()
    {
        var model = Load(TestModels.Valid);

        var policy = model.PoliciesFor("auftrag").ShouldHaveSingleItem();
        policy.Column.ShouldBe("mandant_id");
        policy.Parameter.ShouldBe("tenant_id");
        policy.MultiValued.ShouldBeFalse();
    }

    [Fact]
    public void ElementLookups_AreCaseInsensitive()
    {
        var model = Load(TestModels.Valid);

        model.FindEntity("AUFTRAG").ShouldNotBeNull();
        model.FindEntity("auftrag")!.FindMeasure("Umsatz").ShouldNotBeNull();
    }

    [Fact]
    public void MissingLabel_FallsBackToTheName()
    {
        var model = Load(TestModels.Valid);

        model.FindEntity("kunde")!.FindDimension("land")!.Label.ShouldBe("land");
    }

    [Fact]
    public void StatusDefaultsToDraft_SoUnreviewedContentIsVisibleAsSuch()
    {
        var model = Load(TestModels.Valid);

        model.FindEntity("auftrag")!.Status.ShouldBe(ReviewStatus.Draft);
    }

    private static void ShouldReport(string yaml, string expectedCode)
    {
        var result = SemanticModelLoader.Load(yaml, null);

        result.Issues.ShouldContain(
            issue => issue.Code == expectedCode,
            customMessage: string.Join(Environment.NewLine, result.Issues));
    }

    private static SemanticModel Load(string yaml)
    {
        var result = SemanticModelLoader.Load(yaml, null);
        result.Model.ShouldNotBeNull(string.Join(Environment.NewLine, result.Issues));
        return result.Model;
    }
}
