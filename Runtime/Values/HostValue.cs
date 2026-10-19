namespace Horizon.HIDL.Runtime;

/// <summary>
/// A value whose members its host answers for, a thing of the host's own rather than an object of HIDL. <c>thing.name</c>
/// asks <see cref="Get"/>, <c>thing.name = value</c> asks <see cref="Set"/>, and a member that is a function is called
/// like any other (<c>thing.name(1, 2)</c>). How the .NET bridge (<see cref="Interop.HIDLInterop"/>) hands a .NET object
/// to a script, and how any host can hand over something with more members than it wants to make an object of up front.
/// </summary>
public interface IHostValue : IRuntimeValue
{
    /// <summary>What it is called in an error and by typeof().</summary>
    string TypeName { get; }

    /// <summary>A member, null for one it hasn't got.</summary>
    IRuntimeValue Get(string member);

    /// <summary>Sets a member, throwing a <see cref="HidlRuntimeException"/> for one that can't be.</summary>
    void Set(string member, IRuntimeValue value);
}
