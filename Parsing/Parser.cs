using System.Globalization;

using Horizon.HIDL.Lexing;
using Horizon.HIDL.Runtime;

namespace Horizon.HIDL.Parsing;

/// <summary>
/// Turns the tokens of a HIDL program into a syntax tree. Anything wrong with the program throws a
/// <see cref="ParseException"/> that says what was expected and where.
/// <para>
/// The language has let and const, functions (named and anonymous) with return, if / else if / else, while,
/// do while, for (x in something), break and continue, objects and lists (with spreads of other ones and trailing
/// commas), vec(x, y[, z[, w]]), the usual arithmetic and comparisons, &amp;&amp; and ||, and compound assignment.
/// Semicolons are optional. Comments go anywhere.
/// </para>
/// </summary>
public class Parser
{
    // The tokens without the comments and directives, and which one is next. It was a queue fed through a LINQ filter,
    // and looking ahead walked the queue
    private Token[] tokens = [];
    private int position;

    private Token Peek() => tokens[position];

    private Token Consume()
    {
        Token token = tokens[position];
        if (position < tokens.Length - 1)
            position++;
        return token;
    }

    private Token Consume(TokenType expected, string? what = null)
    {
        Token token = Peek();
        if (token.Type != expected)
            throw Unexpected(token, what ?? Describe(expected));

        return Consume();
    }

    /// <summary>
    /// Whether a token is a word, a name or one of the language's own, which is what a key of an object or a member after
    /// a dot may be. <c>{ for: 1 }</c>, <c>move.in</c> and <c>{ null: 0 }</c> are all fine.
    /// </summary>
    private static bool IsWord(in Token token) => token.Type is TokenType.Identifier or TokenType.Let or TokenType.Const or TokenType.Function
        or TokenType.If or TokenType.Else or TokenType.While or TokenType.Do or TokenType.For or TokenType.In or TokenType.Return
        or TokenType.Break or TokenType.Continue or TokenType.Delete or TokenType.Vector or TokenType.Boolean or TokenType.Null;

    private bool Next(TokenType type) => Peek().Type == type;

    private bool NextOperator(params string[] operators) =>
        Peek().Type == TokenType.BinaryOperation && Array.IndexOf(operators, Peek().Value) >= 0;

    private static ParseException Unexpected(Token token, string expected)
    {
        string got = token.Type == TokenType.EndOfFile ? "the end of the file" : $"'{token.Value}'";
        return new ParseException($"Expected {expected} but found {got} at line {token.Line}, column {token.Col}.", token.Line, token.Col, Math.Max(1, token.Value.Length));
    }

    private static string Describe(TokenType type) => type switch
    {
        TokenType.Identifier => "a name",
        TokenType.OpenParenthesis => "'('",
        TokenType.CloseParenthesis => "')'",
        TokenType.OpenBracket => "'{'",
        TokenType.CloseBracket => "'}'",
        TokenType.OpenBrace => "'['",
        TokenType.CloseBrace => "']'",
        TokenType.Colon => "':'",
        TokenType.Equals => "'='",
        TokenType.In => "'in'",
        TokenType.While => "'while'",
        TokenType.Semicolon => "';'",
        _ => type.ToString()
    };

    public ProgramStatement ProduceSyntaxTree(in Token[] inputTokens) => ProduceSyntaxTree(inputTokens, null);

    // The most mistakes a parse that goes on after them collects, past that the file is something else entirely
    private const int MOST_ERRORS = 50;

