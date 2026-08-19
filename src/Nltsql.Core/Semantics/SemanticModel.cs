namespace Nltsql.Core.Semantics;

/// <summary>
/// The application's view of the semantic layer, projected from Cube's
/// <c>/v1/meta</c> response.
/// </summary>
/// <remarks>
/// This is the allow-list for everything downstream. A member that is
/// not in here cannot be queried, cannot be filtered on, and cannot
/// reach the generated SQL — which is what makes it safe to let a
/// language model propose queries.
/// </remarks>
public sealed class SemanticModel(IReadOnlyList<SemanticView> views)
{
    public IReadOnlyList<SemanticView> Views { get; } = views;

    public static SemanticModel Empty { get; } = new([]);

    public SemanticView? FindView(string? name) =>
        name is null
            ? null
            : Views.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A queryable subject area. Maps to a Cube <c>view</c>, never to a raw
/// cube: views are the published contract, cubes are implementation.
/// </summary>
public sealed class SemanticView
{
    public required string Name { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<SemanticMeasure> Measures { get; init; } = [];

    public IReadOnlyList<SemanticDimension> Dimensions { get; init; } = [];

    /// <summary>Time-typed dimensions, the only ones usable as a time axis.</summary>
    public IEnumerable<SemanticDimension> TimeDimensions =>
        Dimensions.Where(d => d.Type == SemanticType.Time);

    public SemanticMeasure? FindMeasure(string name) =>
        Measures.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public SemanticDimension? FindDimension(string name) =>
        Dimensions.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    public SemanticMember? FindMember(string name) =>
        (SemanticMember?)FindMeasure(name) ?? FindDimension(name);
}

/// <summary>Data type of a semantic member, as reported by Cube.</summary>
public enum SemanticType
{
    String,
    Number,
    Time,
    Boolean,
}

/// <summary>Shared shape of measures and dimensions.</summary>
public abstract class SemanticMember
{
    /// <summary>Name relative to its view, e.g. <c>oee</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Business label shown in the UI, e.g. "OEE".</summary>
    public required string Title { get; init; }

    public string? Description { get; init; }

    public SemanticType Type { get; init; } = SemanticType.String;

    /// <summary>
    /// Alternative business wordings carried in the Cube model's
    /// <c>meta.synonyms</c>. These are what let the planner resolve
    /// "Anlageneffektivität" to <c>oee</c> without guessing.
    /// </summary>
    public IReadOnlyList<string> Synonyms { get; init; } = [];
}

public sealed class SemanticMeasure : SemanticMember
{
    /// <summary>Cube's display format, e.g. <c>percent</c> or <c>currency</c>.</summary>
    public string? Format { get; init; }
}

public sealed class SemanticDimension : SemanticMember;
