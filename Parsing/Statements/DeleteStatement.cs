namespace Horizon.HIDL.Parsing;

/// <summary>
/// <c>delete name</c>, which takes a variable out of its scope, or <c>delete thing.key</c>, <c>delete thing[key]</c> and
/// <c>delete list[2]</c>, which take a key out of an object or an item out of a list.
/// </summary>
public sealed class DeleteStatement(in string target) : IStatement
{
    public NodeType Type { get; init; } = NodeType.DeleteStatement;

    /// <summary>The variable, empty when it is a key or an item that goes.</summary>
    public string Target { get; init; } = target;

    /// <summary>The key or the item that goes, null when it is a variable.</summary>
    public MemberExpression? Member { get; init; }
}