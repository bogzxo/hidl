namespace Horizon.HIDL.Runtime;

/// <summary>
/// A scope. The variables declared in it, which of them are constants, and the scope it sits inside of, which is
/// asked for anything that isn't here. A function call makes one of these over the scope the function was declared in.
/// System variables are what the host puts in for scripts to use (the compositor of a UI, the fight of a game).
/// They stay through a <see cref="Reset"/> and can't be assigned to.
/// </summary>
public class Environment
{
    public Environment? Parent { get; private set; }

    internal bool Captured { get; private set; }

    internal void MarkCaptured()
    {
        for (Environment? current = this; current is not null && !current.Captured; current = current.Parent)
            current.Captured = true;
    }

    internal void AttachParent(Environment? parent) => Parent = parent;

    internal void ResetForPool()
    {
        Parent = null;
        Captured = false;
        Reset(resetSystem: true);
    }

    // Made when something is first put in them. A scope is made for every call and every turn of a for, and the
    // system variables are only ever in the user scope, three collections a scope was most of the garbage of a call
    private Dictionary<string, IRuntimeValue>? systemVariables;
    private Dictionary<string, IRuntimeValue>? variables;
    private HashSet<string>? constants;

    // The first few variables of a scope, kept in a little array until there are too many of them and they go into the
    // dictionary. A call with a parameter or two, or the turn of a for, was a dictionary every time. A name and its value
    // side by side, two arrays were two for every scope
    private const int MOST_SMALL = 8;
    private Slot[]? slots;
    private int count;

    private struct Slot(string name, IRuntimeValue value)
    {
        public readonly string Name = name;
        public IRuntimeValue Value = value;
    }

    private static readonly Dictionary<string, IRuntimeValue> Nothing = [];

    /// <summary>
    /// The variables declared in this scope itself, without the ones of its parents or its system variables.
    /// </summary>
    public IReadOnlyDictionary<string, IRuntimeValue> Variables
    {
        get
        {
            // Whoever wants them as a dictionary gets the scope's own, so it stays up to date as the scope changes
            if (variables is null && count > 0)
                Grow();
            return variables ?? Nothing;
        }
    }

    /// <summary>
    /// Whether a variable of this scope was declared as a constant.
    /// </summary>
    public bool IsConstant(in string name) => constants?.Contains(name) == true;

    private bool Holds(string name) => IndexOf(name) >= 0 || variables?.ContainsKey(name) == true || systemVariables?.ContainsKey(name) == true;

