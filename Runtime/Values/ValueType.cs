namespace Horizon.HIDL.Runtime;

public enum ValueType : byte
{
    Null,
    Number,
    Boolean,
    Object,
    NativeFunction,
    NativeValue,
    Function,
    AnonymousFunction,
    String,
    Vector2,
    Vector3,
    Vector4,
    List,

    // A thing of the host's whose members it answers for, see IHostValue
    Host
}
