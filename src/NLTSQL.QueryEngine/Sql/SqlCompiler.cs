using System.Globalization;
using System.Text;
using NLTSQL.Core.Expressions;
using NLTSQL.Core.Query;
using NLTSQL.Semantics.Model;

namespace NLTSQL.QueryEngine.Sql;

/// <summary>
/// Renders a resolved query as a parameterised statement.
/// </summary>
/// <remarks>
/// <para>
/// Every value — filter operands, relative time boundaries, row-policy values, <c>LIKE</c>
/// patterns — becomes a bound parameter. Nothing but identifiers from the semantic model and
/// numeric literals from parsed expressions is ever written into the statement text, which is what
/// makes the generated SQL safe by construction rather than by escaping.
/// </para>
/// <para>
/// The compiler has no error path. Everything that could fail was decided by
/// <see cref="QuerySpecResolver"/>; the one exception is a missing row-policy value, which throws
/// rather than degrading, because a query that quietly loses its tenant filter is the worst
/// outcome the system has.
/// </para>
/// </remarks>
public sealed class SqlCompiler(ISqlDialect dialect)
{
    private const string TableAlias = "t0";

    /// <summary>Compiles <paramref name="query"/> using the supplied policy values.</summary>
    /// <exception cref="InvalidOperationException">A row policy has no value in <paramref name="context"/>.</exception>
    public CompiledQuery Compile(ResolvedQuery query, QueryExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        var parameters = new List<QueryParameter>();
        var columns = new List<ResultColumn>();
        var sql = new StringBuilder();

        var aggregateSql = this.BuildAggregateSql(query);

        sql.Append("SELECT\n");
        AppendSelectList(sql, query, aggregateSql, columns);

        var table = $"{this.Quote(query.Entity.Table.Schema)}.{this.Quote(query.Entity.Table.Name)}";
        sql.Append(CultureInfo.InvariantCulture, $"\nFROM {table} {this.Quote(TableAlias)}");

        this.AppendWhere(sql, query, context, parameters);
        this.AppendGroupBy(sql, query);
        this.AppendOrderBy(sql, query);
        dialect.AppendRowLimit(sql, query.Limit);

        return new CompiledQuery(sql.ToString(), parameters, columns, dialect.Name);
    }

    private void AppendSelectList(
        StringBuilder sql,
        ResolvedQuery query,
        Dictionary<string, string> aggregateSql,
        List<ResultColumn> columns)
    {
        var items = new List<string>();

        foreach (var grouping in query.Groupings)
        {
            var expression = dialect.TruncateToGrain(this.Column(grouping.Column), grouping.Grain);
            items.Add($"  {expression} AS {this.Quote(grouping.Name)}");
            columns.Add(new ResultColumn(
                grouping.Name,
                grouping.Label,
                ResultColumnKind.Grouping,
                grouping.DataType,
                grouping.Grain));
        }

        foreach (var field in query.Fields)
        {
            switch (field)
            {
                case SelectedMeasure measure:
                    items.Add($"  {this.RenderAggregate(measure.Measure)} AS {this.Quote(measure.Name)}");
                    columns.Add(new ResultColumn(measure.Name, measure.Label, ResultColumnKind.Measure, Format: measure.Format));
                    break;

                case SelectedMetric metric:
                    // A metric's references are replaced by the aggregate SQL of the measures it
                    // names, so the arithmetic happens after aggregation. Substituting the raw
                    // column instead would compute the ratio per row and then aggregate it, which
                    // is a different — and wrong — number.
                    items.Add($"  {RenderExpression(metric.Metric.Expression, name => aggregateSql[name])} AS {this.Quote(metric.Name)}");
                    columns.Add(new ResultColumn(metric.Name, metric.Label, ResultColumnKind.Metric, Format: metric.Format));
                    break;

                default:
                    throw new NotSupportedException($"Unhandled selected field '{field.GetType().Name}'.");
            }
        }

        sql.AppendJoin(",\n", items);
    }

