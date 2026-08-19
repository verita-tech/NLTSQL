using Nltsql.Core.Semantics;

namespace Nltsql.Tests;

/// <summary>
/// A small stand-in for the semantic model Cube would report.
/// </summary>
/// <remarks>
/// Mirrors the shape of the real <c>fertigung</c> view — a percentage
/// measure, a plain measure, a string dimension and a time dimension —
/// because those four cases are what the validator, the SQL renderer and
/// the CSV writer each treat differently.
/// </remarks>
internal static class TestModel
{
    public const string ViewName = "fertigung";

    public static SemanticView View { get; } = new()
    {
        Name = ViewName,
        Title = "Fertigung & OEE",
        Description = "Produktionskennzahlen je Maschine.",
        Measures =
        [
            new SemanticMeasure
            {
                Name = "oee",
                Title = "OEE",
                Type = SemanticType.Number,
                Format = "percent",
                Synonyms = ["Gesamtanlageneffektivität", "Anlageneffektivität"],
            },
            new SemanticMeasure
            {
                Name = "scrap_qty",
                Title = "Ausschussmenge",
                Type = SemanticType.Number,
            },
        ],
        Dimensions =
        [
            new SemanticDimension
            {
                Name = "machine_name",
                Title = "Maschine",
                Type = SemanticType.String,
                Synonyms = ["Anlage"],
            },
            new SemanticDimension
            {
                Name = "started_at",
                Title = "Schichtbeginn",
                Type = SemanticType.Time,
            },
        ],
    };

    public static SemanticModel Model { get; } = new([View]);
}
