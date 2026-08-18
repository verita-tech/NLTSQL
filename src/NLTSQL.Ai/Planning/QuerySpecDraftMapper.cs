using System.Collections.Frozen;
using NLTSQL.Core.Query;
using NLTSQL.Semantics.Validation;

namespace NLTSQL.Ai.Planning;

/// <summary>
/// Maps what the model wrote onto the strict query spec.
/// </summary>
/// <remarks>
/// <para>
/// The tolerance here is intentional and bounded. It covers spelling — <c>=</c> for equals,
/// <c>monthly</c> or <c>monat</c> for month, <c>descending</c> for desc — because those are
/// near-misses with exactly one sensible reading, and spending a repair round on them buys nothing.
/// </para>
/// <para>
/// It does not extend to meaning. An operator nobody recognises is reported rather than guessed at,
/// and no name is ever corrected: whether <c>umsatz</c> exists is the resolver's question, and it
/// answers with the list of what does.
/// </para>
/// </remarks>
public static class QuerySpecDraftMapper
{
    private static readonly FrozenDictionary<string, FilterOperator> Operators =
        new Dictionary<string, FilterOperator>(StringComparer.OrdinalIgnoreCase)
        {
            ["equals"] = FilterOperator.Equals,
            ["equal"] = FilterOperator.Equals,
            ["eq"] = FilterOperator.Equals,
            ["="] = FilterOperator.Equals,
            ["=="] = FilterOperator.Equals,
            ["is"] = FilterOperator.Equals,
            ["gleich"] = FilterOperator.Equals,
            ["not_equals"] = FilterOperator.NotEquals,
            ["notequals"] = FilterOperator.NotEquals,
            ["not_equal"] = FilterOperator.NotEquals,
            ["ne"] = FilterOperator.NotEquals,
            ["!="] = FilterOperator.NotEquals,
            ["<>"] = FilterOperator.NotEquals,
            ["ungleich"] = FilterOperator.NotEquals,
            ["in"] = FilterOperator.In,
            ["one_of"] = FilterOperator.In,
            ["not_in"] = FilterOperator.NotIn,
            ["notin"] = FilterOperator.NotIn,
            ["greater_than"] = FilterOperator.GreaterThan,
            ["greaterthan"] = FilterOperator.GreaterThan,
            ["gt"] = FilterOperator.GreaterThan,
            [">"] = FilterOperator.GreaterThan,
            ["greater_or_equal"] = FilterOperator.GreaterOrEqual,
            ["greater_than_or_equal"] = FilterOperator.GreaterOrEqual,
            ["gte"] = FilterOperator.GreaterOrEqual,
            [">="] = FilterOperator.GreaterOrEqual,
            ["less_than"] = FilterOperator.LessThan,
            ["lessthan"] = FilterOperator.LessThan,
            ["lt"] = FilterOperator.LessThan,
            ["<"] = FilterOperator.LessThan,
            ["less_or_equal"] = FilterOperator.LessOrEqual,
            ["less_than_or_equal"] = FilterOperator.LessOrEqual,
            ["lte"] = FilterOperator.LessOrEqual,
            ["<="] = FilterOperator.LessOrEqual,
            ["between"] = FilterOperator.Between,
            ["is_null"] = FilterOperator.IsNull,
            ["isnull"] = FilterOperator.IsNull,
            ["is_not_null"] = FilterOperator.IsNotNull,
            ["isnotnull"] = FilterOperator.IsNotNull,
            ["not_null"] = FilterOperator.IsNotNull,
            ["contains"] = FilterOperator.Contains,
            ["enthaelt"] = FilterOperator.Contains,
            ["starts_with"] = FilterOperator.StartsWith,
            ["startswith"] = FilterOperator.StartsWith,
            ["ends_with"] = FilterOperator.EndsWith,
            ["endswith"] = FilterOperator.EndsWith,
            ["in_last_days"] = FilterOperator.InLastDays,
            ["last_days"] = FilterOperator.InLastDays,
            ["in_last_months"] = FilterOperator.InLastMonths,
            ["last_months"] = FilterOperator.InLastMonths,
            ["in_last_years"] = FilterOperator.InLastYears,
            ["last_years"] = FilterOperator.InLastYears,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, TimeGrain> Grains =
        new Dictionary<string, TimeGrain>(StringComparer.OrdinalIgnoreCase)
        {
            ["day"] = TimeGrain.Day,
            ["daily"] = TimeGrain.Day,
            ["tag"] = TimeGrain.Day,
            ["taeglich"] = TimeGrain.Day,
            ["week"] = TimeGrain.Week,
            ["weekly"] = TimeGrain.Week,
            ["woche"] = TimeGrain.Week,
            ["month"] = TimeGrain.Month,
            ["monthly"] = TimeGrain.Month,
            ["monat"] = TimeGrain.Month,
            ["monatlich"] = TimeGrain.Month,
            ["quarter"] = TimeGrain.Quarter,
            ["quarterly"] = TimeGrain.Quarter,
            ["quartal"] = TimeGrain.Quarter,
            ["year"] = TimeGrain.Year,
            ["yearly"] = TimeGrain.Year,
            ["annual"] = TimeGrain.Year,
            ["jahr"] = TimeGrain.Year,
            ["jaehrlich"] = TimeGrain.Year,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps <paramref name="draft"/>, collecting anything it could not make sense of.</summary>
    /// <returns>The spec, or <see langword="null"/> when the draft is unusable.</returns>
    public static QuerySpec? Map(QuerySpecDraft draft, List<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(issues);

        if (draft.Entity is not { Length: > 0 } entity)
        {
            issues.Add(ValidationIssue.Error(
                "draft.entity.missing",
                "entity",
                "Die Antwort nennt keine Entitaet. Gib im Feld 'entity' den Namen genau einer Entitaet aus dem Datenmodell an."));
            return null;
        }

        return new QuerySpec
        {
            Entity = entity.Trim(),
            Measures = [.. (draft.Measures ?? []).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim())],
            GroupBy = [.. (draft.GroupBy ?? []).Select(g => MapGroupBy(g, issues)).OfType<GroupByItem>()],
            Filters = [.. (draft.Filters ?? []).Select(f => MapFilter(f, issues)).OfType<FilterItem>()],
            OrderBy = [.. (draft.OrderBy ?? []).Select(o => MapOrderBy(o, issues)).OfType<OrderByItem>()],
            Limit = draft.Limit,
        };
    }

    private static GroupByItem? MapGroupBy(GroupByDraft draft, List<ValidationIssue> issues)
    {
        if (draft.Field is not { Length: > 0 } field)
        {
            issues.Add(ValidationIssue.Error("draft.group_by.field_missing", "group_by", "Eine Gruppierung ohne 'field' ist unvollstaendig."));
            return null;
        }

        var grain = TimeGrain.None;

        if (draft.Grain is { Length: > 0 } text && !string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
        {
            if (!Grains.TryGetValue(text.Trim(), out grain))
            {
                issues.Add(ValidationIssue.Error(
                    "draft.grain.unknown",
                    $"group_by.{field}",
                    $"'{text}' ist keine Zeitstufe. Erlaubt: day, week, month, quarter, year."));
                return null;
            }
        }

        return new GroupByItem(field.Trim(), grain);
    }

    private static FilterItem? MapFilter(FilterDraft draft, List<ValidationIssue> issues)
    {
        if (draft.Field is not { Length: > 0 } field)
        {
            issues.Add(ValidationIssue.Error("draft.filter.field_missing", "filters", "Ein Filter ohne 'field' ist unvollstaendig."));
            return null;
        }

        // An omitted operator with exactly one value means equals often enough that assuming it is
        // safer than a repair round; anything else has to be stated.
        var text = draft.Operator?.Trim();
        FilterOperator op;

        if (string.IsNullOrEmpty(text))
        {
            if (draft.Values is not { Count: 1 })
            {
                issues.Add(ValidationIssue.Error(
                    "draft.filter.operator_missing",
                    $"filters.{field}",
                    "Ein Filter braucht ein 'operator'. Erlaubt sind unter anderem equals, in, between, greater_or_equal, in_last_months."));
                return null;
            }

            op = FilterOperator.Equals;
        }
        else if (!Operators.TryGetValue(text, out op))
        {
            issues.Add(ValidationIssue.Error(
                "draft.filter.operator_unknown",
                $"filters.{field}",
                $"'{text}' ist kein bekannter Operator. Erlaubt: equals, not_equals, in, not_in, greater_than, " +
                "greater_or_equal, less_than, less_or_equal, between, is_null, is_not_null, contains, starts_with, " +
                "ends_with, in_last_days, in_last_months, in_last_years."));
            return null;
        }

        return new FilterItem(field.Trim(), op, draft.Values ?? []);
    }

    private static OrderByItem? MapOrderBy(OrderByDraft draft, List<ValidationIssue> issues)
    {
        if (draft.Field is not { Length: > 0 } field)
        {
            issues.Add(ValidationIssue.Error("draft.order_by.field_missing", "order_by", "Eine Sortierung ohne 'field' ist unvollstaendig."));
            return null;
        }

        var direction = draft.Direction?.Trim().ToLowerInvariant() switch
        {
            null or "" => SortDirection.Descending,
            "asc" or "ascending" or "aufsteigend" or "up" => SortDirection.Ascending,
            "desc" or "descending" or "absteigend" or "down" => SortDirection.Descending,
            _ => (SortDirection?)null,
        };

        if (direction is null)
        {
            issues.Add(ValidationIssue.Error(
                "draft.order_by.direction_unknown",
                $"order_by.{field}",
                $"'{draft.Direction}' ist keine Sortierrichtung. Erlaubt: asc, desc."));
            return null;
        }

        return new OrderByItem(field.Trim(), direction.Value);
    }
}
