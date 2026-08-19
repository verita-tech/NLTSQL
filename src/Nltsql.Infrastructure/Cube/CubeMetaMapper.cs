using System.Text.Json;
using Nltsql.Core.Semantics;

namespace Nltsql.Infrastructure.Cube;

/// <summary>
/// Projects Cube's <c>/v1/meta</c> response onto <see cref="SemanticModel"/>.
/// </summary>
/// <remarks>
/// Cube reports every publicly visible entity in its <c>cubes</c> array.
/// Because the model marks the underlying cubes <c>public: false</c>,
/// what arrives here is exactly the set of published views — the app
/// never has to distinguish the two.
/// </remarks>
internal static class CubeMetaMapper
{
    public static SemanticModel Map(JsonElement root)
    {
        if (!root.TryGetProperty("cubes", out var cubes) || cubes.ValueKind != JsonValueKind.Array)
        {
            return SemanticModel.Empty;
        }

        var views = new List<SemanticView>();

        foreach (var cube in cubes.EnumerateArray())
        {
            var name = GetString(cube, "name");
            if (string.IsNullOrEmpty(name) || IsHidden(cube))
            {
                continue;
            }

            views.Add(new SemanticView
            {
                Name = name,
                Title = GetString(cube, "title") ?? name,
                Description = GetString(cube, "description"),
                Measures = MapMeasures(cube),
                Dimensions = MapDimensions(cube),
            });
        }

        return new SemanticModel(views);
    }

    private static List<SemanticMeasure> MapMeasures(JsonElement cube)
    {
        var measures = new List<SemanticMeasure>();

        foreach (var member in Members(cube, "measures"))
        {
            measures.Add(new SemanticMeasure
            {
                Name = CubeQueryTranslator.Unqualify(GetString(member, "name")!),
                Title = MemberTitle(member),
                Description = GetString(member, "description"),
                Type = ParseType(GetString(member, "type")),
                Format = GetString(member, "format"),
                Synonyms = Synonyms(member),
            });
        }

        return measures;
    }

    private static List<SemanticDimension> MapDimensions(JsonElement cube)
    {
        var dimensions = new List<SemanticDimension>();

        foreach (var member in Members(cube, "dimensions"))
        {
            dimensions.Add(new SemanticDimension
            {
                Name = CubeQueryTranslator.Unqualify(GetString(member, "name")!),
                Title = MemberTitle(member),
                Description = GetString(member, "description"),
                Type = ParseType(GetString(member, "type")),
                Synonyms = Synonyms(member),
            });
        }

        return dimensions;
    }

    private static IEnumerable<JsonElement> Members(JsonElement cube, string property)
    {
        if (!cube.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var member in array.EnumerateArray())
        {
            if (GetString(member, "name") is null || IsHidden(member))
            {
                continue;
            }

            yield return member;
        }
    }

    /// <summary>
    /// Cube's <c>title</c> on a member is prefixed with the cube title;
    /// <c>shortTitle</c> is the business label the UI wants.
    /// </summary>
    private static string MemberTitle(JsonElement member) =>
        GetString(member, "shortTitle")
        ?? GetString(member, "title")
        ?? CubeQueryTranslator.Unqualify(GetString(member, "name")!);

    private static List<string> Synonyms(JsonElement member)
    {
        if (!member.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!meta.TryGetProperty("synonyms", out var synonyms) || synonyms.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return synonyms.EnumerateArray()
            .Where(s => s.ValueKind == JsonValueKind.String)
            .Select(s => s.GetString()!)
            .ToList();
    }

    private static bool IsHidden(JsonElement element) =>
        (element.TryGetProperty("isVisible", out var visible) && visible.ValueKind == JsonValueKind.False)
        || (element.TryGetProperty("public", out var isPublic) && isPublic.ValueKind == JsonValueKind.False);

    private static SemanticType ParseType(string? type) => type?.ToLowerInvariant() switch
    {
        "number" => SemanticType.Number,
        "time" => SemanticType.Time,
        "boolean" => SemanticType.Boolean,
        _ => SemanticType.String,
    };

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
