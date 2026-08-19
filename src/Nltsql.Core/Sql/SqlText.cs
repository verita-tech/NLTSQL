namespace Nltsql.Core.Sql;

/// <summary>Quoting helpers for generated SQL.</summary>
/// <remarks>
/// Metabase native cards carry plain SQL text, so there is no parameter
/// binding available at that boundary. Values are therefore escaped here,
/// and control characters are rejected outright rather than escaped —
/// nothing legitimate in this domain contains them.
/// </remarks>
public static class SqlText
{
    /// <summary>Quotes an identifier, doubling any embedded quote.</summary>
    public static string Identifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>Quotes a string literal, doubling any embedded apostrophe.</summary>
    public static string Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                throw new ArgumentException(
                    "Filterwerte dürfen keine Steuerzeichen enthalten.", nameof(value));
            }
        }

        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}
