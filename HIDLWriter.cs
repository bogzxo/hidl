using System.Globalization;
using System.Text;

using Horizon.HIDL.Lexing;
using Horizon.HIDL.Runtime;

using Environment = Horizon.HIDL.Runtime.Environment;

namespace Horizon.HIDL;

/// <summary>
/// Writes runtime values back out as HIDL source, so that evaluating the text gives the values back.
/// This is how data that changes while a program runs (settings, key bindings) is saved. Build an
/// <see cref="ObjectValue"/>, write it with <see cref="WriteDeclaration"/> and read it back by evaluating the file.
/// Numbers, texts, booleans, null, vectors, lists and objects can be written. Functions can't, they are left out of
/// the object or scope they are in. A native value is written as whatever it reads as at the time.
/// </summary>
public static class HIDLWriter
{
    private const string INDENT = "    ";

    // Deeper than any data anyone would write by hand, so getting here means an object contains itself
    private const int MAX_DEPTH = 64;

    // How long a list is allowed to be on one line before it goes one item a line
    private const int SHORT_LIST = 72;

    /// <summary>
    /// Writes a value as an expression, e.g. <c>vec(1, 2)</c> or an object literal over several lines.
    /// </summary>
    /// <exception cref="NotSupportedException">The value has no way of being written, see <see cref="CanWrite"/>.</exception>
    public static string Write(IRuntimeValue value) => Write(value, false);

    /// <summary>
    /// Writes a value as an expression, with a comma after the last item of every object and list that goes over
    /// several lines when <paramref name="trailingCommas"/> says so. That's how a file a tool writes whole is written,
    /// every line of an object the same, so adding or taking away the last key is a change of one line in a diff.
    /// </summary>
    public static string Write(IRuntimeValue value, bool trailingCommas)
    {
        StringBuilder text = new();
        Append(text, value, 0, trailingCommas);
        return text.ToString();
    }

    /// <summary>
    /// Writes a value as the statement that declares it, <c>let name = value;</c>
    /// </summary>
    /// <exception cref="ArgumentException">The name isn't one a variable can have.</exception>
    /// <exception cref="NotSupportedException">The value has no way of being written, see <see cref="CanWrite"/>.</exception>
    public static string WriteDeclaration(string name, IRuntimeValue value, bool isConst = false)
    {
        if (!IsIdentifier(name))
            throw new ArgumentException($"'{name}' isn't a name a variable can have.", nameof(name));

        StringBuilder text = new();
        AppendDeclaration(text, name, value, isConst);
        return text.ToString();
    }