    /// <summary>
    /// Parses, and with somewhere to put them, goes on after a mistake instead of stopping at it. What was wrong is
    /// collected and the parse picks up again at the next statement (after a ; or at a let, const or func outside of
    /// any brackets), so an editor gets every mistake in a file and the tree of everything that did parse. Without, the
    /// first mistake is thrown as it always was.
    /// </summary>
    public ProgramStatement ProduceSyntaxTree(Token[] inputTokens, List<ParseException>? errors)
    {
        // Comments are for people, the parser never sees them
        int kept = 0;
        foreach (Token token in inputTokens)
        {
            if (token.Type is not (TokenType.Comment or TokenType.Directive))
                kept++;
        }

        tokens = new Token[Math.Max(1, kept)];
        kept = 0;
        foreach (Token token in inputTokens)
        {
            if (token.Type is not (TokenType.Comment or TokenType.Directive))
                tokens[kept++] = token;
        }

        if (kept == 0 || tokens[kept - 1].Type != TokenType.EndOfFile)
            tokens[^1] = new Token(TokenType.EndOfFile, string.Empty);

        // Which lines have documentation, worked out only for a file that has any
        docs = HIDLDocs.Lines.Of(inputTokens);

        position = 0;
        List<IStatement> statements = [];

        while (!Next(TokenType.EndOfFile))
        {
            if (errors is null)
            {
                if (ParseStatement() is { } statement)
                    statements.Add(statement);
                continue;
            }

            int from = position;
            try
            {
                if (ParseStatement() is { } statement)
                    statements.Add(statement);
            }
            catch (ParseException e)
            {
                errors.Add(e);
                if (errors.Count >= MOST_ERRORS)
                    break;

                Recover(from);
            }
        }

        return new ProgramStatement { Body = statements };
    }

    /// <summary>
    /// Helper method to skip to where the next statement starts after a mistake, past a ; or up to a let, const or func
    /// that isn't inside of any brackets. Always at least a token on from where the statement started, or it would go round.
    /// </summary>
    private void Recover(int from)
    {
        if (position <= from)
            Consume();

        int depth = 0;
        while (!Next(TokenType.EndOfFile))
        {
            Token token = Peek();
            if (depth == 0 && (token.Type is TokenType.Let or TokenType.Const || (token.Type == TokenType.Function && PeekAhead(1).Type == TokenType.Identifier)))
                return;

            Consume();
            switch (token.Type)
            {
                case TokenType.OpenBracket or TokenType.OpenBrace or TokenType.OpenParenthesis:
                    depth++;
                    break;
                case TokenType.CloseBracket or TokenType.CloseBrace or TokenType.CloseParenthesis:
                    depth = Math.Max(0, depth - 1);
                    break;
                case TokenType.Semicolon when depth == 0:
                    return;
            }
        }
    }

    /* Statements */

    private IStatement? ParseStatement()
    {
        if (Next(TokenType.Semicolon))
        {
            Consume();
            return null;
        }

        IStatement statement = Peek().Type switch
        {
            TokenType.Let or TokenType.Const => ParseVariableDeclaration(),
            TokenType.Delete => ParseDeleteStatement(),
            TokenType.Return => ParseReturnStatement(),
            TokenType.Break => ParseKeywordStatement(TokenType.Break, new BreakStatement()),
            TokenType.Continue => ParseKeywordStatement(TokenType.Continue, new ContinueStatement()),
            TokenType.For => ParseForStatement(),
            TokenType.Function when PeekAhead(1).Type == TokenType.Identifier => ParseFunctionDeclaration(),
            _ => ParseExpression(),
        };

        // A semicolon after a statement is welcome and never needed
        if (Next(TokenType.Semicolon))
            Consume();

        return statement;
    }

    private Token PeekAhead(int offset) => tokens[Math.Min(position + offset, tokens.Length - 1)];

    // Where the documentation of the file is, null for a file without any
    private HIDLDocs.Lines? docs;

    /// <summary>What documents what is on a line, see HIDLDocs.</summary>
    private string? DocFor(in Token token) => docs?.For(token.Line);

    // The token that was taken last, where what was just parsed ends
    private Token Last => tokens[Math.Max(0, position - 1)];

    private IStatement ParseKeywordStatement(TokenType keyword, IStatement statement)
    {
        Consume(keyword);
        return statement;
    }

    private IStatement ParseDeleteStatement()
    {
        Consume(TokenType.Delete);
        string identifier = Consume(TokenType.Identifier, "the name of the variable to delete").Value;
        return new DeleteStatement(identifier);
    }

