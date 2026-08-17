using System.Collections.Frozen;

namespace NLTSQL.Core.Expressions;

/// <summary>
/// The functions a semantic-model expression may call.
/// </summary>
/// <remarks>
/// The admission criterion is deliberately narrow: a function belongs here only if Oracle and
/// PostgreSQL both provide it under the same name with the same semantics. That keeps the SQL
/// compiler free of per-dialect function mapping and, more importantly, keeps a model authored
/// against one database from silently changing meaning against the other.
/// </remarks>
public static class SqlFunctions
{
    private static readonly FrozenDictionary<string, Arity> Allowed = new Dictionary<string, Arity>(StringComparer.OrdinalIgnoreCase)
    {
        ["COALESCE"] = new(2, null),
        ["NULLIF"] = new(2, 2),
        ["ABS"] = new(1, 1),
        ["ROUND"] = new(1, 2),
        ["FLOOR"] = new(1, 1),
        ["CEIL"] = new(1, 1),
        ["GREATEST"] = new(2, null),
        ["LEAST"] = new(2, null),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The callable function names, in canonical upper case.</summary>
    public static IReadOnlyCollection<string> Names { get; } = [.. Allowed.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Whether <paramref name="name"/> names a permitted function.</summary>
    public static bool IsAllowed(string name) => Allowed.ContainsKey(name);

    /// <summary>Canonical upper-case spelling, or <paramref name="name"/> unchanged when not permitted.</summary>
    public static string Canonicalise(string name) =>
        Allowed.TryGetValue(name, out _) ? name.ToUpperInvariant() : name;

    /// <summary>
    /// Checks the argument count for <paramref name="name"/>.
    /// </summary>
    /// <returns>An error message, or <see langword="null"/> when the count is acceptable.</returns>
    public static string? ValidateArgumentCount(string name, int argumentCount)
    {
        if (!Allowed.TryGetValue(name, out var arity))
        {
            return $"Function '{name}' is not permitted. Allowed: {string.Join(", ", Names)}.";
        }

        if (argumentCount < arity.Minimum)
        {
            return $"Function '{Canonicalise(name)}' expects at least {arity.Minimum} argument(s) but got {argumentCount}.";
        }

        return arity.Maximum is { } maximum && argumentCount > maximum
            ? $"Function '{Canonicalise(name)}' expects at most {maximum} argument(s) but got {argumentCount}."
            : null;
    }

    private readonly record struct Arity(int Minimum, int? Maximum);
}
