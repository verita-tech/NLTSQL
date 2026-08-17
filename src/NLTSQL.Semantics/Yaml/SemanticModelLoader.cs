using System.Globalization;
using NLTSQL.Core.Expressions;
using NLTSQL.Semantics.Model;
using NLTSQL.Semantics.Validation;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NLTSQL.Semantics.Yaml;

/// <summary>The outcome of loading a semantic model.</summary>
/// <param name="Model">The model, or <see langword="null"/> when it could not be built at all.</param>
/// <param name="Issues">Everything found while reading and mapping the files.</param>
public sealed record SemanticModelLoadResult(SemanticModel? Model, IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>Whether a usable model came out.</summary>
    public bool Success => this.Model is not null && !this.Issues.HasErrors();
}

/// <summary>
/// Reads <c>*.generated.yaml</c> plus <c>*.overrides.yaml</c> and maps them onto the domain model.
/// </summary>
/// <remarks>
/// This stage covers syntax and shape: is the YAML well-formed, are enum spellings recognised, do
/// expressions parse. Whether names actually resolve against each other is
/// <see cref="SemanticModelValidator"/>'s job, which needs a mapped model to work on.
/// </remarks>
public static class SemanticModelLoader
{
    /// <summary>Suffix of the machine-generated file, which scaffolding rewrites wholesale.</summary>
    public const string GeneratedSuffix = ".generated.yaml";

    /// <summary>Suffix of the hand-maintained file, which scaffolding never touches.</summary>
    public const string OverridesSuffix = ".overrides.yaml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        // YamlDotNet rejects unmatched properties by default, and that default is load-bearing
        // here: a model that says "synonym:" instead of "synonyms:" would otherwise parse cleanly
        // and quietly answer worse. Do not add IgnoreUnmatchedProperties to silence a typo.
        .Build();

