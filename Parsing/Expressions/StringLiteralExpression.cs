namespace Horizon.HIDL.Parsing;

public sealed class StringLiteralExpression(in string value) : IExpression
{
    public NodeType Type { get; init; } = NodeType.StringLiteral;
    public string Value { get; init; } = value;

    /// <summary>The value, made once by the parser, values never change so it is handed out as it is.</summary>
    public Horizon.HIDL.Runtime.IRuntimeValue? Constant { get; init; }

    public override string ToString() => Value;
}