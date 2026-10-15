namespace Horizon.HIDL.Parsing;

/// <summary>
/// <c>[a, b, c]</c>. An entry can be a spread of another list, see <see cref="SpreadExpression"/>.
/// </summary>
public sealed class ListLiteralExpression(in IExpression[] items) : IExpression
{
    public NodeType Type { get; init; } = NodeType.ListLiteral;
    public IExpression[] Items { get; init; } = items;

    /// <summary>Where each of the items starts, one for every item, for an editor going from a value to its text.</summary>
    public SourceSpan[] ItemSpans { get; init; } = [];

    /// <summary>The whole of each item, from its first token to its last.</summary>
    public SourceSpan[] ItemValueSpans { get; init; } = [];
}

/// <summary>
/// <c>...other</c> inside of an object or a list literal, which puts everything of the other one in at that spot.
/// In an object it is a property with this as its value and no key.
/// </summary>
public sealed class SpreadExpression(in IExpression source) : IExpression
{
    public NodeType Type { get; init; } = NodeType.Spread;
    public IExpression Source { get; init; } = source;
}

/// <summary>
/// <c>for (name in list) { }</c>. Walks a list, the keys of an object, the characters of a string or 0 up to a number.
/// </summary>
public sealed class ForInStatement(in string variable, in IExpression source, in IStatement[] body) : IStatement
{
    public NodeType Type { get; init; } = NodeType.ForInStatement;
    public string Variable { get; init; } = variable;
    public IExpression Source { get; init; } = source;
    public IStatement[] Body { get; init; } = body;
}

/// <summary>
/// <c>return value;</c> or a bare <c>return;</c>, which hands null back.
/// </summary>
public sealed class ReturnStatement(in IExpression? value) : IStatement
{
    public NodeType Type { get; init; } = NodeType.ReturnStatement;
    public IExpression? Value { get; init; } = value;
}

/// <summary>
/// <c>test ? a : b</c>. One or the other, only the one that is picked is evaluated.
/// </summary>
public sealed class ConditionalExpression(in IExpression condition, in IExpression then, in IExpression otherwise) : IExpression
{
    public NodeType Type { get; init; } = NodeType.Conditional;
    public IExpression Condition { get; init; } = condition;
    public IExpression Then { get; init; } = then;
    public IExpression Otherwise { get; init; } = otherwise;
}

public sealed class BreakStatement() : IStatement
{
    public NodeType Type { get; init; } = NodeType.BreakStatement;
}

public sealed class ContinueStatement() : IStatement
{
    public NodeType Type { get; init; } = NodeType.ContinueStatement;
}
