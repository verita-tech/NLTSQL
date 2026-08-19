using System.Globalization;
using Nltsql.Core.Queries;

namespace Nltsql.Core.Sql;

/// <summary>
/// Translates the rolling date ranges into the two dialects the app
/// speaks: Cube's REST <c>dateRange</c> shorthand and Cube SQL.
/// </summary>
/// <remarks>
/// Both translations live together on purpose. A rolling window has to
/// mean exactly the same thing in the in-app table and in the Metabase
/// card, otherwise the same tile shows two different numbers.
/// </remarks>
public static class RelativeRange
{
    /// <summary>The <c>dateRange</c> string understood by Cube's REST API.</summary>
    public static string ToCubeDateRange(RelativeDateRange range) => range switch
    {
        RelativeDateRange.Today => "today",
        RelativeDateRange.Yesterday => "yesterday",
        RelativeDateRange.Last7Days => "last 7 days",
        RelativeDateRange.Last30Days => "last 30 days",
        RelativeDateRange.Last90Days => "last 90 days",
        RelativeDateRange.Last12Months => "last 12 months",
        RelativeDateRange.ThisWeek => "this week",
        RelativeDateRange.ThisMonth => "this month",
        RelativeDateRange.ThisQuarter => "this quarter",
        RelativeDateRange.ThisYear => "this year",
        RelativeDateRange.LastWeek => "last week",
        RelativeDateRange.LastMonth => "last month",
        RelativeDateRange.LastQuarter => "last quarter",
        RelativeDateRange.LastYear => "last year",
        _ => "last 30 days",
    };

    public static string ToGermanLabel(RelativeDateRange range) => range switch
    {
        RelativeDateRange.Today => "Heute",
        RelativeDateRange.Yesterday => "Gestern",
        RelativeDateRange.Last7Days => "Letzte 7 Tage",
        RelativeDateRange.Last30Days => "Letzte 30 Tage",
        RelativeDateRange.Last90Days => "Letzte 90 Tage",
        RelativeDateRange.Last12Months => "Letzte 12 Monate",
        RelativeDateRange.ThisWeek => "Diese Woche",
        RelativeDateRange.ThisMonth => "Dieser Monat",
        RelativeDateRange.ThisQuarter => "Dieses Quartal",
        RelativeDateRange.ThisYear => "Dieses Jahr",
        RelativeDateRange.LastWeek => "Letzte Woche",
        RelativeDateRange.LastMonth => "Letzter Monat",
        RelativeDateRange.LastQuarter => "Letztes Quartal",
        RelativeDateRange.LastYear => "Letztes Jahr",
        _ => range.ToString(),
    };

    /// <summary>
    /// SQL predicates for the same window. Emitted relative to
    /// <c>NOW()</c> so the Metabase card keeps rolling forward.
    /// </summary>
    public static IEnumerable<string> ToSql(string identifier, RelativeDateRange range)
    {
        switch (range)
        {
            case RelativeDateRange.Today:
                yield return $"{identifier} >= DATE_TRUNC('day', NOW())";
                break;

            case RelativeDateRange.Yesterday:
                yield return $"{identifier} >= DATE_TRUNC('day', NOW()) - INTERVAL '1 day'";
                yield return $"{identifier} < DATE_TRUNC('day', NOW())";
                break;

            case RelativeDateRange.Last7Days:
            case RelativeDateRange.Last30Days:
            case RelativeDateRange.Last90Days:
                yield return RollingDays(identifier, range switch
                {
                    RelativeDateRange.Last7Days => 7,
                    RelativeDateRange.Last30Days => 30,
                    _ => 90,
                });
                break;

            case RelativeDateRange.Last12Months:
                yield return $"{identifier} >= DATE_TRUNC('day', NOW()) - INTERVAL '12 months'";
                break;

            case RelativeDateRange.ThisWeek:
            case RelativeDateRange.ThisMonth:
            case RelativeDateRange.ThisQuarter:
            case RelativeDateRange.ThisYear:
                yield return $"{identifier} >= DATE_TRUNC('{PeriodOf(range)}', NOW())";
                break;

            case RelativeDateRange.LastWeek:
            case RelativeDateRange.LastMonth:
            case RelativeDateRange.LastQuarter:
            case RelativeDateRange.LastYear:
                var period = PeriodOf(range);
                yield return $"{identifier} >= DATE_TRUNC('{period}', NOW()) - INTERVAL '1 {period}'";
                yield return $"{identifier} < DATE_TRUNC('{period}', NOW())";
                break;

            default:
                yield return RollingDays(identifier, 30);
                break;
        }
    }

    private static string RollingDays(string identifier, int days) =>
        $"{identifier} >= DATE_TRUNC('day', NOW()) - INTERVAL '{days.ToString(CultureInfo.InvariantCulture)} days'";

    private static string PeriodOf(RelativeDateRange range) => range switch
    {
        RelativeDateRange.ThisWeek or RelativeDateRange.LastWeek => "week",
        RelativeDateRange.ThisMonth or RelativeDateRange.LastMonth => "month",
        RelativeDateRange.ThisQuarter or RelativeDateRange.LastQuarter => "quarter",
        _ => "year",
    };
}
