# Shapes, and documentation that goes where the value goes

A move list is right when every move in it has what a move has. Nothing in HIDL says what that is, Fighter2D's files say
it in a comment at the top and the game finds out the hard way, a missing key is null and a misspelt one is a key nobody
reads. Hex guesses it from the siblings. A shape says it outright, once, and everything else (the check, the defaults, the
editor's completion and hover, the table's columns) comes off it.

The first sketch in `plan.md` (`shape Phase = { cancel?: number }`, `{ ...: Move }`) had the right idea and the wrong
pattern. A shape that only says no is a lint. This one does four things, and the four are why it's worth having.

1. **It checks.** A key a shape doesn't have is an error at that key with the nearest one it does have, a value of the
   wrong kind is an error at the value, a key it needs that isn't there is an error at the object.
2. **It fills in.** A field with a default is filled in where it's missing, so the value a game reads is whole and the
   `?? 0` all over a game's loading code goes. The shape is the schema and the defaults in one place.
3. **It documents.** A `// @doc` on a field of a shape is the documentation of that key in every value checked against
   it, in every file, without the files saying anything. Hovering `cancel` in any move of any character shows what
   `cancel` is.
4. **It's a value.** A shape is declared like anything else, included from another file, merged, handed in by a host. A
   game ships `shapes.hor` next to its data or declares its shapes from C#, and the data files never change to use them.

## Documentation, first

The smallest part and the one everything else leans on. A comment line that starts with `@doc` is documentation, of the
declaration, key or field right under it, or of the one it is at the end of the line of.

```
// Greeting messages inspired by that stupid American movie...
// @doc The list of greetings, one picked at random every time Hex starts
let welcome_greetings = [ ... ];

let jab = {
    damage: 5,      // @doc how much of the bar it takes, out of 100
};
```

`@comment` is the same thing. More than one `@doc` line in a row is one documentation, their lines joined. A plain
comment between them and the declaration is fine, a blank line isn't (the documentation is of the next thing, not of
something further down). The lexer keeps the text of a doc comment on its token (and nothing for a plain comment), the
parser hangs it on the declaration, key or field, the source map keeps it with where the thing was written, and
`HIDLDocs.Read(tokens)` lists every documented name of a file without running it, for an editor indexing a workspace.

An editor shows it wherever the name is, the declaration, every use of it in the file, every use in a file that
includes this one (the source map follows includes), and every use anywhere in the workspace by its name (the index),
and as the tooltip of the value.

## Declaring a shape

```
// @doc One phase of a move, the art it plays and the frames that matter
shape Phase {
    // @doc The tag of the move's art this phase plays
    animation: text,

    // @doc From which frame of the phase another move may cut it short
    cancel: number = 0,

    // @doc Until which frame it can't be let go of
    commit: number?,

    loop_while: "always" | "held" | "never" = "never",
    status: text?,
}

shape Move {
    stances: flags("standing", "crouching", "jumping", "falling"),
    phases: { [text]: Phase },
    cancellable: bool = false,
    priority: int(0, 100) = 10,
    input: text?,
    ...
}
```

