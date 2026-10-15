using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Horizon.HIDL.Library;
using Horizon.HIDL.Runtime;

using ValueType = Horizon.HIDL.Runtime.ValueType;

namespace Horizon.HIDL.Interop;

/// <summary>
/// HIDL's bridge to .NET. Declared in a runtime (<see cref="Declare"/>) as one object, <c>dotnet</c> unless told
/// otherwise, it loads assemblies (<c>dotnet.load("Game.dll")</c>), finds types in them or in what is loaded already
/// (<c>dotnet.type("System.Math")</c>) and makes objects (<c>dotnet.new("System.Text.StringBuilder")</c>). A type or an
/// object comes to a script as a <see cref="ClrValue"/>, whose fields, properties and methods are its members, public or
/// not (it's the host's own code), static on a type and the rest on an object.
/// <para>
/// Values go across the way they'd be expected to. A number becomes whatever number (or enum) the member wants, a text a
/// string (or an enum by its name), a list an array or a list, an object a dictionary, a vector a Vector2, 3 or 4, null
/// null. What comes back goes the other way, and anything else stays the .NET object itself. A method with out
/// parameters hands back an object, what it returned as <c>returned</c> and every out parameter by its name. The overload is
/// picked by the arguments, the one every argument goes into with the least converting.
/// </para>
/// <para>
/// It is reflection and nothing else. A native AOT build can't load an assembly while it runs and can't be sure what it
/// trimmed, <c>load</c> says so there and the rest carries on as far as it can. A member that takes a Span can't be called
/// through reflection at all. It is off unless a host declares it, a script can do anything .NET can with it.
/// </para>
/// </summary>
[RequiresUnreferencedCode("The bridge reaches into types by name, which trimming can't see.")]
[RequiresDynamicCode("The bridge loads assemblies and calls members by reflection.")]
public static class HIDLInterop
{
    private const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic;

    // The assemblies that were loaded, by their full path, each in a context of its own that shares what is loaded already
    private static readonly Dictionary<string, Assembly> loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock loading = new();

    /// <summary>
    /// Declares the bridge in a runtime's user scope. A path given to <c>load</c> that isn't rooted is from
    /// <paramref name="baseDirectory"/>, the runtime's own folder when that is null.
    /// </summary>
    public static void Declare(HIDLRuntime runtime, string name = "dotnet", string? baseDirectory = null)
    {
        runtime.UserScope.DeclareSystem(name, Natives.Object(
            ("load", Natives.Function("load", args =>
            {
                string path = args.Text(0);
                string? folder = baseDirectory ?? runtime.BaseDirectory;
                string full = Path.GetFullPath(Path.IsPathRooted(path) || folder is null ? path : Path.Combine(folder, path));
                return new AssemblyValue(Load(full));
            })),
            ("type", Natives.Function("type", args => new ClrValue(null, FindType(args.Text(0))))),
            ("new", Natives.Function("new", args =>
            {
                Type type = args[0] is ClrValue { Target: null } given ? given.Of : FindType(args.Text(0));
                return ClrValue.Wrap(Construct(type, args.All[1..]));
            })),
            ("can_load", Natives.Function("can_load", _ => Values.Bool(RuntimeFeature.IsDynamicCodeSupported)))));
    }

    /// <summary>
    /// Loads an assembly from a file, once. What it depends on is looked for next to it, and an assembly that is loaded
    /// already (Horizon, .NET) is the one it gets rather than a copy, so the types it shares with the host are the host's.
    /// </summary>
    public static Assembly Load(string path)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new HidlRuntimeException($"This build of the host is compiled ahead of time and can't load '{Path.GetFileName(path)}' while it runs, a .NET build can.");

        if (!File.Exists(path))
            throw new HidlRuntimeException($"There is no '{path}' to load.");

