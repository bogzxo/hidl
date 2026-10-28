# HIDL, the reference

HIDL is the little language everything in Horizon is written in that isn't C#. Layouts, skins, texts and their
translations, Fighter2D's moves and behaviours, Hex's settings, keys, sessions and extensions. It looks like JavaScript
that went to Python's school and came out a C family language, and it's built for two jobs at once. Most of the time
it's data with a bit of maths in it, a file that ends up as an object a game or an editor reads. Some of the time it's
a script, a button's handler or a dummy's behaviour, run over and over. This is what it can do as of October 2026, for
whoever has to write it.

`README.md` is the short version and `docs/plan.md` is where it goes next. This one is everything in between.

## A file

A file is statements one after the other. A semicolon after one is welcome and never needed, so write them or don't,
just don't mix the two in one file because it looks like a mess.

```js
// a comment to the end of the line
/* and one that goes
   over several */
let speed = 3
let doubled = speed * 2;
```

What a file comes to is its last value, and everything it declares at the top is there for whoever ran it to read.
That's how a layout file is a layout. It declares `layout` and the UI takes it from there.

## Values

There are eight kinds of value, and `typeof(x)` says which one you've got.

| what | written | `typeof` |
| --- | --- | --- |
| a number | `3`, `1.5`, `.5` | `"number"` |
| a text | `"hello"`, `` `hi {name}` `` | `"text"` |
| true or false | `true`, `false` | `"boolean"` |
| nothing | `null` | `"null"` |
| a list | `[1, 2, 3]` | `"list"` |
| an object | `{ hp: 100, name: "Ryu" }` | `"object"` |
| a vector | `vec(1, 2)` up to `vec(1, 2, 3, 4)` | `"vector"` |
| a function | `func(x) { return x * 2; }`, `x => x * 2` | `"function"` |

Numbers are 32 bit floats, there's no separate whole number, and `7 / 2` is `3.5`. A number is digits with one dot at
most, `0` to `9` and nothing else (the digits of other scripts used to sneak in and blow the lexer up on a png opened
as text). No hex, no exponents, nobody's asked.

What counts as true in an `if` is everything except `null`, `false`, `0`, an empty text and an empty list. Two values
are `==` when they're numbers, texts, booleans or vectors that hold the same thing, and anything else only when it's
the very same thing, two objects that look alike aren't equal.

## Names

`let` declares a name, `const` declares one that can't change, and plain `=` changes one that's there.

```js
let health = 100
const MAX_HEALTH = 100
health = health - 30
health -= 10        // += -= *= /= %= all work the way you'd think
let nothing_yet     // a let without a value is null
delete nothing_yet  // and a name can be taken away again
```

A `let` (or a `const`) can take an object or a list apart and make a name of every piece in one go. Names only, no
renaming and no defaults inside the brackets, that's where the JavaScript version stops being readable. A key that isn't
there is `null`, and so is an item past the end. A vector comes apart like a list.

```js
let move = { damage: 8, startup: 4, recovery: 12 }
let { damage, startup } = move                 // damage is 8, startup is 4
let [x, y] = vec(10, 20)                       // x is 10, y is 20
let [first, second, third] = ["jab", "kick"]   // third is null
```

