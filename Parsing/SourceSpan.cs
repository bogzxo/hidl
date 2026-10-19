using Horizon.HIDL.Lexing;

namespace Horizon.HIDL.Parsing;

/// <summary>
/// Where something is in the text it was parsed from. Lines and columns from 1 the way the lexer counts them, the index
/// and length in characters. The default is nowhere, a node the parser made up or a value a host made.
/// </summary>
public readonly record struct SourceSpan(int Line, int Column, int Index, int Length)
{
    /// <summary>Whether this is a place in some text, the default isn't.</summary>
    public bool IsKnown => Line > 0;

    /// <summary>Where a token is.</summary>
    public static SourceSpan Of(in Token token) => new(token.Line, token.Col, token.Index, Math.Max(token.Length, token.Value.Length));

    /// <summary>From the start of one token to the end of another, the whole of what they and everything between them is.</summary>
    public static SourceSpan Between(in Token first, in Token last) =>
        new(first.Line, first.Col, first.Index, Math.Max(0, last.Index + Math.Max(last.Length, last.Value.Length) - first.Index));
}

/// <summary>
/// A place in a file, the file being null for a program that wasn't read out of one. <see cref="Span"/> is the name of what
/// is there (a key, the name of a let), <see cref="Value"/> the whole of the value written for it, and <see cref="Literal"/>
/// whether that value is written as itself (a number, a text, true, a vec of numbers), which an editor can change by
/// changing that text and nothing else. <see cref="Doc"/> is what a doc comment says about it (see <see cref="HIDLDocs"/>).
/// </summary>
public readonly record struct SourceLocation(string? File, SourceSpan Span, SourceSpan Value = default, bool Literal = false, string? Doc = null);