    private IStatement ParseReturnStatement()
    {
        Consume(TokenType.Return);

        // A bare return hands back null. The value, if there is one, is on the same line as far as the grammar cares
        if (Next(TokenType.Semicolon) || Next(TokenType.CloseBracket) || Next(TokenType.EndOfFile))
            return new ReturnStatement(null);

        return new ReturnStatement(ParseExpression());
    }

    private IStatement ParseForStatement()
    {
        Consume(TokenType.For);
        Consume(TokenType.OpenParenthesis);

        // for (let x in list) and for (x in list) both do
        if (Next(TokenType.Let) || Next(TokenType.Const))
            Consume();

        string variable = Consume(TokenType.Identifier, "the name of the loop variable").Value;
        Consume(TokenType.In);
        IExpression source = ParseExpression();
        Consume(TokenType.CloseParenthesis);

        return new ForInStatement(variable, source, ParseBlock());
    }

    private IStatement ParseVariableDeclaration()
    {
        bool isConst = Consume().Type == TokenType.Const;
        Token name = Consume(TokenType.Identifier, "the name of the variable");

        if (Lexer.IsKeyword(name.Value))
            throw new ParseException($"'{name.Value}' is a word of the language, a variable can't be called that (line {name.Line}).", name.Line, name.Col);

        if (!Next(TokenType.Equals))
        {
            if (isConst)
                throw new ParseException($"The constant '{name.Value}' needs a value (line {name.Line}).", name.Line, name.Col);

            return new VariableDeclarationExpression(name.Value, null, false) { Span = SourceSpan.Of(name), Doc = DocFor(name) };
        }

        Consume(TokenType.Equals);
        Token first = Peek();
        IExpression value = ParseExpression();
        return new VariableDeclarationExpression(name.Value, value, isConst) { Span = SourceSpan.Of(name), ValueSpan = SourceSpan.Between(first, Last), Doc = DocFor(name) };
    }

    private IExpression ParseFunctionDeclaration()
    {
        Consume(TokenType.Function);

        if (Next(TokenType.Identifier))
        {
            Token name = Consume();
            var (parameters, body) = ParseParametersAndBody();
            return new FunctionDeclarationExpression(name.Value, parameters, body) { Span = SourceSpan.Of(name), Doc = DocFor(name) };
        }

        var (anonymousParameters, anonymousBody) = ParseParametersAndBody();
        return new AnonymousFunctionDeclarationExpression(anonymousParameters, anonymousBody);
    }

    private (string[] Parameters, IStatement[] Body) ParseParametersAndBody()
    {
        Consume(TokenType.OpenParenthesis);
        List<string> parameters = [];

        while (!Next(TokenType.CloseParenthesis))
        {
            parameters.Add(Consume(TokenType.Identifier, "the name of a parameter").Value);
            if (Next(TokenType.Comma))
                Consume();
            else
                break;
        }

        Consume(TokenType.CloseParenthesis);
        return ([.. parameters], ParseBlock());
    }

    /// <summary>
    /// Helper method to read <c>{ statements }</c>.
    /// </summary>
    private IStatement[] ParseBlock()
    {
        Consume(TokenType.OpenBracket, "'{'");
        List<IStatement> body = [];

        while (!Next(TokenType.CloseBracket))
        {
            if (Next(TokenType.EndOfFile))
                throw Unexpected(Peek(), "'}' to close the block");

            if (ParseStatement() is { } statement)
                body.Add(statement);
        }

        Consume(TokenType.CloseBracket);
        return [.. body];
    }

    private IExpression ParseIf()
    {
        Consume(TokenType.If);
        Consume(TokenType.OpenParenthesis);
        IExpression condition = ParseExpression();
        Consume(TokenType.CloseParenthesis);

        IStatement[] body = ParseBlock();
        IStatement[]? otherwise = null;

        if (Next(TokenType.Else))
        {
            Consume();

            // else if is an if inside of the else
            otherwise = Next(TokenType.If) ? [ParseIf()] : ParseBlock();
        }

        return new IfDeclarationExpression(condition, body, otherwise);
    }

    private IExpression ParseWhile()
    {
        Consume(TokenType.While);
        Consume(TokenType.OpenParenthesis);
        IExpression condition = ParseExpression();
        Consume(TokenType.CloseParenthesis);

        return new WhileDeclarationExpression(condition, ParseBlock());
    }