    /// <summary>Helper method to find a variable in the little arrays, -1 when it isn't in them (or they're gone).</summary>
    private int IndexOf(string name)
    {
        for (int i = 0; i < count; i++)
        {
            if (string.Equals(slots![i].Name, name, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    /// <summary>Helper method for a variable of this scope itself, not a system one.</summary>
    private bool TryGetOwn(string name, out IRuntimeValue value)
    {
        if (variables is not null)
            return variables.TryGetValue(name, out value!);

        int at = IndexOf(name);
        value = at >= 0 ? slots![at].Value : Values.Null;
        return at >= 0;
    }

    /// <summary>Helper method to move the variables out of the little arrays into a dictionary, in the order they were declared.</summary>
    private void Grow()
    {
        variables = new Dictionary<string, IRuntimeValue>(Math.Max(count * 2, 4));
        for (int i = 0; i < count; i++)
            variables.Add(slots![i].Name, slots[i].Value);

        slots = null;
        count = 0;
    }

    /// <param name="parent">The scope this one sits inside of, null for the outermost.</param>
    /// <param name="copy">Whether to take a copy of everything the parent holds instead of looking things up in it.</param>
    public Environment(in Environment? parent = null, in bool copy = false)
    {
        if (copy && parent is not null)
        {
            Parent = parent.Parent;
            Copy(parent);
        }
        else
        {
            Parent = parent;
        }
    }

    /// <summary>
    /// Declares a variable in this scope. Declaring one that is here already is a mistake worth hearing about.
    /// </summary>
    public IRuntimeValue Declare(in string identifier, in IRuntimeValue? value, in bool isConst = false)
    {
        if (Holds(identifier))
            throw new HidlRuntimeException($"There is a '{identifier}' already, use another name or assign to it.");

        IRuntimeValue stored = value ?? Values.Null;
        if (variables is null && count < MOST_SMALL)
        {
            if (slots is null || count == slots.Length)
                Array.Resize(ref slots, slots is null ? 4 : MOST_SMALL);

            slots[count++] = new Slot(identifier, stored);
        }
        else
        {
            if (variables is null)
                Grow();
            variables!.Add(identifier, stored);
        }

        if (isConst) (constants ??= []).Add(identifier);
        return stored;
    }

    /// <summary>
    /// Declares a variable the host owns. Scripts can read it and call into it but never assign to it or reset it away.
    /// </summary>
    public IRuntimeValue DeclareSystem(in string identifier, in IRuntimeValue value)
    {
        if (systemVariables?.ContainsKey(identifier) == true)
            throw new HidlRuntimeException($"The system variable '{identifier}' exists already.");

        (systemVariables ??= []).Add(identifier, value);
        return value;
    }

    /// <summary>
    /// Gives an existing variable a new value, in whichever scope it was declared in. A variable nobody declared is a mistake.
    /// </summary>
    public IRuntimeValue Assign(in string name, in IRuntimeValue value)
    {
        Environment? owner = Resolve(name);
        if (owner is null)
            throw new HidlRuntimeException($"There is no '{name}' to assign to, declare it with let first.");

        if (owner.IsConstant(name))
            throw new HidlRuntimeException($"'{name}' is a constant, it can't be changed.");

        if (owner.systemVariables?.ContainsKey(name) == true || !owner.TryGetOwn(name, out IRuntimeValue current))
            throw new HidlRuntimeException($"'{name}' belongs to the program, it can't be assigned to.");

        // A value the host owns is handed to the host rather than replaced
        if (current is NativeValue native)
        {
            native.MutatorCallback(value);
            return native.AccessorCallback();
        }

        if (owner.variables is not null)
            owner.variables[name] = value;
        else
            owner.slots![owner.IndexOf(name)].Value = value;
        return value;
    }

    /// <summary>
    /// Throws away every variable and constant, keeping the system variables unless told to drop those as well.
    /// </summary>
    public void Reset(in bool resetSystem = false)
    {
        variables?.Clear();
        if (count > 0)
        {
            Array.Clear(slots!, 0, count);
            count = 0;
        }

        constants?.Clear();
        if (resetSystem) systemVariables?.Clear();
    }

    /// <summary>
    /// Finds a variable here or in any scope around this one. Null (the value, not nothing) if there is none.
    /// </summary>
    public IRuntimeValue Lookup(in string name)
    {
        for (Environment? scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope.TryGetOwn(name, out IRuntimeValue value))
                return value;

            if (scope.systemVariables is not null && scope.systemVariables.TryGetValue(name, out IRuntimeValue? system))
                return system;
        }

        return Values.Null;
    }

    /// <summary>
    /// Whether a variable of that name exists, here or around this scope.
    /// </summary>
    public bool Has(in string name) => Resolve(name) is not null;

    /// <summary>
    /// Finds the scope a variable was declared in, null if it was never declared.
    /// </summary>
    public Environment? Resolve(in string name)
    {
        for (Environment? scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope.Holds(name))
                return scope;
        }

        return null;
    }

    /// <summary>
    /// Everything that can be seen from this scope, the nearest declaration of every name winning.
    /// </summary>
    public Dictionary<string, IRuntimeValue> GetAllDeclaredValues(bool includeParents = true)
    {
        Dictionary<string, IRuntimeValue> result = [];
        if (includeParents && Parent is not null)
        {
            foreach (var item in Parent.GetAllDeclaredValues(true))
                result[item.Key] = item.Value;
        }

        foreach (var item in systemVariables ?? Nothing)
            result[item.Key] = item.Value;

        foreach (var item in variables ?? Nothing)
            result[item.Key] = item.Value;

        for (int i = 0; i < count; i++)
            result[slots![i].Name] = slots[i].Value;

        return result;
    }

    /// <summary>
    /// Takes a variable or a constant out of this scope.
    /// </summary>
    public void Delete(string target)
    {
        variables?.Remove(target);
        constants?.Remove(target);

        int at = IndexOf(target);
        if (at < 0)
            return;

        count--;
        Array.Copy(slots!, at + 1, slots!, at, count - at);
        slots![count] = default;
    }

    internal void Copy(Environment other)
    {
        constants = other.constants is null ? null : [.. other.constants];
        variables = other.variables is null ? null : new Dictionary<string, IRuntimeValue>(other.variables);
        systemVariables = other.systemVariables is null ? null : new Dictionary<string, IRuntimeValue>(other.systemVariables);
        slots = other.slots is null ? null : (Slot[])other.slots.Clone();
        count = other.count;
    }
}