        lock (loading)
        {
            if (loaded.TryGetValue(path, out Assembly? known))
                return known;

            // Loaded already by the host (Horizon itself, say), that one and not a copy whose types aren't the host's
            AssemblyName name = AssemblyName.GetAssemblyName(path);
            foreach (Assembly already in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (AssemblyName.ReferenceMatchesDefinition(name, already.GetName()))
                    return loaded[path] = already;
            }

            var context = new SharingContext(path);
            Assembly assembly = context.LoadFromAssemblyPath(path);
            loaded[path] = assembly;
            return assembly;
        }
    }

    /// <summary>A type by its full name, in whatever is loaded, the host's own and every assembly the bridge loaded.</summary>
    public static Type FindType(string name)
    {
        if (Type.GetType(name) is { } simple)
            return simple;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetType(name) is { } found)
                return found;
        }

        throw new HidlRuntimeException($"There is no type '{name}' in anything that is loaded.");
    }

    /// <summary>A context that loads what an assembly needs from its folder, and shares what is loaded already.</summary>
    private sealed class SharingContext(string path) : AssemblyLoadContext($"HIDL {Path.GetFileName(path)}")
    {
        private readonly string folder = Path.GetDirectoryName(path)!;

        protected override Assembly? Load(AssemblyName name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (AssemblyName.ReferenceMatchesDefinition(name, assembly.GetName()))
                    return assembly;
            }

            string beside = Path.Combine(folder, name.Name + ".dll");
            return File.Exists(beside) ? LoadFromAssemblyPath(beside) : null;
        }
    }

    /* Calling */

    /// <summary>Makes an object of a type with the constructor the arguments fit best.</summary>
    internal static object? Construct(Type type, ReadOnlySpan<IRuntimeValue> arguments)
    {
        if (type.IsValueType && arguments.Length == 0)
            return Activator.CreateInstance(type);

        var (constructor, converted) = Pick(type.GetConstructors(ALL | BindingFlags.Instance), arguments, type.Name);
        return ((ConstructorInfo)constructor).Invoke(converted);
    }

    /// <summary>Calls a method of a name with the overload the arguments fit best, out parameters and all.</summary>
    internal static IRuntimeValue Call(Type type, object? target, string name, ReadOnlySpan<IRuntimeValue> arguments)
    {
        BindingFlags flags = ALL | (target is null ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);
        MethodBase[] methods = [.. type.GetMethods(flags).Where(method => method.Name == name && !method.IsGenericMethodDefinition)];
        var (picked, converted) = Pick(methods, arguments, $"{type.Name}.{name}");

        object? result = ((MethodInfo)picked).Invoke(target, converted);
        ParameterInfo[] parameters = picked.GetParameters();
        if (!parameters.Any(parameter => parameter.IsOut || parameter.ParameterType.IsByRef))
            return ToHidl(result);

        // The out parameters come back with the result, by their names
        var returned = new Dictionary<string, IRuntimeValue> { ["returned"] = ToHidl(result) };
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].IsOut || parameters[i].ParameterType.IsByRef)
                returned[parameters[i].Name ?? $"out{i}"] = ToHidl(converted[i]);
        }

        return new ObjectValue(returned);
    }

    /// <summary>
    /// Helper method to pick the overload every argument goes into with the least converting. Out parameters at the end can
    /// be left off, missing optional ones take their defaults.
    /// </summary>
    private static (MethodBase Method, object?[] Arguments) Pick(MethodBase[] candidates, ReadOnlySpan<IRuntimeValue> arguments, string what)
    {
        MethodBase? best = null;
        object?[]? bestArguments = null;
        int bestCost = int.MaxValue;

        foreach (MethodBase candidate in candidates)
        {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (parameters.Any(parameter => IsSpan(parameter.ParameterType)))
                continue;

            var converted = new object?[parameters.Length];
            int cost = 0;
            bool fits = arguments.Length <= parameters.Length;

            for (int i = 0; fits && i < parameters.Length; i++)
            {
                Type wanted = parameters[i].ParameterType;
                if (wanted.IsByRef) wanted = wanted.GetElementType()!;

                if (i < arguments.Length && !parameters[i].IsOut)
                {
                    fits = TryToClr(arguments[i], wanted, out converted[i], out int step);
                    cost += step;
                }
                else if (parameters[i].IsOut)
                {
                    converted[i] = wanted.IsValueType ? Activator.CreateInstance(wanted) : null;
                }
                else if (parameters[i].HasDefaultValue)
                {
                    converted[i] = parameters[i].DefaultValue;
                    cost += 1;
                }
                else
                {
                    fits = false;
                }
            }

            if (fits && cost < bestCost)
                (best, bestArguments, bestCost) = (candidate, converted, cost);
        }

        if (best is null)
        {
            string given = string.Join(", ", arguments.ToArray().Select(Values.TypeName));
            throw new HidlRuntimeException(candidates.Length == 0
                ? $"{what} isn't there, or isn't something that can be called."
                : $"{what} has nothing that takes ({given}). It takes {string.Join(" or ", candidates.Select(Describe))}.");
        }

        return (best, bestArguments!);
    }

    private static bool IsSpan(Type type) =>
        type.IsByRefLike || (type.IsGenericType && type.GetGenericTypeDefinition() is { } definition && (definition == typeof(Span<>) || definition == typeof(ReadOnlySpan<>)));

    private static string Describe(MethodBase method) => $"({string.Join(", ", method.GetParameters().Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"))})";

    /* Values across */

    /// <summary>A .NET value as a value of HIDL, the object itself (a <see cref="ClrValue"/>) for what HIDL has nothing like.</summary>
    public static IRuntimeValue ToHidl(object? value) => value switch
    {
        null => Values.Null,
        IRuntimeValue already => already,
        bool flag => Values.Bool(flag),
        string text => new StringValue(text),
        char character => new StringValue(character.ToString()),
        float number => new NumberValue(number),
        double number => new NumberValue((float)number),
        decimal number => new NumberValue((float)number),
        int or long or short or byte or sbyte or uint or ulong or ushort => new NumberValue(Convert.ToSingle(value)),
        Enum named => new StringValue(named.ToString()),
        Vector2 vector => new Vector2Value(vector),
        Vector3 vector => new Vector3Value(vector),
        Vector4 vector => new Vector4Value(vector),
        IDictionary dictionary when IsTextKeyed(dictionary) => FromDictionary(dictionary),
        IList list => new ListValue([.. list.Cast<object?>().Select(ToHidl)]),
        Type type => new ClrValue(null, type),
        _ => new ClrValue(value, value.GetType())
    };

    private static bool IsTextKeyed(IDictionary dictionary) =>
        dictionary.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>) && i.GetGenericArguments()[0] == typeof(string));

    private static ObjectValue FromDictionary(IDictionary dictionary)
    {
        var properties = new Dictionary<string, IRuntimeValue>();
        foreach (DictionaryEntry entry in dictionary)
            properties[(string)entry.Key] = ToHidl(entry.Value);
        return new ObjectValue(properties);
    }

    /// <summary>A value of HIDL as what a member wants, with how much converting it took, false if it can't be.</summary>
    public static bool TryToClr(IRuntimeValue value, Type wanted, out object? converted, out int cost)
    {
        converted = null;
        cost = 0;
        Type? nullable = Nullable.GetUnderlyingType(wanted);
        Type type = nullable ?? wanted;

        if (value is NativeValue native)
            value = native.AccessorCallback();

        switch (value)
        {
            case NullValue:
                return !type.IsValueType || nullable is not null;

            case ClrValue clr when clr.Target is { } target && type.IsInstanceOfType(target):
                converted = target;
                return true;

            case ClrValue clr when clr.Target is null && type == typeof(Type):
                converted = clr.Of;
                return true;

            case NumberValue number when type.IsEnum:
                converted = Enum.ToObject(type, (int)number.Value);
                cost = 2;
                return true;

            case NumberValue number when IsNumber(type):
                converted = Convert.ChangeType(type == typeof(float) || type == typeof(double) || type == typeof(decimal) ? number.Value : MathF.Round(number.Value), type);
                cost = type == typeof(float) ? 0 : type == typeof(double) ? 1 : 2;
                return true;

            case NumberValue number when type == typeof(object):
                converted = number.Value;
                cost = 5;
                return true;

            case StringValue text when type == typeof(string) || type == typeof(object):
                converted = text.Value;
                cost = type == typeof(string) ? 0 : 5;
                return true;

            case StringValue text when type == typeof(char) && text.Value.Length == 1:
                converted = text.Value[0];
                return true;

            case StringValue text when type.IsEnum && Enum.TryParse(type, text.Value.Replace("_", string.Empty), true, out object? parsed):
                converted = parsed;
                cost = 1;
                return true;

            case BooleanValue flag when type == typeof(bool) || type == typeof(object):
                converted = flag.Value;
                return true;

            case Vector2Value vector when type == typeof(Vector2):
                converted = vector.Value;
                return true;

            case Vector3Value vector when type == typeof(Vector3):
                converted = vector.Value;
                return true;

            case Vector4Value vector when type == typeof(Vector4):
                converted = vector.Value;
                return true;

            case ListValue list when ElementOf(type) is { } element:
                var items = Array.CreateInstance(element, list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    if (!TryToClr(list[i], element, out object? item, out _))
                        return false;
                    items.SetValue(item, i);
                }

                converted = type.IsArray ? items : Activator.CreateInstance(typeof(List<>).MakeGenericType(element), items);
                cost = 2;
                return true;

            case ObjectValue obj when DictionaryValueOf(type) is { } valueType:
                var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType))!;
                foreach (var (key, inner) in obj.Properties)
                {
                    if (!TryToClr(inner, valueType, out object? item, out _))
                        return false;
                    dictionary[key] = item;
                }

                converted = dictionary;
                cost = 2;
                return true;

            case ObjectValue or ListValue or FunctionValue or AnonymousFunctionValue when type == typeof(object) || type == typeof(IRuntimeValue):
                converted = value;
                cost = 6;
                return true;

            default:
                if (type.IsInstanceOfType(value))
                {
                    converted = value;
                    cost = 3;
                    return true;
                }

                return false;
        }
    }

    private static bool IsNumber(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(long)
        || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort)
        || type == typeof(sbyte) || type == typeof(decimal);

    // What a list goes into, the element of an array, a List<T>, an IList<T>, an IEnumerable<T> or IReadOnlyList<T>
    private static Type? ElementOf(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (!type.IsGenericType) return type == typeof(IEnumerable) || type == typeof(IList) ? typeof(object) : null;

        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(IEnumerable<>)
            || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(ICollection<>)
            ? type.GetGenericArguments()[0] : null;
    }

    private static Type? DictionaryValueOf(Type type)
    {
        if (!type.IsGenericType) return null;
        Type definition = type.GetGenericTypeDefinition();
        Type[] arguments = type.GetGenericArguments();
        return (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)) && arguments[0] == typeof(string)
            ? arguments[1] : null;
    }
}

