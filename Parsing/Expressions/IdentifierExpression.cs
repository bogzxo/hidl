namespace Horizon.HIDL.Parsing;

public sealed class IdentifierExpression(in string symbol) : IExpression
{
    public NodeType Type { get; init; } = NodeType.Identifier;
    public string Symbol { get; init; } = symbol;

    /// <summary>Where it was written, see <see cref="SourceSpan"/>.</summary>
    public SourceSpan Span { get; init; }

    public override string ToString()
    {
        return $"[{Type}] Symbol: {Symbol}";
    }
}