    /// <summary>Names of every model that has a generated file in <paramref name="directory"/>.</summary>
    public static IReadOnlyList<string> DiscoverModelNames(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return [.. Directory
            .EnumerateFiles(directory, $"*{GeneratedSuffix}")
            .Select(path => Path.GetFileName(path)[..^GeneratedSuffix.Length])
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Loads one model from <paramref name="directory"/> by name.</summary>
    public static SemanticModelLoadResult LoadFromDirectory(string directory, string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        var generatedPath = Path.Combine(directory, modelName + GeneratedSuffix);
        var overridesPath = Path.Combine(directory, modelName + OverridesSuffix);

        if (!File.Exists(generatedPath))
        {
            return new SemanticModelLoadResult(
                null,
                [ValidationIssue.Error("model.file.missing", modelName, $"No generated model at '{generatedPath}'. Run 'nltsql scaffold' first.")]);
        }

        return Load(
            File.ReadAllText(generatedPath),
            File.Exists(overridesPath) ? File.ReadAllText(overridesPath) : null);
    }

    /// <summary>Loads a model from YAML text.</summary>
    /// <param name="generatedYaml">Contents of the generated file.</param>
    /// <param name="overridesYaml">Contents of the overrides file, if any.</param>
    public static SemanticModelLoadResult Load(string generatedYaml, string? overridesYaml)
    {
        ArgumentNullException.ThrowIfNull(generatedYaml);

        var issues = new List<ValidationIssue>();

        var generated = Parse(generatedYaml, GeneratedSuffix, issues);
        var overrides = overridesYaml is null ? null : Parse(overridesYaml, OverridesSuffix, issues);

        if (generated is null)
        {
            return new SemanticModelLoadResult(null, issues);
        }

        var document = ModelDocumentMerger.Merge(generated, overrides);
        var model = Map(document, issues);
        return new SemanticModelLoadResult(model, issues);
    }

    private static ModelDocument? Parse(string yaml, string origin, List<ValidationIssue> issues)
    {
        try
        {
            return Deserializer.Deserialize<ModelDocument>(yaml) ?? new ModelDocument();
        }
        catch (YamlException exception)
        {
            issues.Add(ValidationIssue.Error(
                "model.yaml.invalid",
                origin,
                $"Line {exception.Start.Line}, column {exception.Start.Column}: {exception.Message}"));
            return null;
        }
    }

    private static SemanticModel? Map(ModelDocument document, List<ValidationIssue> issues)
    {
        var name = Required(document.Model, "model", "model.name.missing", "A model must declare 'model'.", issues);
        var dataSource = Required(document.DataSource, "data_source", "model.datasource.missing", "A model must declare 'data_source'.", issues);

        if (document.Version is null)
        {
            issues.Add(ValidationIssue.Error("model.version.missing", "version", "A model must declare an integer 'version'."));
        }

        if (name is null || dataSource is null || document.Version is null)
        {
            return null;
        }

        return new SemanticModel
        {
            Name = name,
            Version = document.Version.Value,
            DataSource = dataSource,
            Label = document.Label,
            Description = document.Description,
            Entities = [.. (document.Entities ?? []).Select(entity => MapEntity(entity, issues)).OfType<Entity>()],
            Relationships = [.. (document.Relationships ?? []).Select(r => MapRelationship(r, issues)).OfType<Relationship>()],
            Glossary = [.. (document.Glossary ?? []).Select(g => MapGlossary(g, issues)).OfType<GlossaryTerm>()],
            RowPolicies = [.. (document.RowPolicies ?? []).Select(p => MapRowPolicy(p, issues)).OfType<RowPolicy>()],
        };
    }

    private static Entity? MapEntity(EntityDocument document, List<ValidationIssue> issues)
    {
        var name = Required(document.Name, "entities[]", "entity.name.missing", "Every entity needs a 'name'.", issues);
        if (name is null)
        {
            return null;
        }

        if (document.Table?.Schema is not { Length: > 0 } schema || document.Table?.Name is not { Length: > 0 } table)
        {
            issues.Add(ValidationIssue.Error(
                "entity.table.missing",
                name,
                "An entity must map to a table with both 'schema' and 'name'. A schema-less reference " +
                "would resolve differently depending on the connection's search path."));
            return null;
        }

        return new Entity
        {
            Name = name,
            Label = document.Label ?? name,
            Description = document.Description,
            Synonyms = Strings(document.Synonyms),
            Hidden = document.Hidden ?? false,
            Status = ParseStatus(document.Status, name, issues),
            Table = new TableReference(schema, table),
            PrimaryKey = Strings(document.PrimaryKey),
            Dimensions = [.. (document.Dimensions ?? []).Select(d => MapDimension(d, name, issues)).OfType<Dimension>()],
            TimeDimensions = [.. (document.TimeDimensions ?? []).Select(d => MapTimeDimension(d, name, issues)).OfType<TimeDimension>()],
            Measures = [.. (document.Measures ?? []).Select(m => MapMeasure(m, name, issues)).OfType<Measure>()],
            Metrics = [.. (document.Metrics ?? []).Select(m => MapMetric(m, name, issues)).OfType<Metric>()],
        };
    }

    private static Dimension? MapDimension(DimensionDocument document, string entity, List<ValidationIssue> issues)
    {
        var path = $"{entity}.dimensions[]";
        var name = Required(document.Name, path, "dimension.name.missing", "Every dimension needs a 'name'.", issues);
        if (name is null)
        {
            return null;
        }

        path = $"{entity}.dimensions.{name}";
        var column = Required(document.Column, path, "dimension.column.missing", "Every dimension needs a 'column'.", issues);
        if (column is null)
        {
            return null;
        }

        return new Dimension
        {
            Name = name,
            Label = document.Label ?? name,
            Description = document.Description,
            Synonyms = Strings(document.Synonyms),
            Hidden = document.Hidden ?? false,
            Status = ParseStatus(document.Status, path, issues),
            Column = column,
            DataType = ParseEnum(document.Type, DataType.String, path, "dimension.type.invalid", issues),
            Values = Strings(document.Values),
        };
    }

    private static TimeDimension? MapTimeDimension(TimeDimensionDocument document, string entity, List<ValidationIssue> issues)
    {
        var path = $"{entity}.time_dimensions[]";
        var name = Required(document.Name, path, "time_dimension.name.missing", "Every time dimension needs a 'name'.", issues);
        if (name is null)
        {
            return null;
        }

        path = $"{entity}.time_dimensions.{name}";
        var column = Required(document.Column, path, "time_dimension.column.missing", "Every time dimension needs a 'column'.", issues);
        if (column is null)
        {
            return null;
        }

        var dataType = ParseEnum(document.Type, DataType.Date, path, "time_dimension.type.invalid", issues);
        if (dataType is not (DataType.Date or DataType.Timestamp))
        {
            issues.Add(ValidationIssue.Error(
                "time_dimension.type.not_temporal",
                path,
                $"A time dimension must be 'date' or 'timestamp', not '{dataType.ToString().ToLowerInvariant()}'."));
            return null;
        }

        var granularities = ParseGranularities(document.Granularities, path, issues);

        return new TimeDimension
        {
            Name = name,
            Label = document.Label ?? name,
            Description = document.Description,
            Synonyms = Strings(document.Synonyms),
            Hidden = document.Hidden ?? false,
            Status = ParseStatus(document.Status, path, issues),
            Column = column,
            DataType = dataType,
            Granularities = granularities,
        };
    }

    private static Measure? MapMeasure(MeasureDocument document, string entity, List<ValidationIssue> issues)
    {
        var path = $"{entity}.measures[]";
        var name = Required(document.Name, path, "measure.name.missing", "Every measure needs a 'name'.", issues);
        if (name is null)
        {
            return null;
        }

        path = $"{entity}.measures.{name}";

        if (document.Column is not null && document.Expression is not null)
        {
            issues.Add(ValidationIssue.Error(
                "measure.source.ambiguous",
                path,
                "A measure declares either 'column' or 'expression', not both."));
            return null;
        }

        var aggregation = ParseAggregation(document.Agg, path, issues);

        SqlExpression? expression = null;
        if (document.Column is { Length: > 0 } column)
        {
            expression = new ReferenceExpression(column);
        }
        else if (document.Expression is { Length: > 0 } text)
        {
            var parsed = ExpressionParser.Parse(text);
            if (!parsed.Success)
            {
                issues.Add(ValidationIssue.Error(
                    "measure.expression.invalid",
                    path,
                    $"Offset {parsed.Position}: {parsed.Error}"));
                return null;
            }

            expression = parsed.Expression;
        }
        else if (aggregation is not Aggregation.Count)
        {
            issues.Add(ValidationIssue.Error(
                "measure.source.missing",
                path,
                $"A '{aggregation.ToString().ToLowerInvariant()}' measure needs a 'column' or an 'expression'. " +
                "Only 'count' may omit both, in which case it counts rows."));
            return null;
        }

        return new Measure
        {
            Name = name,
            Label = document.Label ?? name,
            Description = document.Description,
            Synonyms = Strings(document.Synonyms),
            Hidden = document.Hidden ?? false,
            Status = ParseStatus(document.Status, path, issues),
            Aggregation = aggregation,
            Expression = expression,
            Format = MapFormat(document.Format, path, issues),
        };
    }

    private static Metric? MapMetric(MetricDocument document, string entity, List<ValidationIssue> issues)
    {
        var path = $"{entity}.metrics[]";
        var name = Required(document.Name, path, "metric.name.missing", "Every metric needs a 'name'.", issues);
        if (name is null)
        {
            return null;
        }

        path = $"{entity}.metrics.{name}";
        var text = Required(document.Expression, path, "metric.expression.missing", "Every metric needs an 'expression'.", issues);
        if (text is null)
        {
            return null;
        }

        var parsed = ExpressionParser.Parse(text);
        if (!parsed.Success)
        {
            issues.Add(ValidationIssue.Error("metric.expression.invalid", path, $"Offset {parsed.Position}: {parsed.Error}"));
            return null;
        }

        return new Metric
        {
            Name = name,
            Label = document.Label ?? name,
            Description = document.Description,
            Synonyms = Strings(document.Synonyms),
            Hidden = document.Hidden ?? false,
            Status = ParseStatus(document.Status, path, issues),
            Expression = parsed.Expression!,
            Format = MapFormat(document.Format, path, issues),
        };
    }

    private static Relationship? MapRelationship(RelationshipDocument document, List<ValidationIssue> issues)
    {
        var from = ParseEntityColumn(document.From, "relationships[].from", issues);
        var to = ParseEntityColumn(document.To, "relationships[].to", issues);
        if (from is null || to is null)
        {
            return null;
        }

        var name = document.Name is { Length: > 0 } declared
            ? declared
            : $"{from.Entity}_{from.Column}_to_{to.Entity}";

        return new Relationship
        {
            Name = name,
            From = from,
            To = to,
            Cardinality = ParseEnum(
                document.Type?.Replace("_", string.Empty, StringComparison.Ordinal),
                Cardinality.ManyToOne,
                $"relationships.{name}",
                "relationship.type.invalid",
                issues),
        };
    }

    private static GlossaryTerm? MapGlossary(GlossaryDocument document, List<ValidationIssue> issues)
    {
        var term = Required(document.Term, "glossary[]", "glossary.term.missing", "Every glossary entry needs a 'term'.", issues);
        var definition = Required(document.Definition, $"glossary.{document.Term}", "glossary.definition.missing", "Every glossary entry needs a 'definition'.", issues);
        return term is null || definition is null ? null : new GlossaryTerm(term, definition);
    }

    private static RowPolicy? MapRowPolicy(RowPolicyDocument document, List<ValidationIssue> issues)
    {
        const string path = "row_policies[]";
        var entity = Required(document.Entity, path, "row_policy.entity.missing", "Every row policy needs an 'entity'.", issues);
        var column = Required(document.Column, path, "row_policy.column.missing", "Every row policy needs a 'column'.", issues);
        var parameter = Required(document.Parameter, path, "row_policy.parameter.missing", "Every row policy needs a 'parameter'.", issues);

        if (entity is null || column is null || parameter is null)
        {
            return null;
        }

        return new RowPolicy
        {
            Entity = entity,
            Column = column,
            Parameter = parameter,
            MultiValued = document.MultiValued ?? false,
        };
    }

    private static ValueFormat MapFormat(FormatDocument? document, string path, List<ValidationIssue> issues)
    {
        if (document is null)
        {
            return ValueFormat.Default;
        }

        var kind = ParseEnum(document.Kind, ValueFormatKind.Number, path, "format.kind.invalid", issues);
        var decimals = document.Decimals ?? (kind is ValueFormatKind.Integer ? 0 : 2);

        if (kind is ValueFormatKind.Currency && document.Currency is not { Length: > 0 })
        {
            issues.Add(ValidationIssue.Error("format.currency.missing", path, "A 'currency' format needs a 'currency' code."));
        }

        return new ValueFormat(kind, decimals, document.Currency);
    }

    private static EntityColumn? ParseEntityColumn(string? text, string path, List<ValidationIssue> issues)
    {
        if (text is not { Length: > 0 })
        {
            issues.Add(ValidationIssue.Error("relationship.side.missing", path, "Expected 'entity.column'."));
            return null;
        }

        var separator = text.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == text.Length - 1)
        {
            issues.Add(ValidationIssue.Error("relationship.side.malformed", path, $"'{text}' is not of the form 'entity.column'."));
            return null;
        }

        return new EntityColumn(text[..separator], text[(separator + 1)..]);
    }

    private static List<Granularity> ParseGranularities(List<string>? values, string path, List<ValidationIssue> issues)
    {
        if (values is null or { Count: 0 })
        {
            return [Granularity.Day, Granularity.Week, Granularity.Month, Granularity.Quarter, Granularity.Year];
        }

        var parsed = new List<Granularity>(values.Count);
        foreach (var value in values)
        {
            if (Enum.TryParse<Granularity>(value, ignoreCase: true, out var granularity))
            {
                parsed.Add(granularity);
            }
            else
            {
                issues.Add(ValidationIssue.Error(
                    "time_dimension.granularity.invalid",
                    path,
                    $"'{value}' is not a granularity. Expected one of: {string.Join(", ", Enum.GetNames<Granularity>().Select(n => n.ToLowerInvariant()))}."));
            }
        }

        return parsed;
    }

    private static Aggregation ParseAggregation(string? value, string path, List<ValidationIssue> issues)
    {
        // The YAML spellings are the ones an analyst would write, not the CLR member names.
        var normalised = value?.Trim().ToLowerInvariant() switch
        {
            null or "" => nameof(Aggregation.Sum),
            "sum" => nameof(Aggregation.Sum),
            "avg" or "average" or "mean" => nameof(Aggregation.Average),
            "min" or "minimum" => nameof(Aggregation.Minimum),
            "max" or "maximum" => nameof(Aggregation.Maximum),
            "count" => nameof(Aggregation.Count),
            "count_distinct" or "countdistinct" or "distinct_count" => nameof(Aggregation.CountDistinct),
            var other => other,
        };

        if (Enum.TryParse<Aggregation>(normalised, ignoreCase: true, out var aggregation))
        {
            return aggregation;
        }

        issues.Add(ValidationIssue.Error(
            "measure.agg.invalid",
            path,
            $"'{value}' is not an aggregate. Expected: sum, avg, min, max, count, count_distinct."));
        return Aggregation.Sum;
    }

    private static ReviewStatus ParseStatus(string? value, string path, List<ValidationIssue> issues) =>
        ParseEnum(value, ReviewStatus.Draft, path, "status.invalid", issues);

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback, string path, string code, List<ValidationIssue> issues)
        where TEnum : struct, Enum
    {
        if (value is not { Length: > 0 })
        {
            return fallback;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        issues.Add(ValidationIssue.Error(
            code,
            path,
            $"'{value}' is not valid here. Expected one of: {string.Join(", ", Enum.GetNames<TEnum>().Select(n => n.ToLowerInvariant()))}."));
        return fallback;
    }

    private static string? Required(string? value, string path, string code, string message, List<ValidationIssue> issues)
    {
        if (value is { Length: > 0 })
        {
            return value.Trim();
        }

        issues.Add(ValidationIssue.Error(code, path, message));
        return null;
    }

    private static IReadOnlyList<string> Strings(List<string>? values) =>
        values is null ? [] : [.. values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())];
}
