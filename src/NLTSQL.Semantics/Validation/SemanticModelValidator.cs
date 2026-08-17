using NLTSQL.Core.Expressions;
using NLTSQL.Semantics.Model;

namespace NLTSQL.Semantics.Validation;

/// <summary>
/// Checks that a mapped model is internally consistent.
/// </summary>
/// <remarks>
/// <para>
/// The loader can only see one element at a time; this stage sees the whole model and so is where
/// cross-references are resolved. Everything checked here would otherwise surface as a database
/// error in front of a customer, halfway through a question — which is the worst possible place to
/// discover that a measure points at a column somebody renamed.
/// </para>
/// <para>
/// Errors block use of the model. Warnings do not, but they all describe things that measurably
/// degrade answer quality: an entity with no measures cannot answer a quantitative question, and a
/// measure with no description or synonyms is one retrieval will struggle to find.
/// </para>
/// </remarks>
public static class SemanticModelValidator
{
    /// <summary>Validates <paramref name="model"/> and returns every finding.</summary>
    public static IReadOnlyList<ValidationIssue> Validate(SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var issues = new List<ValidationIssue>();

        ValidateUniqueNames(model.Entities.Select(e => e.Name), "entity.duplicate", "entities", issues);

        foreach (var entity in model.Entities)
        {
            ValidateEntity(entity, issues);
        }

        ValidateRelationships(model, issues);
        ValidateRowPolicies(model, issues);
        ValidateGlossary(model, issues);

        if (model.Entities.Count == 0)
        {
            issues.Add(ValidationIssue.Error("model.entities.empty", model.Name, "A model must declare at least one entity."));
        }

        return issues;
    }

    private static void ValidateEntity(Entity entity, List<ValidationIssue> issues)
    {
        // Dimensions, time dimensions, measures and metrics share one namespace: a QuerySpec names
        // a field without saying which kind it is, so a name that means two things is unresolvable.
        var allNames = entity.Dimensions.Select(d => d.Name)
            .Concat(entity.TimeDimensions.Select(d => d.Name))
            .Concat(entity.Measures.Select(m => m.Name))
            .Concat(entity.Metrics.Select(m => m.Name));

        ValidateUniqueNames(allNames, "entity.field.duplicate", entity.Name, issues);

        foreach (var measure in entity.Measures)
        {
            ValidateMeasure(entity, measure, issues);
        }

        foreach (var metric in entity.Metrics)
        {
            ValidateMetric(entity, metric, issues);
        }

        if (entity.PrimaryKey.Count == 0)
        {
            issues.Add(ValidationIssue.Warning(
                "entity.primary_key.missing",
                entity.Name,
                "No primary key. Count-distinct measures and fan-out-safe joins both need one."));
        }

        if (!entity.Hidden && entity.Measures.Count == 0 && entity.Metrics.Count == 0)
        {
            issues.Add(ValidationIssue.Warning(
                "entity.measures.empty",
                entity.Name,
                "No measures or metrics, so this entity cannot answer a quantitative question."));
        }

        if (!entity.Hidden && string.IsNullOrWhiteSpace(entity.Description))
        {
            issues.Add(ValidationIssue.Warning(
                "entity.description.missing",
                entity.Name,
                "No description. Retrieval matches questions against descriptions and synonyms."));
        }
    }

    private static void ValidateMeasure(Entity entity, Measure measure, List<ValidationIssue> issues)
    {
        var path = $"{entity.Name}.measures.{measure.Name}";

        if (measure.Expression is null)
        {
            if (measure.Aggregation is not Aggregation.Count)
            {
                issues.Add(ValidationIssue.Error(
                    "measure.expression.missing",
                    path,
                    $"A '{measure.Aggregation}' measure needs an expression."));
            }

            return;
        }

        foreach (var reference in measure.Expression.References().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Inside a measure a reference is a physical column, so it is checked against the
            // columns the model declares rather than against other model elements.
            if (!DeclaresColumn(entity, reference))
            {
                issues.Add(ValidationIssue.Error(
                    "measure.column.unknown",
                    path,
                    $"References column '{reference}', which no dimension or time dimension of '{entity.Name}' maps. " +
                    "Add it as a hidden dimension if it is only needed for arithmetic."));
            }
        }
    }