The words of the language can't be names, those are `let const func if else while do for in return break continue
delete vec true false null`. Any word is still fine as a key of an object though, `{ for: 1 }` and `move.in` are both
perfectly legal, the parser only cares where it's looking for a name.

A name is looked up in the scope it's used in, then the ones round it, then the global one where the library lives. A
`func` or a loop body gets a scope of its own, so a `let` inside one stays inside it.

## Operators

From the loosest to the tightest.

| | operators | |
| --- | --- | --- |
| assignment | `=` `+=` `-=` `*=` `/=` `%=` | right to left |
| conditional | `a ? b : c` | either side can be another one |
| nullish | `a ?? b` | `a` unless it's null, `b` isn't even worked out otherwise |
| or | `\|\|` (or `\|`) | only looks right when it has to |
| and | `&&` (or `&`) | same |
| equality | `==` `!=` | |
| comparison | `<` `>` `<=` `>=` `in` | numbers and texts, and `in` for whether it's there |
| additive | `+` `-` | `+` with a text on either side joins them |
| multiplicative | `*` `/` `%` | |
| unary | `!` `-` `+` | |
| member, index, call | `a.b` `a?.b` `a[b]` `a?.[b]` `a(b)` `a?.(b)` | as many as you like, in any order |

`??` sits looser than `||` on purpose, so `a.b ?? c || d` is `a.b ?? (c || d)`. Vectors add, subtract and scale with
the arithmetic ones, and a number on one side of a vector does the same to every component.

`in` asks whether something is in something else. A key in an object, an item in a list (by `==`), a piece in a text.
It's `has` with the arguments the way you'd say it, and nothing is ever in `null`. It binds like `<`, so `!` needs
brackets round the lot to mean not in, and `1 + 1 in [2]` is `2 in [2]`.

```js
let jab = { damage: 5 }
let hurts = "damage" in jab                    // true
let blank = " " in [" ", "\t", "\n"]           // true, one of these
let missing = !("startup" in jab)              // true, the brackets matter
let shouts = "!" in "KO!"                      // true
```

`?.` goes in front of an index and a call too. `list?.[0]` is `null` when the list is, and `on_hit?.(3)` only calls
`on_hit` when there's one to call, which is how a handler that might not be set gets called.

```js
let state = { drag: null, on_drop: null }
let start = state.drag?.[0]                    // null, nothing's being dragged
state.on_drop?.(start)                         // nothing happens, nobody listens
```

## Texts

Double quotes, with the usual escapes `\"` `\\` `\n` `\t` `\r` `\0`, and `\u` with four hex digits for a character by
its number (`"\u001b[2J"` clears a terminal, `"é"` is é). A `\u` without its four digits is a `u`, as it always was.

```js
let name = "Ryu"
let line = name + " has " + 70 + " left"      // a text on either side of + joins the lot
```

A template is a text in backticks with expressions in braces, and it's what you want nine times out of ten. `\{` is a
brace that's only a brace.

