namespace NLTSQL.Semantics.Yaml;

/// <summary>
/// Layers a hand-maintained overrides document on top of a generated one.
/// </summary>
/// <remarks>
/// <para>
/// Scaffolding rewrites <c>*.generated.yaml</c> wholesale on every run. That is only tolerable if
/// human work lives somewhere the generator never touches, which is what <c>*.overrides.yaml</c>
/// is for. A generator that can destroy a domain expert's labels gets run once and then abandoned,
/// and the model stops tracking the database — so this separation is what keeps the whole
/// scaffolding idea usable past week one.
/// </para>
/// <para>The rules:</para>
/// <list type="bullet">
/// <item>Scalars: an override replaces the generated value when present; absent leaves it alone.</item>
/// <item>Scalar lists (synonyms, values, granularities, primary key): replaced wholesale when present.</item>
/// <item>Element lists: matched by <c>name</c> case-insensitively and merged recursively.</item>
/// <item>Elements only in the overrides file are appended, so experts can add what the generator cannot infer.</item>
/// <item>Nothing is ever deleted; use <c>hidden: true</c> to take an element out of circulation.</item>
/// </list>
/// </remarks>
public static class ModelDocumentMerger
{
    /// <summary>Produces a single document from a generated base and optional overrides.</summary>
    /// <param name="generated">The generated document. Never mutated.</param>
    /// <param name="overrides">The hand-maintained document, or <see langword="null"/>.</param>
    public static ModelDocument Merge(ModelDocument generated, ModelDocument? overrides)
    {
        ArgumentNullException.ThrowIfNull(generated);

        if (overrides is null)
        {
            return generated;
        }

        return new ModelDocument
        {
            Model = overrides.Model ?? generated.Model,
            Version = overrides.Version ?? generated.Version,
            DataSource = overrides.DataSource ?? generated.DataSource,
            Label = overrides.Label ?? generated.Label,
            Description = overrides.Description ?? generated.Description,
            Entities = MergeByName(generated.Entities, overrides.Entities, MergeEntity),
            Relationships = MergeByKey(
                generated.Relationships,
                overrides.Relationships,
                relationship => relationship.Name,
                MergeRelationship),
            Glossary = MergeByKey(
                generated.Glossary,
                overrides.Glossary,
                entry => entry.Term,
                static (_, over) => over),
            RowPolicies = MergeByKey(
                generated.RowPolicies,
                overrides.RowPolicies,
                policy => $"{policy.Entity}.{policy.Column}",
                MergeRowPolicy),
        };
    }

    private static EntityDocument MergeEntity(EntityDocument generated, EntityDocument overrides)
    {
        var merged = new EntityDocument
        {
            Table = MergeTable(generated.Table, overrides.Table),
            PrimaryKey = overrides.PrimaryKey ?? generated.PrimaryKey,
            Dimensions = MergeByName(generated.Dimensions, overrides.Dimensions, MergeDimension),
            TimeDimensions = MergeByName(generated.TimeDimensions, overrides.TimeDimensions, MergeTimeDimension),
            Measures = MergeByName(generated.Measures, overrides.Measures, MergeMeasure),
            Metrics = MergeByName(generated.Metrics, overrides.Metrics, MergeMetric),
        };

        CopyElementFields(merged, generated, overrides);
        return merged;
    }

    private static DimensionDocument MergeDimension(DimensionDocument generated, DimensionDocument overrides)
    {
        var merged = new DimensionDocument
        {
            Column = overrides.Column ?? generated.Column,
            Type = overrides.Type ?? generated.Type,
            Values = overrides.Values ?? generated.Values,
        };

        CopyElementFields(merged, generated, overrides);
        return merged;
    }

    private static TimeDimensionDocument MergeTimeDimension(TimeDimensionDocument generated, TimeDimensionDocument overrides)
    {
        var merged = new TimeDimensionDocument
        {
            Column = overrides.Column ?? generated.Column,
            Type = overrides.Type ?? generated.Type,
            Granularities = overrides.Granularities ?? generated.Granularities,
        };

        CopyElementFields(merged, generated, overrides);
        return merged;
    }

