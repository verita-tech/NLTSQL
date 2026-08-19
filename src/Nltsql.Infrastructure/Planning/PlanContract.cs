using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nltsql.Infrastructure.Planning;

/// <summary>
/// The JSON shape the planner is constrained to produce.
/// </summary>
/// <remarks>
/// Enum members are spelled exactly like the corresponding C# enums, so
/// parsing is a direct <c>Enum.TryParse</c> with no translation table to
/// drift out of sync.
/// </remarks>
internal sealed record PlanContract
{
    [JsonPropertyName("answerable")]
    public bool Answerable { get; init; }

    /// <summary>Why the question cannot be answered from the model.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("interpretation")]
    public string? Interpretation { get; init; }

    [JsonPropertyName("view")]
    public string? View { get; init; }

    [JsonPropertyName("measures")]
    public List<string> Measures { get; init; } = [];

    [JsonPropertyName("dimensions")]
    public List<string> Dimensions { get; init; } = [];

    [JsonPropertyName("time_dimension")]
    public PlanTimeDimension? TimeDimension { get; init; }

    [JsonPropertyName("filters")]
    public List<PlanFilter> Filters { get; init; } = [];

    [JsonPropertyName("order")]
    public List<PlanOrder> Order { get; init; } = [];

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }
}

internal sealed record PlanTimeDimension
{
    [JsonPropertyName("dimension")]
    public string? Dimension { get; init; }

    [JsonPropertyName("granularity")]
    public string? Granularity { get; init; }

    [JsonPropertyName("relative_range")]
    public string? RelativeRange { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("to")]
    public string? To { get; init; }
}

internal sealed record PlanFilter
{
    [JsonPropertyName("member")]
    public string? Member { get; init; }

    [JsonPropertyName("operator")]
    public string? Operator { get; init; }

    [JsonPropertyName("values")]
    public List<string> Values { get; init; } = [];
}

internal sealed record PlanOrder
{
    [JsonPropertyName("member")]
    public string? Member { get; init; }

    [JsonPropertyName("direction")]
    public string? Direction { get; init; }
}

/// <summary>
/// JSON Schema handed to the Messages API as a structured output format.
/// </summary>
/// <remarks>
/// Constraining the response at the API level removes a whole class of
/// failure: the planner cannot return prose, markdown-fenced JSON or an
/// invented granularity. What it still can get wrong — naming a member
/// that does not exist — is caught by
/// <see cref="Nltsql.Core.Queries.SemanticQueryValidator"/>.
/// </remarks>
internal static class PlanSchema
{
    public static Dictionary<string, JsonElement> Create()
    {
        using var document = JsonDocument.Parse(Json);

        return document.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private const string Json = """
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["answerable", "interpretation", "view", "measures", "dimensions"],
      "properties": {
        "answerable": {
          "type": "boolean",
          "description": "False when the question cannot be answered from the available views."
        },
        "reason": {
          "type": ["string", "null"],
          "description": "If answerable is false: what is missing, in German, addressed to the user."
        },
        "interpretation": {
          "type": ["string", "null"],
          "description": "One German sentence restating what the query actually computes."
        },
        "view": {
          "type": ["string", "null"],
          "description": "Name of the chosen view."
        },
        "measures": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Measure names, unqualified, exactly as listed in the catalogue."
        },
        "dimensions": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Dimension names to group by, unqualified."
        },
        "time_dimension": {
          "type": ["object", "null"],
          "additionalProperties": false,
          "required": ["dimension"],
          "properties": {
            "dimension": { "type": "string" },
            "granularity": {
              "type": ["string", "null"],
              "enum": ["Day", "Week", "Month", "Quarter", "Year", null],
              "description": "Only set when the answer should be bucketed over time."
            },
            "relative_range": {
              "type": ["string", "null"],
              "enum": ["Today", "Yesterday", "Last7Days", "Last30Days", "Last90Days",
                       "Last12Months", "ThisWeek", "ThisMonth", "ThisQuarter", "ThisYear",
                       "LastWeek", "LastMonth", "LastQuarter", "LastYear", null]
            },
            "from": { "type": ["string", "null"], "description": "ISO date, only for a fixed range." },
            "to": { "type": ["string", "null"], "description": "ISO date, only for a fixed range." }
          }
        },
        "filters": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["member", "operator", "values"],
            "properties": {
              "member": { "type": "string" },
              "operator": {
                "type": "string",
                "enum": ["Equals", "NotEquals", "Contains", "NotContains", "StartsWith",
                         "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual",
                         "Set", "NotSet"]
              },
              "values": { "type": "array", "items": { "type": "string" } }
            }
          }
        },
        "order": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["member", "direction"],
            "properties": {
              "member": { "type": "string" },
              "direction": { "type": "string", "enum": ["Ascending", "Descending"] }
            }
          }
        },
        "limit": { "type": ["integer", "null"], "minimum": 1, "maximum": 50000 }
      }
    }
    """;
}
