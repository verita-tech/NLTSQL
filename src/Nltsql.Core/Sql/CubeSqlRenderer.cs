using System.Globalization;
using System.Text;
using Nltsql.Core.Queries;
using Nltsql.Core.Semantics;

namespace Nltsql.Core.Sql;

/// <summary>
/// Renders a semantic query as SQL for Cube's SQL API.
/// </summary>
/// <remarks>
/// This exists so Metabase reads through the semantic layer instead of
/// the warehouse. The generated statement selects from a Cube <em>view</em>
/// and wraps measures in <c>MEASURE()</c>, so Cube — not the card — owns
/// how OEE is computed. A user who opens the card in Metabase and adds
/// their own filters still gets the governed metric.
/// <para>
/// Rolling date ranges are emitted as relative SQL rather than resolved
/// dates, so a card stays current instead of freezing at its creation
/// date.
/// </para>
/// <para>
/// Injection: identifiers are never taken from user input — they are
/// resolved against the semantic model by
/// <see cref="SemanticQueryValidator"/> before rendering, and this class
/// re-checks that every member exists. Only filter <em>values</em> are
/// free text, and those go through <see cref="SqlText.Literal"/>.
/// </para>
/// </remarks>
public static class CubeSqlRenderer
{
    public static string Render(SemanticQuery query, SemanticView view)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(view);

        var selects = new List<string>();
        var groupBy = new List<int>();

        AppendTimeAxis(query, view, selects, groupBy);
        AppendDimensions(query, view, selects, groupBy);
        AppendMeasures(query, view, selects);

        if (selects.Count == 0)
        {
            throw new InvalidOperationException("Die Abfrage enthält keine auswählbaren Felder.");
        }

        var sql = new StringBuilder();
        sql.Append("SELECT\n  ").AppendJoin(",\n  ", selects).Append('\n');
        sql.Append("FROM ").Append(SqlText.Identifier(view.Name)).Append('\n');

        AppendWhere(query, view, sql);
        AppendGroupBy(groupBy, sql);
        AppendHaving(query, view, sql);
        AppendOrderBy(query, selects.Count, sql);

        sql.Append("LIMIT ").Append(Math.Clamp(query.Limit, 1, QueryLimits.MaxRows).ToString(CultureInfo.InvariantCulture));

