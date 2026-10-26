namespace Horizon.HIDL.Parsing;

/// <summary>
/// <c>let { a, b } = thing;</c>, a variable for each name with what the object has under that key, or
/// <c>let [first, second] = list;</c>, a variable for each item in turn. A key that isn't there or an item past the end
/// is null.
/// </summary>
public sealed class DestructuringDeclaration(in string[] names, in SourceSpan[] spans, in IExpression value, in bool list, in bool constant) : IExpression
{
    public NodeType Type { get; init; } = NodeType.Destructuring;
    public string[] Names { get; init; } = names;

    /// <summary>Where each of the names was written.</summary>
    public SourceSpan[] Spans { get; init; } = spans;

    public IExpression Value { get; init; } = value;

    /// <summary>Whether it takes the items of a list rather than the keys of an object.</summary>
    public bool List { get; init; } = list;

    public bool ReadOnly { get; init; } = constant;

    /// <summary>What a doc comment over the let says about every name of it, null for nothing.</summary>
    public string? Doc { get; init; }

    /// <summary>Where the first name was written.</summary>
    public SourceSpan Span => Spans.Length > 0 ? Spans[0] : default;
}
