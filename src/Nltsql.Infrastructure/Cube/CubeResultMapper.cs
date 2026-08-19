using System.Globalization;
using System.Text.Json;
using Nltsql.Core.Queries;
using Nltsql.Core.Results;
using Nltsql.Core.Semantics;

namespace Nltsql.Infrastructure.Cube;

/// <summary>
/// Projects a Cube load response onto <see cref="QueryResultSet"/>.
/// </summary>
/// <remarks>
/// Column order follows the query rather than the JSON, so the table the
/// user sees matches the order they built: breakdowns first, measures
/// last. Titles and formats come from Cube's annotation block, which
/// keeps the business labels defined in the semantic model.
/// </remarks>
internal static class CubeResultMapper
{
    public static QueryResultSet Map(
        JsonElement root,
        SemanticQuery query,
        SemanticView view,
        TimeSpan duration)
    {
        var payload = Unwrap(root);

        if (!payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return QueryResultSet.Empty with { Duration = duration };
        }

        var columns = BuildColumns(query, view, payload);
        var rows = new List<object?[]>(data.GetArrayLength());

        foreach (var record in data.EnumerateArray())
        {
            var row = new object?[columns.Count];

            for (var i = 0; i < columns.Count; i++)
            {
                row[i] = ReadValue(record, columns[i]);
            }

            rows.Add(row);
        }

        return new QueryResultSet
        {
            Columns = columns.Select(c => c.Column).ToList(),
            Rows = rows,
            Duration = duration,
            FromPreAggregation = UsedPreAggregation(payload),
            IsTruncated = rows.Count >= query.Limit,
        };
    }

    /// <summary>
    /// Cube returns a single query's payload either at the root or inside
    /// a one-element <c>results</c> array, depending on version and query
    /// type. Both shapes are accepted.
    /// </summary>
    private static JsonElement Unwrap(JsonElement root)
    {
        if (root.TryGetProperty("results", out var results)
            && results.ValueKind == JsonValueKind.Array
            && results.GetArrayLength() > 0)
        {
            return results[0];
        }

        return root;
    }

    private static List<MappedColumn> BuildColumns(SemanticQuery query, SemanticView view, JsonElement payload)
    {
        var annotation = payload.TryGetProperty("annotation", out var a) ? a : default;
        var columns = new List<MappedColumn>();

        if (query.TimeDimension is { } time)
        {
            var qualified = CubeQueryTranslator.Qualify(query.View, time.Dimension);
            var dimension = view.FindDimension(time.Dimension);

            // With a granularity, Cube keys the bucket as "<member>.<granularity>";
            // without one it uses the bare member name.
            var keys = time.Granularity is { } granularity
                ? new[] { $"{qualified}.{granularity.ToString().ToLowerInvariant()}", qualified }
                : [qualified];

            columns.Add(new MappedColumn(
                keys,
                new ResultColumn(
                    time.Dimension,
                    Title(annotation, "timeDimensions", qualified) ?? dimension?.Title ?? time.Dimension,
                    ResultColumnKind.TimeDimension,
                    SemanticValueType.Time)));
        }

        foreach (var name in query.Dimensions)
        {
            var qualified = CubeQueryTranslator.Qualify(query.View, name);
            var dimension = view.FindDimension(name);

            columns.Add(new MappedColumn(
                [qualified],
                new ResultColumn(
                    name,
                    Title(annotation, "dimensions", qualified) ?? dimension?.Title ?? name,
                    ResultColumnKind.Dimension,
                    ToValueType(dimension?.Type))));
        }

        foreach (var name in query.Measures)
        {
            var qualified = CubeQueryTranslator.Qualify(query.View, name);
            var measure = view.FindMeasure(name);

            columns.Add(new MappedColumn(
                [qualified],
                new ResultColumn(
                    name,
                    Title(annotation, "measures", qualified) ?? measure?.Title ?? name,
                    ResultColumnKind.Measure,
                    ToValueType(measure?.Type),
                    Format(annotation, qualified) ?? measure?.Format)));
        }

        return columns;
    }

    private static object? ReadValue(JsonElement record, MappedColumn column)
    {
        foreach (var key in column.Keys)
        {
            if (record.TryGetProperty(key, out var value))
            {
                return Convert(value, column.Column);
            }
        }

        return null;
    }

    private static object? Convert(JsonElement value, ResultColumn column) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => ConvertString(value.GetString()!, column),
        _ => value.ToString(),
    };

    /// <summary>
    /// Cube returns numbers as JSON strings when the warehouse type is
    /// wider than a double (numeric, bigint), and timestamps as ISO
    /// strings. Both are converted so sorting and CSV formatting behave.
    /// </summary>
    private static object ConvertString(string raw, ResultColumn column)
    {
        if (column.Kind == ResultColumnKind.Measure
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        if (column.ValueType == SemanticValueType.Time
            && DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            return timestamp;
        }

        return raw;
    }

    private static bool UsedPreAggregation(JsonElement payload) =>
        payload.TryGetProperty("usedPreAggregations", out var used)
        && used.ValueKind == JsonValueKind.Object
        && used.EnumerateObject().Any();

    private static string? Title(JsonElement annotation, string group, string member) =>
        Member(annotation, group, member) is { } m
        && m.TryGetProperty("shortTitle", out var title)
        && title.ValueKind == JsonValueKind.String
            ? title.GetString()
            : null;

    private static string? Format(JsonElement annotation, string member) =>
        Member(annotation, "measures", member) is { } m
        && m.TryGetProperty("format", out var format)
        && format.ValueKind == JsonValueKind.String
            ? format.GetString()
            : null;

    private static JsonElement? Member(JsonElement annotation, string group, string member)
    {
        if (annotation.ValueKind != JsonValueKind.Object
            || !annotation.TryGetProperty(group, out var members)
            || members.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return members.TryGetProperty(member, out var entry) ? entry : null;
    }

    private static SemanticValueType ToValueType(SemanticType? type) => type switch
    {
        SemanticType.Number => SemanticValueType.Number,
        SemanticType.Time => SemanticValueType.Time,
        SemanticType.Boolean => SemanticValueType.Boolean,
        _ => SemanticValueType.String,
    };

    /// <summary>A result column together with the JSON keys it may appear under.</summary>
    private sealed record MappedColumn(IReadOnlyList<string> Keys, ResultColumn Column);
}
