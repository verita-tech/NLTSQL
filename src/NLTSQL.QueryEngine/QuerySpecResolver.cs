using System.Globalization;
using Microsoft.Extensions.Options;
using NLTSQL.Core.Expressions;
using NLTSQL.Core.Query;
using NLTSQL.Semantics.Model;
using NLTSQL.Semantics.Validation;

namespace NLTSQL.QueryEngine;

/// <summary>The outcome of resolving a query spec.</summary>
/// <param name="Query">The resolved query, or <see langword="null"/> when resolution failed.</param>
/// <param name="Issues">Everything that went wrong.</param>
public sealed record QueryResolutionResult(ResolvedQuery? Query, IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>Whether a runnable query came out.</summary>
    public bool Success => this.Query is not null;
}

/// <summary>
/// Turns a query spec into a runnable query, or explains precisely why it cannot.
/// </summary>
/// <remarks>
/// <para>
/// This is the gate between the language model and the database. Nothing reaches the SQL compiler
/// that has not been checked here, and every operand is converted to a CLR value on the way
/// through — a value that will not convert is a rejected query rather than a string that has to be
/// escaped later.
/// </para>
/// <para>
/// The wording of the failures matters more than it usually would: they are fed straight back to
/// the model as the repair prompt, so each one names what was wrong <em>and</em> what was
/// available instead. A bare "unknown field" leaves the model to guess again; listing the real
/// names usually gets a correct spec on the next attempt.
/// </para>
/// </remarks>
public sealed class QuerySpecResolver(IOptions<QueryLimits> limits, TimeProvider timeProvider)
{
    private readonly QueryLimits limits = limits?.Value ?? throw new ArgumentNullException(nameof(limits));

    /// <summary>Resolves <paramref name="spec"/> against <paramref name="model"/>.</summary>
    public QueryResolutionResult Resolve(QuerySpec spec, SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(model);

        var issues = new List<ValidationIssue>();

        var entity = model.FindEntity(spec.Entity);
        if (entity is null || entity.Hidden)
        {
            issues.Add(ValidationIssue.Error(
                "query.entity.unknown",
                "entity",
                $"'{spec.Entity}' is not an entity of model '{model.Name}'. Available: {Available(model.Entities.Where(e => !e.Hidden).Select(e => e.Name))}."));
            return new QueryResolutionResult(null, issues);
        }

        var fields = this.ResolveFields(spec, entity, issues);
        var groupings = this.ResolveGroupings(spec, entity, issues);
        var filters = this.ResolveFilters(spec, entity, issues);
        var orderBy = ResolveOrderBy(spec, fields, groupings, issues);

        if (fields.Count == 0 && groupings.Count == 0 && !issues.HasErrors())
        {
            issues.Add(ValidationIssue.Error(
                "query.empty",
                "measures",
                $"A query must select at least one measure or grouping. Measures available on '{entity.Name}': " +
                $"{Available(entity.Measures.Where(m => !m.Hidden).Select(m => m.Name).Concat(entity.Metrics.Where(m => !m.Hidden).Select(m => m.Name)))}."));
        }

        if (issues.HasErrors())
        {
            return new QueryResolutionResult(null, issues);
        }

        var query = new ResolvedQuery
        {
            Model = model,
            Entity = entity,
            Fields = fields,
            Groupings = groupings,
            Filters = filters,
            PolicyFilters = [.. model.PoliciesFor(entity.Name).Select(p => new PolicyFilter(p.Column, p.Parameter, p.MultiValued))],
            OrderBy = orderBy,
            Limit = this.ClampLimit(spec.Limit),
            ReferenceTime = timeProvider.GetUtcNow(),
        };

        return new QueryResolutionResult(query, issues);
    }

    // A request above the ceiling is clamped rather than refused: the user gets an answer with a
    // stated row cap, which is far more useful than an error about a number they never chose.
    private int ClampLimit(int? requested) => requested is > 0
        ? Math.Min(requested.Value, this.limits.MaxRowLimit)
        : this.limits.DefaultRowLimit;

