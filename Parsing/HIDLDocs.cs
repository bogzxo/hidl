using Horizon.HIDL.Lexing;

namespace Horizon.HIDL.Parsing;

/// <summary>A documented name of a file, what it says about itself and where it is.</summary>
public readonly record struct HidlDoc(string Name, string Doc, int Line, int Column);

/// <summary>
/// Documentation comments. A comment that starts with <c>@doc</c> (or <c>@comment</c>) documents the declaration, key or
/// field under it, or the one at the start of the line it ends. More than one in a row are one documentation, a plain
/// comment between them and what they document is fine and a blank line isn't. The lexer keeps the text of one on its
/// token (and nothing for a plain comment), the parser hangs it on what it documents, and <see cref="Read"/> lists every
/// documented name of a file without running it, for an editor indexing a workspace.
/// </summary>
public static class HIDLDocs
{
    private static readonly string[] Markers = ["@doc", "@comment"];

    /// <summary>
    /// Whether the text of a comment (with its slashes or stars) is documentation, and what it says. For the lexer, a
    /// plain comment makes no string.
    /// </summary>
    public static bool IsDoc(ReadOnlySpan<char> comment, out string doc)
    {
        doc = string.Empty;
        ReadOnlySpan<char> inside = comment.StartsWith("//") ? comment[2..] : comment.StartsWith("/*") ? comment[2..].TrimEnd("*/") : comment;
        inside = inside.Trim();

        foreach (string marker in Markers)
        {
            if (!inside.StartsWith(marker, StringComparison.Ordinal) || (inside.Length > marker.Length && char.IsLetterOrDigit(inside[marker.Length])))
                continue;

            doc = inside[marker.Length..].Trim().ToString();
            return true;
        }

        return false;
    }

    /// <summary>Which lines of a file have documentation, code, or only a comment on them, to find what documents what.</summary>
    internal sealed class Lines
    {
        private readonly Dictionary<int, string> docs = [];
        private readonly HashSet<int> code = [];
        private readonly HashSet<int> comments = [];

        /// <summary>The lines of some tokens, null when none of them is documentation (nearly every file), nothing to work out.</summary>
        public static Lines? Of(Token[] tokens)
        {
            Lines? lines = null;
            foreach (Token token in tokens)
            {
                if (token.Type == TokenType.Comment && token.Value.Length > 0)
                {
                    lines ??= new Lines();
                    lines.docs[token.Line] = lines.docs.TryGetValue(token.Line, out string? had) ? $"{had} {token.Value}" : token.Value;
                }
            }

            if (lines is null)
                return null;

            foreach (Token token in tokens)
            {
                if (token.Type == TokenType.Comment)
                    lines.comments.Add(token.Line);
                else if (token.Type != TokenType.EndOfFile)
                    lines.code.Add(token.Line);
            }

            return lines;
        }

        /// <summary>
        /// The documentation of what is on a line, the doc comment at the end of it, or the doc comments over it (plain
        /// comments between are fine, a blank line or a line of code ends it), null for none.
        /// </summary>
        public string? For(int line)
        {
            var found = new List<string>();
            for (int above = line - 1; above > 0; above--)
            {
                if (code.Contains(above))
                    break;

                if (docs.TryGetValue(above, out string? doc))
                    found.Insert(0, doc);
                else if (!comments.Contains(above))
                    break;
            }

            // At the end of the line itself, after the code
            if (docs.TryGetValue(line, out string? trailing) && code.Contains(line))
                found.Add(trailing);

            return found.Count == 0 ? null : string.Join(' ', found);
        }
    }

    /// <summary>
    /// Every documented name of a file by its tokens, a let, a const, a func, a shape or a key of an object, without
    /// running or even parsing it. What a workspace index of an editor is made of.
    /// </summary>
    public static List<HidlDoc> Read(Token[] tokens)
    {
        var found = new List<HidlDoc>();
        if (Lines.Of(tokens) is not { } lines)
            return found;

        for (int i = 0; i + 1 < tokens.Length; i++)
        {
            Token token = tokens[i], next = tokens[i + 1];

            // let { a, b } = thing, every name of it documented by what is over the let
            if (token.Type is TokenType.Let or TokenType.Const && next.Type is TokenType.OpenBracket or TokenType.OpenBrace)
            {
                foreach (Token declared in HIDLDeclarations.Declared(tokens, i))
                {
                    if (lines.For(token.Line) is { } over)
                        found.Add(new HidlDoc(declared.Value, over, declared.Line, declared.Col));
                }

                continue;
            }

            Token? named = token.Type switch
            {
                TokenType.Let or TokenType.Const or TokenType.Function when next.Type == TokenType.Identifier => next,

                // shape is a word of the language only where a shape is declared, a variable can still be called it
                TokenType.Identifier when token.Value == "shape" && next.Type == TokenType.Identifier && (i == 0 || tokens[i - 1].Type is TokenType.Semicolon or TokenType.CloseBracket) => next,
                TokenType.Identifier when next.Type == TokenType.Colon && (i == 0 || tokens[i - 1].Type is not (TokenType.Question or TokenType.Identifier)) => token,
                _ => null
            };

            if (named is { } name && lines.For(name.Line) is { } doc)
                found.Add(new HidlDoc(name.Value, doc, name.Line, name.Col));
        }

        return found;
    }
}
