using NLTSQL.Core.Expressions;
using NLTSQL.Semantics.Model;
using NLTSQL.Semantics.Yaml;

namespace NLTSQL.Semantics.Tests;

/// <summary>
/// The merge is what makes regenerating a model safe, so these tests are really about one
/// question: can scaffolding run again without destroying a domain expert's work?
/// </summary>
public sealed class ModelDocumentMergerTests
{
    private const string Generated = """
        model: vertrieb
        version: 1
        data_source: pg_main
        entities:
          - name: auftrag
            label: auftrag
            table:
              schema: vertrieb
              name: auftrag
            primary_key: [id]
            dimensions:
              - name: status
                label: status
                column: status
                type: string
                values: [offen, versendet]
            measures:
              - name: umsatz
                label: umsatz
                agg: sum
                column: nettobetrag
        """;

    [Fact]
    public void WithoutOverrides_TheGeneratedModelIsUsedAsIs()
    {
        var model = Merge(Generated, null);

        model.FindEntity("auftrag")!.Label.ShouldBe("auftrag");
    }

    [Fact]
    public void OverrideReplacesScalar_AndLeavesEverythingElseAlone()
    {
        var model = Merge(Generated, """
            entities:
              - name: auftrag
                label: Auftrag
                description: Ein vom Kunden erteilter Auftrag.
            """);

        var entity = model.FindEntity("auftrag")!;
        entity.Label.ShouldBe("Auftrag");
        entity.Description.ShouldBe("Ein vom Kunden erteilter Auftrag.");

        // The override said nothing about the table or the measures, so they survive untouched.
        entity.Table.ToString().ShouldBe("vertrieb.auftrag");
        entity.FindMeasure("umsatz").ShouldNotBeNull();
    }

    [Fact]
    public void OverrideMergesIntoNestedElementsByName()
    {
        var model = Merge(Generated, """
            entities:
              - name: auftrag
                dimensions:
                  - name: status
                    label: Auftragsstatus
                    synonyms: [zustand]
            """);

        var status = model.FindEntity("auftrag")!.FindDimension("status")!;
        status.Label.ShouldBe("Auftragsstatus");
        status.Synonyms.ShouldBe(["zustand"]);

        // Column and values came from the generator and were not restated.
        status.Column.ShouldBe("status");
        status.Values.ShouldBe(["offen", "versendet"]);
    }

    [Fact]
    public void OverrideCanAddElementsTheGeneratorCouldNotInfer()
    {
        var model = Merge(Generated, """
            entities:
              - name: auftrag
                metrics:
                  - name: schnitt
                    expression: "{{umsatz}} / {{umsatz}}"
            """);

        model.FindEntity("auftrag")!.FindMetric("schnitt").ShouldNotBeNull();
    }

    [Fact]
    public void OverrideCanAddWholeEntities()
    {
        var model = Merge(Generated, """
            entities:
              - name: sicht_umsatz
                table:
                  schema: vertrieb
                  name: v_umsatz
                measures:
                  - name: anzahl
                    agg: count
            """);

        model.Entities.Count.ShouldBe(2);
        model.FindEntity("sicht_umsatz").ShouldNotBeNull();
    }

    [Fact]
    public void GeneratedOrderIsPreserved_AndAdditionsAreAppended()
    {
        var model = Merge(Generated, """
            entities:
              - name: zusatz
                table:
                  schema: vertrieb
                  name: zusatz
              - name: auftrag
                label: Auftrag
            """);

        model.Entities.Select(e => e.Name).ShouldBe(["auftrag", "zusatz"]);
    }

    [Fact]
    public void HidingIsHowAnElementIsRemovedFromCirculation()
    {
        // Deletion is deliberately not supported: the generator would just put the element back on
        // the next run, whereas hidden survives because it lives in the overrides file.
        var model = Merge(Generated, """
            entities:
              - name: auftrag
                dimensions:
                  - name: status
                    hidden: true
            """);

        model.FindEntity("auftrag")!.FindDimension("status")!.Hidden.ShouldBeTrue();
    }

    [Fact]
    public void ScalarListsAreReplacedWholesale_NotAppendedTo()
    {
        var model = Merge(Generated, """
            entities:
              - name: auftrag
                dimensions:
                  - name: status
                    values: [offen, versendet, storniert]
            """);

        model.FindEntity("auftrag")!.FindDimension("status")!.Values
            .ShouldBe(["offen", "versendet", "storniert"]);
    }

    [Fact]
    public void OverridingAMeasureExpression_ClearsTheGeneratedColumn()
    {
        // Column and expression are two spellings of one thing. Merging them independently would
        // leave both set, and the loader rejects that as ambiguous — so an override that only
        // meant to redefine the measure would fail for a reason that makes no sense to the author.
        var model = Merge(Generated, """
            entities:
              - name: auftrag
                measures:
                  - name: umsatz
                    expression: "{{nettobetrag}} - {{rabattbetrag}}"
            """);

        var umsatz = model.FindEntity("auftrag")!.FindMeasure("umsatz")!;
        umsatz.Expression.ShouldBeOfType<BinaryExpression>();
        umsatz.Expression!.References().ShouldBe(["nettobetrag", "rabattbetrag"]);
    }

    [Fact]
    public void OverridingAMeasureColumn_ClearsTheGeneratedExpression()
    {
        var generatedWithExpression = """
            model: vertrieb
            version: 1
            data_source: pg_main
            entities:
              - name: auftrag
                table:
                  schema: vertrieb
                  name: auftrag
                measures:
                  - name: umsatz
                    agg: sum
                    expression: "{{a}} + {{b}}"
            """;

        var model = Merge(generatedWithExpression, """
            entities:
              - name: auftrag
                measures:
                  - name: umsatz
                    column: nettobetrag
            """);

        model.FindEntity("auftrag")!.FindMeasure("umsatz")!.Expression
            .ShouldBeOfType<ReferenceExpression>().Name.ShouldBe("nettobetrag");
    }

    [Fact]
    public void ModelVersionCanBeRaisedByTheOverridesFile()
    {
        var model = Merge(Generated, """
            version: 7
            """);

        model.Version.ShouldBe(7);
    }

    [Fact]
    public void ElementNamesAreMatchedCaseInsensitively()
    {
        var model = Merge(Generated, """
            entities:
              - name: AUFTRAG
                label: Auftrag
            """);

        model.Entities.Count.ShouldBe(1);
        model.FindEntity("auftrag")!.Label.ShouldBe("Auftrag");
    }

    private static SemanticModel Merge(string generated, string? overrides)
    {
        var result = SemanticModelLoader.Load(generated, overrides);
        result.Model.ShouldNotBeNull(string.Join(Environment.NewLine, result.Issues));
        return result.Model;
    }
}
