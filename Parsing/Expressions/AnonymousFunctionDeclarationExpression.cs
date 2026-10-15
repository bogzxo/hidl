namespace Horizon.HIDL.Parsing;

public sealed class AnonymousFunctionDeclarationExpression(in string[] parameters, in IStatement[] body) : IExpression
{
    public NodeType Type { get; init; } = NodeType.AnonymousFunctionDeclaration;
    public string[] Parameters { get; init; } = parameters;
    public IStatement[] Body { get; init; } = body;
}
