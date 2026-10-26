namespace Horizon.HIDL.Parsing;

/// <summary>
/// One <c>key: value</c> of an object literal. Without a value it is the shorthand <c>{ key }</c>, which takes the
/// variable of that name. A key of null with a <see cref="SpreadExpression"/> as the value is <c>...other</c>, and a key of
/// null with a <see cref="ComputedKey"/> is <c>[name]: value</c>.
/// </summary>
public sealed class PropertyExpression(in string? key, in IExpression? value) : IExpression
{
    public NodeType Type { get; init; } = NodeType.Property;
    public string? Key { get; init; } = key;
    public IExpression? Value { get; init; } = value;

    /// <summary>The key worked out as it is made, <c>{ [name]: value }</c>, null for a key written as it is.</summary>
    public IExpression? ComputedKey { get; init; }

    /// <summary>Where it was written, see <see cref="SourceSpan"/>.</summary>
    public SourceSpan Span { get; init; }

    /// <summary>Where its value was written, all of it.</summary>
    public SourceSpan ValueSpan { get; init; }

    /// <summary>What a doc comment says about it, null for nothing, see <see cref="HIDLDocs"/>.</summary>
    public string? Doc { get; init; }

    public override string ToString()
    {
        return $"[{Type}] {Key}: {Value}";
    }
}
