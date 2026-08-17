using System.Collections.Frozen;
using NLTSQL.Core.Expressions;

namespace NLTSQL.Semantics.Model;

/// <summary>Everything a model element shares: how it is named, described and reviewed.</summary>
public abstract record ModelElement
{
    /// <summary>Stable machine name, unique within its scope. Matched case-insensitively.</summary>
    public required string Name { get; init; }

    /// <summary>Human-facing name shown in the UI.</summary>
    public required string Label { get; init; }

    /// <summary>Business meaning, in the language of the domain rather than the schema.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Alternative wordings a user might use.
    /// </summary>
    /// <remarks>
    /// These carry a lot of weight: retrieval matches a question against them, so a missing
    /// synonym is the single most common reason a perfectly modelled measure is never found.
    /// </remarks>
    public IReadOnlyList<string> Synonyms { get; init; } = [];

    /// <summary>Whether this element is withheld from users and from the model excerpt sent to the LLM.</summary>
    public bool Hidden { get; init; }

    /// <summary>Whether a domain expert has confirmed this element.</summary>
    public ReviewStatus Status { get; init; } = ReviewStatus.Draft;
}

/// <summary>The physical table or view an entity maps to.</summary>
/// <param name="Schema">Owning schema. Required, so a model never depends on the session's search path.</param>
/// <param name="Name">Table or view name.</param>
public sealed record TableReference(string Schema, string Name)
{
    /// <inheritdoc/>
    public override string ToString() => $"{this.Schema}.{this.Name}";
}

/// <summary>How a numeric value is rendered.</summary>
/// <param name="Kind">The formatting style.</param>
/// <param name="Decimals">Number of decimal places.</param>
/// <param name="Currency">ISO 4217 code, required when <paramref name="Kind"/> is <see cref="ValueFormatKind.Currency"/>.</param>
public sealed record ValueFormat(ValueFormatKind Kind, int Decimals = 2, string? Currency = null)
{
    /// <summary>The default used when a measure declares no format.</summary>
    public static ValueFormat Default { get; } = new(ValueFormatKind.Number);
}

/// <summary>A groupable, filterable attribute of an entity.</summary>
public sealed record Dimension : ModelElement
{
    /// <summary>The physical column.</summary>
    public required string Column { get; init; }

    /// <summary>The logical type, which decides which filter operators apply.</summary>
    public required DataType DataType { get; init; }

    /// <summary>
    /// The distinct values, when the column is low-cardinality.
    /// </summary>
    /// <remarks>
    /// Populated by profiling during scaffolding. This is what lets a model turn "storniert"
    /// in a question into the right filter without the LLM having to guess the encoding.
    /// </remarks>
    public IReadOnlyList<string> Values { get; init; } = [];
}

/// <summary>A date or timestamp column that results can be bucketed by.</summary>
public sealed record TimeDimension : ModelElement
{
    /// <summary>The physical column.</summary>
    public required string Column { get; init; }

    /// <summary>Whether the column carries a time component.</summary>
    public required DataType DataType { get; init; }

    /// <summary>The buckets this column may be rolled up to.</summary>
    public IReadOnlyList<Granularity> Granularities { get; init; } =
        [Granularity.Day, Granularity.Week, Granularity.Month, Granularity.Quarter, Granularity.Year];
}

/// <summary>An aggregatable quantity.</summary>
public sealed record Measure : ModelElement
{
    /// <summary>The aggregate to apply.</summary>
    public required Aggregation Aggregation { get; init; }

    /// <summary>
    /// The expression to aggregate, over columns of the owning entity.
    /// </summary>
    /// <remarks>
    /// Null only for <see cref="Aggregation.Count"/>, which then counts rows rather than values.
    /// </remarks>
    public SqlExpression? Expression { get; init; }

    /// <summary>How the aggregated value is rendered.</summary>
    public ValueFormat Format { get; init; } = ValueFormat.Default;
}

/// <summary>A quantity derived from other measures of the same entity.</summary>
/// <remarks>
/// Metrics are entity-scoped because measure names are. A ratio such as average order value is
/// arithmetic over already-aggregated measures, which is why it cannot be expressed as a measure:
/// the division has to happen after aggregation, not before it.
/// </remarks>
public sealed record Metric : ModelElement
{
    /// <summary>Arithmetic over measure names of the owning entity.</summary>
    public required SqlExpression Expression { get; init; }

    /// <summary>How the computed value is rendered.</summary>
    public ValueFormat Format { get; init; } = ValueFormat.Default;
}

/// <summary>A business object, mapped to exactly one table or view.</summary>
public sealed record Entity : ModelElement
{
    /// <summary>The physical table or view.</summary>
    public required TableReference Table { get; init; }

    /// <summary>The primary key columns, used to make count-distinct and fan-out safe.</summary>
    public IReadOnlyList<string> PrimaryKey { get; init; } = [];

    /// <summary>Groupable attributes.</summary>
    public IReadOnlyList<Dimension> Dimensions { get; init; } = [];

    /// <summary>Date and timestamp attributes.</summary>
    public IReadOnlyList<TimeDimension> TimeDimensions { get; init; } = [];

    /// <summary>Aggregatable quantities.</summary>
    public IReadOnlyList<Measure> Measures { get; init; } = [];

    /// <summary>Quantities derived from this entity's measures.</summary>
    public IReadOnlyList<Metric> Metrics { get; init; } = [];

    private FrozenDictionary<string, Dimension>? dimensionIndex;
    private FrozenDictionary<string, TimeDimension>? timeDimensionIndex;
    private FrozenDictionary<string, Measure>? measureIndex;
    private FrozenDictionary<string, Metric>? metricIndex;

    /// <summary>Finds a dimension by name, case-insensitively.</summary>
    public Dimension? FindDimension(string name) =>
        Lookup(ref this.dimensionIndex, this.Dimensions, static d => d.Name, name);

    /// <summary>Finds a time dimension by name, case-insensitively.</summary>
    public TimeDimension? FindTimeDimension(string name) =>
        Lookup(ref this.timeDimensionIndex, this.TimeDimensions, static d => d.Name, name);

    /// <summary>Finds a measure by name, case-insensitively.</summary>
    public Measure? FindMeasure(string name) =>
        Lookup(ref this.measureIndex, this.Measures, static m => m.Name, name);

    /// <summary>Finds a metric by name, case-insensitively.</summary>
    public Metric? FindMetric(string name) =>
        Lookup(ref this.metricIndex, this.Metrics, static m => m.Name, name);

    private static TValue? Lookup<TValue>(
        ref FrozenDictionary<string, TValue>? index,
        IReadOnlyList<TValue> items,
        Func<TValue, string> keySelector,
        string name)
        where TValue : class
    {
        // Built on first use rather than in the constructor: validation rejects duplicate names,
        // but the loader has to be able to construct a not-yet-valid model in order to report on it.
        index ??= items
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return index.GetValueOrDefault(name);
    }
}
