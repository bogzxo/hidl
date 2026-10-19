namespace Horizon.HIDL.Parsing;

/// <summary>
/// A template text, <c>`{name} has {health} left`</c>. The texts between the expressions (one more of them than there are
/// expressions, empty where two meet) and the expressions, each written into the text the way <c>text()</c> writes it.
/// </summary>
public sealed class TemplateExpression(in string[] parts, in IExpression[] holes) : IExpression
{
    public NodeType Type { get; init; } = NodeType.Template;
    public string[] Parts { get; init; } = parts;
    public IExpression[] Holes { get; init; } = holes;

    /// <summary>Where it was written, see <see cref="SourceSpan"/>.</summary>
    public SourceSpan Span { get; init; }
}