    private static void ValidateMetric(Entity entity, Metric metric, List<ValidationIssue> issues)
    {
        var path = $"{entity.Name}.metrics.{metric.Name}";
        var references = metric.Expression.References().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (references.Count == 0)
        {
            issues.Add(ValidationIssue.Warning(
                "metric.constant",
                path,
                "References no measure, so it evaluates to a constant."));
        }

        foreach (var reference in references)
        {
            // Inside a metric a reference is another measure: the arithmetic happens after
            // aggregation, which is exactly what distinguishes a metric from a measure.
            if (entity.FindMeasure(reference) is null)
            {
                var hint = entity.FindMetric(reference) is not null
                    ? " Metrics may not reference other metrics, only measures."
                    : string.Empty;

                issues.Add(ValidationIssue.Error(
                    "metric.measure.unknown",
                    path,
                    $"References '{reference}', which is not a measure of '{entity.Name}'.{hint}"));
            }
        }
    }

    private static void ValidateRelationships(SemanticModel model, List<ValidationIssue> issues)
    {
        ValidateUniqueNames(model.Relationships.Select(r => r.Name), "relationship.duplicate", "relationships", issues);

        foreach (var relationship in model.Relationships)
        {
            ValidateSide(model, relationship.From, $"relationships.{relationship.Name}.from", issues);
            ValidateSide(model, relationship.To, $"relationships.{relationship.Name}.to", issues);
        }
    }

    private static void ValidateSide(SemanticModel model, EntityColumn side, string path, List<ValidationIssue> issues)
    {
        var entity = model.FindEntity(side.Entity);
        if (entity is null)
        {
            issues.Add(ValidationIssue.Error("relationship.entity.unknown", path, $"Unknown entity '{side.Entity}'."));
            return;
        }

        // A join column need not be exposed as a dimension — surrogate keys usually should not be —
        // so the primary key counts as a declaration too.
        if (!DeclaresColumn(entity, side.Column) &&
            !entity.PrimaryKey.Contains(side.Column, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(ValidationIssue.Warning(
                "relationship.column.undeclared",
                path,
                $"Column '{side.Column}' is not declared on '{entity.Name}'. The join will still be " +
                "compiled, but nothing verifies the column exists until the query runs."));
        }
    }

    private static void ValidateRowPolicies(SemanticModel model, List<ValidationIssue> issues)
    {
        foreach (var policy in model.RowPolicies)
        {
            var path = $"row_policies.{policy.Entity}.{policy.Column}";
            var entity = model.FindEntity(policy.Entity);

            if (entity is null)
            {
                // Deliberately an error, not a warning. A policy that names a mistyped entity
                // silently protects nothing, and the failure mode is one tenant seeing another's
                // rows — so it has to stop the model from loading.
                issues.Add(ValidationIssue.Error(
                    "row_policy.entity.unknown",
                    path,
                    $"Unknown entity '{policy.Entity}'. A row policy that matches no entity enforces nothing."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(policy.Parameter))
            {
                issues.Add(ValidationIssue.Error("row_policy.parameter.empty", path, "The parameter name must not be empty."));
            }
        }

        foreach (var entity in model.Entities.Where(e => !e.Hidden))
        {
            if (!model.PoliciesFor(entity.Name).Any())
            {
                issues.Add(ValidationIssue.Warning(
                    "row_policy.entity.unguarded",
                    entity.Name,
                    "No row policy. Every row of this entity is visible to every user who can query the model."));
            }
        }
    }

    private static void ValidateGlossary(SemanticModel model, List<ValidationIssue> issues) =>
        ValidateUniqueNames(model.Glossary.Select(term => term.Term), "glossary.duplicate", "glossary", issues);

    private static bool DeclaresColumn(Entity entity, string column) =>
        entity.Dimensions.Any(d => string.Equals(d.Column, column, StringComparison.OrdinalIgnoreCase)) ||
        entity.TimeDimensions.Any(d => string.Equals(d.Column, column, StringComparison.OrdinalIgnoreCase));

    private static void ValidateUniqueNames(
        IEnumerable<string> names,
        string code,
        string path,
        List<ValidationIssue> issues)
    {
        var duplicates = names
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var duplicate in duplicates)
        {
            issues.Add(ValidationIssue.Error(
                code,
                path,
                $"'{duplicate}' is declared more than once. Names are matched case-insensitively."));
        }
    }
}
