using Horizon.HIDL.Runtime;

using Environment = Horizon.HIDL.Runtime.Environment;

namespace Horizon.HIDL.Library;

/// <summary>
/// What a list can do. list.push(x), list.pop(), list.count and the rest hang off the list itself, the way a
/// script expects. A list carries nothing but its items. <c>list.push(x)</c> is called straight away by the interpreter
/// (<see cref="TryCall"/>), and only a method taken as a value, <c>let add = list.push</c>, is made into a function.
/// </summary>
public static class ListLibrary
{
    // What a list has that is called, rather than read
    private static readonly HashSet<string> Methods =
    [
        "push", "pop", "insert", "remove", "remove_at", "clear", "contains", "index_of", "last_index_of", "join", "reverse", "copy",
        "slice", "sort", "map", "filter", "any", "all", "find", "find_index", "reduce", "sum", "unique", "flat", "each"
    ];

    private static readonly string[] Everything = ["count", "length", "first", "last", .. Methods];

    /// <summary>
    /// list.something, for the interpreter.
    /// </summary>
    public static IRuntimeValue Member(ListValue list, string name) => name switch
    {
        "count" or "length" => new NumberValue(list.Count),
        "first" => list.Count > 0 ? list[0] : Values.Null,
        "last" => list.Count > 0 ? list[^1] : Values.Null,
        _ when Methods.Contains(name) => Natives.Function(name, args => Call(list, name, args)),
        _ => throw new HidlRuntimeException($"A list has no '{name}'.{Suggest.Nearest(name, Everything)} It has {Suggest.Listed(Everything)}.")
    };

    /// <summary>
    /// list.method(arguments), called without making a function of the method first. False for a name that isn't a
    /// method of a list, which the interpreter then reads as a member (and says what's wrong).
    /// </summary>
    public static bool TryCall(ListValue list, string name, IRuntimeValue[] arguments, Environment scope, out IRuntimeValue result)
    {
        if (!Methods.Contains(name))
        {
            result = Values.Null;
            return false;
        }

        result = Call(list, name, new Arguments(name, arguments, scope));
        return true;
    }

    /// <summary>Helper method to call a function of a script on an item (or two), from a method like map.</summary>
    private static IRuntimeValue Through(Arguments args, IRuntimeValue function, params IRuntimeValue[] items)
    {
        var runtime = HIDLRuntime.Current ?? throw args.Wrong("that needs a running program.");
        return runtime.Interpreter.Call(function, items, args.Scope);
    }

