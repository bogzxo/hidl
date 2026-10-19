# HIDL, where it goes next

HIDL started as a calculator with `let` and grew into what Horizon's layouts, skins, texts, Fighter2D's moves and
behaviours, Hex's settings and now Hex's extensions are written in. It grew a feature at a time, each bolted on where it
fit, and it shows in three ways. It is slow for what it does and makes garbage doing it, every token is a string,
every node a boxed struct, every number boxed, every scope three dictionaries. It knows nothing about where anything was
written once it has parsed it, so an error said "There is no 'x'" and nothing else until this round. And it can only be
extended by editing it, the operators are a ladder of methods, the library is lambdas declared one by one, a host binds
its objects by hand.

This is the plan for the language, the lexer, the parser, the runtime, the library and the services an editor needs,
measured where it can be. The numbers are on a 2200 line file (four of Fighter2D's move lists, 61 KB) and Fighter2D's
dummy behaviours, Release, October 2026. See also `hex/docs/performance.md` and `hex/docs/hidl-revamp.md`.

## What is done (this round)

- **Source spans.** Declarations, keys, list items, names, members and calls keep where they were written
  (`SourceSpan`, line, column, index, length). The other nodes say nowhere.
- **Errors say where.** A `HidlRuntimeException` is placed on its way out of the nearest node that knows where it was
  written, file and all (`Line`, `Column`, `File`, `Reason` without them, `Message` with). An error in an included file
  says which file and keeps its line instead of being re-wrapped by every include on the way out.
- **The source map.** A runtime with `Sources` set writes down where every key, item and declaration it makes was
  written, across includes, carrying the places through a spread (`HIDLSourceMap`). Hex's values go to the text with it.
- **Safe to run while typing.** Calls go 256 deep at the most (a stack overflow can't be caught in .NET, it took Hex with
  it) and every loop and call can be cancelled (`Interpreter.Cancellation`). The flow of a function is put back when it
  throws, an error half way down left a return on its way out for the next program the runtime ran.
- **Scopes made lazily.** A scope's dictionaries are made when something is first declared in it.
- **`merge(a, b, ...)`**, deep, later wins, nothing given changed. How a character's moves go over the shared ones.

## The lexer

What it does now. A `StringBuilder` for every comment, text, number and name, `ToString` on every one-character operator,
a scan back to the start of the line for every `#`, the numbers kept as text for the parser to parse, `true`, `false` and
`null` lexed as names and looked up through every scope at run time, and everything into a list copied into an array.
1.6 ms and 1.9 MB for 61 KB.

The plan.

1. **Tokens are places.** `Token` keeps its index and length (it has them), its value is a constant string for every
   operator and keyword, the number already parsed into a float on the token, and only names and texts get a string.
   Names through a small pool, so the forty `phases` of a move list are one string, which also makes every dictionary
   lookup of them a reference compare first.
2. **A text without escapes is one `Substring`**, a text with them is built in a pooled buffer.
3. **`true`, `false` and `null` are words of the language**, literals to the parser. A key may still be any word, `{ for: 1 }`
   and `move.in` are fine, the parser takes a keyword where it wants a name.
4. **Line starts are kept**, the lexer knows whether it is at the start of a line without looking back.
5. **New tokens** for what the language gets below, `??`, `?.`, and template texts in backticks.
6. **Line by line, for an editor.** The state at the end of a line (inside a block comment, inside a template) is all
   the next line needs, so an editor can lex the lines that changed and stop when the state at a line start is what
   it was. `Lexer.TokenizeLine(text, start, state)`. This is what the code editor's colours need to stop lexing the whole
   file on every keystroke.

## The parser

What it does now. The tokens go into a `Queue` through a LINQ filter, peeking ahead walks the queue, the precedence of
the operators is a method a level (`ParseOr`, `ParseAnd`, ...), the nodes are `readonly struct`s stored as `IStatement`,
so every one of them is a box, and the interpreter unboxes (copies) one every time it evaluates it. The first error ends
the parse.

The plan.

1. **An array and an index**, comments skipped by the cursor. Done this round.
2. **Literals carry their value.** A number, a text or a boolean literal makes its runtime value once, at parse time,
   and hands that one out every time it is evaluated (values are immutable). Done this round, it was an allocation for
   every literal evaluated, every frame of every behaviour.
3. **Nodes become sealed classes** with their span on the base. No boxes, no copies, a node is a reference the source map
   and an editor can hold.
4. **Operators from a table** (a Pratt parser, binding powers by token), so `??`, `|>`, `in` as an operator or a host's
   own are a line in a table, not a method in a ladder.
5. **It goes on after an error.** On a mistake it skips to the next statement boundary (a `;`, a `}` that closes what
   it was in, a `let`), records the error, and keeps parsing, so an editor gets every error in the file and an outline of
   the rest of it. `ParseResult { Program, Errors }`, the old throwing `Parse` on top of it.
6. **Trivia kept on request.** The comments and the whitespace before every token, kept when asked (`ParseOptions.KeepTrivia`),
   which is what a formatter that keeps comments and an editor showing the comment over a key both need.

## The runtime

What it does now. A tree walking interpreter. Every number, vector, boolean, object made is a struct boxed into an
`IRuntimeValue`. A name is looked up through the dictionaries of every scope up to the one that has it. A call makes a scope
and an array. Included files are parsed every time they are included. A small behaviour costs 1.5 microseconds and 378
bytes a call.

The plan, in the order it pays.

1. **Include once.** Parsed programs kept by full path, with the time the file was written (or the text, for an
   `IncludeReader`), so five characters including the shared moves parse it once. Done this round.
2. **Names resolved to slots.** A pass after parsing that knows every scope (a function, a block, a for) works out,
   for every name, how many scopes up it lives and at which index, and the scopes become arrays. A name not in the
   program is a global or the host's, looked up once by name and cached on the node. This is the biggest win on calls,
   and it is also what an editor needs to say where a name was declared without guessing.
3. **One value type.** `HidlValue`, a struct of a tag, a double and an object, passed around by the interpreter and
   boxed only where it leaves (a host's `IRuntimeValue` API stays as the edge). Numbers, booleans, null and vectors up to
   four never allocate. The bigger change of the lot, every library function moves over to it, so it goes behind a
   `HidlValue <-> IRuntimeValue` bridge that lets the library move a file at a time.
4. **Frames instead of scopes for calls.** Parameters and locals in the slots above, a call is an array off a pool.
5. **A budget instead of a clock.** `Interpreter.Budget`, how many steps a program gets, checked where cancellation is.
   A game running a modder's behaviour wants "no more than this much a tick", not "no more than a second".
6. **Errors carry a stack.** The calls an error went out through, file and line each, so "isn't a function" says it was
   in `pressing`, called from `wander`, called by the game.

## The language

What the files written in it want, from going through them.

1. **`a ?? b` and `a?.b`.** Data files are full of "the cancel window of this phase, or 0 if it hasn't got one", now
   written with `has` and a conditional. `phase.cancel ?? 0` and `move.phases?.raise?.cancel`. A missing key is null
   already, `?.` makes a missing object null instead of an error. Done this round.
2. **Template texts.** `` `{name} has {health} left` ``, any expression in the braces. Texts are built with `+` and
   `text()` everywhere now, and a localisation file's `{name}` stays what it is (that is a plain text, not a template).
   Done this round.
3. **Shapes.** The one thing data files need most, a shape says what an object has, checks it, fills in its defaults and
   documents its keys in every file it is used on. Designed in `shapes.md`, which replaces the first sketch that was here.
4. **Enums of words.** Part of shapes now, `flags("standing", "crouching")` checks the texts a move's stances are.
5. **Imports by name.** `import { moves, rules } from "shared.hor";` over `include`, which an editor can follow name by
   name and which says what a file uses of another.
6. **`with`, an object with a few things changed**, `jab with { damage: 7 }`, the deep merge as an operator, for
   variants of a move without spelling out the move again.
7. **Doc comments.** A comment line that starts with `@doc` (or `@comment`) documents what is under it or at the end of
   its line, kept on the node and in the source map, shown by an editor wherever the name is. See `shapes.md`.
8. **Units, maybe.** `12f` for frames and `0.2s` for seconds would keep move data honest, a game says what a unit is
   worth. Only if shapes don't cover it.

## Extending it

1. **A library is a class.** `[HidlLibrary("math")] static class MathLibrary` with `[HidlFunction] static float lerp(float a,
   float b, float t)`, the arguments converted and checked by a source generator, the way the library is written now
   (a lambda a function, every argument fetched by hand) gone. The generated code is what is written by hand now, so it
   costs nothing at run time and trims for AOT.
2. **Binding a host's types.** `runtime.Bind<Fight>("fight")` with `[HidlExpose]` on what scripts may see, the same
   generator, instead of `Natives.Object(("health", Natives.Property(...)), ...)`. Hex's `hex` object for extensions and
   the UI's components (every `Expose` in `DefineScript`, a `Func` and an `Action` a property a component, the garbage
   of every row a list makes) go over to it.
3. **Hooks for an editor.** `IHidlHost` on the runtime for what a program does that a host wants to know about as it
   happens (a value made, a call made, a print), which the source map becomes one of.

## Services, for editors

What Hex (and anything else) asks of HIDL, done once in the language instead of by every editor with its tokens.

- **`HidlDocument`**, a text, its tokens line by line, its parse with every error, its resolved names, kept up as the
  text is edited.
- **Outline**, every declaration and key with its kind and span, from the syntax tree, not by matching tokens.
- **Go to definition and find references**, from the resolved names, includes and imports followed.
- **Completion**, the names in scope at a place, the keys siblings have (an object in an object is taken to be like its
  siblings), the members of the host's bound types, the keys of a shape.
- **Hover**, what a name is, its doc comment, its value from the last run.
- **Diagnostics**, the parse errors (all of them), names nobody declared, keys a sibling hasn't got that are a letter off
  one most of them have, paths in texts that aren't files, shapes not matched.
- **Formatting** with the trivia kept.

## In what order

1. This round, above.
2. Lexer tokens as places, the parser on an array with literal values, include cache, `??`, `?.`, templates. This
   round too, measured.
3. Nodes as classes, errors recovered from, trivia. Then the outline and diagnostics move from Hex into the services.
4. Names to slots and frames. Measured on Fighter2D's behaviours.
5. `@doc`, then shapes (`shapes.md`), the biggest thing for the files, with Hex's diagnostics, hover, values, table
   and completion built on them.
6. The library and binding generator, the UI's `Expose` moved over.
7. `HidlValue`, the last and largest.