    private List<SelectedField> ResolveFields(QuerySpec spec, Entity entity, List<ValidationIssue> issues)
    {
        var fields = new List<SelectedField>();

        if (spec.Measures.Count > this.limits.MaxSelectedFields)
        {
            issues.Add(ValidationIssue.Error(
                "query.measures.too_many",
                "measures",
                $"{spec.Measures.Count} measures requested; at most {this.limits.MaxSelectedFields} are allowed."));
            return fields;
        }

        foreach (var name in spec.Measures.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (entity.FindMeasure(name) is { } measure)
            {
                if (measure.Hidden)
                {
                    issues.Add(HiddenIssue("query.measure.hidden", "measures", name));
                    continue;
                }

                fields.Add(new SelectedMeasure(measure));
                continue;
            }

            if (entity.FindMetric(name) is { } metric)
            {
                if (metric.Hidden)
                {
                    issues.Add(HiddenIssue("query.metric.hidden", "measures", name));
                    continue;
                }

                // A metric is arithmetic over aggregates, so its inputs have to be aggregated in
                // the same statement. They are resolved here rather than in the compiler so a
                // metric whose measure was later removed fails as a query error with a readable
                // message instead of as a dangling reference during rendering.
                var dependencies = new List<Measure>();
                foreach (var reference in metric.Expression.References().Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (entity.FindMeasure(reference) is { } dependency)
                    {
                        dependencies.Add(dependency);
                    }
                    else
                    {
                        issues.Add(ValidationIssue.Error(
                            "query.metric.broken",
                            $"measures.{name}",
                            $"Metric '{name}' references measure '{reference}', which '{entity.Name}' no longer declares. The model needs fixing, not the question."));
                    }
                }

                fields.Add(new SelectedMetric(metric, dependencies));
                continue;
            }

            issues.Add(ValidationIssue.Error(
                "query.measure.unknown",
                "measures",
                $"'{name}' is not a measure or metric of '{entity.Name}'. Available: " +
                $"{Available(entity.Measures.Where(m => !m.Hidden).Select(m => m.Name).Concat(entity.Metrics.Where(m => !m.Hidden).Select(m => m.Name)))}."));
        }

        return fields;
    }

    private List<SelectedGrouping> ResolveGroupings(QuerySpec spec, Entity entity, List<ValidationIssue> issues)
    {
        var groupings = new List<SelectedGrouping>();

        if (spec.GroupBy.Count > this.limits.MaxGroupings)
        {
            issues.Add(ValidationIssue.Error(
                "query.group_by.too_many",
                "group_by",
                $"{spec.GroupBy.Count} grouping columns requested; at most {this.limits.MaxGroupings} are allowed."));
            return groupings;
        }

        foreach (var item in spec.GroupBy)
        {
            if (entity.FindTimeDimension(item.Field) is { } time)
            {
                if (time.Hidden)
                {
                    issues.Add(HiddenIssue("query.group_by.hidden", "group_by", item.Field));
                    continue;
                }

                var grain = item.Grain;
                if (grain is not TimeGrain.None && !time.Granularities.Contains(ToGranularity(grain)))
                {
                    issues.Add(ValidationIssue.Error(
                        "query.grain.unsupported",
                        $"group_by.{item.Field}",
                        $"'{item.Field}' cannot be grouped by {grain.ToString().ToLowerInvariant()}. Available: " +
                        $"{Available(time.Granularities.Select(g => g.ToString().ToLowerInvariant()))}."));
                    continue;
                }

                groupings.Add(new SelectedGrouping(item.Field, time.Label, time.Column, time.DataType, grain));
                continue;
            }

            if (entity.FindDimension(item.Field) is { } dimension)
            {
                if (dimension.Hidden)
                {
                    issues.Add(HiddenIssue("query.group_by.hidden", "group_by", item.Field));
                    continue;
                }

                groupings.Add(new SelectedGrouping(item.Field, dimension.Label, dimension.Column, dimension.DataType, TimeGrain.None));
                continue;
            }

            issues.Add(ValidationIssue.Error(
                "query.group_by.unknown",
                "group_by",
                $"'{item.Field}' is not a dimension of '{entity.Name}'. Available: {Available(VisibleFieldNames(entity))}."));
        }

        return groupings;
    }

