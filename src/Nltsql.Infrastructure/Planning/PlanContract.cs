using System.Text.Json.Nodes;
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
/// JSON Schema handed to Ollama as the <c>format</c> of the response.
/// </summary>
/// <remarks>
/// <para>
/// Constraining the response removes a whole class of failure: the planner
/// cannot return an invented granularity or an unknown filter operator.
/// What it still can get wrong — naming a member that does not exist — is
/// caught by <see cref="Nltsql.Core.Queries.SemanticQueryValidator"/>.
/// </para>
/// <para>
/// The schema is deliberately written in the conservative subset that
/// llama.cpp's JSON-Schema-to-GBNF conversion handles, because that
/// conversion — not a validating server — is what enforces it. Two
/// constructs are avoided throughout: type unions such as
/// <c>["string", "null"]</c>, and <c>enum</c> lists carrying a
/// <c>null</c> member. Optionality is expressed the one way the grammar
/// understands reliably: by leaving the property out of
/// <c>required</c>. <see cref="PlanContract"/>'s properties are all
/// nullable, so an absent field still deserialises cleanly.
/// </para>
/// </remarks>
internal static class PlanSchema
{
    /// <summary>Parses the schema into a node Ollama can be handed.</summary>
    public static JsonNode Create() =>
        JsonNode.Parse(Json)
        ?? throw new InvalidOperationException("Das Planner-Schema konnte nicht gelesen werden.");

    /// <summary>Exposed for the test that guards the GBNF-safe subset.</summary>
    public static string Text => Json;

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
          "type": "string",
          "description": "If answerable is false: what is missing, in German, addressed to the user."
        },
        "interpretation": {
          "type": "string",
          "description": "One German sentence restating what the query actually computes."
        },
        "view": {
          "type": "string",
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
          "type": "object",
          "additionalProperties": false,
          "required": ["dimension"],
          "properties": {
            "dimension": { "type": "string" },
            "granularity": {
              "type": "string",
              "enum": ["Day", "Week", "Month", "Quarter", "Year"],
              "description": "Only set when the answer should be bucketed over time; omit otherwise."
            },
            "relative_range": {
              "type": "string",
              "enum": ["Today", "Yesterday", "Last7Days", "Last30Days", "Last90Days",
                       "Last12Months", "ThisWeek", "ThisMonth", "ThisQuarter", "ThisYear",
                       "LastWeek", "LastMonth", "LastQuarter", "LastYear"],
              "description": "A rolling window; omit when using from/to or no time filter at all."
            },
            "from": { "type": "string", "description": "ISO date, only for a fixed range." },
            "to": { "type": "string", "description": "ISO date, only for a fixed range." }
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
        "limit": { "type": "integer", "minimum": 1, "maximum": 50000 }
      }
    }
    """;
}
