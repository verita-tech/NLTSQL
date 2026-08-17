namespace NLTSQL.Core.Expressions;

/// <summary>
/// A parsed measure or metric expression.
/// </summary>
/// <remarks>
/// Expressions come from the semantic model, which is versioned and reviewed, but they are
/// still never handed to a database as text. Parsing them into this tree means the SQL
/// compiler emits only shapes it constructed itself, the validator can check that every
/// referenced column exists, and a malformed model fails at load time instead of at query
/// time in front of a customer.
/// </remarks>
public abstract record SqlExpression
{
    /// <summary>Every name referenced anywhere in this expression, in source order.</summary>
    public IEnumerable<string> References() => this switch
    {
        ReferenceExpression reference => [reference.Name],
        NumberExpression => [],
        NegateExpression negate => negate.Operand.References(),
        BinaryExpression binary => binary.Left.References().Concat(binary.Right.References()),
        FunctionExpression function => function.Arguments.SelectMany(argument => argument.References()),
        _ => throw new NotSupportedException($"Unhandled expression node '{GetType().Name}'."),
    };
}

/// <summary>
/// A named reference written as <c>{{name}}</c>.
/// </summary>
/// <remarks>
/// What the name resolves to depends on where the expression appears: inside a measure it is a
/// physical column of the owning entity, inside a metric it is another measure. The parser is
/// deliberately agnostic about this — resolution is the validator's job, which is also the only
/// place that knows the surrounding entity.
/// </remarks>
public sealed record ReferenceExpression(string Name) : SqlExpression;

/// <summary>A numeric literal.</summary>
public sealed record NumberExpression(decimal Value) : SqlExpression;

/// <summary>Unary minus.</summary>
public sealed record NegateExpression(SqlExpression Operand) : SqlExpression;

/// <summary>An arithmetic operator applied to two operands.</summary>
public sealed record BinaryExpression(BinaryOperator Operator, SqlExpression Left, SqlExpression Right) : SqlExpression;

/// <summary>A call to one of the functions on <see cref="SqlFunctions"/>.</summary>
public sealed record FunctionExpression(string Name, IReadOnlyList<SqlExpression> Arguments) : SqlExpression;

/// <summary>The arithmetic operators the expression language supports.</summary>
public enum BinaryOperator
{
    /// <summary>Addition.</summary>
    Add,

    /// <summary>Subtraction.</summary>
    Subtract,

    /// <summary>Multiplication.</summary>
    Multiply,

    /// <summary>Division.</summary>
    Divide,
}