A field is a key, a type, and maybe a default. It is needed unless its type says it can be null (`text?`) or it has a
default. A shape is closed, a key it doesn't have is an error, unless it ends with `...`, which lets anything else
through unchecked (a move list that's halfway to having a shape). The `@doc` over a field, or at the end of its line, is
the documentation of that key wherever it is checked.

The types.

| type | is |
| --- | --- |
| `any` | anything |
| `text`, `number`, `bool`, `func` | what they say |
| `int`, `int(least, most)`, `number(least, most)` | a whole number, a number between two |
| `vec`, `vec2`, `vec3`, `vec4`, `colour` | a vector of any size or of that size, `colour` a `vec4` |
| `"a" \| "b"`, `1 \| 2` | one of those |
| `flags("a", "b")` | a text of those words joined by `\|`, `"standing \| crouching"`, every word one of them |
| `path("ase")` | a text naming a file, its extension that one. Checked by an editor (the file is there), not by the runtime |
| `[T]` | a list of `T` |
| `{ [text]: T }` | an object whose every value is a `T`, any key |
| `{ key: T, ... }` | an object of that shape, written in place |
| `Name` | the shape of that name |
| `T?` | `T` or null, and not needed |
| `A \| B` | either |

## Using one

```
let moves: { [text]: Move } = { jab: { ... }, kick: { ... } };
let phase = check(value, Phase);       // the value with its defaults, or an error
let fine = matches(value, Phase);      // true or false, nothing filled in
```

A `let` with a type after its name checks its value as it is made, and the value it holds is the one with the defaults
filled in. `check` and `matches` are the same from anywhere. A host does it from C#, `runtime.Check(value, "Move")`, or
says which shape a declaration of a file it loads must have, `runtime.Expect("moves", "{ [text]: Move }")`, which is how
a game puts shapes on files that don't mention them. Hex does that to an extension's `let extension = { ... }`.

## What goes wrong, and where

A game wants the first thing wrong to stop it, with where it is. An editor wants everything wrong, each where it is.

- `HidlShapeException` (a `HidlRuntimeException`) is thrown at the first problem, placed at the key or value that's wrong
  (the source map knows where every key and value was written) or at the `let` when there is no map.
- With `runtime.ShapeProblems` set to a list, nothing is thrown, every problem goes in it with where it is, and the value
  is checked as far as it can be and filled in where it can be. That's how Hex underlines all of them.

The messages say what was meant. `cancle isn't a key of Phase, did you mean cancel?` (the nearest key by letters, a
couple off at the most). `stances has "standin", Move's stances are standing, crouching, jumping, falling`.
`Phase needs animation`. `priority is 150, Move's priority is 0 to 100`.

## What an editor gets out of it

- **Diagnostics**, every problem where it is.
- **Hover** on a key, its own `@doc` or its shape's, its type, its default if it was filled in.
- **Completion** inside an object checked against a shape, the keys it hasn't got yet, each with its type and doc, and
  inside a text whose field is a union or flags, the words it can be.
- **Values** show a shaped object with its shape's name, and a filled in default dimmed with "from Phase".
- **The table** of a shaped object of objects has the shape's fields as its columns, in the shape's order, the defaults
  dimmed where a row didn't say.
- The sibling heuristic goes quiet for an object that has a shape, the shape knows better.

## How it's built

- The lexer gets `shape` as a word of the language and keeps the text of doc comments.
- The parser reads `shape Name { field: type = default, ..., ... }` into a `ShapeDeclaration` and a type after a let's
  name into its `TypeExpression`, a little language of its own (names, `?`, `|`, `[T]`, `{ [text]: T }`, calls for the
  ranges, flags and paths, literals).
- A `ShapeValue` is what declaring one makes, a value like any other, with its fields (name, type, default, doc, span).
  Types are resolved by name when the shape is used, not when it is declared, so shapes can name each other in any order.
- `HIDLShapes.Check(value, type, sources, problems)` walks the value and the type together, fills in defaults (a default
  is evaluated where the shape was declared, every time it is used, so a list default isn't shared), and records in the
  source map which field of which shape every key was checked against, which is where hover and the values get the doc
  and the shape's name from.

## In what order

1. `@doc`, the lexer, the parser, the source map, `HIDLDocs.Read`, Hex's hover and tooltips and its workspace index.
2. Shapes in the language, declaring, checking, filling in, the problems, `check` and `matches`, `Expect` for a host.
3. Hex, the diagnostics, hover, the values and the table, completion of a shape's keys.
4. Hex puts a shape on every extension's `let extension` from the start.
5. Fighter2D gets `shapes.hor`, its moves, characters and maps described, which is bogz's to say yes to, it changes what
   the game reads (the defaults).
