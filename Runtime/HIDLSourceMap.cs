using System.Runtime.CompilerServices;

using Horizon.HIDL.Parsing;

namespace Horizon.HIDL.Runtime;

/// <summary>
/// Where the values a program made were written, kept while it runs when a host asks for it (<see cref="HIDLRuntime.Sources"/>).
/// The key of every object made out of a literal, the items of every list, and every declaration, by the file it was
/// in, includes and all. A spread carries the places of what it spread along. This is how an editor goes from a value to
/// the text that made it (a click in Hex's values, an error about a move pointing at the move).
/// </summary>
public sealed class HIDLSourceMap
{
    private readonly ConditionalWeakTable<Dictionary<string, IRuntimeValue>, Dictionary<string, SourceLocation>> keys = [];
    private readonly ConditionalWeakTable<List<IRuntimeValue>, List<SourceLocation>> items = [];
    private readonly Dictionary<string, SourceLocation> declarations = new(StringComparer.Ordinal);

    /// <summary>Where a key of an object was written.</summary>
    public bool TryGetKey(ObjectValue of, string key, out SourceLocation at)
    {
        at = default;
        return of.Properties is { } properties && keys.TryGetValue(properties, out var known) && known.TryGetValue(key, out at);
    }

    /// <summary>Where an item of a list was written.</summary>
    public bool TryGetItem(ListValue of, int index, out SourceLocation at)
    {
        at = default;
        if (!items.TryGetValue(of.Items, out var known) || index < 0 || index >= known.Count)
            return false;

        at = known[index];
        return at.Span.IsKnown;
    }

    /// <summary>Where a name was declared with let, const or func, the last one of that name.</summary>
    public bool TryGetDeclaration(string name, out SourceLocation at) => declarations.TryGetValue(name, out at);

    internal void Key(Dictionary<string, IRuntimeValue> of, string key, SourceLocation at)
    {
        if (!at.Span.IsKnown)
            return;

        keys.GetOrCreateValue(of)[key] = at;
    }

    /// <summary>The places of the keys of one object, copied onto another it was spread into.</summary>
    internal void Spread(Dictionary<string, IRuntimeValue> from, Dictionary<string, IRuntimeValue> into)
    {
        if (!keys.TryGetValue(from, out var known))
            return;

        var places = keys.GetOrCreateValue(into);
        foreach (var (key, at) in known)
            places[key] = at;
    }

    internal void Item(List<IRuntimeValue> of, int index, SourceLocation at)
    {
        var places = items.GetOrCreateValue(of);
        while (places.Count <= index)
            places.Add(default);
        places[index] = at;
    }

    /// <summary>The places of the items of a list spread into another, from where they land in it.</summary>
    internal void SpreadItems(List<IRuntimeValue> from, List<IRuntimeValue> into, int startingAt)
    {
        if (!items.TryGetValue(from, out var known))
            return;

        for (int i = 0; i < known.Count; i++)
            Item(into, startingAt + i, known[i]);
    }

    internal void Declaration(string name, SourceLocation at)
    {
        if (at.Span.IsKnown)
            declarations[name] = at;
    }
}