    private List<ResolvedFilter> ResolveFilters(QuerySpec spec, Entity entity, List<ValidationIssue> issues)
    {
        var filters = new List<ResolvedFilter>();

        foreach (var filter in spec.Filters)
        {
            var path = $"filters.{filter.Field}";

            string column;
            DataType dataType;

            if (entity.FindTimeDimension(filter.Field) is { Hidden: false } time)
            {
                (column, dataType) = (time.Column, time.DataType);
            }
            else if (entity.FindDimension(filter.Field) is { Hidden: false } dimension)
            {
                (column, dataType) = (dimension.Column, dimension.DataType);
            }
            else
            {
                issues.Add(ValidationIssue.Error(
                    "query.filter.unknown_field",
                    path,
                    $"'{filter.Field}' is not a filterable field of '{entity.Name}'. Available: {Available(VisibleFieldNames(entity))}."));
                continue;
            }

            var values = filter.Values ?? [];

            if (!this.ValidateOperator(filter.Operator, dataType, values, path, issues))
            {
                continue;
            }

            var converted = new List<object?>(values.Count);
            var failed = false;

            foreach (var raw in values)
            {
                // Relative time operators carry a count, not a value of the column's type.
                var targetType = IsRelativeTime(filter.Operator) ? DataType.Integer : dataType;

                if (TryConvert(raw, targetType, out var value))
                {
                    converted.Add(value);
                }
                else
                {
                    issues.Add(ValidationIssue.Error(
                        "query.filter.value_invalid",
                        path,
                        $"'{raw}' is not a valid {targetType.ToString().ToLowerInvariant()} value for '{filter.Field}'."));
                    failed = true;
                }
            }

            if (failed)
            {
                continue;
            }

            if (IsRelativeTime(filter.Operator) && converted is [long count] && count <= 0)
            {
                issues.Add(ValidationIssue.Error(
                    "query.filter.value_invalid",
                    path,
                    $"A relative time filter needs a positive number of periods, not {count}."));
                continue;
            }

            filters.Add(new ResolvedFilter(column, dataType, filter.Operator, converted));
        }

        return filters;
    }

    private bool ValidateOperator(
        FilterOperator op,
        DataType dataType,
        IReadOnlyList<string> values,
        string path,
        List<ValidationIssue> issues)
    {
        var (minimum, maximum) = op switch
        {
            FilterOperator.IsNull or FilterOperator.IsNotNull => (0, 0),
            FilterOperator.In or FilterOperator.NotIn => (1, this.limits.MaxFilterValues),
            FilterOperator.Between => (2, 2),
            _ => (1, 1),
        };

        if (values.Count < minimum || values.Count > maximum)
        {
            issues.Add(ValidationIssue.Error(
                "query.filter.arity",
                path,
                maximum == minimum
                    ? $"Operator '{op}' takes exactly {minimum} value(s) but got {values.Count}."
                    : $"Operator '{op}' takes between {minimum} and {maximum} values but got {values.Count}."));
            return false;
        }

        if (op is FilterOperator.Contains or FilterOperator.StartsWith or FilterOperator.EndsWith &&
            dataType is not DataType.String)
        {
            issues.Add(ValidationIssue.Error(
                "query.filter.operator_type",
                path,
                $"Operator '{op}' applies to text fields, but this field is {dataType.ToString().ToLowerInvariant()}."));
            return false;
        }

        if (IsRelativeTime(op) && dataType is not (DataType.Date or DataType.Timestamp))
        {
            issues.Add(ValidationIssue.Error(
                "query.filter.operator_type",
                path,
                $"Operator '{op}' applies to date fields, but this field is {dataType.ToString().ToLowerInvariant()}."));
            return false;
        }

        if (dataType is DataType.Boolean &&
            op is FilterOperator.GreaterThan or FilterOperator.GreaterOrEqual
                or FilterOperator.LessThan or FilterOperator.LessOrEqual or FilterOperator.Between)
        {
            issues.Add(ValidationIssue.Error(
                "query.filter.operator_type",
                path,
                $"Operator '{op}' cannot be applied to a boolean field."));
            return false;
        }

        return true;
    }

