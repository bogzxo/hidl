using System.Globalization;

using Horizon.HIDL.Lexing;
using Horizon.HIDL.Runtime;

namespace Horizon.HIDL.Parsing;

/// <summary>
/// Turns the tokens of a HIDL program into a syntax tree. Anything wrong with the program throws a
/// <see cref="ParseException"/> that says what was expected and where.
/// <para>
/// The language has let and const (and let { a, b } = thing), functions (named, anonymous and arrows) with return and
/// defaults for their parameters, if / else if / else, while, do while, for (x in something) and for (key, value in
/// something), break and continue, objects and lists (with spreads of other ones, keys worked out in brackets and trailing
/// commas), vec(x, y[, z[, w]]), the usual arithmetic and comparisons, in, &amp;&amp; and ||, and compound assignment.
/// What an if, an else or a loop runs is a block or one statement. Semicolons are optional. Comments go anywhere.
/// </para>
/// </summary>
public class Parser
{
    // The tokens, which one is next and which was taken last. The comments and directives are stepped over as it goes,
    // the parser never sees them. It was a queue fed through a LINQ filter, then a copy of the tokens without them, which
    // was most of what parsing Hex's texts made
    private Token[] tokens = [];
    private int position;
    private int last;

    private Token Peek() => tokens[position];

    private Token Consume()
    {
        Token token = tokens[position];
        last = position;
        position = Over(position + 1);
        return token;
    }

    /// <summary>Helper method for the first token from a place on that isn't a comment or a directive, the last one at the most.</summary>
    private int Over(int at)
    {
        int end = tokens.Length - 1;
        if (at > end)
            return end;

        while (at < end && tokens[at].Type is TokenType.Comment or TokenType.Directive)
            at++;
        return at;
    }

    /// <summary>Helper method to start reading some tokens from the first of them.</summary>
    private void Start(Token[] from)
    {
        tokens = from;
        position = Over(0);
        last = 0;
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
        // The lexer always ends with the end of the file, tokens from anywhere else are given one
        Start(inputTokens.Length > 0 && inputTokens[^1].Type == TokenType.EndOfFile
            ? inputTokens
            : [.. inputTokens, new Token(TokenType.EndOfFile, string.Empty)]);

        // Which lines have documentation, worked out only for a file that has any
        docs = HIDLDocs.Lines.Of(inputTokens);

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

            // An if or a loop on its own is a statement, a line after it that starts with a bracket or a minus isn't
            // taken to be called on it, indexed into or taken away from it. As a value (let a = if ...) it still is one
            TokenType.If => ParseIf(),
            TokenType.While => ParseWhile(),
            TokenType.Do => ParseDoWhile(),
            TokenType.Function when PeekAhead(1).Type == TokenType.Identifier => ParseFunctionDeclaration(),
            _ => ParseExpression(),
        };

        // A semicolon after a statement is welcome and never needed
        if (Next(TokenType.Semicolon))
            Consume();

