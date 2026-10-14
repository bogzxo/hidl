using System.Text;

using Horizon.HIDL.Parsing;

namespace Horizon.HIDL.Preprocessing;

/// <summary>
/// The lines of a program that start with a # are for whoever runs it rather than for the program itself. They are
/// read before anything else, and what they define is there to be asked about, by the program with <c>defined("name")</c>
/// and <c>definition("name")</c>, and by the host before or after running it (<see cref="HIDLRuntime.Defines"/>, or
/// <see cref="Read"/> without running anything, which is how an editor tells what a file is).
/// <code>
/// #define layout                  a name, defined
/// #define version 3               a name with a value, the rest of the line (quotes round it are taken off)
/// #undef version
/// #if layout                      what is between kept only while the name is defined
/// #elif !debug                    a ! for not defined
/// #else
/// #endif
/// </code>
/// A directive and every line left out are blanked rather than taken out, so the lines of what is left are where they were
/// and an error on one of them says the right line. The first definition is the one an editor goes by, <c>#define layout</c>
/// says a file builds a layout and Hex opens it with its designer.
/// </summary>
public static class HIDLPreprocessor
{
    /// <summary>What a file says it is a layout with.</summary>
    public const string LAYOUT = "layout";

    // Where a block of #if is, whether what is in it is kept right now, whether a branch of it has been taken, and whether
    // everything around it is kept at all
    private readonly record struct Block(bool Keeping, bool Taken, bool Outer, int Line);

    /// <summary>
    /// Reads the directives of a program, putting what it defines into a set of definitions (which can come with some
    /// already, a host's), and hands back the program the way the lexer is to see it. Throws a <see cref="ParseException"/>
    /// at a directive that makes no sense.
    /// </summary>
    public static string Process(string source, Dictionary<string, string> defines)
    {
        if (!source.Contains('#'))
            return source;

        var output = new StringBuilder(source.Length);
        var blocks = new Stack<Block>();
        bool keeping = true;
        int lineNumber = 0;
        int at = 0;

        while (at <= source.Length)
        {
            int end = source.IndexOf('\n', at);
            if (end < 0) end = source.Length;
            lineNumber++;

            ReadOnlySpan<char> line = source.AsSpan(at, end - at);
            ReadOnlySpan<char> trimmed = line.Trim();

            if (trimmed.StartsWith("#"))
            {
                int column = line.Length - line.TrimStart().Length + 1;
                Directive(trimmed[1..], defines, blocks, ref keeping, lineNumber, column);

                // A carriage return stays, so the line ends the way the file's lines end
                if (line.EndsWith("\r")) output.Append('\r');
            }
            else if (keeping)
            {
                output.Append(line);
            }
            else if (line.EndsWith("\r"))
            {
                output.Append('\r');
            }

            if (end < source.Length) output.Append('\n');
            at = end + 1;
        }

        if (blocks.Count > 0)
            throw new ParseException($"The #if on line {blocks.Peek().Line} is never closed with an #endif.", blocks.Peek().Line, 1);

        return output.ToString();
    }

    /// <summary>
    /// What a program defines, read without running any of it, with nothing defined by anybody else. For an editor that
    /// wants to know what a file is (a layout or not). A program with directives that make no sense defines nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Read(string source)
    {
        var defines = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!source.Contains('#'))
            return defines;

        try
        {
            Process(source, defines);
        }
        catch (ParseException)
        {
            // Whatever was defined before the line that made no sense still counts
        }

        return defines;
    }

    /// <summary>Whether a program defines a name, read without running it.</summary>
    public static bool Defines(string source, string name) => Read(source).ContainsKey(name);

    /// <summary>Whether a program says it is a layout (<c>#define layout</c>).</summary>
    public static bool IsLayout(string source) => Defines(source, LAYOUT);

    private static void Directive(ReadOnlySpan<char> text, Dictionary<string, string> defines, Stack<Block> blocks, ref bool keeping, int line, int column)
    {
        int space = text.IndexOfAny(' ', '\t');
        string name = (space < 0 ? text : text[..space]).ToString();
        ReadOnlySpan<char> rest = space < 0 ? [] : text[(space + 1)..].Trim();

        switch (name)
        {
            case "define":
            {
                if (!keeping) return;
                var (defined, value) = NameAndValue(rest, line, column, "#define");
                defines[defined] = value;
                return;
            }

            case "undef":
                if (!keeping) return;
                defines.Remove(NameAndValue(rest, line, column, "#undef").Name);
                return;

            case "if" or "ifdef" or "ifndef":
            {
                bool wanted = Condition(rest, defines, line, column, name == "ifndef");
                blocks.Push(new Block(keeping && wanted, wanted, keeping, line));
                keeping = keeping && wanted;
                return;
            }

            case "elif":
            {
                if (blocks.Count == 0)
                    throw new ParseException("An #elif with no #if before it.", line, column);

                Block block = blocks.Pop();
                bool wanted = !block.Taken && Condition(rest, defines, line, column, negate: false);
                blocks.Push(block with { Keeping = block.Outer && wanted, Taken = block.Taken || wanted });
                keeping = block.Outer && wanted;
                return;
            }

            case "else":
            {
                if (blocks.Count == 0)
                    throw new ParseException("An #else with no #if before it.", line, column);

                Block block = blocks.Pop();
                blocks.Push(block with { Keeping = block.Outer && !block.Taken, Taken = true });
                keeping = block.Outer && !block.Taken;
                return;
            }

            case "endif":
            {
                if (blocks.Count == 0)
                    throw new ParseException("An #endif with no #if before it.", line, column);

                keeping = blocks.Pop().Outer;
                return;
            }

            default:
                throw new ParseException($"There is no #{name}, a directive is #define, #undef, #if, #elif, #else or #endif.", line, column, name.Length + 1);
        }
    }

    // A name and whatever comes after it on the line, quotes round that taken off
    private static (string Name, string Value) NameAndValue(ReadOnlySpan<char> text, int line, int column, string directive)
    {
        int length = 0;
        while (length < text.Length && (char.IsLetterOrDigit(text[length]) || text[length] == '_')) length++;

        if (length == 0)
            throw new ParseException($"{directive} wants a name after it.", line, column, directive.Length);

        string value = text[length..].Trim().ToString();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];

        return (text[..length].ToString(), value);
    }

    // A name, or a ! and a name, defined or not
    private static bool Condition(ReadOnlySpan<char> text, Dictionary<string, string> defines, int line, int column, bool negate)
    {
        bool not = text.StartsWith("!");
        if (not) text = text[1..].TrimStart();

        string name = NameAndValue(text, line, column, "#if").Name;
        return defines.ContainsKey(name) != (not != negate);
    }
}
