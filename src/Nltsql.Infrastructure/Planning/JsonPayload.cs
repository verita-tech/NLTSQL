namespace Nltsql.Infrastructure.Planning;

/// <summary>
/// Extracts the JSON object from a language model's reply.
/// </summary>
/// <remarks>
/// Ollama's <c>format</c> is a grammar constraint applied during sampling
/// by llama.cpp, not a schema validated by a server before the response is
/// returned. It holds the shape in the ordinary case, but a local model can
/// still wrap its answer in a markdown fence or put a sentence in front of
/// it. Handing that straight to <see cref="System.Text.Json.JsonSerializer"/>
/// turns a usable answer into a parse error, so the fence comes off here
/// instead. This is the one place the switch away from a server-validated
/// structured-output API costs something.
/// </remarks>
internal static class JsonPayload
{
    /// <summary>
    /// Returns the outermost JSON object in <paramref name="text"/>, or
    /// <see langword="null"/> if there is none.
    /// </summary>
    public static string? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        // Brace counting rather than a regex, because a filter value may
        // legitimately contain a brace and a string may contain a quote.
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var current = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString)
            {
                switch (current)
                {
                    case '\\':
                        escaped = true;
                        break;
                    case '"':
                        inString = false;
                        break;
                    default:
                        break;
                }

                continue;
            }

            switch (current)
            {
                case '"':
                    inString = true;
                    break;

                case '{':
                    depth++;
                    break;

                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..(i + 1)];
                    }

                    break;

                default:
                    break;
            }
        }

        // Unbalanced: the model was cut off mid-answer. Nothing to salvage.
        return null;
    }
}
