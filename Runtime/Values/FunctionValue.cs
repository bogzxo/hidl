using Horizon.HIDL.Parsing;

namespace Horizon.HIDL.Runtime;

public readonly struct FunctionValue(in string name, in string[] parameters, in Environment env, in IStatement[] body) : IRuntimeValue
{
    public readonly ValueType Type { get; init; } = ValueType.Function;
    public readonly string Name { get; init; } = name;
    public readonly string[] Parameters { get; init; } = parameters;
    public readonly Environment Environment { get; init; } = env;
    public readonly IStatement[] Body { get; init; } = body;

    /// <summary>What each parameter is when it isn't handed over, null for none at all, see <see cref="FunctionDeclarationExpression.Defaults"/>.</summary>
    public readonly IExpression?[]? Defaults { get; init; }
}

public readonly struct AnonymousFunctionValue(in string[] parameters, in Environment env, in IStatement[] body) : IRuntimeValue
{
    public readonly ValueType Type { get; init; } = ValueType.AnonymousFunction;
    public readonly string[] Parameters { get; init; } = parameters;
    public readonly Environment Environment { get; init; } = env;
    public readonly IStatement[] Body { get; init; } = body;

    /// <summary>What each parameter is when it isn't handed over, null for none at all.</summary>
    public readonly IExpression?[]? Defaults { get; init; }
}