    /// <summary>
    /// Maps each measure a metric depends on to the aggregate SQL that computes it.
    /// </summary>
    private Dictionary<string, string> BuildAggregateSql(ResolvedQuery query) =>
        query.Fields
            .OfType<SelectedMetric>()
            .SelectMany(metric => metric.Dependencies)
            .DistinctBy(measure => measure.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(measure => measure.Name, this.RenderAggregate, StringComparer.OrdinalIgnoreCase);

    private void AppendWhere(
        StringBuilder sql,
        ResolvedQuery query,
        QueryExecutionContext context,
        List<QueryParameter> parameters)
    {
        var conditions = new List<string>();

        // Row policies go first so they are visible at the top of any statement someone reads in
        // an audit log or an execution plan.
        foreach (var policy in query.PolicyFilters)
        {
            if (!context.PolicyValues.TryGetValue(policy.Parameter, out var value) || value is null)
            {
                throw new InvalidOperationException(
                    $"Row policy on '{query.Entity.Name}' needs a value for '{policy.Parameter}' but none was supplied. " +
                    "Refusing to run a query that would drop its tenant filter.");
            }

            conditions.Add(policy.MultiValued && value is IEnumerable<object?> many
                ? $"{this.Column(policy.Column)} IN ({string.Join(", ", many.Select(v => this.Bind(parameters, v)))})"
                : $"{this.Column(policy.Column)} = {this.Bind(parameters, value)}");
        }

        foreach (var filter in query.Filters)
        {
            conditions.Add(this.RenderFilter(filter, query.ReferenceTime, parameters));
        }

        if (conditions.Count == 0)
        {
            return;
        }

        sql.Append("\nWHERE ");
        sql.AppendJoin("\n  AND ", conditions);
    }

    private string RenderFilter(ResolvedFilter filter, DateTimeOffset referenceTime, List<QueryParameter> parameters)
    {
        var column = this.Column(filter.Column);

        switch (filter.Operator)
        {
            case FilterOperator.Equals:
                return $"{column} = {this.Bind(parameters, filter.Values[0])}";

            case FilterOperator.NotEquals:
                // Business intent, not SQL default. Asked for "status is not cancelled", a user
                // means rows whose status is anything else, including rows with no status at all.
                // Plain <> silently drops those, which reads as missing data.
                return $"({column} IS NULL OR {column} <> {this.Bind(parameters, filter.Values[0])})";

            case FilterOperator.In:
                return $"{column} IN ({string.Join(", ", filter.Values.Select(v => this.Bind(parameters, v)))})";

            case FilterOperator.NotIn:
                return $"({column} IS NULL OR {column} NOT IN ({string.Join(", ", filter.Values.Select(v => this.Bind(parameters, v)))}))";

            case FilterOperator.GreaterThan:
                return $"{column} > {this.Bind(parameters, filter.Values[0])}";

            case FilterOperator.GreaterOrEqual:
                return $"{column} >= {this.Bind(parameters, filter.Values[0])}";

            case FilterOperator.LessThan:
                return $"{column} < {this.Bind(parameters, filter.Values[0])}";

            case FilterOperator.LessOrEqual:
                return $"{column} <= {this.Bind(parameters, filter.Values[0])}";

            case FilterOperator.Between:
                return $"{column} BETWEEN {this.Bind(parameters, filter.Values[0])} AND {this.Bind(parameters, filter.Values[1])}";

            case FilterOperator.IsNull:
                return $"{column} IS NULL";

            case FilterOperator.IsNotNull:
                return $"{column} IS NOT NULL";

            case FilterOperator.Contains:
                return this.RenderLike(column, filter.Values[0], "%{0}%", parameters);

            case FilterOperator.StartsWith:
                return this.RenderLike(column, filter.Values[0], "{0}%", parameters);

            case FilterOperator.EndsWith:
                return this.RenderLike(column, filter.Values[0], "%{0}", parameters);

            case FilterOperator.InLastDays:
            case FilterOperator.InLastMonths:
            case FilterOperator.InLastYears:
                return this.RenderRelativeTime(column, filter, referenceTime, parameters);

            default:
                throw new NotSupportedException($"Unhandled filter operator '{filter.Operator}'.");
        }
    }

    private string RenderLike(string column, object? value, string pattern, List<QueryParameter> parameters)
    {
        // The wildcards go into the parameter value rather than into the statement, which avoids
        // needing a string-concatenation function that Oracle and PostgreSQL spell differently —
        // and means a user searching for a literal '%' gets what they asked for.
        var escaped = EscapeLikePattern(value as string ?? string.Empty, dialect.LikeEscapeCharacter);
        var bound = this.Bind(parameters, string.Format(CultureInfo.InvariantCulture, pattern, escaped));
        return $"{column} LIKE {bound} ESCAPE '{dialect.LikeEscapeCharacter}'";
    }

    private string RenderRelativeTime(
        string column,
        ResolvedFilter filter,
        DateTimeOffset referenceTime,
        List<QueryParameter> parameters)
    {
        var count = (int)Convert.ToInt64(filter.Values[0], CultureInfo.InvariantCulture);

        // Computed here rather than with the engine's own date arithmetic: the two databases spell
        // interval maths differently, and a boundary computed in C# is the same boundary in the
        // statement, in the audit record and in a test.
        var from = filter.Operator switch
        {
            FilterOperator.InLastDays => referenceTime.AddDays(-count),
            FilterOperator.InLastMonths => referenceTime.AddMonths(-count),
            FilterOperator.InLastYears => referenceTime.AddYears(-count),
            _ => throw new NotSupportedException($"Unhandled relative operator '{filter.Operator}'."),
        };

        var lower = filter.DataType is DataType.Date ? from.UtcDateTime.Date : from.UtcDateTime;
        var upper = filter.DataType is DataType.Date ? referenceTime.UtcDateTime.Date : referenceTime.UtcDateTime;

        return $"{column} >= {this.Bind(parameters, lower)} AND {column} <= {this.Bind(parameters, upper)}";
    }

    private void AppendGroupBy(StringBuilder sql, ResolvedQuery query)
    {
        if (query.Groupings.Count == 0)
        {
            return;
        }

        // The expression is repeated rather than referenced by alias: neither engine permits a
        // select-list alias in GROUP BY.
        sql.Append("\nGROUP BY ");
        sql.AppendJoin(
            ", ",
            query.Groupings.Select(g => dialect.TruncateToGrain(this.Column(g.Column), g.Grain)));
    }

    private void AppendOrderBy(StringBuilder sql, ResolvedQuery query)
    {
        if (query.OrderBy.Count == 0)
        {
            return;
        }

        sql.Append("\nORDER BY ");
        sql.AppendJoin(
            ", ",
            query.OrderBy.Select(o =>
                $"{this.Quote(o.Alias)} {(o.Direction is SortDirection.Ascending ? "ASC" : "DESC")}"));
    }

    private string RenderAggregate(Measure measure)
    {
        if (measure.Expression is null)
        {
            return "COUNT(*)";
        }

        var inner = RenderExpression(measure.Expression, this.Column);

        return measure.Aggregation switch
        {
            Aggregation.Sum => $"SUM({inner})",
            Aggregation.Average => $"AVG({inner})",
            Aggregation.Minimum => $"MIN({inner})",
            Aggregation.Maximum => $"MAX({inner})",
            Aggregation.Count => $"COUNT({inner})",
            Aggregation.CountDistinct => $"COUNT(DISTINCT {inner})",
            _ => throw new NotSupportedException($"Unhandled aggregate '{measure.Aggregation}'."),
        };
    }

    private static string RenderExpression(SqlExpression expression, Func<string, string> resolveReference) => expression switch
    {
        ReferenceExpression reference => resolveReference(reference.Name),

        // Safe to inline: the value came from the expression parser as a decimal, so it cannot
        // carry anything but digits and a decimal point.
        NumberExpression number => number.Value.ToString(CultureInfo.InvariantCulture),

        NegateExpression negate => $"-({RenderExpression(negate.Operand, resolveReference)})",

        BinaryExpression binary =>
            $"({RenderExpression(binary.Left, resolveReference)} {Operator(binary.Operator)} {RenderExpression(binary.Right, resolveReference)})",

        FunctionExpression function =>
            $"{function.Name}({string.Join(", ", function.Arguments.Select(a => RenderExpression(a, resolveReference)))})",

        _ => throw new NotSupportedException($"Unhandled expression node '{expression.GetType().Name}'."),
    };

    private static string Operator(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        _ => throw new NotSupportedException($"Unhandled operator '{op}'."),
    };

    internal static string EscapeLikePattern(string value, char escape)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var c in value)
        {
            if (c == escape || c is '%' or '_')
            {
                builder.Append(escape);
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private string Bind(List<QueryParameter> parameters, object? value)
    {
        var name = "p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
        parameters.Add(new QueryParameter(name, value));
        return dialect.ParameterReference(name);
    }

    private string Column(string column) => $"{this.Quote(TableAlias)}.{this.Quote(column)}";

    private string Quote(string identifier) => dialect.QuoteIdentifier(identifier);
}
