namespace Nltsql.Core.Queries;

/// <summary>
/// A query expressed purely in business terms against one semantic view.
/// </summary>
/// <remarks>
/// This is the unit that gets saved to a dashboard, re-executed, exported
/// and translated to a Metabase card. It never contains SQL: member names
/// are validated against the live semantic model at execution time, so a
/// saved query keeps working when the physical model underneath changes.
/// <para>
/// Member names are stored unqualified (<c>oee</c>, not
/// <c>fertigung.oee</c>); the view supplies the qualifier at the Cube
/// boundary.
/// </para>
/// </remarks>
public sealed record SemanticQuery
{
    /// <summary>Name of the semantic view being queried.</summary>
    public required string View { get; init; }

    public IReadOnlyList<string> Measures { get; init; } = [];

    public IReadOnlyList<string> Dimensions { get; init; } = [];

    /// <summary>Optional time axis; drives granularity and date range.</summary>
    public QueryTimeDimension? TimeDimension { get; init; }

    public IReadOnlyList<QueryFilter> Filters { get; init; } = [];

    public IReadOnlyList<QueryOrder> Order { get; init; } = [];

    /// <summary>Row cap. Always bounded by <see cref="QueryLimits.MaxRows"/>.</summary>
    public int Limit { get; init; } = QueryLimits.DefaultRows;

    /// <summary>Every member the query touches, in selection order.</summary>
    public IEnumerable<string> SelectedMembers =>
        Dimensions.Concat(TimeDimension is null ? [] : new[] { TimeDimension.Dimension }).Concat(Measures);

    public bool IsEmpty => Measures.Count == 0 && Dimensions.Count == 0 && TimeDimension is null;
}

public static class QueryLimits
{
    public const int DefaultRows = 1_000;

    /// <summary>
    /// Hard ceiling for a self-service tool. Anything larger belongs in a
    /// scheduled export, not in a browser session.
    /// </summary>
    public const int MaxRows = 50_000;
}

/// <summary>Time axis of a query.</summary>
public sealed record QueryTimeDimension
{
    public required string Dimension { get; init; }

    /// <summary>Bucket size; <c>null</c> filters by date without bucketing.</summary>
    public TimeGranularity? Granularity { get; init; }

    public QueryDateRange? DateRange { get; init; }
}

public enum TimeGranularity
{
    Day,
    Week,
    Month,
    Quarter,
    Year,
}

/// <summary>
/// Either a rolling window ("letzte 30 Tage") or a fixed span.
/// </summary>
/// <remarks>
/// Rolling windows are kept symbolic rather than resolved to dates at
/// save time, so a dashboard tile stays current every time it is opened.
/// </remarks>
public sealed record QueryDateRange
{
    public RelativeDateRange? Relative { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public bool IsRelative => Relative is not null;

    public static QueryDateRange Of(RelativeDateRange relative) => new() { Relative = relative };

    public static QueryDateRange Between(DateOnly from, DateOnly to) => new() { From = from, To = to };
}

/// <summary>
/// The rolling windows the UI offers. A closed set rather than free text,
/// so the same window means the same thing in Cube, in the generated SQL
/// and in Metabase.
/// </summary>
public enum RelativeDateRange
{
    Today,
    Yesterday,
    Last7Days,
    Last30Days,
    Last90Days,
    Last12Months,
    ThisWeek,
    ThisMonth,
    ThisQuarter,
    ThisYear,
    LastWeek,
    LastMonth,
    LastQuarter,
    LastYear,
}

public sealed record QueryFilter
{
    public required string Member { get; init; }

    public required FilterOperator Operator { get; init; }

    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>Operators that carry no values.</summary>
    public static bool IsUnary(FilterOperator op) =>
        op is FilterOperator.Set or FilterOperator.NotSet;
}

public enum FilterOperator
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    StartsWith,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Set,
    NotSet,
}

public sealed record QueryOrder
{
    public required string Member { get; init; }

    public SortDirection Direction { get; init; } = SortDirection.Descending;
}

public enum SortDirection
{
    Ascending,
    Descending,
}