/// <summary>
/// A .NET type (its static members, <see cref="Target"/> null) or object (its instance members) handed to a script, see
/// <see cref="HIDLInterop"/>.
/// </summary>
[RequiresUnreferencedCode("The bridge reaches into types by name, which trimming can't see.")]
[RequiresDynamicCode("The bridge loads assemblies and calls members by reflection.")]
public sealed class ClrValue(object? target, Type of) : IHostValue
{
    private const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic;

    public ValueType Type { get; init; } = ValueType.Host;

    /// <summary>The object, null for a type's static members.</summary>
    public object? Target { get; } = target;

    /// <summary>The type it is, or is the static members of.</summary>
    public Type Of { get; } = of;

    public string TypeName => Target is null ? $"type {Of.Name}" : Of.Name;

    /// <summary>A .NET value handed to a script, as HIDL's own when HIDL has the like of it.</summary>
    public static IRuntimeValue Wrap(object? value) => HIDLInterop.ToHidl(value);

    private BindingFlags Flags => ALL | (Target is null ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);

    public IRuntimeValue Get(string member)
    {
        if (Of.GetProperty(member, Flags) is { } property && property.GetIndexParameters().Length == 0)
            return HIDLInterop.ToHidl(property.GetValue(property.GetMethod?.IsStatic == true ? null : Target));

        if (Of.GetField(member, Flags) is { } field)
            return HIDLInterop.ToHidl(field.GetValue(field.IsStatic ? null : Target));

        if (Of.GetMethods(Flags).Any(method => method.Name == member))
            return new NativeFunctionValue((args, _) => HIDLInterop.Call(Of, Target, member, args));

        if (Of.GetNestedType(member, ALL) is { } nested)
            return new ClrValue(null, nested);

        return Values.Null;
    }

