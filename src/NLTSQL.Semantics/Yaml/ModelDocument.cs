namespace NLTSQL.Semantics.Yaml;

/// <summary>
/// The on-disk shape of a semantic model file.
/// </summary>
/// <remarks>
/// <para>
/// Every property is nullable, and that is the whole point of this layer existing separately from
/// <see cref="Model.SemanticModel"/>. An overrides file has to be able to say "change only the
/// label" without restating the rest of the element, which requires telling "absent" apart from
/// "set to empty". The domain model, by contrast, is complete and immutable — merging there would
/// mean inventing a null-ish state it should never have.
/// </para>
/// <para>These types are mutable because YamlDotNet deserialises into properties.</para>
/// </remarks>
public sealed class ModelDocument
{
    /// <summary>Machine name of the model.</summary>
    public string? Model { get; set; }

    /// <summary>Model version.</summary>
    public int? Version { get; set; }

    /// <summary>Key of the configured data source.</summary>
    public string? DataSource { get; set; }

    /// <summary>Human-facing model name.</summary>
    public string? Label { get; set; }

    /// <summary>What business area the model covers.</summary>
    public string? Description { get; set; }

    /// <summary>The entities.</summary>
    public List<EntityDocument>? Entities { get; set; }

    /// <summary>The join paths.</summary>
    public List<RelationshipDocument>? Relationships { get; set; }

    /// <summary>Domain vocabulary.</summary>
    public List<GlossaryDocument>? Glossary { get; set; }

    /// <summary>Filters enforced on every query.</summary>
    public List<RowPolicyDocument>? RowPolicies { get; set; }
}

/// <summary>Fields shared by every named model element on disk.</summary>
public abstract class ElementDocument
{
    /// <summary>Machine name. The merge key; required in generated files.</summary>
    public string? Name { get; set; }

    /// <summary>Human-facing name.</summary>
    public string? Label { get; set; }

    /// <summary>Business meaning.</summary>
    public string? Description { get; set; }

    /// <summary>Alternative wordings.</summary>
    public List<string>? Synonyms { get; set; }

    /// <summary>Whether to withhold this element from users and from the LLM.</summary>
    public bool? Hidden { get; set; }

    /// <summary>Review state: <c>draft</c> or <c>reviewed</c>.</summary>
    public string? Status { get; set; }
}

/// <summary>The on-disk shape of an entity.</summary>
public sealed class EntityDocument : ElementDocument
{
    /// <summary>The physical table or view.</summary>
    public TableDocument? Table { get; set; }

    /// <summary>Primary key columns.</summary>
    public List<string>? PrimaryKey { get; set; }

    /// <summary>Groupable attributes.</summary>
    public List<DimensionDocument>? Dimensions { get; set; }

    /// <summary>Date and timestamp attributes.</summary>
    public List<TimeDimensionDocument>? TimeDimensions { get; set; }

    /// <summary>Aggregatable quantities.</summary>
    public List<MeasureDocument>? Measures { get; set; }

    /// <summary>Quantities derived from this entity's measures.</summary>
    public List<MetricDocument>? Metrics { get; set; }
}

/// <summary>The on-disk shape of a table reference.</summary>
public sealed class TableDocument
{
    /// <summary>Owning schema.</summary>
    public string? Schema { get; set; }

    /// <summary>Table or view name.</summary>
    public string? Name { get; set; }
}

/// <summary>The on-disk shape of a dimension.</summary>
public sealed class DimensionDocument : ElementDocument
{
    /// <summary>Physical column.</summary>
    public string? Column { get; set; }

    /// <summary>Logical type: <c>string</c>, <c>integer</c>, <c>decimal</c>, <c>boolean</c>, <c>date</c>, <c>timestamp</c>.</summary>
    public string? Type { get; set; }

    /// <summary>Known distinct values, for low-cardinality columns.</summary>
    public List<string>? Values { get; set; }
}

/// <summary>The on-disk shape of a time dimension.</summary>
public sealed class TimeDimensionDocument : ElementDocument
{
    /// <summary>Physical column.</summary>
    public string? Column { get; set; }

    /// <summary>Logical type: <c>date</c> or <c>timestamp</c>.</summary>
    public string? Type { get; set; }

    /// <summary>Permitted buckets: <c>day</c>, <c>week</c>, <c>month</c>, <c>quarter</c>, <c>year</c>.</summary>
    public List<string>? Granularities { get; set; }
}

/// <summary>The on-disk shape of a measure.</summary>
public sealed class MeasureDocument : ElementDocument
{
    /// <summary>Aggregate: <c>sum</c>, <c>avg</c>, <c>min</c>, <c>max</c>, <c>count</c>, <c>count_distinct</c>.</summary>
    public string? Agg { get; set; }

    /// <summary>Shorthand for an expression that is a single column.</summary>
    public string? Column { get; set; }

    /// <summary>Arithmetic over columns of the owning entity, using <c>{{column}}</c> references.</summary>
    public string? Expression { get; set; }

    /// <summary>How to render the aggregated value.</summary>
    public FormatDocument? Format { get; set; }
}

/// <summary>The on-disk shape of a derived metric.</summary>
public sealed class MetricDocument : ElementDocument
{
    /// <summary>Arithmetic over measure names of the owning entity, using <c>{{measure}}</c> references.</summary>
    public string? Expression { get; set; }

    /// <summary>How to render the computed value.</summary>
    public FormatDocument? Format { get; set; }
}

/// <summary>The on-disk shape of a value format.</summary>
public sealed class FormatDocument
{
    /// <summary>Style: <c>number</c>, <c>integer</c>, <c>currency</c>, <c>percent</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>Decimal places.</summary>
    public int? Decimals { get; set; }

    /// <summary>ISO 4217 code, required for <c>currency</c>.</summary>
    public string? Currency { get; set; }
}

/// <summary>The on-disk shape of a relationship.</summary>
public sealed class RelationshipDocument
{
    /// <summary>Machine name. Generated from the foreign key when scaffolded.</summary>
    public string? Name { get; set; }

    /// <summary>Referencing side, written as <c>entity.column</c>.</summary>
    public string? From { get; set; }

    /// <summary>Referenced side, written as <c>entity.column</c>.</summary>
    public string? To { get; set; }

    /// <summary>Direction: <c>many_to_one</c>, <c>one_to_many</c>, <c>one_to_one</c>.</summary>
    public string? Type { get; set; }
}

/// <summary>The on-disk shape of a glossary entry.</summary>
public sealed class GlossaryDocument
{
    /// <summary>The term as a user would say it.</summary>
    public string? Term { get; set; }

    /// <summary>What it means in this business.</summary>
    public string? Definition { get; set; }
}

/// <summary>The on-disk shape of a row policy.</summary>
public sealed class RowPolicyDocument
{
    /// <summary>The guarded entity.</summary>
    public string? Entity { get; set; }

    /// <summary>The discriminator column.</summary>
    public string? Column { get; set; }

    /// <summary>Name of the runtime-supplied value bound to the filter.</summary>
    public string? Parameter { get; set; }

    /// <summary>Whether the parameter carries a set of permitted values.</summary>
    public bool? MultiValued { get; set; }
}
