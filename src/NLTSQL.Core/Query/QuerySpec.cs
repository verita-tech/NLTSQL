namespace NLTSQL.Core.Query;

/// <summary>Comparison applied by a filter.</summary>
public enum FilterOperator
{
    /// <summary>Field equals the single value.</summary>
    Equals,

    /// <summary>Field differs from the single value.</summary>
    NotEquals,

    /// <summary>Field is one of the values.</summary>
    In,

    /// <summary>Field is none of the values.</summary>
    NotIn,

    /// <summary>Field is strictly greater than the single value.</summary>
    GreaterThan,

    /// <summary>Field is greater than or equal to the single value.</summary>
    GreaterOrEqual,

    /// <summary>Field is strictly less than the single value.</summary>
    LessThan,

    /// <summary>Field is less than or equal to the single value.</summary>
    LessOrEqual,

    /// <summary>Field lies between the first and second value, inclusive.</summary>
    Between,

    /// <summary>Field has no value.</summary>
    IsNull,

    /// <summary>Field has a value.</summary>
    IsNotNull,

    /// <summary>Text field contains the single value.</summary>
    Contains,

    /// <summary>Text field starts with the single value.</summary>
    StartsWith,

    /// <summary>Text field ends with the single value.</summary>
    EndsWith,

    /// <summary>Time field falls within the last N days, counted back from the query's reference time.</summary>
    InLastDays,

    /// <summary>Time field falls within the last N months.</summary>
    InLastMonths,

    /// <summary>Time field falls within the last N years.</summary>
    InLastYears,
}

/// <summary>Sort direction.</summary>
public enum SortDirection
{
    /// <summary>Smallest first.</summary>
    Ascending,

    /// <summary>Largest first.</summary>
    Descending,
}

/// <summary>Time bucket a time dimension is rolled up to.</summary>
public enum TimeGrain
{
    /// <summary>No bucketing; the raw value.</summary>
    None,

    /// <summary>Calendar day.</summary>
    Day,

    /// <summary>Calendar week.</summary>
    Week,

    /// <summary>Calendar month.</summary>
    Month,

    /// <summary>Calendar quarter.</summary>
    Quarter,

    /// <summary>Calendar year.</summary>
    Year,
}

/// <summary>One grouping column.</summary>
/// <param name="Field">Name of a dimension or time dimension of the queried entity.</param>
/// <param name="Grain">Bucket to apply. Only meaningful for a time dimension.</param>
public sealed record GroupByItem(string Field, TimeGrain Grain = TimeGrain.None);

/// <summary>One filter condition.</summary>
/// <param name="Field">Name of a dimension or time dimension of the queried entity.</param>
/// <param name="Operator">The comparison.</param>
/// <param name="Values">
/// Operands, always as strings. The compiler converts each one according to the field's declared
/// type and binds it as a parameter, so a value that does not convert is a rejected query rather
/// than a malformed statement.
/// </param>
public sealed record FilterItem(string Field, FilterOperator Operator, IReadOnlyList<string> Values);

/// <summary>One sort key.</summary>
/// <param name="Field">A measure, metric, or grouping field named elsewhere in the same spec.</param>
/// <param name="Direction">Sort direction.</param>
public sealed record OrderByItem(string Field, SortDirection Direction = SortDirection.Descending);

/// <summary>
/// A question, expressed against the semantic model rather than against the database.
/// </summary>
/// <remarks>
/// <para>
/// This is the only thing the language model produces in the query path. Everything downstream —
/// joins, grain, aggregate placement, dialect — is decided by code, which is what makes the
/// platform usable with a locally hosted model whose free-form SQL would not be trustworthy.
/// </para>
/// <para>
/// Filters are a flat list combined with AND rather than a boolean tree. That is a deliberate
/// trade: a tree is more expressive, but small models produce malformed nesting often enough that
/// the expressiveness costs more than it returns. Disjunction is still available through
/// <see cref="FilterOperator.In"/>, which covers the overwhelming majority of real questions.
/// </para>
/// </remarks>
public sealed record QuerySpec
{
    /// <summary>Name of the entity to query.</summary>
    public required string Entity { get; init; }

    /// <summary>Names of the measures and metrics to compute.</summary>
    public IReadOnlyList<string> Measures { get; init; } = [];

    /// <summary>Fields to group by. An empty list produces a single total row.</summary>
    public IReadOnlyList<GroupByItem> GroupBy { get; init; } = [];

    /// <summary>Conditions, combined with AND.</summary>
    public IReadOnlyList<FilterItem> Filters { get; init; } = [];

    /// <summary>Sort keys, applied in order.</summary>
    public IReadOnlyList<OrderByItem> OrderBy { get; init; } = [];

    /// <summary>
    /// Maximum rows to return.
    /// </summary>
    /// <remarks>
    /// Always capped server-side regardless of what is requested here; this only lets a question
    /// ask for fewer, as in "the top five customers".
    /// </remarks>
    public int? Limit { get; init; }
}