    public void Set(string member, IRuntimeValue value)
    {
        if (Of.GetProperty(member, Flags) is { CanWrite: true } property)
        {
            if (!HIDLInterop.TryToClr(value, property.PropertyType, out object? converted, out _))
                throw new HidlRuntimeException($"{Of.Name}.{member} is a {property.PropertyType.Name}, not {Values.TypeName(value)}.");
            property.SetValue(property.SetMethod?.IsStatic == true ? null : Target, converted);
            return;
        }

        if (Of.GetField(member, Flags) is { IsInitOnly: false, IsLiteral: false } field)
        {
            if (!HIDLInterop.TryToClr(value, field.FieldType, out object? converted, out _))
                throw new HidlRuntimeException($"{Of.Name}.{member} is a {field.FieldType.Name}, not {Values.TypeName(value)}.");
            field.SetValue(field.IsStatic ? null : Target, converted);
            return;
        }

        throw new HidlRuntimeException($"{Of.Name} has no {member} that can be set.");
    }

    public override bool Equals(object? other) => other is ClrValue clr && Equals(clr.Target, Target) && clr.Of == Of;

    public override int GetHashCode() => HashCode.Combine(Target, Of);

    public override string ToString() => Target?.ToString() ?? $"type {Of.FullName}";
}

/// <summary>An assembly the bridge loaded, its types by <c>type(name)</c> and their names by <c>types()</c>.</summary>
[RequiresUnreferencedCode("The bridge reaches into types by name, which trimming can't see.")]
[RequiresDynamicCode("The bridge loads assemblies and calls members by reflection.")]
public sealed class AssemblyValue(Assembly assembly) : IHostValue
{
    public ValueType Type { get; init; } = ValueType.Host;

    public Assembly Assembly { get; } = assembly;

    public string TypeName => $"assembly {Assembly.GetName().Name}";

    public IRuntimeValue Get(string member) => member switch
    {
        "name" => new StringValue(Assembly.GetName().Name ?? string.Empty),
        "type" => Natives.Function("type", args => Assembly.GetType(args.Text(0)) is { } type
            ? new ClrValue(null, type)
            : throw new HidlRuntimeException($"{Assembly.GetName().Name} has no type '{args.Text(0)}'.")),
        "types" => Natives.Function("types", _ => Natives.List(Assembly.GetTypes().Select(type => type.FullName ?? type.Name))),
        _ => Values.Null
    };

    public void Set(string member, IRuntimeValue value) => throw new HidlRuntimeException("An assembly can't be changed.");

    public override string ToString() => Assembly.FullName ?? "assembly";
}
