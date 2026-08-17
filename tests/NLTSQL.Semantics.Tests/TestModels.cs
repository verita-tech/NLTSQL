namespace NLTSQL.Semantics.Tests;

/// <summary>Shared YAML fixtures.</summary>
internal static class TestModels
{
    /// <summary>
    /// A small but complete model that passes validation without errors.
    /// </summary>
    /// <remarks>
    /// Deliberately exercises the awkward corners rather than the happy path only: a hidden
    /// dimension that exists purely so a measure may reference its column, a count measure with no
    /// expression, a metric over two measures, and a row policy on every entity.
    /// </remarks>
    public const string Valid = """
        model: vertrieb
        version: 3
        data_source: pg_main
        label: Vertrieb
        description: Auftraege und Kunden.
        entities:
          - name: auftrag
            label: Auftrag
            description: Ein vom Kunden erteilter Auftrag.
            synonyms: [bestellung, order]
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
              - name: nettobetrag_wert
                column: nettobetrag
                type: decimal
                hidden: true
            time_dimensions:
              - name: bestelldatum
                column: bestelldatum
                type: date
                granularities: [day, month, year]
            measures:
              - name: umsatz
                label: Umsatz
                description: Nettoumsatz ohne Umsatzsteuer.
                agg: sum
                column: nettobetrag
                format:
                  kind: currency
                  currency: EUR
              - name: anzahl
                label: Anzahl Auftraege
                agg: count
            metrics:
              - name: durchschnittlicher_auftragswert
                label: Durchschnittlicher Auftragswert
                expression: "{{umsatz}} / {{anzahl}}"
                format:
                  kind: currency
                  currency: EUR
          - name: kunde
            label: Kunde
            description: Auftraggeber.
            table:
              schema: vertrieb
              name: kunde
            primary_key: [id]
            dimensions:
              - name: land
                column: land
                type: string
              - name: id
                column: id
                type: integer
                hidden: true
            measures:
              - name: anzahl
                agg: count
        relationships:
          - from: auftrag.kunde_id
            to: kunde.id
            type: many_to_one
        glossary:
          - term: aktiver Kunde
            definition: Kunde mit mindestens einem Auftrag in den letzten zwoelf Monaten.
        row_policies:
          - entity: auftrag
            column: mandant_id
            parameter: tenant_id
          - entity: kunde
            column: mandant_id
            parameter: tenant_id
        """;

    /// <summary>
    /// Wraps <paramref name="entityBody"/> into a complete single-entity model.
    /// </summary>
    /// <remarks>
    /// Defect tests build their own tiny model rather than doing string surgery on
    /// <see cref="Valid"/>: a replacement that silently stops matching turns into a test that
    /// asserts against the unmodified fixture and passes for the wrong reason.
    /// </remarks>
    /// <param name="entityBody">Entity properties, indented by four spaces.</param>
    /// <param name="trailer">Optional top-level sections appended after the entity.</param>
    public static string SingleEntity(string entityBody, string trailer = "") =>
        $"""
        model: t
        version: 1
        data_source: ds
        entities:
          - name: e
            table:
              schema: s
              name: t
            primary_key: [id]
        {entityBody}
        {trailer}
        """;
}