    private IExpression ParseDoWhile()
    {
        Consume(TokenType.Do);
        IStatement[] body = ParseBlock();

        Consume(TokenType.While);
        Consume(TokenType.OpenParenthesis);
        IExpression condition = ParseExpression();
        Consume(TokenType.CloseParenthesis);

        return new DoWhileDeclarationExpression(condition, body);
    }

    /* Expressions, from the loosest binding to the tightest */

    private IExpression ParseExpression() => ParseAssignment();

    private IExpression ParseAssignment()
    {
        IExpression left = ParseConditional();

        if (Next(TokenType.Equals))
        {
            Consume();
            return new AssignmentExpression(left, ParseAssignment());
        }

        if (Next(TokenType.CompoundEquals))
        {
            string op = Consume().Value;
            return new AssignmentExpression(left, ParseAssignment(), op);
        }

        return left;
    }

    // a ? b : c, with either side allowed to be another one
    private IExpression ParseConditional()
    {
        IExpression condition = ParseNullish();
        if (!Next(TokenType.Question))
            return condition;

        Consume();
        IExpression then = ParseAssignment();
        Consume(TokenType.Colon, "':' between the two sides of the ?");
        IExpression otherwise = ParseAssignment();

        return new ConditionalExpression(condition, then, otherwise);
    }

    // a ?? b, a unless it is null, b if it is. Looser than || so a.b ?? c || d is a.b ?? (c || d)
    private IExpression ParseNullish()
    {
        IExpression left = ParseOr();
        while (NextOperator("??"))
        {
            Consume();
            left = new BinaryExpression(left, ParseOr(), "??");
        }

        return left;
    }

    private IExpression ParseOr()
    {
        IExpression left = ParseAnd();
        while (NextOperator("||", "|"))
        {
            string op = Consume().Value;
            left = new BinaryExpression(left, ParseAnd(), op);
        }

        return left;
    }

    private IExpression ParseAnd()
    {
        IExpression left = ParseEquality();
        while (NextOperator("&&", "&"))
        {
            string op = Consume().Value;
            left = new BinaryExpression(left, ParseEquality(), op);
        }

        return left;
    }

    private IExpression ParseEquality()
    {
        IExpression left = ParseComparison();
        while (Next(TokenType.Equality) || Next(TokenType.NotEquality))
        {
            string op = Consume().Value;
            left = new BinaryExpression(left, ParseComparison(), op);
        }

        return left;
    }

    private IExpression ParseComparison()
    {
        IExpression left = ParseAdditive();
        while (NextOperator("<", ">", "<=", ">="))
        {
            string op = Consume().Value;
            left = new BinaryExpression(left, ParseAdditive(), op);
        }

        return left;
    }

    private IExpression ParseAdditive()
    {
        IExpression left = ParseMultiplicative();
        while (NextOperator("+", "-"))
        {
            string op = Consume().Value;
            left = new BinaryExpression(left, ParseMultiplicative(), op);
        }

        return left;
    }

    private IExpression ParseMultiplicative()
    {
        IExpression left = ParseUnary();
        while (NextOperator("*", "/", "%"))
        {
            string op = Consume().Value;
            left = new BinaryExpression(left, ParseUnary(), op);
        }

        return left;
    }

    private IExpression ParseUnary()
    {
        if (Next(TokenType.Exclamation))
        {
            Consume();
            return new BinaryExpression(ParseUnary(), null, "!");
        }

        if (NextOperator("-", "+"))
        {
            string op = Consume().Value;
            return new BinaryExpression(ParseUnary(), null, op);
        }

        return ParseCallMember();
    }

