using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;

using Horizon.HIDL.Runtime;

namespace Horizon.HIDL;

/// <summary>The name a member is written as in HIDL, its own in snake case when it doesn't say.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HidlNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>A comment written over a member in the file, what the setting does say.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HidlCommentAttribute(string comment) : Attribute
{
    public string Comment { get; } = comment;
}

/// <summary>A member that is never written or read.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HidlIgnoreAttribute : Attribute;

/// <summary>
/// C# objects as HIDL values and back, for keeping them in .hor files. The public fields and properties that can be set,
/// by their names in snake case (TextScale is text_scale) or what <see cref="HidlNameAttribute"/> says, with the comment
/// of <see cref="HidlCommentAttribute"/> over each when it is written as a file. Texts, numbers, flags, enums (as their
/// names), vectors, lists, arrays, sets, dictionaries with text keys and objects of the same kind inside. Read back
/// member by member, what a file doesn't say is left as the object had it, so a file from an older version still reads.
/// <code>
/// string text = HIDLSerializer.Document("settings", options).Comment("Hex's settings.").Write();
/// HIDLSerializer.Populate(options, runtime.UserScope.Lookup("settings"));
/// </code>
/// </summary>
public static class HIDLSerializer
{
    /// <summary>A file with an object declared in it, its members commented. Add comments and defines to it before writing it.</summary>
    public static HIDLDocument Document(string name, object value) => new HIDLDocument().Let(name, ToObject(value));

    /// <summary>An object's members as an object with comments over them.</summary>
    public static HIDLObject ToObject(object value)
    {
        var made = new HIDLObject();
        foreach (Member member in MembersOf(value.GetType()))
        {
            object? read = member.Get(value);
            if (ToValue(read) is { } written)
                made.Add(member.Name, written, member.Comment);
        }

        return made;
    }

