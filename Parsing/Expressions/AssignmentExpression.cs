namespace Horizon.HIDL.Parsing;

/// <summary>
/// <c>x = value</c>, or <c>x += value</c> and friends with the operator set.
/// </summary>
public sealed class AssignmentExpression(in IExpression assignee, in IExpression value, in string? op = null) : IExpression
{
    public NodeType Type { get; init; } = NodeType.Assignment;
    public IExpression Assignee { get; init; } = assignee;
    public IExpression Value { get; init; } = value;

    /// <summary>The operator of a compound assignment ("+" for +=), null for a plain one.</summary>
    public string? Operator { get; init; } = op;
}
