namespace Horizon.HIDL.Parsing;

public sealed class DoWhileDeclarationExpression(in IExpression expression, in IStatement[] body) : IExpression
{
    public NodeType Type { get; init; } = NodeType.DoWhileExpression;
    public IExpression Condition { get; init; } = expression;
    public IStatement[] Body { get; init; } = body;
}