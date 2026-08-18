using NLTSQL.Semantics.Validation;
using NLTSQL.Semantics.Yaml;

namespace NLTSQL.Semantics.Tests;

/// <summary>
/// Validates the semantic models actually shipped in <c>semantics/</c>.
/// </summary>
/// <remarks>
/// The model is the platform's most valuable artefact and the easiest to break, because nothing
/// about editing YAML tells you that a measure now names a column no dimension declares. Loading
/// the real files in CI is the cheapest possible guard, and it is the same check the
/// <c>nltsql validate</c> command will run once it exists.
/// </remarks>
public sealed class ShippedModelTests
{
    [Fact]
    public void EveryShippedModel_LoadsAndValidatesWithoutErrors()
    {
        var directory = SemanticsDirectory();
        var names = SemanticModelLoader.DiscoverModelNames(directory);

        names.ShouldNotBeEmpty($"No semantic models found under '{directory}'.");

        foreach (var name in names)
        {
            var result = SemanticModelLoader.LoadFromDirectory(directory, name);

            result.Model.ShouldNotBeNull(Describe(name, result.Issues));
            result.Issues.Where(i => i.Severity is IssueSeverity.Error)
                .ShouldBeEmpty(Describe(name, result.Issues));

            var validation = SemanticModelValidator.Validate(result.Model);
            validation.Where(i => i.Severity is IssueSeverity.Error)
                .ShouldBeEmpty(Describe(name, validation));
        }
    }

    [Fact]
    public void TheOverridesFile_ActuallyTakesEffect()
    {
        // Without this, a renamed entity in the overrides file would silently stop applying and the
        // domain expert's wording would quietly disappear from the model.
        var result = SemanticModelLoader.LoadFromDirectory(SemanticsDirectory(), "vertrieb");

        var auftrag = result.Model!.FindEntity("auftrag")!;
        auftrag.Description!.ShouldContain("Stornierte Auftraege sind enthalten");
        auftrag.FindMeasure("umsatz")!.Status.ShouldBe(Model.ReviewStatus.Reviewed);
    }

    private static string Describe(string name, IEnumerable<ValidationIssue> issues) =>
        $"Model '{name}':{Environment.NewLine}{string.Join(Environment.NewLine, issues)}";

    /// <summary>Walks up from the test binary to the repository's <c>semantics</c> directory.</summary>
    private static string SemanticsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "semantics");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the 'semantics' directory from " + AppContext.BaseDirectory);
    }
}