    private static List<ResolvedOrderBy> ResolveOrderBy(
        QuerySpec spec,
        List<SelectedField> fields,
        List<SelectedGrouping> groupings,
        List<ValidationIssue> issues)
    {
        var aliases = fields.Select(f => f.Name)
            .Concat(groupings.Select(g => g.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orderBy = new List<ResolvedOrderBy>();

        foreach (var item in spec.OrderBy)
        {
            if (!aliases.TryGetValue(item.Field, out var alias))
            {
                // Ordering by something the query does not select is valid SQL but almost never
                // what was meant, and it cannot be shown to the user, so it is refused.
                issues.Add(ValidationIssue.Error(
                    "query.order_by.unknown",
                    "order_by",
                    $"'{item.Field}' is not selected by this query, so it cannot be sorted on. Selected: {Available(aliases)}."));
                continue;
            }

            orderBy.Add(new ResolvedOrderBy(alias, item.Direction));
        }

        return orderBy;
    }

    private static ValidationIssue HiddenIssue(string code, string path, string name) =>
        ValidationIssue.Error(code, path, $"'{name}' is hidden in this model and cannot be queried.");

    private static IEnumerable<string> VisibleFieldNames(Entity entity) =>
        entity.Dimensions.Where(d => !d.Hidden).Select(d => d.Name)
            .Concat(entity.TimeDimensions.Where(d => !d.Hidden).Select(d => d.Name));

    private static string Available(IEnumerable<string> names)
    {
        var list = names.Order(StringComparer.Ordinal).ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }

    private static bool IsRelativeTime(FilterOperator op) =>
        op is FilterOperator.InLastDays or FilterOperator.InLastMonths or FilterOperator.InLastYears;

    private static Granularity ToGranularity(TimeGrain grain) => grain switch
    {
        TimeGrain.Day => Granularity.Day,
        TimeGrain.Week => Granularity.Week,
        TimeGrain.Month => Granularity.Month,
        TimeGrain.Quarter => Granularity.Quarter,
        TimeGrain.Year => Granularity.Year,
        _ => Granularity.Day,
    };

    private static bool TryConvert(string? raw, DataType dataType, out object? value)
    {
        value = null;

        if (raw is null)
        {
            return true;
        }

        var text = raw.Trim();

        switch (dataType)
        {
            case DataType.String:
                value = raw;
                return true;

            case DataType.Integer:
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                {
                    value = integer;
                    return true;
                }

                return false;

            case DataType.Decimal:
                if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    value = number;
                    return true;
                }

                return false;

            case DataType.Boolean:
                switch (text.ToLowerInvariant())
                {
                    case "true" or "1" or "yes":
                        value = true;
                        return true;
                    case "false" or "0" or "no":
                        value = false;
                        return true;
                    default:
                        return false;
                }

            case DataType.Date:
            case DataType.Timestamp:
                // Invariant parsing only. Accepting locale-specific formats here would make
                // "03.04.2025" mean different days depending on where the server runs.
                if (DateTime.TryParse(
                        text,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var timestamp))
                {
                    value = dataType is DataType.Date ? timestamp.Date : timestamp;
                    return true;
                }

                return false;

            default:
                return false;
        }
    }
}
