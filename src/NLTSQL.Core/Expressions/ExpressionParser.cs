using System.Globalization;

namespace NLTSQL.Core.Expressions;

/// <summary>
/// The outcome of parsing a semantic-model expression.
/// </summary>
/// <param name="Expression">The parsed tree, or <see langword="null"/> when parsing failed.</param>
/// <param name="Error">The failure description, or <see langword="null"/> on success.</param>
/// <param name="Position">Zero-based offset the failure was detected at.</param>
public readonly record struct ExpressionParseResult(SqlExpression? Expression, string? Error, int Position)
{
    /// <summary>Whether an expression was produced.</summary>
    public bool Success => Expression is not null;

    internal static ExpressionParseResult Ok(SqlExpression expression) => new(expression, null, 0);

    internal static ExpressionParseResult Fail(string error, int position) => new(null, error, position);
}

/// <summary>
/// Parses the restricted arithmetic language used by measure and metric expressions.
/// </summary>
/// <remarks>
/// <para>The grammar is intentionally small:</para>
/// <code>
/// expression := term (('+' | '-') term)*
/// term       := factor (('*' | '/') factor)*
/// factor     := '-'? primary
/// primary    := number | '{{' reference '}}' | function '(' arguments? ')' | '(' expression ')'
/// </code>
/// <para>
/// There is no string literal, no comparison, no subquery and no arbitrary function call, so
/// nothing an author writes here can widen into a second query. Anything richer than this
/// belongs in a database view that the model then maps as an ordinary column.
/// </para>
/// </remarks>
public static class ExpressionParser
{
    /// <summary>Parses <paramref name="text"/> into an expression tree.</summary>
    public static ExpressionParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return ExpressionParseResult.Fail("Expression is empty.", 0);
        }

        var tokens = Tokenizer.Tokenize(text, out var tokenError, out var tokenPosition);
        if (tokens is null)
        {
            return ExpressionParseResult.Fail(tokenError!, tokenPosition);
        }

        var parser = new Parser(tokens);
        var expression = parser.ParseExpression(out var error, out var position);
        if (expression is null)
        {
            return ExpressionParseResult.Fail(error!, position);
        }

        return parser.AtEnd
            ? ExpressionParseResult.Ok(expression)
            : ExpressionParseResult.Fail("Unexpected trailing input.", parser.CurrentPosition);
    }

    private enum TokenKind
    {
        Number,
        Reference,
        Identifier,
        Plus,
        Minus,
        Star,
        Slash,
        OpenParen,
        CloseParen,
        Comma,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static class Tokenizer
    {
        public static List<Token>? Tokenize(string text, out string? error, out int errorPosition)
        {
            var tokens = new List<Token>();
            var index = 0;
            error = null;
            errorPosition = 0;

            while (index < text.Length)
            {
                var c = text[index];

                if (char.IsWhiteSpace(c))
                {
                    index++;
                    continue;
                }

                var start = index;

                if (c is '{')
                {
                    if (!TryReadReference(text, ref index, out var reference, out error))
                    {
                        errorPosition = start;
                        return null;
                    }

                    tokens.Add(new Token(TokenKind.Reference, reference!, start));
                    continue;
                }

                if (char.IsAsciiDigit(c) || (c is '.' && index + 1 < text.Length && char.IsAsciiDigit(text[index + 1])))
                {
                    while (index < text.Length && (char.IsAsciiDigit(text[index]) || text[index] is '.'))
                    {
                        index++;
                    }

                    var literal = text[start..index];
                    if (!decimal.TryParse(literal, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    {
                        error = $"'{literal}' is not a valid number.";
                        errorPosition = start;
                        return null;
                    }

                    tokens.Add(new Token(TokenKind.Number, literal, start));
                    continue;
                }

                if (char.IsAsciiLetter(c) || c is '_')
                {
                    while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] is '_'))
                    {
                        index++;
                    }

                    tokens.Add(new Token(TokenKind.Identifier, text[start..index], start));
                    continue;
                }

                var kind = c switch
                {
                    '+' => TokenKind.Plus,
                    '-' => TokenKind.Minus,
                    '*' => TokenKind.Star,
                    '/' => TokenKind.Slash,
                    '(' => TokenKind.OpenParen,
                    ')' => TokenKind.CloseParen,
                    ',' => TokenKind.Comma,
                    _ => (TokenKind?)null,
                };

                if (kind is null)
                {
                    error = $"Unexpected character '{c}'.";
                    errorPosition = start;
                    return null;
                }

                tokens.Add(new Token(kind.Value, c.ToString(), start));
                index++;
            }

            if (tokens.Count != 0)
            {
                return tokens;
            }

            error = "Expression is empty.";
            errorPosition = 0;
            return null;
        }

        private static bool TryReadReference(string text, ref int index, out string? reference, out string? error)
        {
            reference = null;
            error = null;

            if (index + 1 >= text.Length || text[index + 1] is not '{')
            {
                error = "Expected '{{' to start a reference.";
                return false;
            }

            var close = text.IndexOf("}}", index + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                error = "Reference is missing its closing '}}'.";
                return false;
            }

            var name = text[(index + 2)..close].Trim();
            if (name.Length == 0)
            {
                error = "Reference is empty.";
                return false;
            }

            // References are rendered as quoted identifiers by the compiler. Anything that is not a
            // plain identifier almost always means the author meant an expression, and rejecting it
            // here produces a far clearer message than a database syntax error would.
            foreach (var ch in name)
            {
                if (!char.IsAsciiLetterOrDigit(ch) && ch is not '_' && ch is not '$' && ch is not '#')
                {
                    error = $"Reference '{name}' contains an unsupported character '{ch}'.";
                    return false;
                }
            }

            reference = name;
            index = close + 2;
            return true;
        }
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int position;

        public bool AtEnd => this.position >= tokens.Count;

        public int CurrentPosition => this.AtEnd ? int.MaxValue : tokens[this.position].Position;

        public SqlExpression? ParseExpression(out string? error, out int errorPosition)
        {
            var left = this.ParseTerm(out error, out errorPosition);
            if (left is null)
            {
                return null;
            }

            while (!this.AtEnd && tokens[this.position].Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var op = tokens[this.position].Kind is TokenKind.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
                this.position++;

                var right = this.ParseTerm(out error, out errorPosition);
                if (right is null)
                {
                    return null;
                }

                left = new BinaryExpression(op, left, right);
            }

            return left;
        }

        private SqlExpression? ParseTerm(out string? error, out int errorPosition)
        {
            var left = this.ParseFactor(out error, out errorPosition);
            if (left is null)
            {
                return null;
            }

            while (!this.AtEnd && tokens[this.position].Kind is TokenKind.Star or TokenKind.Slash)
            {
                var op = tokens[this.position].Kind is TokenKind.Star ? BinaryOperator.Multiply : BinaryOperator.Divide;
                this.position++;

                var right = this.ParseFactor(out error, out errorPosition);
                if (right is null)
                {
                    return null;
                }

                left = new BinaryExpression(op, left, right);
            }

            return left;
        }

        private SqlExpression? ParseFactor(out string? error, out int errorPosition)
        {
            if (!this.AtEnd && tokens[this.position].Kind is TokenKind.Minus)
            {
                this.position++;
                var operand = this.ParseFactor(out error, out errorPosition);
                return operand is null ? null : new NegateExpression(operand);
            }

            return this.ParsePrimary(out error, out errorPosition);
        }

        private SqlExpression? ParsePrimary(out string? error, out int errorPosition)
        {
            error = null;
            errorPosition = 0;

            if (this.AtEnd)
            {
                error = "Unexpected end of expression.";
                errorPosition = tokens.Count == 0 ? 0 : tokens[^1].Position;
                return null;
            }

            var token = tokens[this.position];

            switch (token.Kind)
            {
                case TokenKind.Number:
                    this.position++;
                    return new NumberExpression(decimal.Parse(token.Text, NumberStyles.Number, CultureInfo.InvariantCulture));

                case TokenKind.Reference:
                    this.position++;
                    return new ReferenceExpression(token.Text);

                case TokenKind.OpenParen:
                    this.position++;
                    var grouped = this.ParseExpression(out error, out errorPosition);
                    if (grouped is null)
                    {
                        return null;
                    }

                    if (this.AtEnd || tokens[this.position].Kind is not TokenKind.CloseParen)
                    {
                        error = "Expected ')'.";
                        errorPosition = this.AtEnd ? token.Position : tokens[this.position].Position;
                        return null;
                    }

                    this.position++;
                    return grouped;

                case TokenKind.Identifier:
                    return this.ParseFunctionCall(token, out error, out errorPosition);

                default:
                    error = $"Unexpected token '{token.Text}'.";
                    errorPosition = token.Position;
                    return null;
            }
        }

        private FunctionExpression? ParseFunctionCall(Token name, out string? error, out int errorPosition)
        {
            error = null;
            errorPosition = name.Position;

            if (!SqlFunctions.IsAllowed(name.Text))
            {
                error = $"Function '{name.Text}' is not permitted. Allowed: {string.Join(", ", SqlFunctions.Names)}.";
                return null;
            }

            this.position++;

            if (this.AtEnd || tokens[this.position].Kind is not TokenKind.OpenParen)
            {
                error = $"Expected '(' after function '{name.Text}'.";
                errorPosition = this.AtEnd ? name.Position : tokens[this.position].Position;
                return null;
            }

            this.position++;
            var arguments = new List<SqlExpression>();

            if (!this.AtEnd && tokens[this.position].Kind is TokenKind.CloseParen)
            {
                this.position++;
            }
            else
            {
                while (true)
                {
                    var argument = this.ParseExpression(out error, out errorPosition);
                    if (argument is null)
                    {
                        return null;
                    }

                    arguments.Add(argument);

                    if (this.AtEnd)
                    {
                        error = $"Expected ')' to close function '{name.Text}'.";
                        errorPosition = name.Position;
                        return null;
                    }

                    if (tokens[this.position].Kind is TokenKind.Comma)
                    {
                        this.position++;
                        continue;
                    }

                    if (tokens[this.position].Kind is TokenKind.CloseParen)
                    {
                        this.position++;
                        break;
                    }

                    error = $"Expected ',' or ')' in arguments of '{name.Text}'.";
                    errorPosition = tokens[this.position].Position;
                    return null;
                }
            }

            var arityError = SqlFunctions.ValidateArgumentCount(name.Text, arguments.Count);
            if (arityError is null)
            {
                return new FunctionExpression(SqlFunctions.Canonicalise(name.Text), arguments);
            }

            error = arityError;
            errorPosition = name.Position;
            return null;
        }
    }
}
