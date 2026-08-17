using System.ComponentModel.DataAnnotations;

namespace NLTSQL.QueryEngine;

/// <summary>
/// Ceilings the platform imposes on every query, whatever the question asked for.
/// </summary>
/// <remarks>
/// These are not tuning knobs but the blast radius of a self-service platform. Any user can phrase
/// a question that touches every row of the largest table, so the answer to "what is the worst a
/// question can cost" has to be a configured number rather than whatever the model happened to
/// emit.
/// </remarks>
public sealed class QueryLimits
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Nltsql:QueryLimits";

    /// <summary>Rows returned when a question does not ask for a specific number.</summary>
    [Range(1, 100_000)]
    public int DefaultRowLimit { get; set; } = 1_000;

    /// <summary>Hard ceiling on returned rows. A larger request is clamped to this, not rejected.</summary>
    [Range(1, 1_000_000)]
    public int MaxRowLimit { get; set; } = 50_000;

    /// <summary>How long a single statement may run before it is cancelled.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How many measures and metrics one query may compute.</summary>
    [Range(1, 100)]
    public int MaxSelectedFields { get; set; } = 25;

    /// <summary>How many grouping columns one query may have.</summary>
    /// <remarks>
    /// Grouping by many high-cardinality columns is the most reliable way to turn a small table
    /// into a result set nobody can read and the database cannot cheaply produce.
    /// </remarks>
    [Range(1, 20)]
    public int MaxGroupings { get; set; } = 6;

    /// <summary>How many values a single <c>IN</c> filter may carry.</summary>
    [Range(1, 10_000)]
    public int MaxFilterValues { get; set; } = 500;
}
