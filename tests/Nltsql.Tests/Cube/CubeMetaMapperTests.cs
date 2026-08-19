using System.Text.Json;
using Nltsql.Core.Semantics;
using Nltsql.Infrastructure.Cube;
using Shouldly;

namespace Nltsql.Tests.Cube;

/// <summary>
/// Pins the assumptions this app makes about Cube's <c>/v1/meta</c>
/// payload — qualified member names, the shortTitle/title split, and
/// synonyms carried in <c>meta</c>.
/// </summary>
public sealed class CubeMetaMapperTests
{
    private const string MetaJson = """
    {
      "cubes": [
        {
          "name": "fertigung",
          "title": "Fertigung & OEE",
          "description": "Produktionskennzahlen.",
          "measures": [
            {
              "name": "fertigung.oee",
              "title": "Fertigung & OEE OEE",
              "shortTitle": "OEE",
              "type": "number",
              "format": "percent",
              "meta": { "synonyms": ["Anlageneffektivität", "OEE"] },
              "isVisible": true
            },
            {
              "name": "fertigung.internal_debug",
              "shortTitle": "Intern",
              "type": "number",
              "isVisible": false
            }
          ],
          "dimensions": [
            {
              "name": "fertigung.machine_name",
              "shortTitle": "Maschine",
              "type": "string",
              "isVisible": true
            },
            {
              "name": "fertigung.started_at",
              "shortTitle": "Schichtbeginn",
              "type": "time",
              "isVisible": true
            }
          ]
        }
      ]
    }
    """;

    private static SemanticModel Map(string json)
    {
        using var document = JsonDocument.Parse(json);
        return CubeMetaMapper.Map(document.RootElement);
    }

    [Fact]
    public void Maps_views_with_their_business_titles()
    {
        var model = Map(MetaJson);

        var view = model.FindView("fertigung").ShouldNotBeNull();
        view.Title.ShouldBe("Fertigung & OEE");
    }

    [Fact]
    public void Strips_the_view_prefix_from_member_names()
    {
        var model = Map(MetaJson);

        // Members are stored unqualified so a saved query is not coupled
        // to Cube's naming.
        model.FindView("fertigung")!.FindMeasure("oee").ShouldNotBeNull();
    }

    [Fact]
    public void Prefers_shortTitle_over_the_cube_prefixed_title()
    {
        var model = Map(MetaJson);

        model.FindView("fertigung")!.FindMeasure("oee")!.Title.ShouldBe("OEE");
    }

    [Fact]
    public void Carries_format_and_synonyms_through()
    {
        var measure = Map(MetaJson).FindView("fertigung")!.FindMeasure("oee")!;

        measure.Format.ShouldBe("percent");
        measure.Synonyms.ShouldContain("Anlageneffektivität");
    }

    [Fact]
    public void Skips_members_marked_invisible()
    {
        var view = Map(MetaJson).FindView("fertigung")!;

        view.FindMeasure("internal_debug").ShouldBeNull();
    }

    [Fact]
    public void Recognises_time_dimensions()
    {
        var view = Map(MetaJson).FindView("fertigung")!;

        view.TimeDimensions.Select(d => d.Name).ShouldBe(["started_at"]);
        view.FindDimension("machine_name")!.Type.ShouldBe(SemanticType.String);
    }

    [Fact]
    public void Returns_an_empty_model_for_an_unexpected_payload()
    {
        Map("""{"unexpected": true}""").Views.ShouldBeEmpty();
    }
}
