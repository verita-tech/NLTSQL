using System.Text.Json;
using System.Text.Json.Serialization;

namespace NLTSQL.Core.Serialization;

/// <summary>
/// The one JSON configuration used wherever a query spec is persisted or displayed.
/// </summary>
/// <remarks>
/// A saved dashboard tile stores a spec and reads it back weeks later, so the format has to be
/// stable and self-describing. Enums are written as names rather than numbers for exactly that
/// reason: inserting a value into an enum would silently repoint every stored ordinal, turning a
/// saved "sum" into "average" with nothing to show for it.
/// </remarks>
public static class NltsqlJson
{
    /// <summary>Options for persisting and displaying specs.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The same options, indented, for showing a spec to a person.</summary>
    public static JsonSerializerOptions Readable { get; } = new(Options) { WriteIndented = true };

    /// <summary>Serialises <paramref name="value"/> for storage.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Serialises <paramref name="value"/> for display.</summary>
    public static string Display<T>(T value) => JsonSerializer.Serialize(value, Readable);

    /// <summary>Reads back a value written by <see cref="Serialize{T}"/>.</summary>
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
