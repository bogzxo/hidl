using System.Numerics;

namespace Horizon.HIDL.Parsing;

public sealed class FunctionDeclarationExpression(in string name, in string[] parameters, in IStatement[] body) : IExpression
{
    public NodeType Type { get; init; } = NodeType.FunctionDeclaration;
    public string Name { get; init; } = name;
    public string[] Parameters { get; init; } = parameters;
    public IStatement[] Body { get; init; } = body;

    /// <summary>Where it was written, see <see cref="SourceSpan"/>.</summary>
    public SourceSpan Span { get; init; }

    /// <summary>What a doc comment says about it, null for nothing, see <see cref="HIDLDocs"/>.</summary>
    public string? Doc { get; init; }
}
