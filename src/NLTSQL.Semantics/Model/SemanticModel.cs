using System.Collections.Frozen;

namespace NLTSQL.Semantics.Model;

/// <summary>One side of a relationship.</summary>
/// <param name="Entity">Entity name.</param>
/// <param name="Column">Physical column on that entity's table.</param>
public sealed record EntityColumn(string Entity, string Column)
{
    /// <inheritdoc/>
    public override string ToString() => $"{this.Entity}.{this.Column}";
}

/// <summary>A join path between two entities.</summary>
/// <remarks>
/// Relationships are the part of a schema an LLM is worst at inferring and a database states most
/// precisely, so they are generated from foreign keys rather than described in prose. The query
/// compiler will only join along paths declared here.
/// </remarks>
public sealed record Relationship
{
    /// <summary>Stable machine name, unique within the model.</summary>
    public required string Name { get; init; }

    /// <summary>The referencing side.</summary>
    public required EntityColumn From { get; init; }

    /// <summary>The referenced side.</summary>
    public required EntityColumn To { get; init; }

    /// <summary>Direction, seen from <see cref="From"/>.</summary>
    public Cardinality Cardinality { get; init; } = Cardinality.ManyToOne;
}

/// <summary>A domain term and what it means, for grounding the model rather than for querying.</summary>
/// <param name="Term">The term as a user would say it.</param>
/// <param name="Definition">What it means in this business.</param>
public sealed record GlossaryTerm(string Term, string Definition);

/// <summary>
/// A filter the platform enforces on every query touching an entity.
/// </summary>
/// <remarks>
/// <para>
/// This is structured rather than a SQL fragment on purpose. A raw predicate in the model would be
/// the one string that reaches the database as text, and a row policy is precisely the place where
/// that must not be possible: it is the mechanism that keeps one customer's rows away from another.
/// </para>
/// <para>
/// The column is validated against the entity at load time and the value is always bound as a
/// parameter supplied by the server, never by the model or the language model.
/// </para>
/// </remarks>
public sealed record RowPolicy
{
    /// <summary>The entity this policy guards.</summary>
    public required string Entity { get; init; }

    /// <summary>The physical column carrying the tenant or scope discriminator.</summary>
    public required string Column { get; init; }

    /// <summary>
    /// Name of the runtime-supplied value bound to this filter, for example <c>tenant_id</c>.
    /// </summary>
    public required string Parameter { get; init; }

    /// <summary>
    /// Whether the parameter carries a set of permitted values rather than a single one.
    /// </summary>
    public bool MultiValued { get; init; }
}

/// <summary>A complete, validated business model over one data source.</summary>
public sealed class SemanticModel
{
    private FrozenDictionary<string, Entity>? entityIndex;

    /// <summary>Machine name of the model, matching its file name.</summary>
    public required string Name { get; init; }

    /// <summary>Monotonically increasing version, stamped onto every saved query.</summary>
    public required int Version { get; init; }

    /// <summary>Key of the configured data source this model queries.</summary>
    public required string DataSource { get; init; }

    /// <summary>Human-facing model name.</summary>
    public string? Label { get; init; }

    /// <summary>What business area this model covers.</summary>
    public string? Description { get; init; }

    /// <summary>The entities of the model.</summary>
    public IReadOnlyList<Entity> Entities { get; init; } = [];

    /// <summary>The permitted join paths.</summary>
    public IReadOnlyList<Relationship> Relationships { get; init; } = [];

    /// <summary>Domain vocabulary used to ground the language model.</summary>
    public IReadOnlyList<GlossaryTerm> Glossary { get; init; } = [];

    /// <summary>Filters enforced on every query.</summary>
    public IReadOnlyList<RowPolicy> RowPolicies { get; init; } = [];

    /// <summary>Finds an entity by name, case-insensitively.</summary>
    public Entity? FindEntity(string name)
    {
        this.entityIndex ??= this.Entities
            .GroupBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return this.entityIndex.GetValueOrDefault(name);
    }

    /// <summary>All row policies that apply to <paramref name="entityName"/>.</summary>
    public IEnumerable<RowPolicy> PoliciesFor(string entityName) =>
        this.RowPolicies.Where(policy => string.Equals(policy.Entity, entityName, StringComparison.OrdinalIgnoreCase));
}