        return statement;
    }

    private Token PeekAhead(int offset)
    {
        int at = position;
        for (int i = 0; i < offset; i++)
            at = Over(at + 1);
        return tokens[at];
    }

    // Where the documentation of the file is, null for a file without any
    private HIDLDocs.Lines? docs;

    /// <summary>What documents what is on a line, see HIDLDocs.</summary>
    private string? DocFor(in Token token) => docs?.For(token.Line);

    // The token that was taken last, where what was just parsed ends
    private Token Last => tokens[last];

    private IStatement ParseKeywordStatement(TokenType keyword, IStatement statement)
    {
        Consume(keyword);
        return statement;
    }

    private IStatement ParseDeleteStatement()
    {
        Token keyword = Consume(TokenType.Delete);

        // delete name takes a variable away, delete thing.key or thing[key] a key of an object or an item of a list
        if (Next(TokenType.Identifier) && PeekAhead(1).Type is not (TokenType.Dot or TokenType.OpenBrace or TokenType.QuestionDot))
            return new DeleteStatement(Consume().Value);

        if (!Next(TokenType.Identifier))
            throw Unexpected(Peek(), "the name of the variable, or the key, to delete");

        if (ParseCallMember() is not MemberExpression member)
            throw new ParseException($"delete takes a variable, a key of an object or an item of a list (line {keyword.Line}).", keyword.Line, keyword.Col, 6);

        return new DeleteStatement(string.Empty) { Member = member };
    }

    private IStatement ParseReturnStatement()
    {
        Consume(TokenType.Return);

        // A bare return hands back null. The value, if there is one, is on the same line as far as the grammar cares
        if (Next(TokenType.Semicolon) || Next(TokenType.CloseBracket) || Next(TokenType.EndOfFile) || Next(TokenType.Else))
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

        // for (key, value in object), for (index, item in list)
        string? key = null;
        if (Next(TokenType.Comma))
        {
            Consume();
            key = variable;
            variable = Consume(TokenType.Identifier, "the name of the second loop variable").Value;
        }

        Consume(TokenType.In);
        IExpression source = ParseExpression();
        Consume(TokenType.CloseParenthesis);

        return new ForInStatement(variable, source, ParseBody()) { Key = key };
    }

    private IStatement ParseVariableDeclaration()
    {
        bool isConst = Consume().Type == TokenType.Const;

        // let { a, b } = thing; and let [a, b] = list;
        if (Next(TokenType.OpenBracket) || Next(TokenType.OpenBrace))
            return ParseDestructuring(isConst, DocFor(Last));

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

    /// <summary>
    /// Helper method to read <c>let { a, b } = thing</c> or <c>let [a, b] = list</c>, the let or const taken already.
    /// Names only, a trailing comma is fine.
    /// </summary>
    private IExpression ParseDestructuring(bool isConst, string? doc)
    {
        bool list = Consume().Type == TokenType.OpenBrace;
        TokenType close = list ? TokenType.CloseBrace : TokenType.CloseBracket;
        List<string> names = [];
        List<SourceSpan> spans = [];

        while (!Next(close))
        {
            Token name = Consume(TokenType.Identifier, list ? "the name of an item" : "the name of a key");
            if (Lexer.IsKeyword(name.Value))
                throw new ParseException($"'{name.Value}' is a word of the language, a variable can't be called that (line {name.Line}).", name.Line, name.Col);

            names.Add(name.Value);
            spans.Add(SourceSpan.Of(name));

            if (Next(TokenType.Comma))
                Consume();
            else if (!Next(close))
                throw Unexpected(Peek(), list ? "',' or ']' between the names" : "',' or '}' between the names");
        }

        Token closing = Consume(close);
        if (names.Count == 0)
            throw new ParseException($"There are no names to declare in there (line {closing.Line}).", closing.Line, closing.Col);

        Consume(TokenType.Equals, "'=' and what to take the names out of");
        return new DestructuringDeclaration([.. names], [.. spans], ParseExpression(), list, isConst) { Doc = doc };
    }

    private IExpression ParseFunctionDeclaration()
    {
        Consume(TokenType.Function);

        if (Next(TokenType.Identifier))
        {
            Token name = Consume();
            var (parameters, defaults) = ParseParameters();
            return new FunctionDeclarationExpression(name.Value, parameters, ParseBlock()) { Span = SourceSpan.Of(name), Doc = DocFor(name), Defaults = defaults };
        }

        var (anonymousParameters, anonymousDefaults) = ParseParameters();
        return new AnonymousFunctionDeclarationExpression(anonymousParameters, ParseBlock()) { Defaults = anonymousDefaults };
    }

    /// <summary>
    /// Helper method to read <c>(a, b = 2, c)</c>, the names and what each is when it isn't handed over (null for every
    /// one of them when none has a default).
    /// </summary>
    private (string[] Parameters, IExpression?[]? Defaults) ParseParameters()
    {
        Consume(TokenType.OpenParenthesis);
        List<string> parameters = [];
        List<IExpression?>? defaults = null;

        while (!Next(TokenType.CloseParenthesis))
        {
            parameters.Add(Consume(TokenType.Identifier, "the name of a parameter").Value);

            if (Next(TokenType.Equals))
            {
                Consume();
                defaults ??= [];
                while (defaults.Count < parameters.Count - 1)
                    defaults.Add(null);
                defaults.Add(ParseConditional());
            }

            if (Next(TokenType.Comma))
                Consume();
            else
                break;
        }

        Consume(TokenType.CloseParenthesis);

        if (defaults is not null)
        {
            while (defaults.Count < parameters.Count)
                defaults.Add(null);
        }

        return ([.. parameters], defaults is null ? null : [.. defaults]);
    }

    /// <summary>
    /// Whether the brackets that open here are the parameters of an arrow, <c>(a, b) =&gt;</c>, which is the token after
    /// the one that closes them.
    /// </summary>
    private bool ArrowAhead()
    {
        int depth = 0;
        for (int at = position; at < tokens.Length; at++)
        {
            switch (tokens[at].Type)
            {
                case TokenType.OpenParenthesis:
                    depth++;
                    break;
                case TokenType.CloseParenthesis:
                    if (--depth == 0)
                        return tokens[Over(at + 1)].Type == TokenType.Arrow;
                    break;
                case TokenType.EndOfFile or TokenType.Semicolon or TokenType.OpenBracket:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Helper method to read an arrow, <c>x =&gt; x * 2</c>, <c>(a, b) =&gt; a + b</c> or <c>() =&gt; { ... }</c>. An expression
    /// after the arrow is what the function hands back, a block is a body like any function's.
    /// </summary>
    private IExpression ParseArrow()
    {
        string[] parameters;
        IExpression?[]? defaults = null;

        if (Next(TokenType.Identifier))
            parameters = [Consume().Value];
        else
            (parameters, defaults) = ParseParameters();

        Consume(TokenType.Arrow, "'=>'");

        IStatement[] body = Next(TokenType.OpenBracket) ? ParseBlock() : [new ReturnStatement(ParseAssignment())];
        return new AnonymousFunctionDeclarationExpression(parameters, body) { Defaults = defaults, Arrow = true };
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

    /// <summary>
    /// Helper method to read what an if, an else, a loop or a do runs, a block in braces or one statement without them,
    /// <c>if (done) return;</c>.
    /// </summary>
    private IStatement[] ParseBody()
    {
        if (Next(TokenType.OpenBracket))
            return ParseBlock();

        if (Next(TokenType.EndOfFile) || Next(TokenType.Semicolon) || Next(TokenType.CloseBracket))
            throw Unexpected(Peek(), "'{' or a statement");

        return ParseStatement() is { } statement ? [statement] : [];
    }

    private IExpression ParseIf()
    {
        Consume(TokenType.If);
        Consume(TokenType.OpenParenthesis);
        IExpression condition = ParseExpression();
        Consume(TokenType.CloseParenthesis);

        IStatement[] body = ParseBody();
        IStatement[]? otherwise = null;

        if (Next(TokenType.Else))
        {
            Consume();

            // else if is an if inside of the else
            otherwise = Next(TokenType.If) ? [ParseIf()] : ParseBody();
        }

        return new IfDeclarationExpression(condition, body, otherwise);
    }

    private IExpression ParseWhile()
    {
        Consume(TokenType.While);
        Consume(TokenType.OpenParenthesis);
        IExpression condition = ParseExpression();
        Consume(TokenType.CloseParenthesis);

        return new WhileDeclarationExpression(condition, ParseBody());
    }

    private IExpression ParseDoWhile()
    {
        Consume(TokenType.Do);
        IStatement[] body = ParseBody();

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

    // a < b and the rest, and a in b, whether b has a (a key of an object, an item of a list, a piece of a text)
    private IExpression ParseComparison()
    {
        IExpression left = ParseAdditive();
        while (NextOperator("<", ">", "<=", ">=") || Next(TokenType.In))
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

                // a?.[key] and f?.(x), the same for an index and a call
                if (optional && Next(TokenType.OpenBrace))
                {
                    Token open = Consume();
                    IExpression key = ParseExpression();
                    Consume(TokenType.CloseBrace);
                    expression = new MemberExpression(expression, key, true) { Span = SourceSpan.Of(open), Optional = true };
                    continue;
                }

                if (optional && Next(TokenType.OpenParenthesis))
                {
                    SourceSpan call = SourceSpan.Of(Peek());
                    expression = new CallExpression(ParseArguments(), expression) { Span = call, Optional = true };
                    continue;
                }

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
            // x => x * 2
            case TokenType.Identifier when PeekAhead(1).Type == TokenType.Arrow:
                return ParseArrow();

            case TokenType.Identifier:
            {
                Token name = Consume();
                return new IdentifierExpression(name.Value) { Span = SourceSpan.Of(name) };
            }

            // A literal makes its value here, once, and hands that one out every time it is evaluated. The same number
            // written again is the same value, a sprite sheet writes 1.0 a thousand times
            case TokenType.Number:
                Consume();
                return new NumericLiteralExpression(token.Number) { Constant = NumberConstant(token.Number) };

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

            // (a, b) => a + b
            case TokenType.OpenParenthesis when ArrowAhead():
                return ParseArrow();

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

    // The numbers the parse has made a value of, by what they are, and the small whole ones made once for every parse
    private Dictionary<float, IRuntimeValue>? numbers;
    private static readonly IRuntimeValue[] SmallNumbers = MakeSmallNumbers();

    private static IRuntimeValue[] MakeSmallNumbers()
    {
        var made = new IRuntimeValue[256];
        for (int i = 0; i < made.Length; i++)
            made[i] = new NumberValue(i);
        return made;
    }

    /// <summary>Helper method to hand out one value for every number, values never change so they can be shared.</summary>
    private IRuntimeValue NumberConstant(float number)
    {
        if (number >= 0.0f && number < SmallNumbers.Length && number == MathF.Floor(number) && !float.IsNegative(number))
            return SmallNumbers[(int)number];

        numbers ??= [];
        if (!numbers.TryGetValue(number, out IRuntimeValue? value))
            numbers[number] = value = new NumberValue(number);
        return value;
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
            else if (Next(TokenType.OpenBrace))
            {
                // [name]: value, the key worked out as the object is made
                Token open = Consume();
                IExpression key = ParseExpression();
                Token close = Consume(TokenType.CloseBrace, "']' after the key");
                Consume(TokenType.Colon, "':' after the key");
                Token first = Peek();
                IExpression value = ParseExpression();
                properties.Add(new PropertyExpression(null, value) { ComputedKey = key, Span = SourceSpan.Between(open, close), ValueSpan = SourceSpan.Between(first, Last), Doc = DocFor(open) });
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
            var parser = new Parser();
            parser.Start(inside);
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
