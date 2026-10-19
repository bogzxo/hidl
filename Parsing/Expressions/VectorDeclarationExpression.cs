namespace Horizon.HIDL.Parsing;

public sealed class VectorDeclarationExpression(params IExpression[] valExpressions) : IExpression
{
    public NodeType Type { get; init; } = NodeType.VectorDeclaration;
    public IExpression[] Expressions { get; init; } = valExpressions;
}