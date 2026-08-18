using System.Collections.Frozen;
using Microsoft.Extensions.Options;
using NLTSQL.Semantics.Model;
using NLTSQL.Semantics.Validation;
using NLTSQL.Semantics.Yaml;

namespace NLTSQL.Semantics;

/// <summary>Where the semantic models live.</summary>
public sealed class SemanticModelOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Nltsql:Semantics";

    /// <summary>Directory holding the <c>*.generated.yaml</c> and <c>*.overrides.yaml</c> files.</summary>
    public string Directory { get; set; } = "semantics";
}

/// <summary>The loaded semantic models.</summary>
public interface ISemanticModelRegistry
{
    /// <summary>Names of every model that loaded.</summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>Everything found while loading, across all models.</summary>
    IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>Gets a model by name.</summary>
    /// <exception cref="InvalidOperationException">No such model loaded.</exception>
    SemanticModel GetModel(string name);
}

/// <summary>
/// Loads every model in the configured directory once, at startup.
/// </summary>
/// <remarks>
/// <para>
/// Loading eagerly rather than on first use is deliberate. A broken model is a deployment problem,
/// and finding out about it while the application starts is far better than finding out when the
/// first customer asks the first question of the day.
/// </para>
/// <para>
/// A model with errors is dropped rather than served in a degraded state — a half-loaded model
/// answers questions with a silently missing entity, which looks like the data being wrong.
/// </para>
/// </remarks>
public sealed class SemanticModelRegistry : ISemanticModelRegistry
{
    private readonly FrozenDictionary<string, SemanticModel> models;

    /// <summary>Loads every model in the configured directory.</summary>
    public SemanticModelRegistry(IOptions<SemanticModelOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var directory = Path.GetFullPath(options.Value.Directory);
        var loaded = new Dictionary<string, SemanticModel>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<ValidationIssue>();

        foreach (var name in SemanticModelLoader.DiscoverModelNames(directory))
        {
            var result = SemanticModelLoader.LoadFromDirectory(directory, name);
            issues.AddRange(result.Issues);

            if (result.Model is null || result.Issues.HasErrors())
            {
                continue;
            }

            var validation = SemanticModelValidator.Validate(result.Model);
            issues.AddRange(validation);

            if (!validation.HasErrors())
            {
                loaded[name] = result.Model;
            }
        }

        this.models = loaded.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        this.Issues = issues;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Names => this.models.Keys;

    /// <inheritdoc/>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <inheritdoc/>
    public SemanticModel GetModel(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return this.models.TryGetValue(name, out var model)
            ? model
            : throw new InvalidOperationException(
                $"No semantic model named '{name}' is loaded. Loaded: " +
                (this.models.Count == 0 ? "(none)" : string.Join(", ", this.models.Keys.Order(StringComparer.Ordinal))) +
                ".");
    }
}
