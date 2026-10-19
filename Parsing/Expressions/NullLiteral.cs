namespace Horizon.HIDL.Parsing;

public sealed class NullLiteral() : IExpression
{
    public NodeType Type { get; init; } = NodeType.NullLiteral;

    public override string ToString()
    {
        return "null";
    }
}