    private static IRuntimeValue Call(ListValue list, string name, Arguments args)
    {
        switch (name)
        {
            case "push":
                foreach (IRuntimeValue value in args.All) list.Items.Add(value);
                return list;

            case "pop":
            {
                if (list.Count == 0) return Values.Null;

                IRuntimeValue last = list[^1];
                list.Items.RemoveAt(list.Count - 1);
                return last;
            }

            case "insert":
                list.Items.Insert(Math.Clamp(args.Integer(0), 0, list.Count), args[1]);
                return list;

            case "remove":
            {
                int index = list.Items.FindIndex(item => Values.AreEqual(item, args[0]));
                if (index >= 0) list.Items.RemoveAt(index);
                return Values.Bool(index >= 0);
            }

            case "remove_at":
            {
                int index = args.Integer(0);
                if (index < 0) index += list.Count;
                if (index < 0 || index >= list.Count) throw args.Wrong($"there is no item {args.Integer(0)} in a list of {list.Count}.");

                IRuntimeValue removed = list[index];
                list.Items.RemoveAt(index);
                return removed;
            }

            case "clear":
                list.Items.Clear();
                return list;

            case "contains":
                return Values.Bool(list.Items.Exists(item => Values.AreEqual(item, args[0])));

            case "index_of":
                return new NumberValue(list.Items.FindIndex(item => Values.AreEqual(item, args[0])));

            case "last_index_of":
                return new NumberValue(list.Items.FindLastIndex(item => Values.AreEqual(item, args[0])));

            case "join":
                return new StringValue(string.Join(args.Text(0, ", "), list.Items.Select(Values.Text)));

            case "reverse":
                list.Items.Reverse();
                return list;

            case "copy":
                return new ListValue([.. list.Items]);

            case "slice":
            {
                int from = Math.Clamp(args.Integer(0, 0), 0, list.Count);
                int to = Math.Clamp(args.Integer(1, list.Count), from, list.Count);
                return new ListValue(list.Items.GetRange(from, to - from));
            }

            case "sort":
            {
                // By what the items are, or by what a function says about each of them
                if (args.Has(0))
                {
                    IRuntimeValue key = args.Callable(0);
                    list.Items.Sort((a, b) => Compare(Through(args, key, a), Through(args, key, b)));
                }
                else
                {
                    list.Items.Sort(Compare);
                }

                return list;
            }

            case "map":
            {
                IRuntimeValue function = args.Callable(0);
                var mapped = new List<IRuntimeValue>(list.Count);
                foreach (IRuntimeValue item in list.Items.ToArray()) mapped.Add(Through(args, function, item));
                return new ListValue(mapped);
            }

            case "filter":
            {
                IRuntimeValue function = args.Callable(0);
                var kept = new List<IRuntimeValue>();
                foreach (IRuntimeValue item in list.Items.ToArray())
                {
                    if (Values.IsTruthy(Through(args, function, item))) kept.Add(item);
                }

                return new ListValue(kept);
            }

            // each(f) calls f on every item and hands the list back, for the end of a chain
            case "each":
            {
                IRuntimeValue function = args.Callable(0);
                foreach (IRuntimeValue item in list.Items.ToArray()) Through(args, function, item);
                return list;
            }

            case "any":
            {
                IRuntimeValue function = args.Callable(0);
                foreach (IRuntimeValue item in list.Items.ToArray())
                {
                    if (Values.IsTruthy(Through(args, function, item))) return Values.True;
                }

                return Values.False;
            }

            case "all":
            {
                IRuntimeValue function = args.Callable(0);
                foreach (IRuntimeValue item in list.Items.ToArray())
                {
                    if (!Values.IsTruthy(Through(args, function, item))) return Values.False;
                }

                return Values.True;
            }

            // The first item a function says yes to, null for none, and where it is, -1 for nowhere
            case "find":
            {
                IRuntimeValue function = args.Callable(0);
                foreach (IRuntimeValue item in list.Items.ToArray())
                {
                    if (Values.IsTruthy(Through(args, function, item))) return item;
                }

                return Values.Null;
            }

            case "find_index":
            {
                IRuntimeValue function = args.Callable(0);
                IRuntimeValue[] items = list.Items.ToArray();
                for (int i = 0; i < items.Length; i++)
                {
                    if (Values.IsTruthy(Through(args, function, items[i]))) return new NumberValue(i);
                }

                return new NumberValue(-1);
            }

            // reduce((total, item) => total + item, 0), the first item to start from when there's no second argument
            case "reduce":
            {
                IRuntimeValue function = args.Callable(0);
                IRuntimeValue[] items = list.Items.ToArray();
                int start = 0;
                IRuntimeValue total;
                if (args.Count > 1)
                {
                    total = args[1];
                }
                else
                {
                    if (items.Length == 0) return Values.Null;
                    total = items[0];
                    start = 1;
                }

                for (int i = start; i < items.Length; i++) total = Through(args, function, total, items[i]);
                return total;
            }

            // The numbers added up, or what a function says of each item added up
            case "sum":
            {
                IRuntimeValue? function = args.Has(0) ? args.Callable(0) : null;
                float total = 0.0f;
                foreach (IRuntimeValue item in list.Items.ToArray())
                {
                    IRuntimeValue value = function is null ? item : Through(args, function, item);
                    total += value is NumberValue number ? number.Value : throw args.Wrong($"only numbers add up, not {Values.TypeName(value)}.");
                }

                return new NumberValue(total);
            }

            // Every item once, the first of each kept where it was
            case "unique":
            {
                var kept = new List<IRuntimeValue>(list.Count);
                foreach (IRuntimeValue item in list.Items)
                {
                    if (!kept.Exists(other => Values.AreEqual(other, item))) kept.Add(item);
                }

                return new ListValue(kept);
            }

            // The items of the lists in it put in its place, one level down
            case "flat":
            {
                var flat = new List<IRuntimeValue>(list.Count);
                foreach (IRuntimeValue item in list.Items)
                {
                    if (item is ListValue inner) flat.AddRange(inner.Items);
                    else flat.Add(item);
                }

                return new ListValue(flat);
            }

            default:
                throw new HidlRuntimeException($"A list has no '{name}'.");
        }
    }

