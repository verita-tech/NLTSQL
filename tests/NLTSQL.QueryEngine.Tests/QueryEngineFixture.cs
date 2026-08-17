using Microsoft.Extensions.Options;
using NLTSQL.Core.Query;
using NLTSQL.QueryEngine.Sql;
using NLTSQL.Semantics.Model;
using NLTSQL.Semantics.Yaml;

namespace NLTSQL.QueryEngine.Tests;

/// <summary>Shared model and helpers for the compiler tests.</summary>
internal static class QueryEngineFixture
{
    /// <summary>
    /// The reference time every compiled query is measured against.
    /// </summary>
    /// <remarks>
    /// Fixed so that relative time filters produce identical SQL on every run. Reading the real
    /// clock here would make the golden statements change once a day.
    /// </remarks>
    public static readonly DateTimeOffset Now = new(2026, 3, 15, 10, 30, 0, TimeSpan.Zero);

    public const string ModelYaml = """
        model: vertrieb
        version: 1
        data_source: pg_main
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
              - name: vertriebskanal
                label: Vertriebskanal
                column: vertriebskanal
                type: string
              - name: nettobetrag_wert
                column: nettobetrag
                type: decimal
                hidden: true
              - name: rabattbetrag_wert
                column: rabattbetrag
                type: decimal
                hidden: true
            time_dimensions:
              - name: bestelldatum
                label: Bestelldatum
                column: bestelldatum
                type: date
                granularities: [day, week, month, quarter, year]
            measures:
              - name: umsatz
                label: Umsatz
                agg: sum
                column: nettobetrag
                format:
                  kind: currency
                  currency: EUR
              - name: bruttoumsatz
                label: Bruttoumsatz
                agg: sum
                expression: "{{nettobetrag}} + {{rabattbetrag}}"
              - name: anzahl
                label: Anzahl
                agg: count
              - name: kunden
                label: Kunden
                agg: count_distinct
                column: kunde_id
            metrics:
              - name: durchschnittswert
                label: Durchschnittlicher Auftragswert
                expression: "{{umsatz}} / NULLIF({{anzahl}}, 0)"
                format:
                  kind: currency
                  currency: EUR
        row_policies:
          - entity: auftrag
            column: mandant_id
            parameter: tenant_id
        """;

    public static SemanticModel Model { get; } = LoadModel();

    /// <summary>The policy values a signed-in user of tenant 42 would carry.</summary>
    public static QueryExecutionContext Context { get; } =
        new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["tenant_id"] = 42L });

    public static QuerySpecResolver Resolver(QueryLimits? limits = null) =>
        new(Options.Create(limits ?? new QueryLimits()), new FixedTimeProvider(Now));

    /// <summary>Resolves and compiles, failing the test with the resolver's own words if it cannot.</summary>
    public static CompiledQuery Compile(QuerySpec spec, ISqlDialect dialect, QueryExecutionContext? context = null)
    {
        var resolution = Resolver().Resolve(spec, Model);
        resolution.Query.ShouldNotBeNull(string.Join(Environment.NewLine, resolution.Issues));

        return new SqlCompiler(dialect).Compile(resolution.Query, context ?? Context);
    }

    private static SemanticModel LoadModel()
    {
        var result = SemanticModelLoader.Load(ModelYaml, null);
        return result.Model ?? throw new InvalidOperationException(
            "Fixture model is invalid: " + string.Join(Environment.NewLine, result.Issues));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