        return sql.ToString();
    }

    private static void AppendTimeAxis(SemanticQuery query, SemanticView view, List<string> selects, List<int> groupBy)
    {
        var time = query.TimeDimension;
        if (time?.Granularity is null)
        {
            return;
        }

        var dimension = Require(view.FindDimension(time.Dimension), time.Dimension, view);

        var expression = $"DATE_TRUNC('{Granularity(time.Granularity.Value)}', {SqlText.Identifier(dimension.Name)})";
        selects.Add($"{expression} AS {SqlText.Identifier(dimension.Name)}");
        groupBy.Add(selects.Count);
    }

    private static void AppendDimensions(SemanticQuery query, SemanticView view, List<string> selects, List<int> groupBy)
    {
        foreach (var name in query.Dimensions)
        {
            var dimension = Require(view.FindDimension(name), name, view);

            selects.Add(SqlText.Identifier(dimension.Name));
            groupBy.Add(selects.Count);
        }
    }

    private static void AppendMeasures(SemanticQuery query, SemanticView view, List<string> selects)
    {
        foreach (var name in query.Measures)
        {
            var measure = Require(view.FindMeasure(name), name, view);

            // MEASURE() hands the aggregation back to the semantic layer.
            selects.Add($"MEASURE({SqlText.Identifier(measure.Name)}) AS {SqlText.Identifier(measure.Name)}");
        }
    }

    private static void AppendWhere(SemanticQuery query, SemanticView view, StringBuilder sql)
    {
        var predicates = new List<string>();

        if (query.TimeDimension is { DateRange: not null } time)
        {
            var dimension = Require(view.FindDimension(time.Dimension), time.Dimension, view);
            predicates.AddRange(DateRangePredicates(dimension.Name, time.DateRange!));
        }

        foreach (var filter in query.Filters)
        {
            // Measure filters belong in HAVING, not WHERE.
            if (view.FindMeasure(filter.Member) is not null)
            {
                continue;
            }

            var dimension = Require(view.FindDimension(filter.Member), filter.Member, view);
            predicates.Add(Predicate(SqlText.Identifier(dimension.Name), filter, dimension.Type));
        }

        if (predicates.Count > 0)
        {
            sql.Append("WHERE ").AppendJoin("\n  AND ", predicates).Append('\n');
        }
    }

    private static void AppendGroupBy(List<int> groupBy, StringBuilder sql)
    {
        if (groupBy.Count > 0)
        {
            sql.Append("GROUP BY ")
               .AppendJoin(", ", groupBy.Select(i => i.ToString(CultureInfo.InvariantCulture)))
               .Append('\n');
        }
    }

    private static void AppendHaving(SemanticQuery query, SemanticView view, StringBuilder sql)
    {
        var predicates = new List<string>();

        foreach (var filter in query.Filters)
        {
            var measure = view.FindMeasure(filter.Member);
            if (measure is null)
            {
                continue;
            }

            predicates.Add(Predicate($"MEASURE({SqlText.Identifier(measure.Name)})", filter, measure.Type));
        }

        if (predicates.Count > 0)
        {
            sql.Append("HAVING ").AppendJoin("\n  AND ", predicates).Append('\n');
        }
    }

    private static void AppendOrderBy(SemanticQuery query, int selectCount, StringBuilder sql)
    {
        var positions = new List<string>();
        var ordered = query.SelectedMembers.ToList();

        foreach (var order in query.Order)
        {
            // Order by ordinal: the alias may be a DATE_TRUNC expression,
            // and ordinals are unambiguous across SQL dialects.
            var index = ordered.FindIndex(m => string.Equals(m, order.Member, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index >= selectCount)
            {
                continue;
            }

            var direction = order.Direction == SortDirection.Ascending ? "ASC" : "DESC";
            positions.Add($"{(index + 1).ToString(CultureInfo.InvariantCulture)} {direction}");
        }

        if (positions.Count > 0)
        {
            sql.Append("ORDER BY ").AppendJoin(", ", positions).Append('\n');
        }
    }

    private static string Predicate(string column, QueryFilter filter, SemanticType type)
    {
        switch (filter.Operator)
        {
            case FilterOperator.Set:
                return $"{column} IS NOT NULL";
            case FilterOperator.NotSet:
                return $"{column} IS NULL";
            case FilterOperator.Contains:
                return $"{column} ILIKE {SqlText.Literal($"%{filter.Values[0]}%")}";
            case FilterOperator.NotContains:
                return $"{column} NOT ILIKE {SqlText.Literal($"%{filter.Values[0]}%")}";
            case FilterOperator.StartsWith:
                return $"{column} ILIKE {SqlText.Literal($"{filter.Values[0]}%")}";
        }

        var values = filter.Values.Select(v => Value(v, type)).ToList();

        return filter.Operator switch
        {
            FilterOperator.Equals when values.Count == 1 => $"{column} = {values[0]}",
            FilterOperator.Equals => $"{column} IN ({string.Join(", ", values)})",
            FilterOperator.NotEquals when values.Count == 1 => $"{column} <> {values[0]}",
            FilterOperator.NotEquals => $"{column} NOT IN ({string.Join(", ", values)})",
            FilterOperator.GreaterThan => $"{column} > {values[0]}",
            FilterOperator.GreaterThanOrEqual => $"{column} >= {values[0]}",
            FilterOperator.LessThan => $"{column} < {values[0]}",
            FilterOperator.LessThanOrEqual => $"{column} <= {values[0]}",
            _ => throw new NotSupportedException($"Filteroperator {filter.Operator} wird nicht unterstützt."),
        };
    }

    /// <summary>Numbers and booleans stay unquoted; everything else is a literal.</summary>
    private static string Value(string raw, SemanticType type) => type switch
    {
        SemanticType.Number when double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            => n.ToString(CultureInfo.InvariantCulture),
        SemanticType.Boolean when bool.TryParse(raw, out var b)
            => b ? "TRUE" : "FALSE",
        _ => SqlText.Literal(raw),
    };

    private static IEnumerable<string> DateRangePredicates(string column, QueryDateRange range)
    {
        var identifier = SqlText.Identifier(column);

        if (range.Relative is { } relative)
        {
            foreach (var predicate in RelativeRange.ToSql(identifier, relative))
            {
                yield return predicate;
            }

            yield break;
        }

        if (range.From is { } from)
        {
            yield return $"{identifier} >= DATE {SqlText.Literal(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}";
        }

        if (range.To is { } to)
        {
            // Inclusive end date, expressed as an exclusive upper bound so
            // that timestamps during the last day are not dropped.
            var exclusiveEnd = to.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            yield return $"{identifier} < DATE {SqlText.Literal(exclusiveEnd)}";
        }
    }

    private static string Granularity(TimeGranularity granularity) => granularity switch
    {
        TimeGranularity.Day => "day",
        TimeGranularity.Week => "week",
        TimeGranularity.Month => "month",
        TimeGranularity.Quarter => "quarter",
        TimeGranularity.Year => "year",
        _ => "day",
    };

    private static T Require<T>(T? member, string name, SemanticView view)
        where T : class =>
        member ?? throw new InvalidOperationException(
            $"\"{name}\" existiert im Datenbereich \"{view.Name}\" nicht. " +
            "Die Abfrage muss vor dem Rendern validiert werden.");
}
