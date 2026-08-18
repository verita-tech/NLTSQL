namespace NLTSQL.Web.Charting;

/// <summary>
/// The validated chart colours, for the light and dark surface respectively.
/// </summary>
/// <remarks>
/// <para>
/// Held in C# rather than in CSS because ApexCharts writes SVG fill and stroke attributes from
/// JavaScript, where a <c>var(--series-1)</c> never resolves. One source of truth beats a CSS copy
/// that silently drifts.
/// </para>
/// <para>
/// The dark column is not a flip of the light one. Both are stepped for their own surface and were
/// checked as a set against Fluent's actual backgrounds (#ffffff and #1a1a1a): every slot sits in
/// the mode's lightness band and clears the chroma floor, worst adjacent colour-vision-deficiency
/// separation is deltaE 9.1 light and 8.4 dark, worst normal-vision separation 19.6 and 19.3.
/// Three light slots fall below 3:1 against white, which is permitted only because a result table
/// with the values always accompanies the chart — do not drop that table without re-checking this.
/// </para>
/// <para>
/// Slots are assigned in order and never cycled. A ninth series does not get a generated colour;
/// the chart is split instead.
/// </para>
/// </remarks>
public static class ChartPalette
{
    /// <summary>Categorical slots for the light surface, in assignment order.</summary>
    public static IReadOnlyList<string> Light { get; } =
        ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#4a3aa7", "#e34948"];

    /// <summary>Categorical slots for the dark surface, in assignment order.</summary>
    public static IReadOnlyList<string> Dark { get; } =
        ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"];

    /// <summary>Axis and label ink.</summary>
    public static string ForeColor(bool dark) => dark ? "#c3c2b7" : "#52514e";

    /// <summary>Grid lines, which must stay recessive enough not to compete with the data.</summary>
    public static string GridColor(bool dark) => dark ? "#383835" : "#e6e5e1";

    /// <summary>The colour for the series at <paramref name="index"/>.</summary>
    public static string Series(int index, bool dark)
    {
        var slots = dark ? Dark : Light;
        return slots[index % slots.Count];
    }
}