    /// <summary>
    /// How two values are ordered by sort. Numbers by size, texts alphabetically, everything else stays put.
    /// </summary>
    public static int Compare(IRuntimeValue a, IRuntimeValue b) => (a, b) switch
    {
        (NumberValue x, NumberValue y) => x.Value.CompareTo(y.Value),
        (StringValue x, StringValue y) => string.CompareOrdinal(x.Value, y.Value),
        (BooleanValue x, BooleanValue y) => x.Value.CompareTo(y.Value),
        _ => 0
    };

    /// <summary>
    /// What a for loop walks. The items of a list, the keys of an object, the characters of a text, or 0 up to a number.
    /// </summary>
    public static IEnumerable<IRuntimeValue> Walk(IRuntimeValue source)
    {
        switch (source)
        {
            case ListValue list:
                // Over a copy, so the loop can change the list without tripping over itself
                foreach (IRuntimeValue item in list.Items.ToArray()) yield return item;
                break;

            case ObjectValue obj:
                foreach (string key in obj.Properties.Keys.ToArray()) yield return new StringValue(key);
                break;

            case StringValue text:
                foreach (char c in text.Value ?? string.Empty) yield return new StringValue(c.ToString());
                break;

            case NumberValue number:
                for (int i = 0; i < (int)number.Value; i++) yield return new NumberValue(i);
                break;

            default:
                throw new HidlRuntimeException($"A for loop can walk a list, an object, a text or a number, not {Values.TypeName(source)}.");
        }
    }

    /// <summary>
    /// What a for loop of two names walks. The keys of an object and what each holds, the places of a list and its
    /// items, the places of a text and its characters, and 0 up to a number twice over.
    /// </summary>
    public static IEnumerable<(IRuntimeValue Key, IRuntimeValue Value)> WalkPairs(IRuntimeValue source)
    {
        switch (source)
        {
            case ListValue list:
            {
                IRuntimeValue[] items = list.Items.ToArray();
                for (int i = 0; i < items.Length; i++) yield return (new NumberValue(i), items[i]);
                break;
            }

            case ObjectValue obj:
                foreach (string key in obj.Properties.Keys.ToArray())
                {
                    // A key the loop took out on the way is skipped
                    if (obj.Properties.TryGetValue(key, out IRuntimeValue? value))
                        yield return (new StringValue(key), value is NativeValue native ? native.AccessorCallback() : value);
                }
                break;

            case StringValue text:
            {
                string said = text.Value ?? string.Empty;
                for (int i = 0; i < said.Length; i++) yield return (new NumberValue(i), new StringValue(said[i].ToString()));
                break;
            }

            case NumberValue number:
                for (int i = 0; i < (int)number.Value; i++) yield return (new NumberValue(i), new NumberValue(i));
                break;

            default:
                throw new HidlRuntimeException($"A for loop can walk a list, an object, a text or a number, not {Values.TypeName(source)}.");
        }
    }
}
