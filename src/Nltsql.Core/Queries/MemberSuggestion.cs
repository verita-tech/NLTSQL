using Nltsql.Core.Semantics;

namespace Nltsql.Core.Queries;

/// <summary>
/// Finds the member a user (or a planner) most likely meant.
/// </summary>
/// <remarks>
/// Matching runs over names, business titles and the synonyms carried in
/// the Cube model, which is what makes "Anlageneffektivität" resolve to
/// <c>oee</c>. Used for validation hints and for the repair prompt.
/// </remarks>
public static class MemberSuggestion
{
    /// <summary>Reject weak matches rather than suggest something misleading.</summary>
    private const double MinimumSimilarity = 0.62;

    public static string? Closest(string term, SemanticView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (string.IsNullOrWhiteSpace(term))
        {
            return null;
        }

        string? best = null;
        var bestScore = MinimumSimilarity;

        foreach (var member in view.Measures.Cast<SemanticMember>().Concat(view.Dimensions))
        {
            foreach (var candidate in Candidates(member))
            {
                var score = Similarity(term, candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = member.Name;
                }
            }
        }

        return best;
    }

    private static IEnumerable<string> Candidates(SemanticMember member)
    {
        yield return member.Name;
        yield return member.Title;

        foreach (var synonym in member.Synonyms)
        {
            yield return synonym;
        }
    }

    /// <summary>Normalised edit distance in [0,1]; 1 means identical.</summary>
    private static double Similarity(string left, string right)
    {
        var a = Normalise(left);
        var b = Normalise(right);

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        var distance = Levenshtein(a, b);
        return 1.0 - ((double)distance / Math.Max(a.Length, b.Length));
    }

    /// <summary>
    /// Folds case, German umlauts and separators so that
    /// "Anlagen-Effektivitaet" and "anlageneffektivität" compare equal.
    /// </summary>
    private static string Normalise(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);

        foreach (var ch in value.ToLowerInvariant())
        {
            switch (ch)
            {
                case 'ä': builder.Append("ae"); break;
                case 'ö': builder.Append("oe"); break;
                case 'ü': builder.Append("ue"); break;
                case 'ß': builder.Append("ss"); break;
                case '_' or '-' or ' ' or '.': break;
                default:
                    if (char.IsLetterOrDigit(ch))
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Two-row Levenshtein; the strings here are short.</summary>
    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
