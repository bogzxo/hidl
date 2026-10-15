namespace Horizon.HIDL.Parsing;

public sealed class VariableDeclarationExpression(in string identifier, in IExpression? value, in bool constant) : IExpression
{
    public NodeType Type { get; init; } = NodeType.VariableDeclaration;
    public string Identifier { get; init; } = identifier;
    public IExpression? Value { get; init; } = value;
    public bool ReadOnly { get; init; } = constant;

    /// <summary>Where it was written, see <see cref="SourceSpan"/>.</summary>
    public SourceSpan Span { get; init; }

    /// <summary>Where its value was written, all of it.</summary>
    public SourceSpan ValueSpan { get; init; }

    /// <summary>What a doc comment says about it, null for nothing, see <see cref="HIDLDocs"/>.</summary>
    public string? Doc { get; init; }
}