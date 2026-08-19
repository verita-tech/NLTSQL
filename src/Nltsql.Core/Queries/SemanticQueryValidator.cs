using Nltsql.Core.Semantics;

namespace Nltsql.Core.Queries;

/// <summary>
/// Checks a <see cref="SemanticQuery"/> against the live semantic model.
/// </summary>
/// <remarks>
/// This is the trust boundary. Queries reaching it may come from the UI,
/// from a saved dashboard tile written months ago, or from a language
/// model — all three are treated as untrusted input and must pass the
/// same checks before anything is executed or rendered into SQL.
/// <para>
/// Messages are user-facing German: they are shown in the UI and are also
/// handed back to the planner as repair instructions, so they name the
/// offending member and, where possible, suggest the intended one.
/// </para>
/// </remarks>
public static class SemanticQueryValidator
{
    public static ValidationResult Validate(SemanticQuery query, SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(model);

        var errors = new List<ValidationError>();

        var view = model.FindView(query.View);
        if (view is null)
        {
            var known = string.Join(", ", model.Views.Select(v => v.Name));
            errors.Add(new ValidationError(
                nameof(query.View),
                $"Der Datenbereich \"{query.View}\" existiert nicht. Verfügbar: {known}."));

            // Without a view nothing else can be resolved.
            return new ValidationResult(errors);
        }

        if (query.IsEmpty)
        {
            errors.Add(new ValidationError(
                null,
                "Die Abfrage enthält weder eine Kennzahl noch ein Merkmal."));
        }

        ValidateMeasures(query, view, errors);
        ValidateDimensions(query, view, errors);
        ValidateTimeDimension(query, view, errors);
        ValidateFilters(query, view, errors);
        ValidateOrder(query, errors);
        ValidateLimit(query, errors);

        return new ValidationResult(errors);
    }

    private static void ValidateMeasures(SemanticQuery query, SemanticView view, List<ValidationError> errors)
    {
        foreach (var name in Duplicates(query.Measures))
        {
            errors.Add(new ValidationError(name, $"Die Kennzahl \"{name}\" ist mehrfach ausgewählt."));
        }

        foreach (var name in query.Measures.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (view.FindMeasure(name) is not null)
            {
                continue;
            }

            errors.Add(view.FindDimension(name) is not null
                ? new ValidationError(name, $"\"{name}\" ist ein Merkmal, keine Kennzahl.")
                : new ValidationError(name, UnknownMember(name, view, "Kennzahl")));
        }
    }

    private static void ValidateDimensions(SemanticQuery query, SemanticView view, List<ValidationError> errors)
    {
        foreach (var name in Duplicates(query.Dimensions))
        {
            errors.Add(new ValidationError(name, $"Das Merkmal \"{name}\" ist mehrfach ausgewählt."));
        }

        foreach (var name in query.Dimensions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (view.FindDimension(name) is not null)
            {
                continue;
            }

            errors.Add(view.FindMeasure(name) is not null
                ? new ValidationError(name, $"\"{name}\" ist eine Kennzahl, kein Merkmal.")
                : new ValidationError(name, UnknownMember(name, view, "Merkmal")));
        }
    }

    private static void ValidateTimeDimension(SemanticQuery query, SemanticView view, List<ValidationError> errors)
    {
        var time = query.TimeDimension;
        if (time is null)
        {
            return;
        }

        var dimension = view.FindDimension(time.Dimension);
        if (dimension is null)
        {
            errors.Add(new ValidationError(time.Dimension, UnknownMember(time.Dimension, view, "Zeitmerkmal")));
        }
        else if (dimension.Type != SemanticType.Time)
        {
            errors.Add(new ValidationError(
                time.Dimension,
                $"\"{dimension.Title}\" ist kein Zeitmerkmal und kann keine Zeitachse bilden."));
        }

        if (query.Dimensions.Contains(time.Dimension, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new ValidationError(
                time.Dimension,
                $"\"{time.Dimension}\" ist gleichzeitig als Merkmal und als Zeitachse ausgewählt."));
        }

        var range = time.DateRange;
        if (range is { Relative: null, From: not null, To: not null } && range.From > range.To)
        {
            errors.Add(new ValidationError(
                nameof(time.DateRange),
                "Das Startdatum des Zeitraums liegt nach dem Enddatum."));
        }

        if (range is { Relative: null, From: null, To: null })
        {
            errors.Add(new ValidationError(
                nameof(time.DateRange),
                "Der Zeitraum ist unvollständig: weder ein rollierender Zeitraum noch Start- und Enddatum sind gesetzt."));
        }
    }

