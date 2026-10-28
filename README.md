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

What code wants. An arrow is a function without the ceremony, `x => x * 2`, `(a, b) => a + b`, or `(a) => { ... }` for
more than an expression (an object straight after the arrow has to go in brackets, `x => ({ x })`, or it's a block). A
parameter can have a default, `func box(at, size = vec(16))`, used when the argument is missing or null. One statement
needs no braces, `if (!ready) return;`, for an `else`, a `while`, a `for` and a `do` too. `key in object`, `item in list`
and `part in text` say whether it's there (`!(key in object)` for not, the `!` goes round the brackets).
`for (key, value in object)` and `for (index, item in list)` walk with the place as well. A key can be worked out,
`{ [name]: value }`, and taken out again, `delete thing[name]` (or `delete list[2]` for an item). `let { from, to } =
selection;` makes a name for each key, `let [x, y] = pair;` for each item (a vector works too), a missing one is null.
`a?.[i]` and `f?.()` are null when `a` or `f` is.

Texts have `trim_start`, `trim_end`, `lines`, `chars`, `last_index_of`, `count_of` and the `is_digit`, `is_letter`,
`is_upper`, `is_lower`, `is_space` and `is_word` checks (all of the text, false when it's empty). Lists have `find`,
`find_index`, `reduce`, `sum`, `unique`, `flat`, `each` and `last_index_of` on top of the usual. `format("{0} of {1}", a,
b)` fills a text in, `entries(object)` is its `[key, value]` pairs. A name nobody declared is still null, but calling it
or reaching into it says it was never declared and which declared name was most likely meant.

A file can say things about itself the runtime reads before it runs, `#define layout` (which is how Hex knows a
layout from any other file), `#if defined(x)`, `#ifdef`, `#else`, `#endif`. Hand the runtime some with `Define`, ask
in code with `defined("x")` and `definition("x")`.

An error says where it happened, `HidlRuntimeException.Line`, `Column` and `File`, and the message says it too. A
runtime given a `HIDLSourceMap` (`Sources`) writes down where every key, item and declaration it makes was written,
includes and spreads followed, which is how an editor goes from a value to its text. Calls go 256 deep at the most,
and `Interpreter.Cancellation` stops a program from outside, so a host can run a file somebody is still typing. An
included file is parsed once for the same text.

`HIDLWriter` writes values back out as HIDL, which is how Hex saves a layout. `VSCode/` is the syntax highlighting.
`docs/reference.md` is the whole language for whoever has to write it, and `docs/plan.md` is where the language, the
lexer, the parser and the runtime go next.

Used as a submodule by [horizon](https://github.com/bogzxo/horizon) (at `Horizon.HIDL/`), and edited in
[Hex](https://github.com/bogzxo/hex), which is its IDE. Work on `development`, `master` is what's released.
