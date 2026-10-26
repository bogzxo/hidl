using Horizon.HIDL.Lexing;

namespace Horizon.HIDL.Parsing;

/// <summary>
/// What a run of tokens declares, worked out from the tokens alone for an editor that colours, outlines or looks up
/// names without parsing (and so copes with a file halfway through being typed). The names a let or a const makes,
/// one or a few of them (<c>let { a, b } = thing</c>), and the parameters of an arrow (<c>x =&gt;</c>, <c>(a, b) =&gt;</c>).
/// </summary>
public static class HIDLDeclarations
{
    /// <summary>
    /// The names a let or a const at a place declares, the one after it, or every name of the braces or brackets after it.
    /// Nothing for a token that isn't a let or a const, or one with nothing that can be a name after it.
    /// </summary>
    public static List<Token> Declared(Token[] tokens, int at)
    {
        var names = new List<Token>(1);
        if (at < 0 || at >= tokens.Length || tokens[at].Type is not (TokenType.Let or TokenType.Const))
            return names;

        int next = Skip(tokens, at + 1);
        if (next >= tokens.Length)
            return names;

        if (tokens[next].Type == TokenType.Identifier)
        {
            names.Add(tokens[next]);
            return names;
        }

        // let { a, b } and let [a, b], every name up to the bracket that closes them
        if (tokens[next].Type is not (TokenType.OpenBracket or TokenType.OpenBrace))
            return names;

        TokenType close = tokens[next].Type == TokenType.OpenBracket ? TokenType.CloseBracket : TokenType.CloseBrace;
        for (int i = next + 1; i < tokens.Length; i++)
        {
            Token token = tokens[i];
            if (token.Type == close)
                break;
            if (token.Type == TokenType.Identifier)
                names.Add(token);
            else if (token.Type is not (TokenType.Comma or TokenType.Comment))
                break;
        }

        return names;
    }

    /// <summary>Whether the name at a place is the one parameter of an arrow, <c>x =&gt; ...</c>.</summary>
    public static bool IsArrowParameter(Token[] tokens, int at) =>
        at >= 0 && at < tokens.Length && tokens[at].Type == TokenType.Identifier && Skip(tokens, at + 1) is var next && next < tokens.Length && tokens[next].Type == TokenType.Arrow;

    /// <summary>
    /// Whether the bracket at a place opens the parameters of an arrow, <c>(a, b) =&gt;</c>, which is whether the token after
    /// the one that closes it is an arrow.
    /// </summary>
    public static bool OpensArrowParameters(Token[] tokens, int at)
    {
        if (at < 0 || at >= tokens.Length || tokens[at].Type != TokenType.OpenParenthesis)
            return false;

        int depth = 0;
        for (int i = at; i < tokens.Length; i++)
        {
            switch (tokens[i].Type)
            {
                case TokenType.OpenParenthesis:
                    depth++;
                    break;
                case TokenType.CloseParenthesis when --depth == 0:
                    int next = Skip(tokens, i + 1);
                    return next < tokens.Length && tokens[next].Type == TokenType.Arrow;
                case TokenType.Semicolon or TokenType.OpenBracket or TokenType.CloseBracket or TokenType.EndOfFile:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the bracket at a place opens the parameters of a function, after <c>func</c>, <c>func name</c>, or
    /// before an arrow.
    /// </summary>
    public static bool OpensParameters(Token[] tokens, int at)
    {
        if (at < 0 || at >= tokens.Length || tokens[at].Type != TokenType.OpenParenthesis)
            return false;

        int before = Back(tokens, at - 1);
        if (before >= 0 && tokens[before].Type == TokenType.Function)
            return true;
        if (before >= 0 && tokens[before].Type == TokenType.Identifier && Back(tokens, before - 1) is var func && func >= 0 && tokens[func].Type == TokenType.Function)
            return true;

        return OpensArrowParameters(tokens, at);
    }

    /// <summary>
    /// The names of the parameters from the bracket that opens them, a default after a name skipped over to the comma
    /// that ends it (<c>(a, size = vec(1, 2), b)</c> is a, size and b). Nothing for a bracket that doesn't open parameters.
    /// </summary>
    public static List<Token> Parameters(Token[] tokens, int open)
    {
        var names = new List<Token>(2);
        if (!OpensParameters(tokens, open))
            return names;

        int depth = 0;
        bool expectingName = true;
        for (int i = open + 1; i < tokens.Length; i++)
        {
            Token token = tokens[i];
            switch (token.Type)
            {
                case TokenType.Comment:
                    continue;
                case TokenType.CloseParenthesis when depth == 0:
                case TokenType.EndOfFile:
                    return names;
                case TokenType.Semicolon when depth == 0:
                    return names;
                case TokenType.OpenParenthesis or TokenType.OpenBracket or TokenType.OpenBrace:
                    depth++;
                    break;
                case TokenType.CloseParenthesis or TokenType.CloseBracket or TokenType.CloseBrace:
                    depth--;
                    break;
                case TokenType.Comma when depth == 0:
                    expectingName = true;
                    continue;
            }

            if (expectingName && depth == 0)
            {
                if (token.Type == TokenType.Identifier)
                    names.Add(token);
                expectingName = false;
            }
        }

        return names;
    }

    /// <summary>
    /// The names a for at a place goes through with, <c>for (item in list)</c>, <c>for (let item in list)</c> or the two
    /// of <c>for (key, value in object)</c>. Nothing for a token that isn't a for.
    /// </summary>
    public static List<Token> LoopNames(Token[] tokens, int at)
    {
        var names = new List<Token>(2);
        if (at < 0 || at >= tokens.Length || tokens[at].Type != TokenType.For)
            return names;

        int i = Skip(tokens, at + 1);
        if (i >= tokens.Length || tokens[i].Type != TokenType.OpenParenthesis)
            return names;

        i = Skip(tokens, i + 1);
        if (i < tokens.Length && tokens[i].Type is TokenType.Let or TokenType.Const)
            i = Skip(tokens, i + 1);

        for (; i < tokens.Length; i = Skip(tokens, i + 1))
        {
            if (tokens[i].Type == TokenType.Identifier)
                names.Add(tokens[i]);
            else if (tokens[i].Type == TokenType.In)
                return names;
            else if (tokens[i].Type != TokenType.Comma)
                break;
        }

        // Without its in it's not a loop that can be made out yet
        return [];
    }

    // The first token from a place back that isn't a comment, -1 for none
    private static int Back(Token[] tokens, int at)
    {
        while (at >= 0 && tokens[at].Type == TokenType.Comment)
            at--;
        return at;
    }

    // The first token from a place on that isn't a comment
    private static int Skip(Token[] tokens, int at)
    {
        while (at < tokens.Length && tokens[at].Type == TokenType.Comment)
            at++;
        return at;
    }
}