    private static void ValidateFilters(SemanticQuery query, SemanticView view, List<ValidationError> errors)
    {
        foreach (var filter in query.Filters)
        {
            // Filtering on a measure is legitimate — Cube turns it into a
            // HAVING clause — so both kinds are looked up here.
            var member = view.FindMember(filter.Member);
            if (member is null)
            {
                errors.Add(new ValidationError(filter.Member, UnknownMember(filter.Member, view, "Filterfeld")));
                continue;
            }

            if (QueryFilter.IsUnary(filter.Operator))
            {
                continue;
            }

            if (filter.Values.Count == 0)
            {
                errors.Add(new ValidationError(
                    filter.Member,
                    $"Der Filter auf \"{member.Title}\" hat keinen Wert."));
                continue;
            }

            ValidateFilterOperator(filter, member, errors);
            ValidateFilterValues(filter, member, errors);
        }
    }

    private static void ValidateFilterOperator(QueryFilter filter, SemanticMember member, List<ValidationError> errors)
    {
        var isTextOperator = filter.Operator
            is FilterOperator.Contains or FilterOperator.NotContains or FilterOperator.StartsWith;

        if (isTextOperator && member.Type != SemanticType.String)
        {
            errors.Add(new ValidationError(
                filter.Member,
                $"Der Textfilter \"{filter.Operator}\" ist auf \"{member.Title}\" nicht anwendbar."));
        }

        var isComparison = filter.Operator
            is FilterOperator.GreaterThan or FilterOperator.GreaterThanOrEqual
            or FilterOperator.LessThan or FilterOperator.LessThanOrEqual;

        if (isComparison && member.Type is not (SemanticType.Number or SemanticType.Time))
        {
            errors.Add(new ValidationError(
                filter.Member,
                $"Der Vergleich \"{filter.Operator}\" ist auf \"{member.Title}\" nicht anwendbar."));
        }
    }

    private static void ValidateFilterValues(QueryFilter filter, SemanticMember member, List<ValidationError> errors)
    {
        if (member.Type != SemanticType.Number)
        {
            return;
        }

        foreach (var value in filter.Values)
        {
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                errors.Add(new ValidationError(
                    filter.Member,
                    $"\"{value}\" ist kein gültiger Zahlenwert für \"{member.Title}\"."));
            }
        }
    }

    private static void ValidateOrder(SemanticQuery query, List<ValidationError> errors)
    {
        var selected = query.SelectedMembers.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var order in query.Order)
        {
            if (!selected.Contains(order.Member))
            {
                errors.Add(new ValidationError(
                    order.Member,
                    $"Nach \"{order.Member}\" kann nicht sortiert werden, weil das Feld nicht Teil der Abfrage ist."));
            }
        }
    }

    private static void ValidateLimit(SemanticQuery query, List<ValidationError> errors)
    {
        if (query.Limit is < 1 or > QueryLimits.MaxRows)
        {
            errors.Add(new ValidationError(
                nameof(query.Limit),
                $"Die Zeilenbegrenzung muss zwischen 1 und {QueryLimits.MaxRows} liegen."));
        }
    }

    private static IEnumerable<string> Duplicates(IReadOnlyList<string> names) =>
        names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
             .Where(g => g.Count() > 1)
             .Select(g => g.Key);

    private static string UnknownMember(string name, SemanticView view, string kind)
    {
        var suggestion = MemberSuggestion.Closest(name, view);

        return suggestion is null
            ? $"\"{name}\" ist im Datenbereich \"{view.Title}\" kein bekanntes {kind}."
            : $"\"{name}\" ist im Datenbereich \"{view.Title}\" kein bekanntes {kind}. Meinten Sie \"{suggestion}\"?";
    }
}

public sealed record ValidationError(string? Member, string Message);

public sealed record ValidationResult(IReadOnlyList<ValidationError> Errors)
{
    public static ValidationResult Success { get; } = new([]);

    public bool IsValid => Errors.Count == 0;

    public string Summary => string.Join(" ", Errors.Select(e => e.Message));
}
