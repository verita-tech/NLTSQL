using NLTSQL.Core.Query;

namespace NLTSQL.Core.Charting;

/// <summary>Describes a result well enough to choose how to draw it.</summary>
/// <param name="Alias">Column alias.</param>
/// <param name="IsGrouping">Whether the column is a grouping key rather than a computed value.</param>
/// <param name="Grain">Time bucket, for time groupings.</param>
public readonly record struct ChartColumn(string Alias, bool IsGrouping, TimeGrain Grain = TimeGrain.None);

/// <summary>
/// Chooses a presentation from the shape of a result.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a rule, not a model call. The shape of a result — how many groupings, whether one
/// of them is time, how many rows — determines the right chart almost entirely, so asking a
/// language model would add latency and a source of variance to a decision that has a correct
/// answer. It also means the same query always draws the same way, which matters once these end up
/// on a dashboard somebody looks at every morning.
/// </para>
/// <para>
/// Every branch falls back to a table. A table is never wrong, only sometimes dull, whereas a chart
/// forced onto data it does not fit actively misleads.
/// </para>
/// </remarks>
public static class ChartAdvisor
{
    /// <summary>Above this many categories a bar chart stops being readable.</summary>
    private const int MaxBarCategories = 30;

    /// <summary>Chooses a presentation.</summary>
    /// <param name="columns">The result's columns, in select order.</param>
    /// <param name="rowCount">How many rows came back.</param>
    public static ChartSpec Choose(IReadOnlyList<ChartColumn> columns, int rowCount)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var groupings = columns.Where(c => c.IsGrouping).ToList();
        var values = columns.Where(c => !c.IsGrouping).Select(c => c.Alias).ToList();

        if (values.Count == 0)
        {
            return ChartSpec.AsTable("Die Abfrage liefert keine Kennzahl, die sich darstellen liesse.");
        }

        if (rowCount == 0)
        {
            return ChartSpec.AsTable("Die Abfrage liefert keine Zeilen.");
        }

        if (groupings.Count == 0)
        {
            return values.Count == 1
                ? new ChartSpec(ChartKind.Kpi, null, values, "Ein einzelner Wert ohne Gruppierung.")
                : ChartSpec.AsTable("Mehrere Kennzahlen ohne Gruppierung lassen sich als Zahlen besser vergleichen.");
        }

        if (groupings.Count > 1)
        {
            // A second grouping needs series-per-category, which the prototype does not build. A
            // table shows the data honestly rather than collapsing a dimension without saying so.
            return ChartSpec.AsTable("Mehr als eine Gruppierung — als Tabelle bleibt jede Dimension sichtbar.");
        }

        var grouping = groupings[0];

        if (grouping.Grain is not TimeGrain.None)
        {
            return new ChartSpec(
                ChartKind.Line,
                grouping.Alias,
                values,
                "Zeitverlauf — eine Linie zeigt die Entwicklung besser als Balken.");
        }

        if (rowCount > MaxBarCategories)
        {
            return ChartSpec.AsTable(
                $"{rowCount} Kategorien sind mehr, als ein Balkendiagramm lesbar darstellt.");
        }

        return new ChartSpec(
            ChartKind.Bar,
            grouping.Alias,
            values,
            "Ein Wert je Kategorie — als Balken direkt vergleichbar.");
    }
}
