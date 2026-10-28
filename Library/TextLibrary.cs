using System.Globalization;
using System.Text;

using Horizon.HIDL.Runtime;

using Environment = Horizon.HIDL.Runtime.Environment;

namespace Horizon.HIDL.Library;

/// <summary>
/// What a text can do. "abc".upper(), text.length, text.split(",") and the rest hang off the text itself. A method
/// called on a text is called straight away by the interpreter (<see cref="TryCall"/>), only one taken as a value is
/// made into a function.
/// </summary>
public static class TextLibrary
{
    // What a text has that is called, rather than read
    private static readonly HashSet<string> Methods =
    [
        "upper", "lower", "trim", "trim_start", "trim_end", "contains", "starts_with", "ends_with", "index_of", "last_index_of",
        "count_of", "replace", "split", "lines", "chars", "substring", "pad_left", "pad_right", "repeat",
        "is_digit", "is_letter", "is_upper", "is_lower", "is_space", "is_word"
    ];

    private static readonly string[] Everything = ["length", "count", .. Methods];

    /// <summary>
    /// text.something, for the interpreter.
    /// </summary>
    public static IRuntimeValue Member(string text, string name) => name switch
    {
        "length" or "count" => new NumberValue(text.Length),
        _ when Methods.Contains(name) => Natives.Function(name, args => Call(text, name, args)),
        _ => throw new HidlRuntimeException($"A text has no '{name}'.{Suggest.Nearest(name, Everything)} It has {Suggest.Listed(Everything)}.")
    };

    /// <summary>
    /// text.method(arguments), called without making a function of the method first. False for a name that isn't a
    /// method of a text.
    /// </summary>
    public static bool TryCall(string text, string name, IRuntimeValue[] arguments, Environment scope, out IRuntimeValue result)
    {
        if (!Methods.Contains(name))
        {
            result = Values.Null;
            return false;
        }

        result = Call(text, name, new Arguments(name, arguments, scope));
        return true;
    }

    /// <summary>Helper method for the is_ ones, true for a text that has something in it and is all of that.</summary>
    private static IRuntimeValue All(string text, Func<char, bool> what)
    {
        if (text.Length == 0)
            return Values.False;

        foreach (char c in text)
        {
            if (!what(c))
                return Values.False;
        }

        return Values.True;
    }

