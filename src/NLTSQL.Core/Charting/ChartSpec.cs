namespace NLTSQL.Core.Charting;

/// <summary>How a result should be presented.</summary>
public enum ChartKind
{
    /// <summary>A plain table. The fallback whenever no chart would say more than the numbers do.</summary>
    Table,

    /// <summary>A single figure, shown large.</summary>
    Kpi,

    /// <summary>A line per measure over a time axis.</summary>
    Line,

    /// <summary>A bar per category.</summary>
    Bar,
}

/// <summary>How to draw a result.</summary>
/// <param name="Kind">The presentation to use.</param>
/// <param name="CategoryColumn">Alias of the column forming the axis, if any.</param>
/// <param name="ValueColumns">Aliases of the columns plotted as values.</param>
/// <param name="Reason">Why this presentation was chosen, shown to the user next to the chart.</param>
public sealed record ChartSpec(
    ChartKind Kind,
    string? CategoryColumn,
    IReadOnlyList<string> ValueColumns,
    string Reason)
{
    /// <summary>A table with no category axis.</summary>
    public static ChartSpec AsTable(string reason) => new(ChartKind.Table, null, [], reason);
}
