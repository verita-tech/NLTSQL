using NLTSQL.Core.Expressions;

namespace NLTSQL.Core.Tests.Expressions;

public sealed class ExpressionParserTests
{
    [Fact]
    public void SingleReference_ParsesToReferenceNode()
    {
        var result = ExpressionParser.Parse("{{net_amount}}");

        result.Success.ShouldBeTrue();
        result.Expression.ShouldBeOfType<ReferenceExpression>().Name.ShouldBe("net_amount");
    }

    [Fact]
    public void Whitespace_InsideReference_IsTrimmed()
    {
        var result = ExpressionParser.Parse("{{  net_amount  }}");

        result.Expression.ShouldBeOfType<ReferenceExpression>().Name.ShouldBe("net_amount");
    }

    [Fact]
    public void Multiplication_BindsTighterThanAddition()
    {
        var result = ExpressionParser.Parse("{{a}} + {{b}} * 2");

        // Precedence is the whole reason this is a parser rather than a string template: getting
        // it wrong would silently produce a different number, not an error.
        var root = result.Expression.ShouldBeOfType<BinaryExpression>();
        root.Operator.ShouldBe(BinaryOperator.Add);
        root.Left.ShouldBeOfType<ReferenceExpression>().Name.ShouldBe("a");
        root.Right.ShouldBeOfType<BinaryExpression>().Operator.ShouldBe(BinaryOperator.Multiply);
    }

    [Fact]
    public void Subtraction_IsLeftAssociative()
    {
        var result = ExpressionParser.Parse("10 - 3 - 2");

        // Right-associative parsing would evaluate this as 10 - (3 - 2) = 9 instead of 5.
        var root = result.Expression.ShouldBeOfType<BinaryExpression>();
        root.Operator.ShouldBe(BinaryOperator.Subtract);
        root.Right.ShouldBeOfType<NumberExpression>().Value.ShouldBe(2m);
        root.Left.ShouldBeOfType<BinaryExpression>().Operator.ShouldBe(BinaryOperator.Subtract);
    }

    [Fact]
    public void Parentheses_OverridePrecedence()
    {
        var result = ExpressionParser.Parse("({{a}} + {{b}}) * 2");

        var root = result.Expression.ShouldBeOfType<BinaryExpression>();
        root.Operator.ShouldBe(BinaryOperator.Multiply);
        root.Left.ShouldBeOfType<BinaryExpression>().Operator.ShouldBe(BinaryOperator.Add);
    }

    [Fact]
    public void UnaryMinus_Parses()
    {
        var result = ExpressionParser.Parse("-{{discount}}");

        result.Expression.ShouldBeOfType<NegateExpression>()
            .Operand.ShouldBeOfType<ReferenceExpression>().Name.ShouldBe("discount");
    }

    [Fact]
    public void DecimalLiteral_UsesInvariantCulture()
    {
        // The model is authored in one place and read on machines with any locale. Parsing "0.5"
        // with a German culture would yield 5, which is the kind of defect nothing else catches.
        var result = ExpressionParser.Parse("0.5");

        result.Expression.ShouldBeOfType<NumberExpression>().Value.ShouldBe(0.5m);
    }

    [Fact]
    public void AllowedFunction_ParsesWithArguments()
    {
        var result = ExpressionParser.Parse("COALESCE({{a}}, 0)");

        var call = result.Expression.ShouldBeOfType<FunctionExpression>();
        call.Name.ShouldBe("COALESCE");
        call.Arguments.Count.ShouldBe(2);
    }

    [Fact]
    public void Function_NameIsCanonicalisedToUpperCase()
    {
        var result = ExpressionParser.Parse("coalesce({{a}}, 0)");

        result.Expression.ShouldBeOfType<FunctionExpression>().Name.ShouldBe("COALESCE");
    }

    [Fact]
    public void References_AreCollectedFromTheWholeTree()
    {
        var result = ExpressionParser.Parse("COALESCE({{a}}, 0) + {{b}} * ({{c}} - {{a}})");

        result.Expression!.References().Distinct().ShouldBe(["a", "b", "c"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    public void BlankInput_IsRejected(string input, string expectedFragment)
    {
        var result = ExpressionParser.Parse(input);

        result.Success.ShouldBeFalse();
        result.Error!.ShouldContain(expectedFragment, Case.Insensitive);
    }

    [Theory]
    [InlineData("{{a}} +")]
    [InlineData("({{a}}")]
    [InlineData("{{a}} {{b}}")]
    [InlineData("{{a")]
    [InlineData("{{}}")]
    [InlineData("* {{a}}")]
    public void MalformedInput_IsRejectedWithoutThrowing(string input)
    {
        var result = ExpressionParser.Parse(input);

        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("SLEEP(1)")]
    [InlineData("pg_sleep(1)")]
    [InlineData("VERSION()")]
    public void FunctionOutsideTheAllowList_IsRejected(string input)
    {
        // The expression language is the only place model text influences generated SQL, so the
        // allow-list is a security boundary rather than a convenience.
        var result = ExpressionParser.Parse(input);

        result.Success.ShouldBeFalse();
        result.Error!.ShouldContain("not permitted");
    }

    [Theory]
    [InlineData("{{a; DROP TABLE t}}")]
    [InlineData("{{a'}}")]
    [InlineData("{{a b}}")]
    [InlineData("{{a-b}}")]
    public void ReferenceWithUnsupportedCharacters_IsRejected(string input)
    {
        var result = ExpressionParser.Parse(input);

        result.Success.ShouldBeFalse();
    }

    [Theory]
    [InlineData("NULLIF({{a}})", "at least 2")]
    [InlineData("ABS({{a}}, {{b}})", "at most 1")]
    public void WrongArgumentCount_IsRejected(string input, string expectedFragment)
    {
        var result = ExpressionParser.Parse(input);

        result.Success.ShouldBeFalse();
        result.Error!.ShouldContain(expectedFragment);
    }

    [Fact]
    public void NestedFunctionCalls_Parse()
    {
        var result = ExpressionParser.Parse("ROUND(COALESCE({{a}}, 0) / NULLIF({{b}}, 0), 2)");

        result.Success.ShouldBeTrue();
        result.Expression!.References().Distinct().ShouldBe(["a", "b"], ignoreOrder: true);
    }
}
