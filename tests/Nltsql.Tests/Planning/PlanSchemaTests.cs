using System.Text.Json;
using Nltsql.Core.Queries;
using Nltsql.Infrastructure.Planning;
using Shouldly;

namespace Nltsql.Tests.Planning;

/// <summary>
/// Keeps the planner schema inside the subset llama.cpp's grammar
/// conversion handles, and in step with the enums it mirrors.
/// </summary>
public sealed class PlanSchemaTests
{
    [Fact]
    public void Parses()
    {
        PlanSchema.Create().ShouldNotBeNull();
    }

    [Fact]
    public void Declares_no_type_unions()
    {
        // `"type": ["string", "null"]` is the construct the grammar
        // conversion mishandles. Optionality is expressed by leaving the
        // property out of `required` instead.
        using var document = JsonDocument.Parse(PlanSchema.Text);

        var unions = new List<string>();
        CollectTypeArrays(document.RootElement, "$", unions);

        unions.ShouldBeEmpty();
    }

    [Fact]
    public void Declares_no_enum_containing_null()
    {
        using var document = JsonDocument.Parse(PlanSchema.Text);

        var offenders = new List<string>();
        CollectNullableEnums(document.RootElement, "$", offenders);

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Granularity_enum_matches_the_domain_enum()
    {
        EnumValues("granularity").ShouldBe(Enum.GetNames<TimeGranularity>(), ignoreOrder: true);
    }

    [Fact]
    public void Relative_range_enum_matches_the_domain_enum()
    {
        EnumValues("relative_range").ShouldBe(Enum.GetNames<RelativeDateRange>(), ignoreOrder: true);
    }

    [Fact]
    public void Filter_operator_enum_matches_the_domain_enum()
    {
        EnumValues("operator").ShouldBe(Enum.GetNames<FilterOperator>(), ignoreOrder: true);
    }

    [Fact]
    public void Direction_enum_matches_the_domain_enum()
    {
        EnumValues("direction").ShouldBe(Enum.GetNames<Nltsql.Core.Queries.SortDirection>(), ignoreOrder: true);
    }

    [Fact]
    public void Requires_the_fields_the_mapper_cannot_do_without()
    {
        using var document = JsonDocument.Parse(PlanSchema.Text);

        var required = document.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        required.ShouldContain("answerable");
        required.ShouldContain("view");
    }

    /// <summary>Finds the first enum declared under a property name.</summary>
    private static string[] EnumValues(string propertyName)
    {
        using var document = JsonDocument.Parse(PlanSchema.Text);

        var found = FindEnum(document.RootElement, propertyName);
        found.ShouldNotBeNull($"Das Schema deklariert kein enum für \"{propertyName}\".");

        return found;
    }

    private static string[]? FindEnum(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == propertyName
                    && property.Value.ValueKind == JsonValueKind.Object
                    && property.Value.TryGetProperty("enum", out var values))
                {
                    return values.EnumerateArray().Select(v => v.GetString()!).ToArray();
                }

                var nested = FindEnum(property.Value, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindEnum(item, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static void CollectTypeArrays(JsonElement element, string path, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "type" && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        found.Add($"{path}.type");
                    }

                    CollectTypeArrays(property.Value, $"{path}.{property.Name}", found);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CollectTypeArrays(item, $"{path}[{index++}]", found);
                }

                break;

            default:
                break;
        }
    }

    private static void CollectNullableEnums(JsonElement element, string path, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "enum"
                        && property.Value.ValueKind == JsonValueKind.Array
                        && property.Value.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Null))
                    {
                        found.Add($"{path}.enum");
                    }

                    CollectNullableEnums(property.Value, $"{path}.{property.Name}", found);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CollectNullableEnums(item, $"{path}[{index++}]", found);
                }

                break;

            default:
                break;
        }
    }
}
