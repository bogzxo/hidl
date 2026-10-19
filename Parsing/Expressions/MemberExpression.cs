namespace Horizon.HIDL.Parsing;

public sealed class MemberExpression(in IExpression obj, in IExpression property, in bool computed) : IExpression
{
    public NodeType Type { get; init; } = NodeType.MemberExpression;
    public IExpression Object { get; init; } = obj;
    public IExpression Property { get; init; } = property;
    public bool Computed { get; init; } = computed;

    /// <summary>Whether it was written a?.b, null when a is instead of an error.</summary>
    public bool Optional { get; init; }

    /// <summary>Where it was written, see <see cref="SourceSpan"/>.</summary>
    public SourceSpan Span { get; init; }
}