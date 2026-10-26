namespace Horizon.HIDL.Parsing;

/// <summary>
/// A function without a name, <c>func(a, b) { ... }</c>, or an arrow, <c>(a, b) => a + b</c>. An arrow with an expression
/// after it has a body of one return of it.
/// </summary>
public sealed class AnonymousFunctionDeclarationExpression(in string[] parameters, in IStatement[] body) : IExpression
{
    public NodeType Type { get; init; } = NodeType.AnonymousFunctionDeclaration;
    public string[] Parameters { get; init; } = parameters;
    public IStatement[] Body { get; init; } = body;

    /// <summary>What a parameter is when it isn't handed over, see <see cref="FunctionDeclarationExpression.Defaults"/>.</summary>
    public IExpression?[]? Defaults { get; init; }

    /// <summary>Whether it was written as an arrow.</summary>
    public bool Arrow { get; init; }
}
