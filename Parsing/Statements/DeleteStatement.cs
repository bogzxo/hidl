namespace Horizon.HIDL.Parsing;

public sealed class DeleteStatement(in string target) : IStatement
{
    public NodeType Type { get; init; } = NodeType.DeleteStatement;
    public string Target { get; init; } = target;
}