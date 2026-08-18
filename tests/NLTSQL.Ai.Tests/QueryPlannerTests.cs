using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NLTSQL.Ai.Planning;
using NLTSQL.Core.Query;
using NLTSQL.QueryEngine;
using NLTSQL.Semantics.Model;
using NLTSQL.Semantics.Yaml;

namespace NLTSQL.Ai.Tests;

public sealed class QueryPlannerTests
{
    private const string ModelYaml = """
        model: vertrieb
        version: 1
        data_source: pg_demo
        entities:
          - name: auftrag
            label: Auftrag
            description: Ein vom Kunden erteilter Auftrag.
            table:
              schema: vertrieb
              name: auftrag
            primary_key: [id]
            dimensions:
              - name: status
                label: Auftragsstatus
                column: status
                type: string
                values: [offen, versendet, storniert]
              - name: betrag_wert
                column: nettobetrag
                type: decimal
                hidden: true
            time_dimensions:
              - name: bestelldatum
                label: Bestelldatum
                column: bestelldatum
                type: date
            measures:
              - name: umsatz
                label: Umsatz
                agg: sum
                column: nettobetrag
              - name: anzahl_auftraege
                label: Anzahl
                agg: count
        row_policies:
          - entity: auftrag
            column: mandant_id
            parameter: tenant_id
        """;

    [Fact]
    public async Task WellFormedResponse_PlansInOneAttempt()
    {
        var client = new ScriptedChatClient("""
            {"entity":"auftrag","measures":["umsatz"],
             "group_by":[{"field":"bestelldatum","grain":"month"}]}
            """);

        var result = await Plan(client, "Umsatz pro Monat");

        result.Success.ShouldBeTrue(string.Join(Environment.NewLine, result.Issues));
        result.Attempts.ShouldBe(1);
        result.Query!.Groupings.ShouldHaveSingleItem().Grain.ShouldBe(TimeGrain.Month);
    }

    [Fact]
    public async Task ProseAroundTheJson_IsToleratedRatherThanRepaired()
    {
        // Small models routinely ignore "reply with JSON only". Spending a repair round on a
        // leading sentence would waste the budget on something entirely unambiguous.
        var client = new ScriptedChatClient("""
            Gerne! Hier ist die passende Abfrage:

            ```json
            {"entity":"auftrag","measures":["umsatz"]}
            ```

            Damit erhaeltst du den Gesamtumsatz.
            """);

        var result = await Plan(client, "Wie hoch ist der Umsatz?");

        result.Success.ShouldBeTrue(string.Join(Environment.NewLine, result.Issues));
        result.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task UnknownMeasure_IsRepairedUsingTheResolversOwnWords()
    {
        var client = new ScriptedChatClient(
            """{"entity":"auftrag","measures":["gewinn"]}""",
            """{"entity":"auftrag","measures":["umsatz"]}""");

        var result = await Plan(client, "Wie hoch ist der Gewinn?");

        result.Success.ShouldBeTrue();
        result.Attempts.ShouldBe(2);

        // The repair turn has to carry what was wrong *and* what was available, or the second
        // attempt is just another guess.
        var repair = client.Sent[^1].Text;
        repair.ShouldContain("gewinn");
        repair.ShouldContain("umsatz");
    }

    [Fact]
    public async Task PersistentlyWrongModel_GivesUpAndReportsWhy()
    {
        var client = new ScriptedChatClient("""{"entity":"rechnung","measures":["umsatz"]}""");

        var result = await Plan(client, "Umsatz je Rechnung");

        result.Success.ShouldBeFalse();
        client.CallCount.ShouldBe(3, "one attempt plus two repairs");
        result.Issues.ShouldContain(i => i.Code == "query.entity.unknown");
    }

    [Fact]
    public async Task ResponseWithoutJson_IsReportedAsSuch()
    {
        var client = new ScriptedChatClient("Das kann ich leider nicht beantworten.");

        var result = await Plan(client, "Wie ist das Wetter?");

        result.Success.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "draft.json.missing");
    }

    [Fact]
    public async Task HiddenDimension_IsRefused_SoTheModelCannotGroupByARawAmount()
    {
        var client = new ScriptedChatClient("""
            {"entity":"auftrag","measures":["umsatz"],"group_by":[{"field":"betrag_wert"}]}
            """);

        var result = await Plan(client, "Umsatz nach Betrag");

        result.Success.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "query.group_by.hidden");
    }

    [Fact]
    public async Task TheFailedSpecIsStillReturned_SoTheUserCanSeeWhatWasAttempted()
    {
        var client = new ScriptedChatClient("""{"entity":"auftrag","measures":["gewinn"]}""");

        var result = await Plan(client, "Gewinn");

        result.Success.ShouldBeFalse();
        result.Spec.ShouldNotBeNull();
        result.Spec.Measures.ShouldBe(["gewinn"]);
    }

    [Fact]
    public async Task SystemPromptCarriesTheModelExcerpt_IncludingPermittedValues()
    {
        // Without the value list, "cancelled orders" becomes a guess at the encoding.
        var client = new ScriptedChatClient("""{"entity":"auftrag","measures":["umsatz"]}""");

        await Plan(client, "Umsatz");

        var system = client.Sent[0].Text;
        system.ShouldContain("auftrag");
        system.ShouldContain("storniert");
        system.ShouldNotContain("betrag_wert", Case.Insensitive);
    }

    private static async Task<QueryPlanResult> Plan(ScriptedChatClient client, string question)
    {
        var model = SemanticModelLoader.Load(ModelYaml, null).Model
            ?? throw new InvalidOperationException("Fixture model is invalid.");

        var resolver = new QuerySpecResolver(Options.Create(new QueryLimits()), TimeProvider.System);
        var planner = new QueryPlanner(client, resolver, NullLogger<QueryPlanner>.Instance);

        return await planner.PlanAsync(question, model, CancellationToken.None);
    }
}