    private static IRuntimeValue Call(string text, string name, Arguments args)
    {
        switch (name)
        {
            case "upper": return new StringValue(text.ToUpperInvariant());
            case "lower": return new StringValue(text.ToLowerInvariant());
            case "trim": return new StringValue(text.Trim());
            case "trim_start": return new StringValue(text.TrimStart());
            case "trim_end": return new StringValue(text.TrimEnd());
            case "contains": return Values.Bool(text.Contains(args.Text(0), StringComparison.Ordinal));
            case "starts_with": return Values.Bool(text.StartsWith(args.Text(0), StringComparison.Ordinal));
            case "ends_with": return Values.Bool(text.EndsWith(args.Text(0), StringComparison.Ordinal));
            case "index_of": return new NumberValue(text.IndexOf(args.Text(0), StringComparison.Ordinal));
            case "last_index_of": return new NumberValue(text.LastIndexOf(args.Text(0), StringComparison.Ordinal));
            case "replace": return new StringValue(text.Replace(args.Text(0), args.Text(1)));

            // How many times a piece is in it, not counting overlaps
            case "count_of":
            {
                string piece = args.Text(0);
                if (piece.Length == 0) return new NumberValue(0);

                int found = 0;
                for (int at = text.IndexOf(piece, StringComparison.Ordinal); at >= 0; at = text.IndexOf(piece, at + piece.Length, StringComparison.Ordinal))
                    found++;
                return new NumberValue(found);
            }

            case "split":
            {
                // Split on a separator, trimming the pieces. "a, b" split on "," is ["a", "b"]
                string separator = args.Text(0, ",");
                return Natives.List(text.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }

            // Every line as it is, blank ones too, \r\n or \n
            case "lines": return Natives.List(text.ReplaceLineEndings("\n").Split('\n'));

            case "chars":
            {
                var list = new ListValue(new List<IRuntimeValue>(text.Length));
                foreach (char c in text) list.Items.Add(new StringValue(c.ToString()));
                return list;
            }

            case "substring":
            {
                int from = Math.Clamp(args.Integer(0), 0, text.Length);
                int to = Math.Clamp(args.Integer(1, text.Length), from, text.Length);
                return new StringValue(text[from..to]);
            }

            case "pad_left": return new StringValue(text.PadLeft(args.Integer(0), args.Text(1, " ")[0]));
            case "pad_right": return new StringValue(text.PadRight(args.Integer(0), args.Text(1, " ")[0]));
            case "repeat": return new StringValue(string.Concat(Enumerable.Repeat(text, Math.Max(0, args.Integer(0)))));

            // What every character of it is, false for an empty text. A letter is a to z either way, a digit 0 to 9, a
            // word character a letter, a digit or _, the way a name in code is
            case "is_digit": return All(text, char.IsAsciiDigit);
            case "is_letter": return All(text, char.IsAsciiLetter);
            case "is_upper": return All(text, char.IsAsciiLetterUpper);
            case "is_lower": return All(text, char.IsAsciiLetterLower);
            case "is_space": return All(text, char.IsWhiteSpace);
            case "is_word": return All(text, c => char.IsAsciiLetterOrDigit(c) || c == '_');

            default:
                throw new HidlRuntimeException($"A text has no '{name}'.");
        }
    }

    /// <summary>
    /// The functions about texts and types that stand on their own. text(x), number(x), typeof(x), len(x) and format.
    /// </summary>
    public static void Declare(Environment scope)
    {
        scope.Declare("text", Natives.Function("text", args =>
        {
            // Everything handed over, joined. text(a, " ", b) is a cheap way to build a line
            if (args.Count <= 1) return new StringValue(Values.Text(args[0]));

            var pieces = new string[args.Count];
            for (int i = 0; i < pieces.Length; i++) pieces[i] = Values.Text(args[i]);
            return new StringValue(string.Concat(pieces));
        }), true);

        scope.Declare("number", Natives.Function("number", args =>
        {
            switch (args[0])
            {
                case NumberValue number:
                    return number;
                case BooleanValue boolean:
                    return new NumberValue(boolean.Value ? 1.0f : 0.0f);
                case StringValue text when float.TryParse(text.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed):
                    return new NumberValue(parsed);
                default:
                    // What can't be read as a number comes back as whatever the caller said, null if they said nothing
                    return args.Has(1) ? args[1] : Values.Null;
            }
        }), true);

        scope.Declare("format", Natives.Function("format", args =>
        {
            // format("{0} of {1}", a, b), or format("{name} has {health}", object), what is in the braces filled in
            if (args[0] is StringValue pattern)
                return new StringValue(Fill(pattern.Value, args));

            // A number with so many digits after the point, for a HUD that shouldn't jitter
            int digits = Math.Clamp(args.Integer(1, 2), 0, 9);
            return new StringValue(args.Number(0).ToString("F" + digits, CultureInfo.InvariantCulture));
        }), true);

        scope.Declare("typeof", Natives.Function("typeof", args => new StringValue(Values.TypeName(args[0]))), true);

        scope.Declare("len", Natives.Function("len", args => args[0] switch
        {
            ListValue list => new NumberValue(list.Count),
            StringValue text => new NumberValue(text.Value?.Length ?? 0),
            ObjectValue obj => new NumberValue(obj.Properties.Count),
            _ => throw args.Wrong($"a {Values.TypeName(args[0])} has no length.")
        }), true);
    }

    /// <summary>
    /// Helper method for format with a text, {0} the first value after it, {1} the second, and {name} a key of an object
    /// when the one value is an object. What isn't one of those is left as it is written.
    /// </summary>
    private static string Fill(string pattern, Arguments args)
    {
        ObjectValue? named = args.Count == 2 && args[1] is ObjectValue obj ? obj : null;
        var text = new StringBuilder(pattern.Length + 16);

        for (int at = 0; at < pattern.Length; at++)
        {
            int close = pattern[at] == '{' ? pattern.IndexOf('}', at + 1) : -1;
            if (close < 0)
            {
                text.Append(pattern[at]);
                continue;
            }

            ReadOnlySpan<char> inside = pattern.AsSpan(at + 1, close - at - 1);
            if (int.TryParse(inside, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index + 1 < args.Count)
                text.Append(Values.Text(args[index + 1]));
            else if (named is { } values && values.Properties.TryGetValue(inside.ToString(), out IRuntimeValue? value))
                text.Append(Values.Text(value));
            else
            {
                text.Append(pattern[at]);
                continue;
            }

            at = close;
        }

        return text.ToString();
    }
}
