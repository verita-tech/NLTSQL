using System.Globalization;
using Nltsql.Core.Results;

namespace Nltsql.Web.Services;

/// <summary>Formats result values for display.</summary>
/// <remarks>
/// Percentage measures come back from Cube as a 0..1 ratio; showing that
/// raw would read as "0,75 %" instead of "75,2 %". The CSV export applies
/// the same rule, so the file and the screen agree.
/// </remarks>
public static class ValueFormatter
{
    private static readonly CultureInfo Display = CultureInfo.GetCultureInfo("de-DE");

    public static string Format(object? value, ResultColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        return value switch
        {
            null => "—",
            double number when column.IsPercent => (number * 100).ToString("N1", Display) + " %",
            double number => FormatNumber(number),
            DateTime timestamp => timestamp.ToString(
                column.Kind == ResultColumnKind.TimeDimension ? "dd.MM.yyyy" : "dd.MM.yyyy HH:mm", Display),
            bool flag => flag ? "ja" : "nein",
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Whole numbers lose their decimals; fractional values keep two.
    /// Counts and quantities are the common case and read badly as
    /// "1.234,00".
    /// </summary>
    private static string FormatNumber(double number) =>
        Math.Abs(number % 1) < 0.0000001
            ? number.ToString("N0", Display)
            : number.ToString("N2", Display);

    public static bool IsNumeric(ResultColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        return column.ValueType == SemanticValueType.Number || column.Kind == ResultColumnKind.Measure;
    }

    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds < 1
            ? $"{duration.TotalMilliseconds.ToString("N0", Display)} ms"
            : $"{duration.TotalSeconds.ToString("N1", Display)} s";
}
