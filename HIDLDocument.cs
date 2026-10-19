using System.Text;

using Horizon.HIDL.Runtime;

namespace Horizon.HIDL;

/// <summary>
/// A whole .hor file written out, the comments at its top, its defines, and its declarations, the objects in them with a
/// comment over any key that has one. What every program that keeps a file of HIDL (settings, keys, sessions, a
/// workspace) writes it with, rather than a StringBuilder of its own, so they all come out the same and all read back.
/// <code>
/// string text = new HIDLDocument()
///     .Comment("The terminal's settings.")
///     .Define("dusk")
///     .Let("settings", new HIDLObject().Add("theme", "horizon", "Which theme."))
///     .Write();
/// </code>
/// </summary>
public sealed class HIDLDocument
{
    private readonly List<string> comments = [];
    private readonly List<(string Name, string? Value)> defines = [];
    private readonly List<(string Name, object Value, string? Comment, bool Constant)> declarations = [];

    /// <summary>Comment lines at the top of the file, a line of the text a line of comment.</summary>
    public HIDLDocument Comment(string text)
    {
        foreach (string line in text.ReplaceLineEndings("\n").Split('\n'))
            comments.Add(line);
        return this;
    }

    /// <summary>A #define under the comments, how a file says what it is (#define layout, #define dusk).</summary>
    public HIDLDocument Define(string name, string? value = null)
    {
        defines.Add((name, value));
        return this;
    }

    /// <summary>A declaration of a value.</summary>
    public HIDLDocument Let(string name, IRuntimeValue value, string? comment = null, bool constant = false) => Declare(name, value, comment, constant);

    /// <summary>A declaration of an object with comments over its keys.</summary>
    public HIDLDocument Let(string name, HIDLObject value, string? comment = null, bool constant = false) => Declare(name, value, comment, constant);

    private HIDLDocument Declare(string name, object value, string? comment, bool constant)
    {
        if (!HIDLWriter.IsIdentifier(name))
            throw new ArgumentException($"'{name}' isn't a name a variable can have.", nameof(name));
        declarations.Add((name, value, comment, constant));
        return this;
    }

    /// <summary>The file's text.</summary>
    public string Write()
    {
        var text = new StringBuilder();
        foreach (string line in comments)
            text.Append("//").Append(line.Length > 0 ? " " : string.Empty).Append(line).Append('\n');

        foreach (var (name, value) in defines)
            text.Append("#define ").Append(name).Append(value is null ? string.Empty : " " + value).Append('\n');

        if (comments.Count > 0 || defines.Count > 0)
            text.Append('\n');

        for (int i = 0; i < declarations.Count; i++)
        {
            var (name, value, comment, constant) = declarations[i];
            if (i > 0)
                text.Append('\n');
            HIDLObject.AppendComment(text, comment, 0);
            text.Append(constant ? "const " : "let ").Append(name).Append(" = ");
            HIDLObject.AppendValue(text, value, 0);
            text.Append(";\n");
        }

        return text.ToString();
    }

    public override string ToString() => Write();
}

/// <summary>
/// An object written in the order its keys were added, any of them with a comment over it, and objects of the same kind
/// inside it. Turned into a plain <see cref="ObjectValue"/> with <see cref="ToValue"/> for a program that wants the values.
/// </summary>
public sealed class HIDLObject
{
    private const string INDENT = "    ";

    private readonly List<(string Key, object Value, string? Comment)> entries = [];

    public int Count => entries.Count;

    public HIDLObject Add(string key, IRuntimeValue value, string? comment = null) => Put(key, value, comment);

    public HIDLObject Add(string key, HIDLObject value, string? comment = null) => Put(key, value, comment);

    public HIDLObject Add(string key, string value, string? comment = null) => Put(key, new StringValue(value), comment);

    public HIDLObject Add(string key, float value, string? comment = null) => Put(key, new NumberValue(value), comment);

    public HIDLObject Add(string key, bool value, string? comment = null) => Put(key, Values.Bool(value), comment);

    /// <summary>A list of objects with comments, or of values.</summary>
    public HIDLObject Add(string key, IEnumerable<HIDLObject> items, string? comment = null) => Put(key, items.ToList(), comment);

    /// <summary>A comment on a line of its own with no key under it, an example to copy in a file that starts empty.</summary>
    public HIDLObject Note(string comment)
    {
        entries.Add((string.Empty, NoteMark.Instance, comment));
        return this;
    }

    private sealed class NoteMark
    {
        public static readonly NoteMark Instance = new();
    }

    private HIDLObject Put(string key, object value, string? comment)
    {
        entries.RemoveAll(entry => entry.Key == key);
        entries.Add((key, value, comment));
        return this;
    }

    /// <summary>The plain object, the comments gone.</summary>
    public ObjectValue ToValue()
    {
        var properties = new Dictionary<string, IRuntimeValue>(StringComparer.Ordinal);
        foreach (var (key, value, _) in entries)
        {
            if (value is not NoteMark)
                properties[key] = Plain(value);
        }
        return new ObjectValue(properties);
    }

    private static IRuntimeValue Plain(object value) => value switch
    {
        HIDLObject nested => nested.ToValue(),
        List<HIDLObject> items => new ListValue(items.Select(item => (IRuntimeValue)item.ToValue()).ToList()),
        IRuntimeValue plain => plain,
        _ => Values.Null
    };

    internal static void AppendValue(StringBuilder text, object value, int depth)
    {
        switch (value)
        {
            case HIDLObject nested:
                nested.Append(text, depth);
                break;
            case List<HIDLObject> items:
                AppendItems(text, items, depth);
                break;
            case IRuntimeValue plain:
                HIDLWriter.WriteInto(text, plain, depth);
                break;
        }
    }

    internal static void AppendComment(StringBuilder text, string? comment, int depth)
    {
        if (string.IsNullOrEmpty(comment))
            return;
        foreach (string line in comment.ReplaceLineEndings("\n").Split('\n'))
        {
            Indent(text, depth);
            text.Append("// ").Append(line).Append('\n');
        }
    }

    private void Append(StringBuilder text, int depth)
    {
        if (entries.Count == 0)
        {
            text.Append("{}");
            return;
        }

        text.Append("{\n");
        int last = entries.FindLastIndex(entry => entry.Value is not NoteMark);
        for (int i = 0; i < entries.Count; i++)
        {
            var (key, value, comment) = entries[i];
            AppendComment(text, comment, depth + 1);
            if (value is NoteMark)
                continue;
            Indent(text, depth + 1);
            HIDLWriter.WriteKey(text, key);
            text.Append(": ");
            AppendValue(text, value, depth + 1);
            text.Append(i < last ? ",\n" : "\n");
        }

        Indent(text, depth);
        text.Append('}');
    }

    private static void AppendItems(StringBuilder text, List<HIDLObject> items, int depth)
    {
        if (items.Count == 0)
        {
            text.Append("[]");
            return;
        }

        text.Append("[\n");
        for (int i = 0; i < items.Count; i++)
        {
            Indent(text, depth + 1);
            items[i].Append(text, depth + 1);
            text.Append(i < items.Count - 1 ? ",\n" : "\n");
        }

        Indent(text, depth);
        text.Append(']');
    }

    private static void Indent(StringBuilder text, int depth)
    {
        for (int i = 0; i < depth; i++)
            text.Append(INDENT);
    }
}
