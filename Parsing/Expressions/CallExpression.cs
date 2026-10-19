namespace Horizon.HIDL.Parsing;

public sealed class CallExpression(in IExpression[] args, in IExpression caller) : IExpression
{
    public NodeType Type { get; init; } = NodeType.CallExpression;
    public IExpression[] Arguments { get; init; } = args;
    public IExpression Caller { get; init; } = caller;

    /// <summary>Where it was written, see <see cref="SourceSpan"/>.</summary>
    public SourceSpan Span { get; init; }
}