    /// <summary>
    /// Helper method for everything that hangs off a value: <c>a.b</c>, <c>a[b]</c> and <c>a(b)</c>, in any order and as many as there are.
    /// </summary>
    private IExpression ParseCallMember()
    {
        IExpression expression = ParsePrimary();

        while (true)
        {
            if (Next(TokenType.Dot) || Next(TokenType.QuestionDot))
            {
                // a?.b is null when a is, a.b is an error then
                bool optional = Consume().Type == TokenType.QuestionDot;
                Token name = Peek();
                if (!IsWord(name))
                    throw Unexpected(name, "a name after the dot");
                Consume();

                expression = new MemberExpression(expression, new IdentifierExpression(name.Value) { Span = SourceSpan.Of(name) }, false)
                {
                    Span = SourceSpan.Of(name),
                    Optional = optional
                };
            }
            else if (Next(TokenType.OpenBrace))
            {
                Token open = Consume();
                IExpression index = ParseExpression();
                Consume(TokenType.CloseBrace);
                expression = new MemberExpression(expression, index, true) { Span = SourceSpan.Of(open) };
            }
            else if (Next(TokenType.OpenParenthesis))
            {
                SourceSpan at = SourceSpan.Of(Peek());
                expression = new CallExpression(ParseArguments(), expression) { Span = at };
            }
            else
            {
                return expression;
            }
        }
    }

    private IExpression[] ParseArguments()
    {
        Consume(TokenType.OpenParenthesis);
        List<IExpression> arguments = [];

        while (!Next(TokenType.CloseParenthesis))
        {
            arguments.Add(ParseExpression());

            if (Next(TokenType.Comma))
                Consume();
            else if (!Next(TokenType.CloseParenthesis))
                throw Unexpected(Peek(), "',' or ')' in the arguments");
        }

        Consume(TokenType.CloseParenthesis);
        return [.. arguments];
    }

    private IExpression ParsePrimary()
    {
        Token token = Peek();

        switch (token.Type)
        {
            case TokenType.Identifier:
            {
                Token name = Consume();
                return new IdentifierExpression(name.Value) { Span = SourceSpan.Of(name) };
            }

            // A literal makes its value here, once, and hands that one out every time it is evaluated
            case TokenType.Number:
                Consume();
                return new NumericLiteralExpression(token.Number) { Constant = new NumberValue(token.Number) };

            case TokenType.TextLiteral:
                Consume();
                return new StringLiteralExpression(token.Value) { Constant = new StringValue(token.Value) };

            case TokenType.Boolean:
                Consume();
                return new BooleanLiteralExpression(token.Value == "true");

            case TokenType.Template:
                return ParseTemplate(Consume());

            case TokenType.Function:
                return ParseFunctionDeclaration();

            case TokenType.Vector:
                return ParseVector();

            case TokenType.If:
                return ParseIf();

            case TokenType.While:
                return ParseWhile();

            case TokenType.Do:
                return ParseDoWhile();

            case TokenType.OpenBracket:
                return ParseObjectLiteral();

            case TokenType.OpenBrace:
                return ParseListLiteral();

            case TokenType.OpenParenthesis:
            {
                Consume();
                IExpression inner = ParseExpression();
                Consume(TokenType.CloseParenthesis);
                return inner;
            }

            case TokenType.Null:
                Consume();
                return new NullLiteral();

            default:
                throw Unexpected(token, "a value");
        }
    }

    private IExpression ParseVector()
    {
        Token keyword = Consume(TokenType.Vector);
        IExpression[] components = ParseArguments();

        if (components.Length is < 1 or > 4)
            throw new ParseException($"vec takes 1 to 4 numbers, not {components.Length} (line {keyword.Line}).", keyword.Line, keyword.Col, 3);

        return new VectorDeclarationExpression(components);
    }

