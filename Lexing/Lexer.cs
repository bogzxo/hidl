using System.Globalization;
using System.Text;

using Horizon.HIDL.Parsing;

namespace Horizon.HIDL.Lexing;

/// <summary>
/// Cuts HIDL source into tokens. Every token knows the line and column it started on and where it is in the text, so
/// whoever trips over one can say where. Comments come out as tokens of their own (the editor colours them in) with no
/// value, the text of one is the source at its index and length, and the parser skips them.
/// <para>
/// It makes as little as it can. An operator or a keyword is a constant string, a number is worked out onto the token,
/// a name comes out of a pool so the forty "phases" of a move list are one string (and the pool is kept from one call to
/// the next on the same thread, an editor lexing the same file over and over makes next to no names), and a text without
/// escapes is one substring. It was a StringBuilder a token, 1.9 MB for a 61 KB file.
/// </para>
/// </summary>
public static class Lexer
{
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "let", TokenType.Let },
        { "const", TokenType.Const },
        { "func", TokenType.Function },
        { "if", TokenType.If },
        { "else", TokenType.Else },
        { "while", TokenType.While },
        { "do", TokenType.Do },
        { "for", TokenType.For },
        { "in", TokenType.In },
        { "return", TokenType.Return },
        { "break", TokenType.Break },
        { "continue", TokenType.Continue },
        { "delete", TokenType.Delete },
        { "vec", TokenType.Vector },
        { "true", TokenType.Boolean },
        { "false", TokenType.Boolean },
        { "null", TokenType.Null },
    };

    private static readonly Dictionary<string, TokenType>.AlternateLookup<ReadOnlySpan<char>> KeywordsBySpan = Keywords.GetAlternateLookup<ReadOnlySpan<char>>();

    // The names lexed on this thread so far, handed out again rather than made again. Let go of when it gets big
    private const int MAX_POOLED = 8192;

    [ThreadStatic]
    private static HashSet<string>? pool;

    [ThreadStatic]
    private static StringBuilder? escaped;

    /// <summary>
    /// Whether a word is one the language keeps to itself, which no variable can be called. Any word is still a key of
    /// an object, <c>{ for: 1 }</c> and <c>move.in</c> are fine.
    /// </summary>
    public static bool IsKeyword(string word) => Keywords.ContainsKey(word);

    public static Token[] Tokenize(in string source) => Tokenize(source, lenient: false);

    /// <summary>
    /// Cuts source into tokens. Lenient, it never throws: a character the language has no use for comes out as an
    /// <see cref="TokenType.Unknown"/> token and a text that is never closed ends with its line, which is what an
    /// editor wants while somebody is halfway through typing. Strict, either of those is a <see cref="ParseException"/>.
    /// </summary>
    public static Token[] Tokenize(string src, bool lenient) => Tokenize(src, lenient, 0, src.Length, 1, 1);

    /// <summary>
    /// Cuts a piece of a text into tokens, as if it started at a line and column of its own, for the expressions inside
    /// of a template text, whose tokens say where they are in the whole file.
    /// </summary>
    internal static Token[] Tokenize(string src, bool lenient, int start, int end, int line, int col)
    {
        // Built into a list kept on the thread and handed out as an array of exactly the tokens there are. It was a list
        // as long as a quarter of the file every time, 640 KB for Hex's texts, and then a copy of it
        List<Token> tokens = building ?? new List<Token>(256);
        building = null;
        tokens.Clear();

        try
        {
            return TokenizeInto(tokens, src, lenient, start, end, line, col);
        }
        finally
        {
            if (tokens.Capacity <= MAX_KEPT_TOKENS)
                building = tokens;
        }
    }

    // The list tokens are built in, kept for the next file unless a huge one made it huge
    private const int MAX_KEPT_TOKENS = 1 << 16;

    [ThreadStatic]
    private static List<Token>? building;

    private static Token[] TokenizeInto(List<Token> tokens, string src, bool lenient, int start, int end, int line, int col)
    {
        var names = pool ??= new HashSet<string>(StringComparer.Ordinal);
        if (names.Count > MAX_POOLED)
            names.Clear();
        var lookup = names.GetAlternateLookup<ReadOnlySpan<char>>();

        int index = start;
        bool lineStart = true;

        void Add(TokenType type, string value, int at, int startLine, int startCol, float number = 0.0f) =>
            tokens.Add(new Token(type, value, startLine, startCol, at, index - at) { Number = number });

        // How many characters from a place to the end of its line, for a mistake about all of what's left of it (a string
        // never closed is underlined to the end of its line, it was its quote)
        int ToLineEnd(int from)
        {
            int stop = from;
            while (stop < end && src[stop] is not ('\n' or '\r')) stop++;
            return Math.Max(1, stop - from);
        }

        // One character on, keeping count of lines
        void Step()
        {
            if (src[index++] == '\n')
            {
                line++;
                col = 1;
                lineStart = true;
            }
            else
            {
                col++;
            }
        }

        while (index < end)
        {
            char current = src[index];

            if (char.IsWhiteSpace(current))
            {
                Step();
                continue;
            }

            int at = index, startLine = line, startCol = col;
            char next = index + 1 < end ? src[index + 1] : '\0';
            bool wasLineStart = lineStart;
            lineStart = false;

            // A directive, a # first on its line, to the end of it (see HIDLPreprocessor)
            if (current == '#' && wasLineStart)
            {
                while (index < end && src[index] is not ('\n' or '\r'))
                    Step();

                Add(TokenType.Directive, src[at..index], at, startLine, startCol);
                continue;
            }

            // Comments, to the end of the line or to the closing star
            if (current == '/' && next == '/')
            {
                while (index < end && src[index] is not ('\n' or '\r'))
                    Step();

                // A plain comment is a place and nothing more, documentation keeps what it says (see HIDLDocs)
                Add(TokenType.Comment, HIDLDocs.IsDoc(src.AsSpan(at, index - at), out string doc) ? doc : string.Empty, at, startLine, startCol);
                continue;
            }

            if (current == '/' && next == '*')
            {
                Step(); Step();
                while (index < end && !(src[index] == '*' && index + 1 < end && src[index + 1] == '/'))
                    Step();
                if (index < end) { Step(); Step(); }

                Add(TokenType.Comment, HIDLDocs.IsDoc(src.AsSpan(at, index - at), out string blockDoc) ? blockDoc : string.Empty, at, startLine, startCol);
                continue;
            }

            // Texts, with the usual escapes, \" \\ \n \t \r \0 and \u001b
            if (current == '"')
            {
                Step();
                int from = index;
                bool escapes = false;
                while (index < end && src[index] != '"' && !(lenient && src[index] == '\n'))
                {
                    if (src[index] == '\\' && index + 1 < end)
                    {
                        escapes = true;
                        Step();
                    }

                    Step();
                }

                string text = escapes ? Unescape(src, from, index) : src[from..index];
                if (index >= end || src[index] != '"')
                {
                    if (!lenient)
                        throw new ParseException($"The string starting at line {startLine}, column {startCol} is never closed.", startLine, startCol, ToLineEnd(at));

                    // Halfway through typing it. What there is of it is a text as far as the colours go
                    Add(TokenType.TextLiteral, text, at, startLine, startCol);
                    continue;
                }

                Step();
                Add(TokenType.TextLiteral, text, at, startLine, startCol);
                continue;
            }

            // A template, `...{expression}...`, the value what is between the backticks for the parser to take apart
            if (current == '`')
            {
                Step();
                int from = index, depth = 0;
                while (index < end && (src[index] != '`' || depth > 0))
                {
                    if (src[index] == '\\' && index + 1 < end) Step();
                    else if (src[index] == '{') depth++;
                    else if (src[index] == '}' && depth > 0) depth--;
                    Step();
                }

                if (index >= end)
                {
                    if (!lenient)
                        throw new ParseException($"The template starting at line {startLine}, column {startCol} is never closed.", startLine, startCol, ToLineEnd(at));

                    Add(TokenType.Template, src[from..index], at, startLine, startCol);
                    continue;
                }

                string inside = src[from..index];
                Step();
                Add(TokenType.Template, inside, at, startLine, startCol);
                continue;
            }

            // Three, then two character operators first, or they come out as the single ones
            if (current == '.' && next == '.' && index + 2 < end && src[index + 2] == '.')
            {
                Step(); Step(); Step();
                Add(TokenType.Spread, "...", at, startLine, startCol);
                continue;
            }

            (TokenType Type, string Value)? pair = (current, next) switch
            {
                ('=', '=') => (TokenType.Equality, "=="),
                ('=', '>') => (TokenType.Arrow, "=>"),
                ('!', '=') => (TokenType.NotEquality, "!="),
                ('>', '=') => (TokenType.BinaryOperation, ">="),
                ('<', '=') => (TokenType.BinaryOperation, "<="),
                ('&', '&') => (TokenType.BinaryOperation, "&&"),
                ('|', '|') => (TokenType.BinaryOperation, "||"),
                ('?', '?') => (TokenType.BinaryOperation, "??"),
                ('+', '=') => (TokenType.CompoundEquals, "+"),
                ('-', '=') => (TokenType.CompoundEquals, "-"),
                ('*', '=') => (TokenType.CompoundEquals, "*"),
                ('/', '=') => (TokenType.CompoundEquals, "/"),
                ('%', '=') => (TokenType.CompoundEquals, "%"),
                // ?.5 is a ? and a number, a?.b a member that may be null
                ('?', '.') when !(index + 2 < end && char.IsAsciiDigit(src[index + 2])) => (TokenType.QuestionDot, "?."),
                _ => null
            };

            if (pair is { } two)
            {
                Step(); Step();
                Add(two.Type, two.Value, at, startLine, startCol);
                continue;
            }

            (TokenType Type, string Value)? one = current switch
            {
                '(' => (TokenType.OpenParenthesis, "("),
                ')' => (TokenType.CloseParenthesis, ")"),
                ';' => (TokenType.Semicolon, ";"),
                ':' => (TokenType.Colon, ":"),
                ',' => (TokenType.Comma, ","),
                // .5 is a number, see below
                '.' when !char.IsAsciiDigit(next) => (TokenType.Dot, "."),
                '!' => (TokenType.Exclamation, "!"),
                '?' => (TokenType.Question, "?"),
                // The lexer has always called these the other way round, a { is an OpenBracket
                '{' => (TokenType.OpenBracket, "{"),
                '}' => (TokenType.CloseBracket, "}"),
                '[' => (TokenType.OpenBrace, "["),
                ']' => (TokenType.CloseBrace, "]"),
                '=' => (TokenType.Equals, "="),
                '/' => (TokenType.BinaryOperation, "/"),
                '+' => (TokenType.BinaryOperation, "+"),
                '-' => (TokenType.BinaryOperation, "-"),
                '*' => (TokenType.BinaryOperation, "*"),
                '%' => (TokenType.BinaryOperation, "%"),
                '<' => (TokenType.BinaryOperation, "<"),
                '>' => (TokenType.BinaryOperation, ">"),
                '|' => (TokenType.BinaryOperation, "|"),
                '&' => (TokenType.BinaryOperation, "&"),
                _ => null
            };

            if (one is { } single)
            {
                Step();
                Add(single.Type, single.Value, at, startLine, startCol);
                continue;
            }

            // Numbers, digits with one dot at most. 1.5 and .5 and 10. Only 0 to 9, the digits of other scripts are digits to
            // char.IsDigit but not to float.Parse, and a png opened as text threw on one
            if (char.IsAsciiDigit(current) || (current == '.' && char.IsAsciiDigit(next)))
            {
                bool dotted = false;
                while (index < end && (char.IsAsciiDigit(src[index]) || (src[index] == '.' && !dotted && index + 1 < end && char.IsAsciiDigit(src[index + 1]))))
                {
                    if (src[index] == '.') dotted = true;
                    Step();
                }

                ReadOnlySpan<char> digits = src.AsSpan(at, index - at);
                float number = !dotted && digits.Length <= 7 ? Whole(digits) : float.Parse(digits, NumberStyles.Float, CultureInfo.InvariantCulture);
                Add(TokenType.Number, Pooled(lookup, digits), at, startLine, startCol, number);
                continue;
            }

            // Names and the words of the language
            if (char.IsLetter(current) || current == '_')
            {
                while (index < end && (char.IsLetterOrDigit(src[index]) || src[index] == '_'))
                    Step();

                ReadOnlySpan<char> word = src.AsSpan(at, index - at);
                TokenType type = KeywordsBySpan.TryGetValue(word, out TokenType keyword) ? keyword : TokenType.Identifier;
                Add(type, Pooled(lookup, word), at, startLine, startCol);
                continue;
            }

            if (lenient)
            {
                Step();
                Add(TokenType.Unknown, src[at..index], at, startLine, startCol);
                continue;
            }

            throw new ParseException($"There is no making sense of '{current}' at line {startLine}, column {startCol}.", startLine, startCol, 1);
        }

        tokens.Add(new Token(TokenType.EndOfFile, string.Empty, line, col, index, 0));
        return [.. tokens];
    }

    /// <summary>
    /// Helper method to read a whole number of a few digits, most of the numbers in a file (a sprite sheet is nothing but
    /// them), without float.Parse. Seven digits at most, every one of those is exactly a float.
    /// </summary>
    private static float Whole(ReadOnlySpan<char> digits)
    {
        int value = 0;
        foreach (char digit in digits)
            value = value * 10 + (digit - '0');
        return value;
    }

    /// <summary>Helper method to hand out the string of a name made before rather than make it again.</summary>
    private static string Pooled(HashSet<string>.AlternateLookup<ReadOnlySpan<char>> lookup, ReadOnlySpan<char> word)
    {
        if (!lookup.TryGetValue(word, out string? known))
        {
            // The set makes the string out of the span as it adds it
            lookup.Add(word);
            lookup.TryGetValue(word, out known);
        }

        return known!;
    }

    /// <summary>Helper method to read a text with escapes in it, between two places of the source.</summary>
    internal static string Unescape(string src, int from, int to)
    {
        StringBuilder text = escaped ??= new StringBuilder();
        text.Clear();

        for (int i = from; i < to; i++)
        {
            char c = src[i];
            if (c == '\\' && i + 1 < to)
            {
                // \u001b, a character by its number in four hex digits. Without the four digits a \u is a u, as it was
                if (src[i + 1] == 'u' && i + 5 < to && ushort.TryParse(src.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                {
                    text.Append((char)code);
                    i += 5;
                    continue;
                }

                char after = src[++i];
                text.Append(after switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '0' => '\0',
                    _ => after
                });
                continue;
            }

            text.Append(c);
        }

        return text.ToString();
    }
}
