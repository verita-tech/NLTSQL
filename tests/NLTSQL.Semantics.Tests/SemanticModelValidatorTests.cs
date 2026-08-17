using NLTSQL.Semantics.Model;
using NLTSQL.Semantics.Validation;
using NLTSQL.Semantics.Yaml;

namespace NLTSQL.Semantics.Tests;

public sealed class SemanticModelValidatorTests
{
    [Fact]
    public void TheReferenceModel_HasNoErrors()
    {
        var issues = SemanticModelValidator.Validate(Load(TestModels.Valid));

        issues.Where(i => i.Severity is IssueSeverity.Error)
            .ShouldBeEmpty(string.Join(Environment.NewLine, issues));
    }

    [Fact]
    public void MeasureReferencingAnUndeclaredColumn_IsAnError()
    {
        // This is the failure the validator exists to prevent: without it, a column somebody
        // renamed surfaces as a database error mid-question, in front of a customer.
        var issues = Validate("""
                dimensions:
                  - name: d
                    column: a
                    type: decimal
                measures:
                  - name: m
                    agg: sum
                    expression: "{{a}} + {{does_not_exist}}"
            """);

        var issue = issues.Where(i => i.Severity is IssueSeverity.Error).ShouldHaveSingleItem();
        issue.Code.ShouldBe("measure.column.unknown");
        issue.Message.ShouldContain("does_not_exist");
    }

    [Fact]
    public void MeasureMayReferenceAColumnExposedOnlyAsAHiddenDimension()
    {
        // Numeric columns usually should not be groupable, but a measure still has to name them.
        // Declaring them hidden is the intended way, so it must not trip the validator.
        var issues = Validate("""
                dimensions:
                  - name: betrag_wert
                    column: betrag
                    type: decimal
                    hidden: true
                measures:
                  - name: m
                    agg: sum
                    column: betrag
            """);

        issues.ShouldNotContain(i => i.Code == "measure.column.unknown");
    }

    [Fact]
    public void MetricReferencingAnUnknownMeasure_IsAnError()
    {
        var issues = Validate("""
                measures:
                  - name: m
                    agg: count
                metrics:
                  - name: r
                    expression: "{{m}} / {{missing}}"
            """);

        issues.ShouldContain(i => i.Code == "metric.measure.unknown" && i.Message.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void MetricReferencingAnotherMetric_SaysSoExplicitly()
    {
        // Metrics compose arithmetic over aggregates. Allowing metric-of-metric would need a
        // dependency order the compiler does not have, so the message names the actual rule
        // rather than just reporting an unknown name.
        var issues = Validate("""
                measures:
                  - name: m
                    agg: count
                metrics:
                  - name: a
                    expression: "{{m}} * 2"
                  - name: b
                    expression: "{{a}} * 2"
            """);

        var issue = issues.First(i => i.Code == "metric.measure.unknown");
        issue.Message.ShouldContain("may not reference other metrics");
    }

    [Fact]
    public void DuplicateFieldNamesWithinAnEntity_AreAnError()
    {
        // Dimensions, measures and metrics share one namespace because a QuerySpec names a field
        // without saying which kind it is.
        var issues = Validate("""
                dimensions:
                  - name: betrag
                    column: betrag
                    type: decimal
                measures:
                  - name: betrag
                    agg: sum
                    column: betrag
            """);

        issues.ShouldContain(i => i.Code == "entity.field.duplicate");
    }

    [Fact]
    public void DuplicateEntityNames_AreAnError()
    {
        var issues = SemanticModelValidator.Validate(Load("""
            model: t
            version: 1
            data_source: ds
            entities:
              - name: e
                table: { schema: s, name: a }
              - name: E
                table: { schema: s, name: b }
            """));

        issues.ShouldContain(i => i.Code == "entity.duplicate");
    }

    [Fact]
    public void RowPolicyNamingAnUnknownEntity_IsAnErrorRatherThanAWarning()
    {
        // A mistyped entity name in a row policy silently protects nothing, and the failure mode
        // is one tenant seeing another's rows. That has to stop the model loading.
        var issues = SemanticModelValidator.Validate(Load("""
            model: t
            version: 1
            data_source: ds
            entities:
              - name: e
                table: { schema: s, name: t }
            row_policies:
              - entity: typo
                column: mandant_id
                parameter: tenant_id
            """));

        var issue = issues.First(i => i.Code == "row_policy.entity.unknown");
        issue.Severity.ShouldBe(IssueSeverity.Error);
    }

    [Fact]
    public void EntityWithoutAnyRowPolicy_IsWarnedAbout()
    {
        var issues = Validate("""
                measures:
                  - name: m
                    agg: count
            """);

        var issue = issues.First(i => i.Code == "row_policy.entity.unguarded");
        issue.Severity.ShouldBe(IssueSeverity.Warning);
    }

    [Fact]
    public void RelationshipToAnUnknownEntity_IsAnError()
    {
        var issues = SemanticModelValidator.Validate(Load("""
            model: t
            version: 1
            data_source: ds
            entities:
              - name: e
                table: { schema: s, name: t }
                primary_key: [id]
            relationships:
              - from: e.other_id
                to: nowhere.id
            """));

        issues.ShouldContain(i => i.Code == "relationship.entity.unknown" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void JoinColumnThatIsNotDeclared_IsOnlyAWarning()
    {
        // Surrogate keys usually should not be exposed as dimensions, so an undeclared join column
        // is normal enough that blocking the model would be wrong.
        var issues = SemanticModelValidator.Validate(Load("""
            model: t
            version: 1
            data_source: ds
            entities:
              - name: a
                table: { schema: s, name: a }
                primary_key: [id]
              - name: b
                table: { schema: s, name: b }
                primary_key: [id]
            relationships:
              - from: a.b_id
                to: b.id
            """));

        var issue = issues.First(i => i.Code == "relationship.column.undeclared");
        issue.Severity.ShouldBe(IssueSeverity.Warning);
    }

    [Fact]
    public void EntityWithNoMeasures_IsWarnedAbout()
    {
        var issues = Validate("""
                dimensions:
                  - name: d
                    column: c
                    type: string
            """);

        issues.ShouldContain(i => i.Code == "entity.measures.empty" && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void EntityWithoutADescription_IsWarnedAbout()
    {
        var issues = Validate("""
                measures:
                  - name: m
                    agg: count
            """);

        issues.ShouldContain(i => i.Code == "entity.description.missing" && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void ModelWithNoEntities_IsAnError()
    {
        var issues = SemanticModelValidator.Validate(Load("""
            model: t
            version: 1
            data_source: ds
            """));

        issues.ShouldContain(i => i.Code == "model.entities.empty" && i.Severity == IssueSeverity.Error);
    }

    private static IReadOnlyList<ValidationIssue> Validate(string entityBody) =>
        SemanticModelValidator.Validate(Load(TestModels.SingleEntity(entityBody)));

    private static SemanticModel Load(string yaml)
    {
        var result = SemanticModelLoader.Load(yaml, null);
        result.Model.ShouldNotBeNull(string.Join(Environment.NewLine, result.Issues));
        return result.Model;
    }
}
