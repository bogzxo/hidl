using Horizon.HIDL.Runtime;

using Environment = Horizon.HIDL.Runtime.Environment;

namespace Horizon.HIDL.Library;

/// <summary>
/// The functions every program gets. Walking objects (keys, values, has), making ranges, print, and include for
/// pulling another file in. The text and math ones are in their own files.
/// </summary>
public static class CoreLibrary
{
    public static void Declare(HIDLRuntime runtime, Environment scope)
    {
        scope.Declare("keys", Natives.Function("keys", args => Natives.List(args.Object(0).Properties.Keys)), true);

        scope.Declare("values", Natives.Function("values", args =>
        {
            var list = new ListValue();
            foreach (IRuntimeValue value in args.Object(0).Properties.Values)
                list.Items.Add(value is NativeValue native ? native.AccessorCallback() : value);

            return list;
        }), true);

        scope.Declare("has", Natives.Function("has", args => args[0] switch
        {
            ObjectValue obj => Values.Bool(obj.Properties.ContainsKey(args.Text(1))),
            ListValue list => Values.Bool(list.Items.Exists(item => Values.AreEqual(item, args[1]))),
            StringValue text => Values.Bool(text.Value.Contains(args.Text(1), StringComparison.Ordinal)),
            _ => throw args.Wrong($"a {Values.TypeName(args[0])} has nothing to look in.")
        }), true);

        // entries(object) is a list of [key, value], one a key, for handing the pairs on somewhere
        scope.Declare("entries", Natives.Function("entries", args =>
        {
            var list = new ListValue();
            foreach (var (key, value) in args.Object(0).Properties)
                list.Items.Add(new ListValue([new StringValue(key), value is NativeValue native ? native.AccessorCallback() : value]));

            return list;
        }), true);

        // range(n) is 0 up to n, range(a, b) is a up to b, range(a, b, step) in steps
        scope.Declare("range", Natives.Function("range", args =>
        {
            float from = args.Has(1) ? args.Number(0) : 0.0f;
            float to = args.Has(1) ? args.Number(1) : args.Number(0);
            float step = args.Number(2, 1.0f);

            if (step == 0.0f) throw args.Wrong("a step of 0 never gets anywhere.");
            if ((to - from) / step > 10_000_000) throw args.Wrong("that is too many numbers for one list.");

            var list = new ListValue();
            for (float value = from; step > 0.0f ? value < to : value > to; value += step)
                list.Items.Add(new NumberValue(value));

            return list;
        }), true);

        scope.Declare("print", Natives.Function("print", args =>
        {
            var pieces = new string[args.Count];
            for (int i = 0; i < pieces.Length; i++) pieces[i] = Values.Text(args[i]);

            string line = string.Join(" ", pieces);
            runtime.Output?.Invoke(line);
            return new StringValue(line);
        }), true);

        // merge(base, over, ...) is base with what over says on top, all the way down. An object in both is merged in
        // turn, anything else (a number, a list) over has is what it is. How a character's own moves go over the shared
        // ones, or a game's options over its defaults, without spelling out every key again. Nothing given is changed
        scope.Declare("merge", Natives.Function("merge", args =>
        {
            IRuntimeValue merged = args[0];
            for (int i = 1; i < args.Count; i++)
                merged = Merge(merged, args[i]);

            return merged;
        }), true);

        // error("message") stops the program with a message of the script's own
        scope.Declare("error", Natives.Function("error", args => throw new HidlRuntimeException(args.Text(0, "The script said stop."))), true);

        // include("other.hor") runs another file and hands back an object of everything it declared.
        // The path is next to the file that is running, or wherever the runtime was told its files are
        scope.Declare("include", Natives.Function("include", args => runtime.Include(args.Text(0))), true);

        // defined("layout") says whether a name was defined, by the host or a #define, and definition("version") what
        // it was defined as, null for a name that wasn't
        scope.Declare("defined", Natives.Function("defined", args => new BooleanValue(runtime.IsDefined(args.Text(0)))), true);
        scope.Declare("definition", Natives.Function("definition", args =>
            runtime.Defines.TryGetValue(args.Text(0), out string? value) ? new StringValue(value) : (IRuntimeValue)new NullValue()), true);
    }

    /// <summary>
    /// What <c>item in container</c> says. A key of an object (a number is the key it is written as), an item of a list,
    /// a piece of a text, a member of a thing of the host's that isn't null. Nothing is in null.
    /// </summary>
    public static bool Contains(IRuntimeValue container, IRuntimeValue item)
    {
        if (container is NativeValue native) container = native.AccessorCallback();
        if (item is NativeValue nativeItem) item = nativeItem.AccessorCallback();

        return container switch
        {
            ObjectValue obj => obj.Properties.ContainsKey(item is StringValue key ? key.Value : Values.Text(item)),
            ListValue list => list.Items.Exists(other => Values.AreEqual(other, item)),
            StringValue text when item is StringValue piece => text.Value.Contains(piece.Value, StringComparison.Ordinal),
            StringValue => throw new HidlRuntimeException($"Only a text can be in a text, not {Values.TypeName(item)}."),
            IHostValue host => host.Get(item is StringValue member ? member.Value : Values.Text(item)) is not (null or NullValue),
            NullValue => false,
            _ => throw new HidlRuntimeException($"Nothing is in {Values.TypeName(container)}, 'in' looks in an object, a list or a text.")
        };
    }

    /// <summary>One value over another, objects key by key and the rest replaced, see merge above.</summary>
    public static IRuntimeValue Merge(IRuntimeValue under, IRuntimeValue over)
    {
        if (under is not ObjectValue below || over is not ObjectValue above)
            return over is NullValue ? under : over;

        var merged = new Dictionary<string, IRuntimeValue>(below.Properties);
        foreach (var (key, value) in above.Properties)
            merged[key] = merged.TryGetValue(key, out IRuntimeValue? was) ? Merge(was, value) : value;

        return new ObjectValue(merged);
    }
}
