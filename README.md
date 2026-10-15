# HIDL

The little interpreted language Horizon's layouts, data files and scripts are written in. `let`, objects, lists,
functions, `if`, `while`, `a ? b : c`, `include("other.hor")` for another file, and a library of the usual (maths,
text, lists, print). A host adds its own by declaring things in the runtime's user scope.

```csharp
var runtime = new HIDLRuntime();
var (ok, result) = runtime.Evaluate("let answer = 6 * 7;");
```

What a data file wants. `a ?? b` is a unless it is null, `a?.b` is null when a is (a.b is an error then), so
`phase.cancel ?? 0` and `move.phases?.raise?.cancel`. A template in backticks writes what is in its braces into the
text, `` `{name} has {health - 30} left` `` (`\{` for a brace that is only a brace). `merge(shared, own)` is one object
over another all the way down, how a character's own moves go over the shared ones. `true`, `false` and `null` are words
of the language, and any word is a key, `{ for: 1 }` and `move.in` are fine.

A file can say things about itself the runtime reads before it runs, `#define layout` (which is how Hex knows a
layout from any other file), `#if defined(x)`, `#ifdef`, `#else`, `#endif`. Hand the runtime some with `Define`, ask
in code with `defined("x")` and `definition("x")`.

An error says where it happened, `HidlRuntimeException.Line`, `Column` and `File`, and the message says it too. A
runtime given a `HIDLSourceMap` (`Sources`) writes down where every key, item and declaration it makes was written,
includes and spreads followed, which is how an editor goes from a value to its text. Calls go 256 deep at the most,
and `Interpreter.Cancellation` stops a program from outside, so a host can run a file somebody is still typing. An
included file is parsed once for the same text.

`HIDLWriter` writes values back out as HIDL, which is how Hex saves a layout. `VSCode/` is the syntax highlighting.
`docs/plan.md` is where the language, the lexer, the parser and the runtime go next.

Used as a submodule by [horizon](https://github.com/bogzxo/horizon) (at `Horizon.HIDL/`), and edited in
[Hex](https://github.com/bogzxo/hex), which is its IDE. Work on `development`, `master` is what's released.
