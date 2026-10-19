namespace Horizon.HIDL.Parsing;

/// <summary>
/// <c>if (condition) { } else if (other) { } else { }</c>. An else-if chain is an if inside of the else.
/// </summary>
public sealed class IfDeclarationExpression(in IExpression expression, in IStatement[] body, in IStatement[]? otherwise = null) : IExpression
{
    public NodeType Type { get; init; } = NodeType.IfExpression;
    public IExpression Condition { get; init; } = expression;
    public IStatement[] Body { get; init; } = body;

    /// <summary>What runs when the condition doesn't hold, null for nothing.</summary>
    public IStatement[]? Else { get; init; } = otherwise;
}