    /// <summary>
    /// Writes every variable declared in a scope as a declaration of its own, constants as constants.
    /// The variables of its parents and its system variables aren't written, and neither is anything the host put
    /// there for scripts to call or to reach into (functions and native values). Evaluating the text in a scope
    /// set up the same way gives the same state back.
    /// </summary>
    /// <param name="include">Decides per variable whether it is written, for leaving some out. All of them if null.</param>
    public static string Write(Environment environment, Func<string, IRuntimeValue, bool>? include = null)
    {
        StringBuilder text = new();

        foreach (var (name, value) in environment.Variables)
        {
            if (value.Type == Runtime.ValueType.NativeValue || !CanWrite(value))
                continue;

            if (include is not null && !include(name, value))
                continue;

            AppendDeclaration(text, name, value, environment.IsConstant(name));
            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>
    /// Whether a value can be written. Objects and lists always can, what can't be written in them is left out.
    /// </summary>
    public static bool CanWrite(IRuntimeValue value) => value.Type switch
    {
        Runtime.ValueType.Function or Runtime.ValueType.AnonymousFunction or Runtime.ValueType.NativeFunction => false,
        Runtime.ValueType.NativeValue => CanWrite(((NativeValue)value).AccessorCallback()),
        Runtime.ValueType.Number => float.IsFinite(((NumberValue)value).Value),
        _ => true
    };

    /// <summary>
    /// Whether a text can be the name of a variable or the key of a property. The lexer decides.
    /// </summary>
    public static bool IsIdentifier(string? name)
    {
        if (string.IsNullOrEmpty(name) || Lexer.IsKeyword(name))
            return false;

        try
        {
            Token[] tokens = Lexer.Tokenize(name);
            return tokens.Length == 2 && tokens[0].Type == TokenType.Identifier && tokens[0].Value == name;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A value written into text at a depth, so a writer of whole files (HIDLDocument) lays nested values out the same way.</summary>
    public static void WriteInto(StringBuilder text, IRuntimeValue value, int depth) => Append(text, value, depth, false);

    /// <summary>A value written into text at a depth, with a comma after the last item of what goes over several lines when <paramref name="trailingCommas"/> says so.</summary>
    public static void WriteInto(StringBuilder text, IRuntimeValue value, int depth, bool trailingCommas) => Append(text, value, depth, trailingCommas);

    /// <summary>A key as an object writes it, a plain name as it is and anything else in quotes.</summary>
    public static void WriteKey(StringBuilder text, string key)
    {
        if (IsIdentifier(key)) text.Append(key);
        else AppendString(text, key);
    }

    private static void AppendDeclaration(StringBuilder text, string name, IRuntimeValue value, bool isConst)
    {
        text.Append(isConst ? "const " : "let ").Append(name).Append(" = ");
        Append(text, value, 0, false);
        text.Append(';');
    }

    private static void Append(StringBuilder text, IRuntimeValue value, int depth, bool trailing)
    {
        switch (value)
        {
            case NullValue:
                text.Append("null");
                break;

            case NumberValue number:
                AppendNumber(text, number.Value);
                break;

            case BooleanValue boolean:
                text.Append(boolean.Value ? "true" : "false");
                break;

            case StringValue str:
                AppendString(text, str.Value);
                break;

            case Vector2Value vector:
                AppendVector(text, [vector.Value.X, vector.Value.Y]);
                break;

            case Vector3Value vector:
                AppendVector(text, [vector.Value.X, vector.Value.Y, vector.Value.Z]);
                break;

            case Vector4Value vector:
                AppendVector(text, [vector.Value.X, vector.Value.Y, vector.Value.Z, vector.Value.W]);
                break;

            case NativeValue native:
                Append(text, native.AccessorCallback(), depth, trailing);
                break;

            case ObjectValue obj:
                AppendObject(text, obj, depth, trailing);
                break;

            case ListValue list:
                AppendList(text, list, depth, trailing);
                break;

            default:
                throw new NotSupportedException($"A value of type {value.Type} can't be written as text.");
        }
    }

    private static void AppendObject(StringBuilder text, ObjectValue obj, int depth, bool trailing)
    {
        if (depth >= MAX_DEPTH)
            throw new NotSupportedException("An object that contains itself can't be written as text.");

        bool any = false;
        if (obj.Properties is not null)
        {
            foreach (var (key, value) in obj.Properties)
            {
                if (!CanWriteProperty(value))
                    continue;

                // Commas go between properties, and after the last one too in a file a tool writes whole (see Write)
                text.Append(any ? "," : "{").AppendLine();
                AppendIndent(text, depth + 1);

                // A key that isn't a plain name is written in quotes, which the parser takes too
                if (IsIdentifier(key)) text.Append(key);
                else AppendString(text, key);

                text.Append(": ");
                Append(text, value, depth + 1, trailing);
                any = true;
            }
        }

        if (!any)
        {
            text.Append("{}");
            return;
        }

        if (trailing)
            text.Append(',');
        text.AppendLine();
        AppendIndent(text, depth);
        text.Append('}');
    }

    private static void AppendList(StringBuilder text, ListValue list, int depth, bool trailing)
    {
        if (depth >= MAX_DEPTH)
            throw new NotSupportedException("A list that contains itself can't be written as text.");

        if (list.Count == 0)
        {
            text.Append("[]");
            return;
        }

        // Short lists of plain values go on one line, anything else one item a line
        bool plain = list.Items.TrueForAll(item => item is not (ObjectValue or ListValue));
        if (plain)
        {
            StringBuilder line = new("[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) line.Append(", ");
                Append(line, list[i], depth + 1, trailing);
            }
            line.Append(']');

            if (line.Length <= SHORT_LIST)
            {
                text.Append(line);
                return;
            }
        }

        text.Append('[');
        for (int i = 0; i < list.Count; i++)
        {
            if (!CanWriteProperty(list[i]))
                continue;

            text.Append(i > 0 ? "," : string.Empty).AppendLine();
            AppendIndent(text, depth + 1);
            Append(text, list[i], depth + 1, trailing);
        }

        if (trailing)
            text.Append(',');
        text.AppendLine();
        AppendIndent(text, depth);
        text.Append(']');
    }

    // Inside of an object only functions are left out. Anything else that can't be written is a mistake
    // worth hearing about, and throws when it is written
    private static bool CanWriteProperty(IRuntimeValue value) =>
        value.Type is not (Runtime.ValueType.Function or Runtime.ValueType.AnonymousFunction or Runtime.ValueType.NativeFunction);

    private static void AppendVector(StringBuilder text, ReadOnlySpan<float> components)
    {
        text.Append("vec(");
        for (int i = 0; i < components.Length; i++)
        {
            if (i > 0)
                text.Append(", ");
            AppendNumber(text, components[i]);
        }
        text.Append(')');
    }

    private static void AppendString(StringBuilder text, string? value)
    {
        text.Append('"');
        foreach (char c in value ?? string.Empty)
        {
            // The escapes the lexer reads back
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default: text.Append(c); break;
            }
        }
        text.Append('"');
    }

    private static void AppendNumber(StringBuilder text, float number)
    {
        if (!float.IsFinite(number))
            throw new NotSupportedException($"{number} can't be written as text.");

        // The shortest text that reads back as the same number. The lexer only knows digits and a dot,
        // so the exponent very large and very small numbers come with has to be written out
        string digits = number.ToString("R", CultureInfo.InvariantCulture);
        int exponentAt = digits.IndexOf('E');

        if (exponentAt < 0)
            text.Append(digits);
        else
            AppendExpanded(text, digits.AsSpan(0, exponentAt), int.Parse(digits.AsSpan(exponentAt + 1), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Writes a number that was given as a mantissa and an exponent ("1.5" and -5) without one ("0.000015").
    /// </summary>
    private static void AppendExpanded(StringBuilder text, ReadOnlySpan<char> mantissa, int exponent)
    {
        if (mantissa[0] == '-')
        {
            text.Append('-');
            mantissa = mantissa[1..];
        }

        int point = mantissa.IndexOf('.');
        Span<char> digits = stackalloc char[mantissa.Length];
        int count = 0;
        foreach (char c in mantissa)
        {
            if (c != '.')
                digits[count++] = c;
        }

        int moved = (point < 0 ? count : point) + exponent;

        if (moved <= 0)
        {
            text.Append("0.").Append('0', -moved).Append(digits[..count]);
        }
        else if (moved >= count)
        {
            text.Append(digits[..count]).Append('0', moved - count);
        }
        else
        {
            text.Append(digits[..moved]).Append('.').Append(digits[moved..count]);
        }
    }

    private static void AppendIndent(StringBuilder text, int depth)
    {
        for (int i = 0; i < depth; i++)
            text.Append(INDENT);
    }
}
