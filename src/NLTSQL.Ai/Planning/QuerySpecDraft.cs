using System.Text.Json;
using System.Text.Json.Serialization;

namespace NLTSQL.Ai.Planning;

/// <summary>
/// The shape the language model is asked to fill in.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="Core.Query.QuerySpec"/> itself. That type is strict — required
/// members, enums, no nulls — which is exactly right for the query path and exactly wrong as a
/// deserialisation target for a small local model. A single unexpected spelling would throw before
/// anything could be repaired, and the repair loop needs a parsed draft to complain about.
/// </para>
/// <para>
/// Everything here is nullable and every enum arrives as text, so any syntactically valid JSON
/// deserialises. Whether it means anything is then a question the mapper and the resolver answer
/// with messages the model can act on.
/// </para>
/// </remarks>
public sealed class QuerySpecDraft
{
    /// <summary>Name of the entity to query.</summary>
    [JsonPropertyName("entity")]
    public string? Entity { get; set; }

    /// <summary>Names of the measures or metrics to compute.</summary>
    [JsonPropertyName("measures")]
    public List<string>? Measures { get; set; }

    /// <summary>Fields to group by.</summary>
    [JsonPropertyName("group_by")]
    public List<GroupByDraft>? GroupBy { get; set; }

    /// <summary>Conditions, all combined with AND.</summary>
    [JsonPropertyName("filters")]
    public List<FilterDraft>? Filters { get; set; }

    /// <summary>Sort keys.</summary>
    [JsonPropertyName("order_by")]
    public List<OrderByDraft>? OrderBy { get; set; }

    /// <summary>Maximum rows, when the question asked for a specific number.</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }
}

/// <summary>One grouping, as the model writes it.</summary>
public sealed class GroupByDraft
{
    /// <summary>Name of a dimension or time dimension.</summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>Time bucket: day, week, month, quarter or year. Omitted for plain dimensions.</summary>
    [JsonPropertyName("grain")]
    public string? Grain { get; set; }
}

/// <summary>One filter, as the model writes it.</summary>
public sealed class FilterDraft
{
    /// <summary>Name of a dimension or time dimension.</summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>The comparison to apply.</summary>
    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    /// <summary>Operands.</summary>
    [JsonPropertyName("values")]
    [JsonConverter(typeof(LenientStringListConverter))]
    public List<string>? Values { get; set; }
}

/// <summary>One sort key, as the model writes it.</summary>
public sealed class OrderByDraft
{
    /// <summary>A measure, metric or grouping field the same query selects.</summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>asc or desc.</summary>
    [JsonPropertyName("direction")]
    public string? Direction { get; set; }
}

/// <summary>
/// Reads filter operands whether the model quoted them or not.
/// </summary>
/// <remarks>
/// The schema asks for strings, and a model asked for the year 2025 will sooner or later write
/// <c>2025</c> rather than <c>"2025"</c>. Failing the whole response over a missing pair of quotes
/// would waste a repair round on something with no ambiguity in it — the value converts to the
/// field's declared type later regardless of how it arrived.
/// </remarks>
public sealed class LenientStringListConverter : JsonConverter<List<string>?>
{
    /// <inheritdoc/>
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        // A single scalar where a list was asked for is the other common near-miss.
        if (reader.TokenType is not JsonTokenType.StartArray)
        {
            var single = ReadScalar(ref reader);
            return single is null ? null : [single];
        }

        var values = new List<string>();

        while (reader.Read() && reader.TokenType is not JsonTokenType.EndArray)
        {
            if (ReadScalar(ref reader) is { } value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }

    private static string? ReadScalar(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => ReadNumber(ref reader),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        JsonTokenType.Null => null,
        _ => null,
    };

    private static string ReadNumber(ref Utf8JsonReader reader) =>
        reader.TryGetInt64(out var integer)
            ? integer.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture);
}
