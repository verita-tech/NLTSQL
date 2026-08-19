using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nltsql.Core.Queries;

/// <summary>
/// Round-trips a <see cref="SemanticQuery"/> for storage.
/// </summary>
/// <remarks>
/// Enums are written as names rather than numbers on purpose: a saved
/// dashboard tile outlives the code that wrote it, and reordering an
/// enum must not silently turn a "Last30Days" tile into "Yesterday".
/// </remarks>
public static class SemanticQuerySerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize(SemanticQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return JsonSerializer.Serialize(query, Options);
    }

    public static SemanticQuery? Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<SemanticQuery>(json, Options);
}