    /// <summary>A value as HIDL, null for one that can't be written (a delegate, say).</summary>
    public static IRuntimeValue? ToValue(object? value)
    {
        switch (value)
        {
            case null: return Values.Null;
            case IRuntimeValue already: return already;
            case string text: return new StringValue(text);
            case bool flag: return Values.Bool(flag);
            case Enum named: return new StringValue(SnakeCase(named.ToString()));
            case float or double or int or long or short or byte or uint or ulong or ushort or sbyte or decimal:
                return new NumberValue(Convert.ToSingle(value, CultureInfo.InvariantCulture));
            case Vector2 v2: return new Vector2Value(v2);
            case Vector3 v3: return new Vector3Value(v3);
            case Vector4 v4: return new Vector4Value(v4);
            case Delegate: return null;
            case IDictionary dictionary:
            {
                var properties = new Dictionary<string, IRuntimeValue>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is string key && ToValue(entry.Value) is { } written)
                        properties[key] = written;
                }

                return new ObjectValue(properties);
            }
            case IEnumerable items:
            {
                var list = new List<IRuntimeValue>();
                foreach (object? item in items)
                {
                    if (ToValue(item) is { } written)
                        list.Add(written);
                }

                return new ListValue(list);
            }
            default:
                return ToObject(value).ToValue();
        }
    }

    /// <summary>What an object says set on a C# object, member by member, what it doesn't say left alone.</summary>
    public static void Populate(object target, IRuntimeValue said)
    {
        if (said is not ObjectValue from)
            return;

        foreach (Member member in MembersOf(target.GetType()))
        {
            if (!from.Properties.TryGetValue(member.Name, out IRuntimeValue? value))
                continue;

            object? current = member.Get(target);
            // a collection that can't be set (a readonly set, a list) is filled where it is
            if (!member.CanSet && current is not null)
            {
                Fill(current, value, member.Type);
                continue;
            }

            if (member.CanSet && TryRead(value, member.Type, current, out object? read))
                member.Set(target, read);
        }
    }

    /// <summary>A value read as a type, false when it isn't one.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "The types it reads and writes are the program's own settings and the like, which say to keep their members where they are used (DynamicDependency on the caller).")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The types it reads and writes are the program's own settings and the like, which say to keep their members where they are used (DynamicDependency on the caller).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "An array of a type the program has, its elements were read into a list of the same type first.")]
    public static bool TryRead(IRuntimeValue value, Type type, object? current, out object? read)
    {
        read = null;
        Type? underlying = Nullable.GetUnderlyingType(type);
        if (value is NullValue)
            return !type.IsValueType || underlying is not null;
        type = underlying ?? type;

        switch (value)
        {
            case StringValue text when type == typeof(string):
                read = text.Value;
                return true;
            case StringValue text when type.IsEnum:
                foreach (string name in Enum.GetNames(type))
                {
                    if (string.Equals(SnakeCase(name), text.Value, StringComparison.OrdinalIgnoreCase) || string.Equals(name, text.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        read = Enum.Parse(type, name);
                        return true;
                    }
                }
                return false;
            case BooleanValue flag when type == typeof(bool):
                read = flag.Value;
                return true;
            case NumberValue number when IsNumber(type):
                read = Convert.ChangeType(number.Value, type, CultureInfo.InvariantCulture);
                return true;
            case Vector2Value v2 when type == typeof(Vector2):
                read = v2.Value;
                return true;
            case Vector3Value v3 when type == typeof(Vector3):
                read = v3.Value;
                return true;
            case Vector4Value v4 when type == typeof(Vector4):
                read = v4.Value;
                return true;
        }

        if (typeof(IRuntimeValue).IsAssignableFrom(type))
        {
            read = value;
            return true;
        }

        if (type.IsArray && value is ListValue array)
        {
            Type element = type.GetElementType()!;
            var made = Array.CreateInstance(element, array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                if (TryRead(array[i], element, null, out object? item))
                    made.SetValue(item, i);
            }

            read = made;
            return true;
        }

        if ((value is ListValue || value is ObjectValue) && (typeof(IList).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type) || IsSet(type)))
        {
            object made = current ?? Activator.CreateInstance(type)!;
            Fill(made, value, type);
            read = made;
            return true;
        }

        if (value is ObjectValue && !type.IsPrimitive && type != typeof(string))
        {
            object? made = current ?? (type.GetConstructor(Type.EmptyTypes) is not null || type.IsValueType ? Activator.CreateInstance(type) : null);
            if (made is null)
                return false;
            Populate(made, value);
            read = made;
            return true;
        }

        return false;
    }

    // a list, a set or a dictionary emptied and filled with what a value says
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The types it reads and writes are the program's own settings and the like, which say to keep their members where they are used (DynamicDependency on the caller).")]
    private static void Fill(object collection, IRuntimeValue value, Type type)
    {
        if (collection is IDictionary dictionary && value is ObjectValue from)
        {
            Type valueType = type.IsGenericType ? type.GetGenericArguments()[1] : typeof(object);
            dictionary.Clear();
            foreach (var (key, item) in from.Properties)
            {
                if (TryRead(item, valueType, null, out object? read))
                    dictionary[key] = read;
            }

            return;
        }

        if (value is not ListValue list)
            return;

        Type element = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
        if (collection is IList items)
        {
            items.Clear();
            foreach (IRuntimeValue item in list.Items)
            {
                if (TryRead(item, element, null, out object? read))
                    items.Add(read);
            }

            return;
        }

        if (IsSet(collection.GetType()))
        {
            MethodInfo clear = collection.GetType().GetMethod("Clear")!, add = collection.GetType().GetMethod("Add")!;
            clear.Invoke(collection, null);
            foreach (IRuntimeValue item in list.Items)
            {
                if (TryRead(item, element, null, out object? read))
                    add.Invoke(collection, [read]);
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The types it reads and writes are the program's own settings and the like, which say to keep their members where they are used (DynamicDependency on the caller).")]
    private static bool IsSet(Type type) => type.IsGenericType && type.GetInterfaces().Any(face => face.IsGenericType && face.GetGenericTypeDefinition() == typeof(ISet<>));

    private static bool IsNumber(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(long)
        || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte) || type == typeof(decimal);

    /// <summary>TextScale as text_scale.</summary>
    public static string SnakeCase(string name)
    {
        var text = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                bool wordStarts = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) || i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]));
                if (wordStarts && text.Length > 0 && text[^1] != '_')
                    text.Append('_');
                text.Append(char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private sealed record Member(string Name, string? Comment, Type Type, bool CanSet, Func<object, object?> Get, Action<object, object?> Set);

    private static readonly Dictionary<Type, Member[]> members = [];

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The types it reads and writes are the program's own settings and the like, which say to keep their members where they are used (DynamicDependency on the caller).")]
    private static Member[] MembersOf(Type type)
    {
        lock (members)
        {
            if (members.TryGetValue(type, out Member[]? known))
                return known;

            var found = new List<Member>();
            foreach (MemberInfo info in type.GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                if (info.GetCustomAttribute<HidlIgnoreAttribute>() is not null)
                    continue;

                // what a program does (a callback) is no data
                Type? memberType = (info as FieldInfo)?.FieldType ?? (info as PropertyInfo)?.PropertyType;
                if (memberType is null || typeof(Delegate).IsAssignableFrom(memberType))
                    continue;

                string name = info.GetCustomAttribute<HidlNameAttribute>()?.Name ?? SnakeCase(info.Name);
                string? comment = info.GetCustomAttribute<HidlCommentAttribute>()?.Comment;
                switch (info)
                {
                    case FieldInfo field when !field.IsLiteral:
                        found.Add(new Member(name, comment, field.FieldType, !field.IsInitOnly, field.GetValue, field.SetValue));
                        break;
                    case PropertyInfo property when property.CanRead && property.GetIndexParameters().Length == 0 && (property.CanWrite || IsCollection(property.PropertyType)):
                        found.Add(new Member(name, comment, property.PropertyType, property.CanWrite && property.SetMethod!.IsPublic, property.GetValue, (target, value) => property.SetValue(target, value)));
                        break;
                }
            }

            return members[type] = [.. found];
        }
    }

    private static bool IsCollection(Type type) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
}
