using System.Diagnostics.CodeAnalysis;

namespace NLTSQL.Semantics.Model;

/// <summary>How far an element has progressed from generated draft to reviewed fact.</summary>
/// <remarks>
/// Scaffolding infers labels and descriptions, and inference is sometimes wrong. Carrying the
/// review state in the model itself lets the UI show what a domain expert has actually blessed
/// instead of presenting every guess with the same confidence.
/// </remarks>
public enum ReviewStatus
{
    /// <summary>Generated or inferred, not yet confirmed by a domain expert.</summary>
    Draft,

    /// <summary>Confirmed by a domain expert.</summary>
    Reviewed,
}

/// <summary>The aggregate a measure applies.</summary>
public enum Aggregation
{
    /// <summary>Sum of the expression.</summary>
    Sum,

    /// <summary>Arithmetic mean of the expression.</summary>
    Average,

    /// <summary>Smallest value of the expression.</summary>
    Minimum,

    /// <summary>Largest value of the expression.</summary>
    Maximum,

    /// <summary>Row count. Counts rows when no expression is given, otherwise non-null values.</summary>
    Count,

    /// <summary>Count of distinct non-null values of the expression.</summary>
    CountDistinct,
}

/// <summary>The time buckets a time dimension can be rolled up to.</summary>
public enum Granularity
{
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

/// <summary>The logical type of a dimension, independent of the physical database type.</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "These are the domain's own words for SQL types and appear verbatim in the " +
                    "YAML model. Renaming them to avoid a CLR type-name collision would make the " +
                    "C# enum and the authored model disagree, which is the more costly confusion.")]
public enum DataType
{
    /// <summary>Textual value.</summary>
    String,

    /// <summary>Whole number.</summary>
    Integer,

    /// <summary>Fractional number.</summary>
    Decimal,

    /// <summary>True/false value.</summary>
    Boolean,

    /// <summary>Date without a time component.</summary>
    Date,

    /// <summary>Date with a time component.</summary>
    Timestamp,
}

/// <summary>How a numeric result should be rendered to a user.</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Format names are authored verbatim in the YAML model; see DataType.")]
public enum ValueFormatKind
{
    /// <summary>Plain number with the configured number of decimals.</summary>
    Number,

    /// <summary>Whole number.</summary>
    Integer,

    /// <summary>Monetary amount in <see cref="ValueFormat.Currency"/>.</summary>
    Currency,

    /// <summary>Ratio rendered as a percentage.</summary>
    Percent,
}

/// <summary>The direction of a relationship, seen from its <c>from</c> side.</summary>
public enum Cardinality
{
    /// <summary>Many rows on the <c>from</c> side reference one row on the <c>to</c> side.</summary>
    ManyToOne,

    /// <summary>One row on the <c>from</c> side is referenced by many rows on the <c>to</c> side.</summary>
    OneToMany,

    /// <summary>At most one row on each side.</summary>
    OneToOne,
}
