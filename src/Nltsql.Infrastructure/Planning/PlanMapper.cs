using System.Globalization;
using Nltsql.Core.Queries;

namespace Nltsql.Infrastructure.Planning;

/// <summary>Maps the wire contract onto the domain query.</summary>
internal static class PlanMapper
{
    public static SemanticQuery? ToQuery(PlanContract contract)
    {
        if (string.IsNullOrWhiteSpace(contract.View))
        {
            return null;
        }

        return new SemanticQuery
        {
            View = contract.View,
            Measures = contract.Measures,
            Dimensions = contract.Dimensions,
            TimeDimension = ToTimeDimension(contract.TimeDimension),
            Filters = contract.Filters
                .Where(f => !string.IsNullOrWhiteSpace(f.Member))
                .Select(f => new QueryFilter
                {
                    Member = f.Member!,
                    Operator = Parse(f.Operator, FilterOperator.Equals),
                    Values = f.Values,
                })
                .ToList(),
            Order = contract.Order
                .Where(o => !string.IsNullOrWhiteSpace(o.Member))
                .Select(o => new QueryOrder
                {
                    Member = o.Member!,
                    Direction = Parse(o.Direction, SortDirection.Descending),
                })
                .ToList(),
            Limit = Math.Clamp(contract.Limit ?? QueryLimits.DefaultRows, 1, QueryLimits.MaxRows),
        };
    }

    private static QueryTimeDimension? ToTimeDimension(PlanTimeDimension? time)
    {
        if (time is null || string.IsNullOrWhiteSpace(time.Dimension))
        {
            return null;
        }

        return new QueryTimeDimension
        {
            Dimension = time.Dimension,
            Granularity = Enum.TryParse<TimeGranularity>(time.Granularity, ignoreCase: true, out var granularity)
                ? granularity
                : null,
            DateRange = ToDateRange(time),
        };
    }

    private static QueryDateRange? ToDateRange(PlanTimeDimension time)
    {
        if (Enum.TryParse<RelativeDateRange>(time.RelativeRange, ignoreCase: true, out var relative))
        {
            return QueryDateRange.Of(relative);
        }

        var from = ParseDate(time.From);
        var to = ParseDate(time.To);

        return from is null && to is null ? null : new QueryDateRange { From = from, To = to };
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static TEnum Parse<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
