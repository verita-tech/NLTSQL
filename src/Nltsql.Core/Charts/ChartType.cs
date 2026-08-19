using Nltsql.Core.Queries;

namespace Nltsql.Core.Charts;

/// <summary>Visualisations the prototype supports end to end.</summary>
/// <remarks>
/// The names match Metabase's <c>display</c> values, so a chart choice
/// survives the hand-off to a Metabase card without translation.
/// </remarks>
public enum ChartType
{
    Table,
    Bar,
    Line,
    Area,
    Row,
    Pie,
    Scalar,
}

public static class ChartTypeExtensions
{
    /// <summary>The literal Metabase <c>display</c> value.</summary>
    public static string ToMetabaseDisplay(this ChartType type) => type switch
    {
        ChartType.Bar => "bar",
        ChartType.Line => "line",
        ChartType.Area => "area",
        ChartType.Row => "row",
        ChartType.Pie => "pie",
        ChartType.Scalar => "scalar",
        _ => "table",
    };

    public static string ToGermanLabel(this ChartType type) => type switch
    {
        ChartType.Bar => "Säulendiagramm",
        ChartType.Line => "Liniendiagramm",
        ChartType.Area => "Flächendiagramm",
        ChartType.Row => "Balkendiagramm",
        ChartType.Pie => "Kreisdiagramm",
        ChartType.Scalar => "Kennzahl",
        _ => "Tabelle",
    };
}

/// <summary>
/// Picks a sensible default visualisation from the shape of a query.
/// </summary>
/// <remarks>
/// The user can always override the result; this only decides what the
/// preview shows first, so that a question answers itself without a
/// detour through a chart picker.
/// </remarks>
public static class ChartTypeSelector
{
    public static ChartType Suggest(SemanticQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Measures.Count == 0)
        {
            return ChartType.Table;
        }

        var hasTimeAxis = query.TimeDimension?.Granularity is not null;

        // A single number with nothing to break it down by.
        if (!hasTimeAxis && query.Dimensions.Count == 0)
        {
            return query.Measures.Count == 1 ? ChartType.Scalar : ChartType.Table;
        }

        // Anything over time reads as a trend.
        if (hasTimeAxis && query.Dimensions.Count == 0)
        {
            return ChartType.Line;
        }

        // One breakdown, one measure: a ranking.
        if (!hasTimeAxis && query.Dimensions.Count == 1 && query.Measures.Count == 1)
        {
            return ChartType.Bar;
        }

        // Two or more breakdowns, or a breakdown crossed with time, stop
        // being readable as a single chart.
        return ChartType.Table;
    }
}
