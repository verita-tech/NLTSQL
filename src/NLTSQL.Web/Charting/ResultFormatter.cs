using System.Globalization;
using NLTSQL.Core.Query;
using NLTSQL.QueryEngine.Sql;
using NLTSQL.Semantics.Model;

namespace NLTSQL.Web.Charting;

/// <summary>
/// Renders result values the way the semantic model says they should read.
/// </summary>
/// <remarks>
/// The formatting lives in the model, not in the page, because it is a property of the measure
/// rather than of where it is shown: revenue is euros in a table, in a chart tooltip and in a CSV
/// export alike. Time groupings are formatted by their bucket, so a monthly figure reads "03.2026"
/// rather than a spurious first-of-the-month date that invites being read as a day.
/// </remarks>
public static class ResultFormatter
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Formats <paramref name="value"/> for display in <paramref name="column"/>.</summary>
    public static string Format(object? value, ResultColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (value is null)
        {
            // An em dash rather than "null" or an empty cell: absent is a fact about the data and
            // should look deliberate, not like a rendering failure.
            return "—";
        }

        if (column.Kind is ResultColumnKind.Grouping)
        {
            return FormatGrouping(value, column.Grain);
        }

        return FormatNumber(value, column.Format ?? ValueFormat.Default);
    }

    /// <summary>Converts a value to a number for plotting, or null when it is not numeric.</summary>
    public static decimal? ToNumber(object? value) => value switch
    {
        null => null,
        decimal d => d,
        double d => (decimal)d,
        float f => (decimal)f,
        long l => l,
        int i => i,
        short s => s,
        byte b => b,
        _ => decimal.TryParse(
                value.ToString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed)
            ? parsed
            : null,
    };

    /// <summary>Formats a grouping key, bucketing dates by their grain.</summary>
    public static string FormatGrouping(object? value, TimeGrain grain)
    {
        if (value is null)
        {
            return "—";
        }

        if (value is not DateTime timestamp)
        {
            return value is DateTimeOffset offset
                ? FormatGrouping(offset.DateTime, grain)
                : Convert.ToString(value, Culture) ?? string.Empty;
        }

        return grain switch
        {
            TimeGrain.Year => timestamp.ToString("yyyy", Culture),
            TimeGrain.Quarter => $"Q{(timestamp.Month - 1) / 3 + 1} {timestamp.ToString("yyyy", Culture)}",
            TimeGrain.Month => timestamp.ToString("MM.yyyy", Culture),
            TimeGrain.Week => $"KW {ISOWeek.GetWeekOfYear(timestamp).ToString("00", Culture)} {ISOWeek.GetYear(timestamp).ToString(Culture)}",
            _ => timestamp.ToString("dd.MM.yyyy", Culture),
        };
    }

    private static string FormatNumber(object value, ValueFormat format)
    {
        var number = ToNumber(value);

        if (number is null)
        {
            return Convert.ToString(value, Culture) ?? string.Empty;
        }

        return format.Kind switch
        {
            ValueFormatKind.Integer => number.Value.ToString("N0", Culture),
            ValueFormatKind.Percent => (number.Value * 100m).ToString($"N{format.Decimals}", Culture) + " %",
            ValueFormatKind.Currency => number.Value.ToString($"N{format.Decimals}", Culture) + " " + (format.Currency ?? string.Empty),
            _ => number.Value.ToString($"N{format.Decimals}", Culture),
        };
    }
}
