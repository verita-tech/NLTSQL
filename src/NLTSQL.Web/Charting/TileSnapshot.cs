using System.Globalization;
using System.Text.Json;
using NLTSQL.Core.Query;
using NLTSQL.Core.Serialization;
using NLTSQL.QueryEngine.Execution;
using NLTSQL.QueryEngine.Sql;

namespace NLTSQL.Web.Charting;

/// <summary>
/// A result frozen at a point in time, for a snapshot tile.
/// </summary>
/// <remarks>
/// <para>
/// Values are kept as JSON rather than as formatted text so a snapshot can still be charted and
/// still respects the measure's formatting when it is read back. Storing the rendered strings would
/// have been shorter but would turn every frozen tile into a picture of a table.
/// </para>
/// <para>
/// The one lossy spot is dates: JSON has no date type, so a grouped timestamp comes back as a
/// string and is re-parsed using the column's grain. That is why <see cref="ToResult"/> needs the
/// column metadata and not just the values.
/// </para>
/// </remarks>
/// <param name="Columns">The result's columns.</param>
/// <param name="Rows">The values, one list per row.</param>
/// <param name="Truncated">Whether the row cap had been reached when the snapshot was taken.</param>
/// <param name="TakenAt">When it was frozen. Always shown, so nobody mistakes it for current.</param>
public sealed record TileSnapshot(
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<IReadOnlyList<JsonElement>> Rows,
    bool Truncated,
    DateTimeOffset TakenAt)
{
    /// <summary>Freezes <paramref name="result"/>.</summary>
    public static TileSnapshot From(QueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new TileSnapshot(
            result.Columns,
            [.. result.Rows.Select(row => (IReadOnlyList<JsonElement>)
                [.. row.Select(value => JsonSerializer.SerializeToElement(value, NltsqlJson.Options))])],
            result.Truncated,
            DateTimeOffset.UtcNow);
    }

    /// <summary>Reads the snapshot back into the shape the table and chart components expect.</summary>
    public QueryResult ToResult() =>
        new(
            this.Columns,
            [.. this.Rows.Select(row => (IReadOnlyList<object?>)
                [.. row.Select((value, index) => Convert(value, this.Columns[index]))])],
            this.Truncated,
            TimeSpan.Zero);

    private static object? Convert(JsonElement value, ResultColumn column) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetDecimal(out var number) ? number : value.GetDouble(),
        JsonValueKind.String => ConvertString(value.GetString(), column),
        _ => value.ToString(),
    };

    private static object? ConvertString(string? text, ResultColumn column)
    {
        if (text is null)
        {
            return null;
        }

        // Only a grouped time column is expected to hold a date. Parsing every string would risk
        // turning a product code that happens to look like a date into one.
        var isTemporal = column.Kind is ResultColumnKind.Grouping &&
            (column.Grain is not TimeGrain.None || column.DataType is Semantics.Model.DataType.Date or Semantics.Model.DataType.Timestamp);

        return isTemporal &&
               DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp
            : text;
    }
}