    private static MeasureDocument MergeMeasure(MeasureDocument generated, MeasureDocument overrides)
    {
        // Column and Expression are two spellings of the same thing, so an override that supplies
        // either one must clear the other. Merging them independently would leave a measure with
        // both set, which the loader rejects as ambiguous — a confusing failure for an override
        // that only meant to replace the definition.
        var redefinesSource = overrides.Column is not null || overrides.Expression is not null;

        var merged = new MeasureDocument
        {
            Agg = overrides.Agg ?? generated.Agg,
            Column = redefinesSource ? overrides.Column : generated.Column,
            Expression = redefinesSource ? overrides.Expression : generated.Expression,
            Format = MergeFormat(generated.Format, overrides.Format),
        };

        CopyElementFields(merged, generated, overrides);
        return merged;
    }

    private static MetricDocument MergeMetric(MetricDocument generated, MetricDocument overrides)
    {
        var merged = new MetricDocument
        {
            Expression = overrides.Expression ?? generated.Expression,
            Format = MergeFormat(generated.Format, overrides.Format),
        };

        CopyElementFields(merged, generated, overrides);
        return merged;
    }

    private static RelationshipDocument MergeRelationship(RelationshipDocument generated, RelationshipDocument overrides) =>
        new()
        {
            Name = overrides.Name ?? generated.Name,
            From = overrides.From ?? generated.From,
            To = overrides.To ?? generated.To,
            Type = overrides.Type ?? generated.Type,
        };

    private static RowPolicyDocument MergeRowPolicy(RowPolicyDocument generated, RowPolicyDocument overrides) =>
        new()
        {
            Entity = overrides.Entity ?? generated.Entity,
            Column = overrides.Column ?? generated.Column,
            Parameter = overrides.Parameter ?? generated.Parameter,
            MultiValued = overrides.MultiValued ?? generated.MultiValued,
        };

    private static TableDocument? MergeTable(TableDocument? generated, TableDocument? overrides)
    {
        if (overrides is null)
        {
            return generated;
        }

        return generated is null
            ? overrides
            : new TableDocument
            {
                Schema = overrides.Schema ?? generated.Schema,
                Name = overrides.Name ?? generated.Name,
            };
    }

    private static FormatDocument? MergeFormat(FormatDocument? generated, FormatDocument? overrides)
    {
        if (overrides is null)
        {
            return generated;
        }

        return generated is null
            ? overrides
            : new FormatDocument
            {
                Kind = overrides.Kind ?? generated.Kind,
                Decimals = overrides.Decimals ?? generated.Decimals,
                Currency = overrides.Currency ?? generated.Currency,
            };
    }

    private static void CopyElementFields(ElementDocument target, ElementDocument generated, ElementDocument overrides)
    {
        target.Name = generated.Name ?? overrides.Name;
        target.Label = overrides.Label ?? generated.Label;
        target.Description = overrides.Description ?? generated.Description;
        target.Synonyms = overrides.Synonyms ?? generated.Synonyms;
        target.Hidden = overrides.Hidden ?? generated.Hidden;
        target.Status = overrides.Status ?? generated.Status;
    }

    private static List<T>? MergeByName<T>(List<T>? generated, List<T>? overrides, Func<T, T, T> merge)
        where T : ElementDocument =>
        MergeByKey(generated, overrides, element => element.Name, merge);

    private static List<T>? MergeByKey<T>(
        List<T>? generated,
        List<T>? overrides,
        Func<T, string?> keySelector,
        Func<T, T, T> merge)
    {
        if (overrides is null)
        {
            return generated;
        }

        if (generated is null)
        {
            return overrides;
        }

        // Keyless entries and duplicates are not the merger's problem to report; it keeps the
        // first of each so the validator sees — and can complain about — the authored shape.
        var overridesByKey = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in overrides)
        {
            if (keySelector(element) is { Length: > 0 } key)
            {
                overridesByKey.TryAdd(key, element);
            }
        }

        var merged = new List<T>(generated.Count + overrides.Count);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in generated)
        {
            var key = keySelector(element);
            if (key is { Length: > 0 } && overridesByKey.TryGetValue(key, out var replacement))
            {
                merged.Add(merge(element, replacement));
                consumed.Add(key);
            }
            else
            {
                merged.Add(element);
            }
        }

        foreach (var element in overrides)
        {
            var key = keySelector(element);
            if (key is not { Length: > 0 } || !consumed.Contains(key))
            {
                merged.Add(element);
            }
        }

        return merged;
    }
}