    /// <summary>
    /// Helper method to read <c>{ key: value, "other key": value, shorthand, ...spread, }</c>.
    /// </summary>
    private IExpression ParseObjectLiteral()
    {
        Consume(TokenType.OpenBracket);
        List<PropertyExpression> properties = [];

        while (!Next(TokenType.CloseBracket))
        {
            if (Next(TokenType.EndOfFile))
                throw Unexpected(Peek(), "'}' to close the object");

            if (Next(TokenType.Spread))
            {
                Consume();
                properties.Add(new PropertyExpression(null, new SpreadExpression(ParseExpression())));
            }
            else
            {
                Token key = Peek();
                if (key.Type != TokenType.TextLiteral && !IsWord(key))
                    throw Unexpected(key, "the name of a property");
                Consume();

                if (Next(TokenType.Colon))
                {
                    Consume();
                    Token first = Peek();
                    IExpression value = ParseExpression();
                    properties.Add(new PropertyExpression(key.Value, value) { Span = SourceSpan.Of(key), ValueSpan = SourceSpan.Between(first, Last), Doc = DocFor(key) });
                }
                else
                {
                    // { name } is { name: name }
                    properties.Add(new PropertyExpression(key.Value, null) { Span = SourceSpan.Of(key), Doc = DocFor(key) });
                }
            }

            if (Next(TokenType.Comma))
                Consume();
            else if (!Next(TokenType.CloseBracket))
                throw Unexpected(Peek(), "',' or '}' after the property");
        }

        Consume(TokenType.CloseBracket);
        return new ObjectLiteralExpression(properties);
    }

    /// <summary>
    /// Helper method to take a template apart, <c>`{name} has {health} left`</c>, into the texts between the braces and
    /// the expressions in them. An expression is lexed where it is in the file, so an error in one says where. A brace
    /// that is only a brace is written <c>\{</c>.
    /// </summary>
    private static IExpression ParseTemplate(Token template)
    {
        string raw = template.Value;
        List<string> parts = [];
        List<IExpression> holes = [];

        // Where the text after the backtick starts, counted the way the lexer does
        int line = template.Line, col = template.Col + 1;
        int from = 0, i = 0;

        void Over(int to)
        {
            for (; i < to; i++)
            {
                if (raw[i] == '\n') { line++; col = 1; }
                else col++;
            }
        }

        while (i < raw.Length)
        {
            if (raw[i] == '\\')
            {
                Over(Math.Min(raw.Length, i + 2));
                continue;
            }

            if (raw[i] != '{')
            {
                Over(i + 1);
                continue;
            }

            parts.Add(Lexer.Unescape(raw, from, i));
            Over(i + 1);

            int start = i, depth = 0, startLine = line, startCol = col;
            while (i < raw.Length && (raw[i] != '}' || depth > 0))
            {
                if (raw[i] == '{') depth++;
                else if (raw[i] == '}') depth--;
                Over(i + 1);
            }

            if (i >= raw.Length)
                throw new ParseException($"A {{ in the template at line {template.Line} is never closed.", template.Line, template.Col);

            Token[] inside = Lexer.Tokenize(raw, lenient: false, start, i, startLine, startCol);
            var parser = new Parser { tokens = inside, position = 0 };
            holes.Add(parser.ParseExpression());
            if (!parser.Next(TokenType.EndOfFile))
                throw Unexpected(parser.Peek(), "'}' to end the expression in the template");

            Over(i + 1);
            from = i;
        }

        parts.Add(Lexer.Unescape(raw, from, raw.Length));
        return new TemplateExpression([.. parts], [.. holes]) { Span = SourceSpan.Of(template) };
    }

    /// <summary>
    /// Helper method to read <c>[a, b, ...others, ]</c>.
    /// </summary>
    private IExpression ParseListLiteral()
    {
        Consume(TokenType.OpenBrace);
        List<IExpression> items = [];
        List<SourceSpan> spans = [], values = [];

        while (!Next(TokenType.CloseBrace))
        {
            if (Next(TokenType.EndOfFile))
                throw Unexpected(Peek(), "']' to close the list");

            Token first = Peek();
            spans.Add(SourceSpan.Of(first));

            if (Next(TokenType.Spread))
            {
                Consume();
                items.Add(new SpreadExpression(ParseExpression()));
            }
            else
            {
                items.Add(ParseExpression());
            }

            values.Add(SourceSpan.Between(first, Last));

            if (Next(TokenType.Comma))
                Consume();
            else if (!Next(TokenType.CloseBrace))
                throw Unexpected(Peek(), "',' or ']' after the item");
        }

        Consume(TokenType.CloseBrace);
        return new ListLiteralExpression([.. items]) { ItemSpans = [.. spans], ItemValueSpans = [.. values] };
    }
}
