namespace Horizon.HIDL.Parsing;

public sealed class BinaryExpression(in IExpression left, in IExpression? right, in string op) : IExpression
{
    public NodeType Type { get; init; } = NodeType.BinaryExpression;
    public IExpression Left { get; init; } = left;
    public IExpression? Right { get; init; } = right;
    public string Operator { get; init; } = op;

    public override string ToString()
    {
        return $"[{Type}] Left: ({Left.ToString()}\t{Operator}\t{(Right != null ? Right.ToString() : "null")})";
    }
}