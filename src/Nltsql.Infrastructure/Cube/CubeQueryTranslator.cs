using System.Globalization;
using System.Text.Json.Nodes;
using Nltsql.Core.Queries;
using Nltsql.Core.Sql;

namespace Nltsql.Infrastructure.Cube;

/// <summary>
/// Translates a <see cref="SemanticQuery"/> into Cube's REST query JSON.
/// </summary>
/// <remarks>
/// Member names are stored unqualified and get the view name prefixed
/// here, which is the single place that knows about Cube's naming.
/// </remarks>
internal static class CubeQueryTranslator
{
    public static JsonObject ToCubeQuery(SemanticQuery query)
    {
        var cubeQuery = new JsonObject
        {
            ["limit"] = Math.Clamp(query.Limit, 1, QueryLimits.MaxRows),
        };

        if (query.Measures.Count > 0)
        {
            cubeQuery["measures"] = ToArray(query.Measures.Select(m => Qualify(query.View, m)));
        }

        if (query.Dimensions.Count > 0)
        {
            cubeQuery["dimensions"] = ToArray(query.Dimensions.Select(d => Qualify(query.View, d)));
        }

        if (query.TimeDimension is { } time)
        {
            cubeQuery["timeDimensions"] = new JsonArray(ToTimeDimension(query.View, time));
        }

        if (query.Filters.Count > 0)
        {
            cubeQuery["filters"] = new JsonArray(
                query.Filters.Select(f => (JsonNode?)ToFilter(query.View, f)).ToArray());
        }

        if (query.Order.Count > 0)
        {
            // Array form preserves the order the user chose; the object
            // form does not guarantee it.
            cubeQuery["order"] = new JsonArray(
                query.Order.Select(o => (JsonNode?)new JsonArray(
                    Qualify(query.View, o.Member),
                    o.Direction == SortDirection.Ascending ? "asc" : "desc")).ToArray());
        }

        return cubeQuery;
    }

    private static JsonObject ToTimeDimension(string view, QueryTimeDimension time)
    {
        var node = new JsonObject
        {
            ["dimension"] = Qualify(view, time.Dimension),
        };

        if (time.Granularity is { } granularity)
        {
            node["granularity"] = granularity.ToString().ToLowerInvariant();
        }

        if (time.DateRange is { } range)
        {
            node["dateRange"] = range.Relative is { } relative
                ? RelativeRange.ToCubeDateRange(relative)
                : new JsonArray(
                    Iso(range.From ?? DateOnly.MinValue),
                    Iso(range.To ?? DateOnly.FromDateTime(DateTime.UtcNow)));
        }

        return node;
    }

    private static JsonObject ToFilter(string view, QueryFilter filter)
    {
        var node = new JsonObject
        {
            ["member"] = Qualify(view, filter.Member),
            ["operator"] = ToCubeOperator(filter.Operator),
        };

        if (!QueryFilter.IsUnary(filter.Operator))
        {
            node["values"] = ToArray(filter.Values);
        }

        return node;
    }

    private static string ToCubeOperator(FilterOperator op) => op switch
    {
        FilterOperator.Equals => "equals",
        FilterOperator.NotEquals => "notEquals",
        FilterOperator.Contains => "contains",
        FilterOperator.NotContains => "notContains",
        FilterOperator.StartsWith => "startsWith",
        FilterOperator.GreaterThan => "gt",
        FilterOperator.GreaterThanOrEqual => "gte",
        FilterOperator.LessThan => "lt",
        FilterOperator.LessThanOrEqual => "lte",
        FilterOperator.Set => "set",
        FilterOperator.NotSet => "notSet",
        _ => throw new NotSupportedException($"Filteroperator {op} wird von Cube nicht unterstützt."),
    };

    public static string Qualify(string view, string member) =>
        member.Contains('.', StringComparison.Ordinal) ? member : $"{view}.{member}";

    /// <summary>Strips the view prefix Cube uses in meta and result keys.</summary>
    public static string Unqualify(string member)
    {
        var separator = member.IndexOf('.', StringComparison.Ordinal);
        return separator < 0 ? member : member[(separator + 1)..];
    }

    private static JsonArray ToArray(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode?)v).ToArray());

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