```js
let health = 100
let line = `{name} has {health - 30} left`     // "Ryu has 70 left"
let braces = `a literal \{ brace`
```

Templates are cheap, a label's text can be one and get evaluated every tick without anyone noticing.

Everything a text can do hangs off the text itself, `"abc".upper()` and `name.length`.

| | |
| --- | --- |
| `length`, `count` | how many characters |
| `upper()`, `lower()`, `trim()`, `trim_start()`, `trim_end()` | what they say |
| `contains(x)`, `starts_with(x)`, `ends_with(x)` | true or false |
| `index_of(x)`, `last_index_of(x)` | where it is first or last, `-1` for nowhere |
| `count_of(x)` | how many times `x` is in it |
| `replace(a, b)` | every `a` swapped for `b` |
| `split(sep)` | a list of the pieces, trimmed, empty ones dropped, `","` if you don't say |
| `lines()`, `chars()` | a list of its lines (any line ending, empty ones kept) or of its characters |
| `substring(from, to)` | a piece of it, `to` is the end if you leave it off |
| `pad_left(n, c)`, `pad_right(n, c)` | padded to `n` with `c`, a space if you leave it off |
| `repeat(n)` | `n` of it |
| `is_digit()`, `is_letter()`, `is_upper()`, `is_lower()`, `is_space()`, `is_word()` | whether all of it is that, false for an empty text, `is_word` is letters, digits and `_` |

The `is_` ones are what every hand written parser wants, a character out of a text is a text of one.

```js
let word = ""
for (c in "jab2 kick") {
    if (c.is_space()) break
    word += c
}
let digits = "jab2".chars().filter(c => c.is_digit()).join("")   // "2"
```

## Lists

```js
let moves = ["jab", "kick", "throw",]          // a comma after the last item is fine
let more = [...moves, "taunt"]                 // ... spreads another list in
let first = moves[0]
let last = moves[-1]                           // a negative index counts from the end
moves[1] = "sweep"
```

An index that isn't there is an error rather than a quiet `null`, on purpose, a typo in a move list should shout.
`delete moves[1]` takes an item out, the ones after it move up, and `delete moves[-1]` the last one.

The list does its own work too, `moves.push("taunt")`, `moves.count`.

| | |
| --- | --- |
| `count`, `length`, `first`, `last` | what they say |
| `push(x)`, `pop()`, `insert(i, x)`, `remove(x)`, `remove_at(i)`, `clear()` | change the list itself |
| `contains(x)`, `index_of(x)`, `last_index_of(x)` | by `==` |
| `join(sep)` | the items as one text, `", "` if you don't say |
| `reverse()`, `sort()`, `sort(f)` | in place, `sort(f)` sorts by what `f` says about each item |
| `copy()`, `slice(from, to)` | a new list |
| `map(f)`, `filter(f)`, `any(f)`, `all(f)`, `each(f)` | the usual suspects |
| `find(f)`, `find_index(f)` | the first item `f` says yes to and where it is, `null` and `-1` for none |
| `reduce(f, start)` | the items folded into one, `f(so_far, item)`, from the first item if there's no start |
| `sum()`, `sum(f)` | the items added up, or what `f` says about each |
| `unique()` | the items without the repeats, in the order they came |
| `flat()` | a list of lists as one list, one level down |

```js
let damage = [8, 12, 30, 5]
let big = damage.filter(d => d > 10)           // [12, 30]
let total = damage.sum()                       // 55
let first_big = damage.find(d => d > 10)       // 12
let worst = damage.reduce((a, b) => math.max(a, b))   // 30
let kinds = ["jab", "kick", "jab"].unique()    // ["jab", "kick"]
```

## Objects

An object is keys and values. A key is any word, or a text in quotes for one with spaces in it.

```js
let ryu = {
    name: "Ryu",
    health: 100,
    "walk speed": 2.5,
}

let health = 80
let short = { health }                         // { health: health }
let jab = { damage: 5, startup: 3 }
let heavy = { ...jab, damage: 12 }             // ... puts the other one in, later keys win
```

A key in square brackets is worked out, whatever it comes to is the key, and `delete` takes a key out again. Between
them and a spread, an object of one key or a copy without one is a line each.

```js
let stance = "crouch"
let routes = { [stance]: "sweep", stand: "jab" }   // { crouch: "sweep", stand: "jab" }
let without = { ...heavy }
delete without.startup                         // a copy without startup, heavy keeps it
let standing = { ...routes }
delete standing[stance]                        // and a key in a variable goes the same way
```

`ryu.name` and `ryu["walk speed"]` read a key, and assigning to either writes one. Reading a key that isn't there is
`null`. Reading a key of `null` is an error, unless you ask nicely with `?.`, which is `null` when what's on its left is.

```js
let move = { phases: { raise: { cancel: 4 } } }
let cancel = move.phases?.raise?.cancel ?? 0   // 4, and 0 if any of it were missing
```

That pair, `?.` and `??`, is what data files are full of, defaults without a wall of `if`s.

`merge(a, b, ...)` is one object over another all the way down. Objects in both get merged in turn, anything else
`b` has replaces what `a` had, and nothing you hand in is changed. It's how a character's own moves go over the shared
ones, or a game's options over its defaults, without spelling every key out again.

```js
let shared = { jab: { damage: 5, startup: 3 }, kick: { damage: 8 } }
let own = { jab: { damage: 6 } }
let moves = merge(shared, own)                 // jab is { damage: 6, startup: 3 }, kick is as it was
```

`keys(obj)`, `values(obj)`, `has(obj, "key")` and `len(obj)` do what you'd guess, `entries(obj)` is a list of
`[key, value]` pairs, and `for (key in obj)` goes over the keys. `for (key, value in obj)` has what each one holds as
well, which is the loop you actually wanted.

## Vectors

`vec` takes one to four numbers, and one number is a square one, `vec(8)` is `vec(8, 8)`. A vector inside a vector
hands its numbers over, so `vec(position, 1)` is a vec3 out of a vec2.

```js
let position = vec(10, 20)
let colour = vec(1, 0.5, 0.2, 1)
let x = position.x                             // x y z w, or r g b a for colours
let moved = position + vec(5, 0) * 2           // vec(20, 20)
position.y = 0                                 // a vector is a value, the whole thing goes back changed
```

Hex draws a swatch next to every colour written like this and lets you pick another, which is reason enough to write
colours as `vec`s.

## Control flow

```js
if (health <= 0) {
    print("KO")
} else if (health < 30) {
    print("danger")
} else {
    print("fine")
}

let i = 0
while (i < 3) { i += 1; }
do { i -= 1; } while (i > 0)

for (move in moves) { print(move); }           // a list's items
for (key in ryu) { print(key); }               // an object's keys
for (c in "abc") { print(c); }                 // a text's characters
for (n in range(5)) { }                        // 0 to 4, range(a, b) and range(a, b, step) too
```

Two names in a `for` and the first is where you are, the key of an object or the place in a list or a text, Go's and
Python's `enumerate` order. One name is what it always was.

```js
for (key, value in ryu) { print(key, value); } // name Ryu, health 100, ...
for (i, move in moves) { print(i, move); }     // 0 jab, 1 sweep, ...
```

One statement needs no braces, after an `if`, an `else`, a `while`, a `for` or a `do`, the way C# is written all over
the engine. A `{` still starts a block, so nothing that was written with braces reads any differently, and an `else`
goes with the nearest `if` like everywhere else.

```js
func verdict(health) {
    if (health <= 0) return "KO"
    else if (health < 30) return "danger"
    return "fine"
}
let shown = 0
for (move in moves) shown += 1
while (shown > 2) shown -= 1
```

`break`, `continue` and `return` do what they do everywhere else. A `return` at the top of a file ends the file.

`if` and the loops are expressions as much as statements, every block hands back its last value, so this works.

```js
let label = if (health > 50) { "healthy" } else { "hurt" }
```

`a ? b : c` reads better for the short ones, but it's there when you need a few lines.

## Functions

```js
func add(a, b) {
    return a + b
}

let double = func(x) { return x * 2; }        // a function is a value like any other
let twice = double(add(1, 2))                  // 6
```

An arrow is the same function without the ceremony, and it's what a callback wants. One parameter needs no brackets,
none or several do, and what comes after the arrow is what it hands back. A block after the arrow is a body like any
function's, `return` and all, so an object straight after an arrow goes in brackets or it's taken for a block.

```js
let triple = x => x * 3
let sum = (a, b) => a + b
let seven = () => 7
let describe = (move) => {
    let speed = move.startup < 5 ? "fast" : "slow"
    return `{move.name} is {speed}`
}
let wrap = x => ({ x })                        // the brackets make it an object
let adder = n => x => x + n                    // adder(5)(10) is 15
```

It's the very same kind of value as `func(x) { }`, a host can't tell them apart, so anything that takes a function takes
an arrow.

A parameter can have a default, used when the argument isn't handed over or is `null` (which is what a host hands for
what it hasn't got). It's worked out when the call happens, in the function's own scope, so it can use the parameters
before it.

```js
func box(at, size = vec(16), colour = "white") {
    return { at, size, colour }
}
let small = box(vec(0, 0))                     // size vec(16, 16), colour "white"
let wide = (w, h = w * 2) => vec(w, h)         // wide(3) is vec(3, 6)
```

A function you don't hand enough arguments gets `null` for the rest (or its default), and extras are ignored, no arity
errors to fight.
A function remembers the scope it was made in, and every turn of a loop gets a scope of its own, so a function made in
a loop keeps the value of its turn and not the last one. Declaring a `func` again with the same name replaces it, which
is how one file fixes up another one it included.

There's no `this`. A function in an object is a key that happens to be a function, call it as `obj.go()` and hand it
what it needs.

## Files that talk to each other

`include("other.hor")` runs another file, next to the one asking, and hands back an object of everything it declared.
An included file is parsed once for the same text, and a file that includes itself is caught rather than looping
forever.

```js
let shared = include("shared_moves.hor")
let moves = merge(shared.moves, { jab: { damage: 6 } })
```

A line that starts with `#` is for whoever runs the file, not for the file. They're read before anything else.

```c
#define layout                  // a name, defined
#define version 3               // a name with a value, the rest of the line
#undef version
#if layout                      // what's between kept only while the name is defined
#elif !debug                    // ! for not defined
#else
#endif
```

`#ifdef` and `#ifndef` are there too. The lines left out are blanked rather than taken out, so an error further down still says the
right line. The first `#define` is the one an editor goes by, `#define layout` is how Hex knows to open a file in its
layout designer. A script asks with `defined("debug")` and `definition("version")` (null for one that isn't defined),
and the host hands some in before running anything.

## Documentation that goes where the value goes

A comment that starts with `@doc` (or `@comment`) documents the declaration, key or field right under it, or the one at
the start of the line it's at the end of. Several in a row are one piece of documentation. A plain comment between it
and what it documents is fine, a blank line isn't.

```js
// @doc How long a jab takes to come out, in frames
let jab_startup = 3

let jab = {
    damage: 5,   // @doc What a clean hit takes off
}
```

Hex shows it in the hover of the name anywhere it's used, in completion, and in the outline, and the language server
hands it to VS Code the same way. Write them on anything somebody else will read, which is everything.

## The library

Declared in the global scope, so it's there in every file.

| | |
| --- | --- |
| `print(...)` | writes its arguments with spaces between them, to the host's output, and hands the line back |
| `text(x)`, `text(a, b, ...)` | a value as text, or several joined, the cheap way to build a line |
| `number(x)`, `number(x, fallback)` | a text or boolean as a number, `null` (or the fallback) if it isn't one |
| `format(x, digits)` | a number with so many digits after the point, two if you don't say, for a HUD that shouldn't jitter |
| `format(text, a, b, ...)` | the text with `{0}` swapped for `a`, `{1}` for `b` and so on, or `{name}` for a key when the one value is an object |
| `typeof(x)`, `len(x)` | what it is, and how long a list, text or object is |
| `keys(o)`, `values(o)`, `entries(o)` | an object's keys, its values, and both as a list of `[key, value]` |
| `has(o, k)` | whether it has a key (or a list an item, or a text a piece), the same as `k in o` |
| `range(n)`, `range(a, b, step)` | a list of numbers, `0` up to `n` or `a` up to `b` |
| `merge(a, b, ...)` | objects deep merged, see above |
| `include(path)` | another file, see above |
| `defined(n)`, `definition(n)` | what `#define` or the host defined |
| `error(message)` | stops the program with your own message |
| `to_text(x)` | a value written back out as the HIDL that makes it |
| `version` | the language's version, `"0.1.0"` |

```js
let line = format("{0} of {1}", 3, 5)                                   // "3 of 5"
let card = format("{name} has {health}", { name: "Blocky", health: 40 }) // "Blocky has 40"
let hud = format(12.345, 1)                                             // "12.3"
```

A template does the first two better most of the time, `format` is for a text that comes from somewhere else, a
translation say, with the places in it.

And `math`, which works on vectors component by component wherever that makes sense.

| | |
| --- | --- |
| `math.pi`, `math.tau`, `math.e` | the constants |
| `abs floor ceil sqrt sign sin cos tan asin acos atan exp log` | one number (or vector) in, one out |
| `math.atan2(y, x)`, `math.pow(a, b)` | two numbers in |
| `math.round(x)`, `math.round(x, digits)` | to whole numbers or so many digits, halves away from zero |
| `math.min(...)`, `math.max(...)`, `math.clamp(x, lo, hi)`, `math.lerp(a, b, t)` | the usual |
| `math.to_radians(x)`, `math.to_degrees(x)` | angles |
| `math.length(v)`, `math.distance(a, b)`, `math.normalize(v)`, `math.dot(a, b)` | vector maths |
| `math.random()`, `math.random(n)`, `math.random(a, b)` | `0` up to `1`, `n`, or `a` up to `b`, whole numbers if both ends are whole |
| `math.chance(p)`, `math.pick(list)`, `math.pick(a, b, ...)` | true with a chance of `p` (`0.25` is one time in four), and one item at random |

## When it goes wrong

An error says where it happened, the file, the line and the column, all the way out of includes without every include
on the way wrapping it again. A mistake the parser doesn't like says what it was looking for (`',' or '}' after the
property`), and Hex's editor parses past every mistake so a file shows all of them at once instead of one at a time.

A name nobody declared still reads as `null`, scripts lean on that and it can't change without breaking them. What did
change is what it says when that goes wrong. Calling a name that was never declared, or reaching into one, says it was
never declared and which declared name it most likely meant, instead of a puzzled line about `null` somewhere else.
A member a list or a text hasn't got says the nearest one it has.

```js
func damage_of(move) { return move.damage; }
// damage_off(jab)   There is no 'damage_off' to call, nothing of that name was declared. Did you mean damage_of?
// [1].pussh(2)      A list has no 'pussh'. Did you mean push? It has count, length, first, ...
```

A program can't take the host down with it. Calls go 256 deep at the most, because a stack overflow can't be caught in
.NET and a typo in a recursive function used to kill Hex outright. A loop that runs a million turns is taken to have
run away and stopped with an error. A host can cancel a program from outside at any point, which is how Hex runs a file
on every keystroke without ever waiting on an old run.

## From C#

```csharp
var runtime = new HIDLRuntime();
runtime.Define("debug");                                   // what #if and defined() see
runtime.Output = line => Log.Info(line);                   // where print goes, the console by default

runtime.GlobalScope.Declare("fight", Natives.Object(
    ("frame", Natives.Property(() => new NumberValue(frame))),
    ("hit", Natives.Function("hit", args => { Hit(args.Number(0)); return Values.Null; }))), true);

IRuntimeValue result = runtime.RunFile("Assets/fighters/ryu.hor");
IRuntimeValue moves = runtime.UserScope.Lookup("moves");   // whatever the file declared
runtime.Invoke(handler, new NumberValue(3));               // call a function a script handed over
```

`Run(text)` and `RunFile(path)` throw with what went wrong, `Evaluate(text)` hands back whether it worked and the result
as text, for a REPL. `HIDLRuntime.Parse` and `ParseAll` give the tree without running anything (`ParseAll` keeps going
after a mistake, for an editor). `Interpreter.Cancellation` stops whatever's running. `IncludeReader` reads included
files from somewhere other than the disk, a pack or a download.

Writing HIDL back out is `HIDLWriter.Write(value)` for a value and `HIDLDocument` for a whole file with comments and
defines, which is what every settings, keys and session file Hex keeps is written with, so they all come out the same.
A whole file has a comma after the last key of every object and the last item of every list that goes over lines, so
adding one at the end is one new line in a diff and not two. `HIDLWriter.Write(value, trailingCommas: true)` writes a
value that way too, the plain one doesn't, it's what goes into a file somebody wrote by hand. Hex's layout designer
writes its layouts the same way.
`Sources` set to a `HIDLSourceMap` before a run writes down where every key, item and declaration was written, through
includes and spreads, which is how an editor goes from a value back to its text.

`HIDLInterop.Declare` gives a script a `dotnet` object that loads assemblies, finds types and makes objects, reflection
and nothing else. It's off unless a host turns it on, because a script can then do anything .NET can, and a native AOT
build can't load an assembly at run time at all.

## Writing it in Hex

Hex is HIDL's IDE, and it's where all of this pays off.

- Colours as you type, by the same lexer the runtime uses, so what's coloured is what runs.
- Every mistake under the code at once, and what a run of the file got wrong at the line it went wrong on.
- Completion of names, keys, components of a layout and the library, with the `@doc` of each.
- Hovers with what a name is and its documentation.
- `F12` to where a name is declared, across included files, and `Alt+F12` to peek at it in a window over the code
  without losing your place. A parameter, a name of a `for` or a `let` inside a block goes to the one round where
  it's used, the nearest one, and a `func` and a name taken apart with `let { a, b }` are found too.
- The parameter each argument of a call goes to written faintly in front of it, `add(a: 1, b: 2)`, for the functions
  a file declares, `func`s and the arrows a `let` holds alike. Inlay hints, and an argument that's already a name the
  same as its parameter goes without. A parameter with a default says its name, the signature over a call being typed
  says the default too, `box(at, size = vec(16))`.
- Arrows, defaults, the names of a `for` and the names a `let` takes apart coloured as what they are, by the grammar
  VS Code uses as much as by Hex.
- Run a file and the value every `let` came to is written after its line, `= 96`, and the Values side shows the lot.
- A swatch after every `vec` colour to click and pick another, and art previews next to the pictures a layout names.
- `#define layout` files open in the layout designer, and a double click on a component edits its text in the
  localisation file it comes from.
- `TODO` and `FIXME` lit in comments, a minimap, the outline, folding, breadcrumbs, the lot.

The same smarts go to any other editor through Hex's language server, `Hex --lsp`, and `editors/vscode` is the syntax
highlighting for VS Code.

## What's new lately

- Arrows, `x => x * 2` and `(a, b) => { ... }`, the same function value `func` makes.
- One statement without braces after an `if`, `else`, `while`, `for` or `do`.
- `in` as an operator, `key in object`, `item in list`, `part in text`.
- Two names in a `for`, the key or the place first.
- Keys in brackets, `{ [name]: value }`, and `delete thing.key` and `delete list[2]`.
- Defaults for parameters, worked out when the call happens.
- `let { a, b } = thing` and `let [a, b] = list`.
- `?.` before an index and a call, `list?.[0]`, `handler?.()`.
- The texts' `is_` checks, `trim_start`, `trim_end`, `lines`, `chars`, `last_index_of` and `count_of`, the lists'
  `find`, `find_index`, `reduce`, `sum`, `unique`, `flat` and `each`, `format` with a text and `entries`.
- `\u` and four hex digits in a text.
- A misspelt name says it was never declared and what was most likely meant.
- Less garbage. The lexer makes a third of what it did, sprite sheets parse in half the time, a call of a list's or
  a text's own method doesn't make a function first, and a small scope makes no dictionary.
- Files Hex writes whole (settings, keys, sessions, tasks, layouts) have a comma after the last of everything that goes
  over lines, so they diff a line at a time.
- `??` and `?.`, so data files stop being walls of `if`s.
- Templates in backticks.
- `merge`, deep, nothing changed.
- `true`, `false` and `null` are words of the language now, and any word is a key.
- Errors say where, file and all, through includes.
- The source map, values back to where they were written.
- Safe to run while typing, 256 calls deep at most, a million turns a loop, cancellable.
- `@doc` comments that follow the value everywhere it's used.
- Only `0` to `9` are digits, the lexer doesn't fall over on other scripts' digits any more.
- A lexer that makes next to no garbage, a pooled name per word and a substring per plain text.
- In Hex, parameter inlay hints and peek definition (`Alt+F12`), both knowing arrows and defaults, and `F12` going to
  the parameter or loop name round where a name is used.

## What's next

Shapes, which say what a value has to have once and get the check, the defaults, the hovers and the editor's
completion off it, are designed in `docs/shapes.md` and not in the language yet, and they're still the biggest thing the
data files are waiting on. The slots, frames and one value type of `docs/plan.md` are the big rewrite of the runtime,
what this round did to calls and loops got the garbage down without it, so it waits for a number that says it's worth
it. `match` was looked at and left out, nearly every chain of `else if`s in the code mixes in another test, and `in`
takes the ones that only pick between words. A line by line lexer for the editor and the rest are in `docs/plan.md`.
Until shapes land, a missing key is `null` and a misspelt one is a key nobody reads, so lean on `?.` and `??` and write
`@doc`s.
