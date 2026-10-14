# HIDL

The little interpreted language Horizon's layouts, data files and scripts are written in. `let`, objects, lists,
functions, `if`, `while`, `a ? b : c`, `include("other.hor")` for another file, and a library of the usual (maths,
text, lists, print). A host adds its own by declaring things in the runtime's user scope.

```csharp
var runtime = new HIDLRuntime();
var (ok, result) = runtime.Evaluate("let answer = 6 * 7;");
```

`HIDLWriter` writes values back out as HIDL, which is how Hex saves a layout. `VSCode/` is the syntax highlighting.

Used as a submodule by [horizon](https://github.com/bogzxo/horizon) (at `Horizon.HIDL/`) and
[hidl-editor](https://github.com/bogzxo/hidl-editor). Work on `development`, `master` is what's released.
