# HIDL, context for coding agents

Paths are relative to `horizon/Horizon.HIDL/` (the submodule of the engine repo, own repo `bogzxo/hidl`, branch `development`) unless they start with `horizon/`, `hex/` or `Fighter2D/`, which are siblings in the workspace root. Top-level `hidl/` and `dusk/` are stray checkouts, ignore them.
`path:line` refs were checked against the code when this was written; `~` marks approximate ones. Lines drift, grep the symbol when a ref misses.
Nothing here was run, it is all read from source.

## 1. What it is

HIDL is a small tree-walking interpreted language (JS-like, C-family flavour). Everything in Horizon that is not C# is written in it: UI layouts, skins, themes, localisation, Fighter2D move lists and dummy behaviours, Hex settings/keys/sessions/extensions, Hex `tasks.hor`. Two uses: data files (a file whose declarations become objects a host reads) and scripts (handlers, behaviours, automations a host calls repeatedly).

- Project `Horizon.HIDL.csproj`: net11.0, `PublishAot=True`, `InvariantGlobalization`, nullable. Embeds `editors/vscode/syntaxes/hidl.tmLanguage.json` (read via `HIDLGrammar.Read/Open`, `HIDLGrammar.cs:18`).
- Namespaces: `Horizon.HIDL` (runtime, writer, serializer, document), `.Lexing`, `.Preprocessing`, `.Parsing`, `.Runtime`, `.Library`, `.Interop`.
- **`.hor` IS HIDL.** `HIDLGrammar.Extensions = [".hor", ".jab", ".hex-workspace"]` (`HIDLGrammar.cs:13`). Root `tasks.hor`, `hex/tasks.hor`, `horizon/tasks.hor`, `Fighter2D/tasks.hor` are HIDL files.
- Docs: `README.md` (short), `docs/reference.md` (599 lines, the language, authoritative), `docs/plan.md` (future runtime rewrite), `docs/shapes.md` (design only, NOT implemented). `hex/docs/layouts-and-hidl.md` and `hex/docs/extensions.md` cover the IDE side.
- `HIDLRuntime.VERSION = "0.1.0"` (`HIDLRuntime.cs:22`), global `version`.

## 2. Language cheat-sheet

### 2.1 Lexical
- Comments: `// to end of line`, `/* ... */` (not nested). `// @doc text` or `// @comment text` (also in a block comment) is documentation, see 2.9. Only plain `""` strings, no single quotes (a `'` is a lexer error: "There is no making sense of ..."), `Lexer.cs:349`.
- Identifiers: `char.IsLetter` or `_` first, then letters, digits, `_` (`Lexer.cs:331`). Numbers: ASCII digits, one dot at most, `1.5`, `.5` (`Lexer.cs:315`; `10.` is the number 10 then a Dot, a dot only joins a number when a digit follows); no hex, no exponents, no `1_000`. All numbers are float32.
- Strings `"..."`: escapes `\" \\ \n \t \r \0 \uXXXX`; any other `\x` is `x` (`Lexer.Unescape`, `Lexer.cs:382`). A real newline inside `"..."` is allowed in strict mode (multi-line text); lenient mode (editors) ends the string at the line end.
- Templates in backticks: `` `{name} has {hp - 30}` ``, any expression in `{}` (nested braces counted), `\{` a literal brace (`Lexer.cs:215`, `Parser.ParseTemplate`, `Parser.cs:936`). Template holes are lexed/parsed with real file positions so errors point at them. No tagged templates.
- Operators lexed (`Lexer.cs:243-304`): `... == => != >= <= && || ?? += -= *= /= %= ?.` then `( ) ; : , . ! ? { } [ ] = / + - * % < > | &`. `?.` followed by a digit is `?` and a number. (Naming quirk: `TokenType.OpenBracket` is `{`, `OpenBrace` is `[`.)
- Directives: a `#` first on a line (leading whitespace ok for the preprocessor, but the lexer wants it first on its line) is a Directive token to end of line (`Lexer.cs:150`).
- Keywords (`Lexer.Keywords`, `Lexer.cs:21`): `let const func if else while do for in return break continue delete vec true false null`. Cannot be variable/parameter names, CAN be object keys and member names (`IsWord` accepts them, `Parser.cs:71`). `include`, `compositor`, `print` are NOT keywords (they are globals/host objects; `HidlAnalysis.Keywords` in Hex adds the first two for completion, `hex/Hex.Core/Hidl/HidlAnalysis.cs:25`).

### 2.2 Statements and values
```js
// semicolons optional; a lone ; is an empty statement. Don't mix styles in one file.
let speed = 3                  // let without = is null: `let later`
const MAX = 100                // const needs a value; reassigning errors
speed = 4; speed += 1          // = += -= *= /= %= (right assoc, target: name, a.b, a[i])
delete speed                   // removes the name;  delete obj.key;  delete list[-1]
let { damage, startup } = move // object destructure, NAMES ONLY, missing -> null, trailing comma ok
let [x, y] = vec(10, 20)       // list/vector destructure, past the end -> null
```
- Value kinds (`typeof`): `"number" "text" "boolean" "null" "list" "object" "vector" "function"`. C# `ValueType` (`Runtime/Values/ValueType.cs`) also has NativeFunction, NativeValue, AnonymousFunction, Host.
- Truthiness (`Values.IsTruthy`, `Values.cs:44`): false for `null false 0 "" []`. `==`/`!=` by value for number/text/boolean/vector, identity for object/list/function (`Values.AreEqual`, `Values.cs:59`).
- `+` joins when either side is a text (numbers print as `0.######`); list + list concatenates; vectors add/subtract/scale; `/` by 0 is float infinity (no error). Text compares with `< > <= >=` ordinally. `|` and `&` (single) are boolean-only, non-short-circuit; `||` `&&` short-circuit and return the deciding operand (JS style) (`HIDLInterpreter.cs:819-930`).
- Lists: `[1, 2, ...rest, ]`, `l[0]`, `l[-1]` (negative counts from the end), missing index is an ERROR; `l[1] = x`. Texts index to a one-char text: `"abc"[0]`.
- Objects: `{ name: "Ryu", "walk speed": 2, [stance]: 1, ...base, hp }`. Key forms: word (keywords ok), quoted text, `[computed]`, spread, shorthand `hp`. Missing key reads `null`; key of `null` errors unless `?.`.
- Vectors: `vec(1)` = `vec(1,1)`, up to `vec(x,y,z,w)`, nested vecs flatten (`vec(pos, 1)`); `.x .y .z .w` or `.r .g .b .a`, `.length`, assign `v.y = 0` writes back (`Parser.ParseVector`, `Parser.cs:860`; `Interpreter.EvaluateVector`, `HIDLInterpreter.cs:160`).

### 2.3 Operators, loosest to tightest (`Parser.cs:544-761`)
`= += -= ...` > `a ? b : c` (branches are full assignments) > `??` > `||` `|` > `&&` `&` > `== !=` > `< > <= >= in` > `+ -` > `* / %` > unary `! - +` > postfix `.b ?.b [i] ?.[i] (args) ?.(args)`.
- `??` is looser than `||` on purpose. `in` binds like `<`: `!("k" in o)` needs the parens. `in`: key in object, item in list (by `==`), piece in text, nothing is in null (`CoreLibrary.Contains`, `CoreLibrary.cs:101`).
- `?.` short-circuits only that link: `move.phases?.raise?.cancel ?? 0`, `handler?.(3)`, `list?.[0]`.

### 2.4 Control flow
```js
if (hp <= 0) { print("KO") } else if (hp < 30) print("danger") else print("fine")
while (i < 3) i += 1
do { i -= 1 } while (i > 0)
for (m in moves) {}  for (i, m in moves) {}  for (k, v in obj) {}  for (c in "abc") {}  for (n in range(5)) {}
for (let m in moves) {}                 // let/const after ( is accepted and ignored
let label = if (hp > 50) { "ok" } else { "hurt" }   // if/while/do are expressions, a block yields its last value
break; continue; return; return value  // bare return = null; a top-level return ends the file
```
- One statement needs no braces after `if/else/while/for/do`. `{` at the START of a statement is an object literal, there are no bare block statements. `if`/loops as statements are parsed so the next line starting with `(`, `[` or `-` isn't glued to them (`Parser.cs:~198-210`).
- `for` over: list items, object keys, text chars. Two names = (index/key, item) (`Parser.ParseForStatement`, `Parser.cs:271`; `EvaluateForIn`, `HIDLInterpreter.cs:760`). Each loop turn has a fresh scope.
- No `switch`/`match` (rejected), no `try/catch`, no `this`, no classes, no `++`/`--`, no ternary chains needing parens.

### 2.5 Functions
```js
func add(a, b) { return a + b }             // statement form needs a name; replaces an earlier same-name func
let double = func(x) { return x * 2 }       // expression form
let f = x => x * 2;  let g = (a, b) => a + b;  let h = () => { return 7 }
let wrap = x => ({ x })                     // object result needs parens, `{` after => is a block
func box(at, size = vec(16), c = "white") { return { at, size, c } }   // defaults: missing OR null arg
let adder = n => x => x + n                 // closures capture their scope
```
- Arrow and `func(...)` produce the same `AnonymousFunctionValue`; hosts can't tell them apart (`Parser.ParseArrow`, `Parser.cs:444`, `ArrowAhead` 418, `ParseParameters` 378). Defaults are parsed with `ParseConditional` (no assignment inside), evaluated in the function's own scope.
- Missing args are `null`, extra args are ignored. Calls go 256 deep (`MAX_CALL_DEPTH`, `HIDLInterpreter.cs:23`), loops 1,000,000 turns (`:18`).
- Real: `hex/hex-extensions/todo/extension.hor` mixes `func on_start(hex)` hooks with `hex.menu(found.map(todo => ({ label: `{todo.line}  {todo.text}`, on_pressed: hex => {...} })))`.

### 2.6 Preprocessor (`Preprocessing/HIDLPreprocessor.cs`)
```c
#define layout            // name, no value;  #define version 3  (rest of line, quotes stripped, is the value)
#undef version
#if layout / #ifdef x / #ifndef x / #elif !debug / #else / #endif    // nesting ok; condition is ONE name, optionally with !
```
- `#if` takes a bare name or `!name`; there is NO `defined(x)` or expression form in directives (`Condition`, `HIDLPreprocessor.cs:~196`, uses only the name). Anything else after `#` throws "There is no #x" (`:~163`). A `//` trailing comment on a `#define` becomes part of its value.
- `Process` (`:39`) blanks directive lines and dropped lines (keeps `\r\n`) so line numbers stay true. Unclosed `#if` throws a ParseException. A text without `#` anywhere is returned untouched.
- Script side: `defined("x")`, `definition("x")` (null if undefined) read `HIDLRuntime.Defines`. The host can pre-seed with `runtime.Define("debug")`. Real markers: `#define layout` (UI layout, Hex opens the designer, `HIDLPreprocessor.IsLayout` `:112`), `#define dusk` (`HidlAnalysis.IsDusk`, `hex/Hex.Core/Hidl/HidlAnalysis.cs:412`), `#define fight_move` (Fighter2D move files, no engine meaning, a marker).
- `HIDLPreprocessor.Read(source)` returns what a file defines without running it (empty if directives are malformed).

### 2.7 Files that talk to each other
`include("shared.hor")` -> object of everything that file declared. Path relative to the including file, rooted paths as-is. Details in 3.4.

### 2.8 Real fragments (from the repos)
```js
// hex/tasks.hor: data with a comment per key, read by HIDLSerializer.Populate into HexTask
let tasks = { build: { command: "dotnet build Hex/Hex.csproj", folder: ".", group: "build", default: true, depends_on: "", terminal: "panel" } };
// tasks.hor (root): automation with an inline script function
let automations = { push_all: { title: "Push all", run: hex => push_all(hex, false) } };
const AFTER = ["hex", "Fighter2D", "."]
func push_all(hex, dry) { if (dry) hex.print("Dry run, nothing is changed"); for (repo in [HIDL, ENGINE]) {...} }
// hex/Hex.Dawn/Assets/hex/ask.hor: a UI layout
#define layout
let answer = compositor.textbox({ size: vec(460, 32), padding: vec(8, 6, 8, 6), text_scale: 0.14, max_length: 400 });
// Fighter2D/Assets/data/blocky_moves.hor: pure data, `/* */` section comments, no semicolons
#define fight_move
let moves = { run: { input: "DPadLeft | DPadRight", trigger: "held", phases: { stride: { animation: "run_loop", cancel: 0 } } } }
// hex-extensions/todo/extension.hor: metadata object + hook functions
let extension = { name: "TODO list", version: "1.0.0", settings: { tags: { kind: "text", default: "TODO, FIXME" } } };
func on_start(hex) { hex.command("Go to TODO...", hex => { ... }); }
```

### 2.9 `@doc`
`// @doc ...` (or `@comment`) directly above a declaration/key (a plain comment between is ok, a blank line is not), or at the end of the line, documents it; several in a row join. Lexer keeps the text on the Comment token only for docs (`HIDLDocs.IsDoc`, `Parsing/HIDLDocs.cs:23`). Parser attaches `Doc` to declarations and keys via `DocFor` (`Parser.cs:232`); `HIDLDocs.Read(tokens)` (`:105`) lists documented names without running. Hex shows them in hover/completion/outline. `HIDLSerializer`'s `[HidlComment]` writes plain `//` comments, not `@doc`.

### 2.10 Gotchas of the syntax
- Undeclared name reads `null` (scripts rely on it) but calling it or taking a member of it throws a "Did you mean" error (`HIDLInterpreter.cs:529,617`). Don't change.
- `let` twice in the same scope errors (`Environment.Declare`, `Environment.cs:102`); inner scopes may shadow.
- Reading an absent list index errors, absent object key is null, absent vector component errors (`ComponentIndex`, `HIDLInterpreter.cs:458`).
- Arrow `x => ({...})`; `{` right after `=>` is a block.
- No number formats beyond decimal float; `7 / 2` is `3.5`.

## 3. Runtime semantics

### 3.1 Pipeline
```
source -> HIDLPreprocessor.Process(source, defines)        Preprocessing/HIDLPreprocessor.cs:39
       -> Lexer.Tokenize(src[, lenient])                    Lexing/Lexer.cs:59/66   (always ends with EndOfFile)
       -> Parser.ProduceSyntaxTree(tokens[, errors])        Parsing/Parser.cs:103/114  -> ProgramStatement
       -> HIDLInterpreter.Evaluate(node, Environment)       Runtime/HIDLInterpreter.cs:46 (EvaluateNode switch :80)
Back out: HIDLWriter / HIDLDocument / HIDLSerializer.
```
- Parser: hand-written recursive descent, one method per precedence level. Nodes are sealed classes in `Parsing/Expressions/*.cs`, `Parsing/Statements/*.cs`, tagged by `NodeType` (`Parsing/NodeType.cs`), optional `SourceSpan Span` (only declarations, keys, items, names, members, calls). With an `errors` list it recovers (`Recover`, `Parser.cs:158`: skip to next `;` or `let/const/func name` at bracket depth 0, max 50 errors) so editors see all errors; without it the first `ParseException(message, line, col, length)` is thrown.
- Interpreter control flow uses a `Flow` field + `returned` value, not exceptions (`Signal` `:116`). `HidlRuntimeException` gets Line/Column/Length/File on its way out from the nearest node with a known span (`Evaluate` `:46-60`, `Place`). `Message` appends `(line N of file.hor)`; `Reason` is the bare message (`Values.cs:144`).
- List/text members: `ListLibrary.TryCall` / `TextLibrary.TryCall` are called straight from `EvaluateCall` (`:575`) with no function allocation; `Member` (value form) allocates a native function. Other member reads: object -> property or null, vector -> component, `IHostValue.Get`, null -> error unless `?.` (`MemberOf`, `:480`).
- Literals cache a `Constant` value on the node; the interpreter hands out the same value each time. Small whole numbers are shared (`Parser.cs:838`).

### 3.2 Scopes (`Runtime/Environment.cs`)
- `GlobalScope` (library, `true false null version reset to_text`, all const) <- `UserScope` (what a file declares; `HIDLRuntime.cs:38`). Each call, loop turn and include gets a child scope. Lookups walk parents; assignment to an undeclared name errors; `Declare(name, value, isConst)` (`:102`), `Assign` (`:147`), `Lookup` (`:193`, null for absent), `Has`, `Resolve`, `Delete` (`:253`), `Reset(resetSystem=false)` (`:176`, the `reset()` global clears UserScope but keeps system vars), `GetAllDeclaredValues`, `Variables`.
- `DeclareSystem(name, value)` (`:135`): host-owned names, readable and callable by scripts, never assignable/resettable/written by `HIDLWriter`. Redeclaring a system name throws.
- Storage: 8 inline slots then a lazily-built dictionary.

### 3.3 Host objects
- `Natives.Object(("fn", Natives.Function("fn", a => ...)), ("prop", Natives.Property(get, set)))` builds an `ObjectValue` of functions and accessor properties; declare it with `scope.DeclareSystem("name", obj)` (`Library/Natives.cs:23-51`). A `NativeValue` (Property) is unwrapped on every read, writes call the setter; a Property without setter throws "can be read but not changed".
- `IHostValue` (`Runtime/Values/HostValue.cs:9`): `TypeName`, `Get(member)`, `Set(member, value)`; the interpreter calls `Get` for `host.name` and for calls like `host.name(1)` when `Get` returns a function. Used by `ClrValue` (interop) and `horizon/Horizon.UI/Scripting/UIScriptedLayout.cs`.
- `Arguments` (`Natives.cs:83`) checked accessors: `Number(i[,fallback])`, `Integer`, `Text`, `Bool`, `List`, `Object`, `Callable`, `Vector2/3/4`, `Components`, `Has(i)` (non-null), `Wrong(msg)`, `this[i]`, `All`, `Count`, `Scope`. A wrong type throws `"<function>: wanted a number ..."`.
- Callbacks into script functions from a native: `HIDLRuntime.Current.Invoke(fn, args)` (thread-static, set during `Run`/`Invoke`). Examples: `sort`/`map` in `ListLibrary`.

### 3.4 Includes (`HIDLRuntime.Include`, `HIDLRuntime.cs:322`)
Runs the file in a NEW scope whose parent is GlobalScope (the includer's variables are invisible), returns an object of its declarations. Path relative to `BaseDirectory` (set by `RunFile` and during includes). Cycle -> "includes itself". `IncludeReader(file, folder)` overrides disk reads (packs/Android); `FilePathResolver` normalises. The parse is cached statically by full path (case-insensitive) + exact preprocessed text, max 128 entries then cleared (`ParseIncluded`, `:293-320`). Host+includer defines flow in, the included file's own defines stay local. Errors in an included file keep their own line/file.

### 3.5 Library catalogue (global scope)
Registered in the `HIDLRuntime` constructor (`HIDLRuntime.cs:112-128`): `true false null version reset to_text` there, then `CoreLibrary.Declare(this, GlobalScope)`, `TextLibrary.Declare(GlobalScope)`, `MathLibrary.Declare(GlobalScope)`.
- `Library/CoreLibrary.cs:13`: `keys values has entries range print merge error include defined definition` (`Contains` `:101`, `Merge` `:119`, deep merge, inputs untouched). `print` writes to `HIDLRuntime.Output` and returns the line (`:61`).
- `Library/TextLibrary.cs:141`: `text number format typeof len`. `format(x, digits)` number formatting, `format(pattern, a, b)` fills `{0}`/`{name}`.
- Text members (`TextLibrary.Methods` `:18`): `upper lower trim trim_start trim_end contains starts_with ends_with index_of last_index_of count_of replace split lines chars substring pad_left pad_right repeat is_digit is_letter is_upper is_lower is_space is_word`; props `length count`.
- List members (`ListLibrary.Methods` `:15`): `push pop insert remove remove_at clear contains index_of last_index_of join reverse copy slice sort map filter any all find find_index reduce sum unique flat each`; props `count length first last`. `Compare` (`:286`) orders for `sort`; `Walk`/`WalkPairs` (`:297,327`) feed `for`.
- `Library/MathLibrary.cs:17` declares the `math` object: `pi tau e`, `abs floor ceil sqrt sign sin cos tan asin acos atan exp log atan2 pow round min max clamp lerp to_radians to_degrees length distance normalize dot random chance pick`, component-wise on vectors (`Apply/Combine/Fold`, `:133-170`).
- Typo help: `Library/Suggest.cs` (`Nearest`, `Listed`, `Distance`).

### 3.6 Errors, limits, diagnostics
- Parse: `ParseException` with line/col/length (1-based). Runtime: `HidlRuntimeException` (position + file filled by nearest spanned node). `error("msg")` throws one on purpose.
- Limits: 256 call depth (a .NET stack overflow is uncatchable, keep the cap), 1,000,000 loop turns, `Interpreter.Cancellation` checked per call and per loop turn -> `"Stopped before it was done."` (`HIDLInterpreter.cs:702`). Flow state is per interpreter, not thread-safe: one runtime per thread.
- Messages are deliberately friendly and specific ("A list has no 'pussh'. Did you mean push?"); tests assert on some wording, grep tests before rewording.

### 3.7 Source maps (`Runtime/HIDLSourceMap.cs`)
Set `runtime.Sources = new HIDLSourceMap()` before a run. Afterwards `TryGetKey(obj, key, out SourceLocation)` (`:20`), `TryGetItem(list, i, out)` (`:27`), `TryGetDeclaration(name, out)` (`:38`) give `SourceLocation(File, Span, ValueSpan, Literal, Doc)`. Follows includes and spreads. Hex uses it for click-to-source in the Values panel and in-place literal edits. Recording happens in `EvaluateObjectLiteral` (`:195`), `EvaluateListLiteral` (`:232`), declarations (`:267`) via `At(...)` (`:63`).

## 4. Public consumer API

`HIDLRuntime` (`HIDLRuntime.cs`):
- Props: `UserScope` `:38`, `GlobalScope` `:43`, `Interpreter` `:48` (`.Cancellation`), `Output` `:53` (default `Console.WriteLine`, null silences), `BaseDirectory` `:58`, `IncludeReader` `:64`, `FilePathResolver` `:71`, `FileReader` `:74`, `Sources` `:80`, `SourceFile` `:86`, `Defines` `:93`, `Define`/`IsDefined` `:96-98`. Static `Current` (thread-static, `:33`).
- `Run(text)` `:192` and `RunFile(path)` `:197` throw (`HidlRuntimeException`/`ParseException`, files wrapped as `'path': msg`); `Evaluate(text, useGlobalScope=false)` `:176` returns `(success, resultText)`, never throws (REPL and the usual consumer pattern); `GenerateValue` `:146` runs in a scratch scope and returns the declared values; `Run(ProgramStatement, Environment)` `:230`; `Invoke(fn, args...)` `:248` (null for missing args); `ToText()`/`Save(path)` write UserScope back (`:135,140`).
- Statics: `Parse` `:265`, `ParseAll(text)` `:271` (tree + all errors, empty defines, a lex error is the one error), `ParseWithDirectives` `:287`. `ParseAll`/`GenerateValue` ignore host `Defines`, `Run` uses them.
- `ReadingContent()` extension (`horizon/Horizon/Content/ContentScripts.cs:17`): points `FileReader`/`IncludeReader`/`FilePathResolver` at `ContentFiles` so scripts load from packs/Android/iOS.

Sketches from real call sites:
```csharp
// Fighter2D data (Fighter2D/Content/HorReader.cs:31): read a file, take the object it declares
var rt = new HIDLRuntime().ReadingContent();
var (ok, msg) = rt.Evaluate(text);                       // never throws
if (!ok) throw new Exception($"'{file}': {msg}");
if (rt.UserScope.Lookup("moves") is not ObjectValue o) throw ...;   // o.Properties is the Dictionary
// Engine UI (horizon/Horizon.UI/UIModule.Runtime.cs:51,91): one runtime, host object of component factories
public HIDLRuntime Runtime { get; init; } = new HIDLRuntime().ReadingContent();
Runtime.UserScope.DeclareSystem("compositor", new ObjectValue(factories));   // layouts call compositor.textbox({...})
// Engine skins/themes (horizon/Horizon.UI/Skinning/UISkin.Loading.cs:52-55): Evaluate(path, "skin") the same way
// Fighter2D script behaviours (Fighter2D/Simulation/Dummy/DummyScript.cs:95-128):
var runtime = new HIDLRuntime { Output = line => Log.Info(line) }.ReadingContent();
... runtime.UserScope.Lookup("behaviours") is ObjectValue b ...; each tick: runtime.Invoke(function, viewObject)
// Hex tasks (hex/Hex.Dawn/Tasks/HexTasks.cs:90-118): file text -> Populate C# objects
HIDLRuntime runtime = new() { Output = null };  runtime.Evaluate(text);
runtime.UserScope.Lookup("tasks"); runtime.UserScope.Lookup("automations");
HIDLSerializer.Populate(task, value);                    // C# members by snake_case; unspecified stay
// Hex extensions (hex/Hex.Dawn/Extensions/HexExtension.cs:40,89-93): FILE = "extension.hor"
Runtime = new HIDLRuntime { BaseDirectory = Folder, Output = printed };
Runtime.UserScope.DeclareSystem("hex", hex);  Runtime.RunFile(Path.Combine(Folder, FILE));
// later hooks: runtime.Invoke(on_open, ...) wrapped by HexExtension.Invoke (:145-155) which catches script errors
// Hex automations (hex/Hex.Dawn/Tasks/HexAutomationRun.cs:50-69): new runtime per run,
//   Interpreter.Cancellation = work.Token, DeclareSystem("hex", hex), HexInterop.Declare, then Invoke(run, hex)
```
Other consumers (grep `Horizon.HIDL`): `horizon/Horizon/Content/Localisation.cs`, `Rendering/2D/Spriting/*`, `Input/Gamepads`, `horizon/Horizon.UI/Scripting/UIScript.cs` (value -> UI types: `ToNumber/ToText/ToTexts/FromTexts`), `UIScriptedLayout.cs`, `HidlHighlighter.cs` (completion names = `new HIDLRuntime().GlobalScope.Variables` + `compositor`, `:71`), `horizon/Horizon.Terminal/Hosting/TerminalHidlRepl.cs`, `hex/Hex.Core/Hidl/*`, `hex/Hex.Core/Tasks`, `hex/Hex.Dawn/{Extensions,Tasks,Settings,Designer,Values}`.

Hex task/automation details: `tasks.hor` is found next to the file on screen, in workspace folders and next to the `.hex-workspace`; at least one of `tasks`/`automations` must be an object. `HexTask`: `command folder group(build|test|run) default depends_on terminal(panel|dusk)`. `HexAutomation`: `title key command shell(bash|pwsh|powershell|cmd|wsl:Name) folder script run`; those without `command` are HIDL (`IsHidl`), run off the editor thread with `hex.run(program,args,folder,{timeout,quiet})`, `print`, `progress`, `ask`, `prompt`, `matches`, `sleep`, `cancelled`, `folder` and the extension API. Details in `hex/docs/extensions.md`.

## 5. Writer and serializer round-trip

- `HIDLWriter` (`HIDLWriter.cs`): `Write(value[, trailingCommas])` `:32/39` (a value as an expression), `WriteDeclaration(name, value, isConst)` `:51`, `Write(Environment, include?)` `:68` (every UserScope variable as `let`/`const`; skips parents, system vars, functions, native values), `CanWrite` `:90`, `IsIdentifier` `:101` (the lexer decides, keywords rejected), `WriteInto` `:118`, `WriteKey` `:124` (plain word or quoted). Writable: number, text, boolean, null, vector, list, object. Functions are dropped from objects; `NativeValue` is written as its current value. Lists longer than 72 chars go one item per line (`SHORT_LIST` `:26`); depth cap 64 detects a self-containing object (`:23`). Numbers are written without exponents (`:326`).
- Trailing commas: whole files (HIDLDocument, Hex layout designer) put a comma after the last entry of every multi-line object/list so appends are one-line diffs; plain `Write(value)` does not (what goes into hand-written files).
- `HIDLDocument` (`HIDLDocument.cs:21`): fluent `Comment(text)` `:28`, `Define(name, value?)` `:36`, `Let/Const-like Let(name, value|HIDLObject, comment, constant)` `:43/46`, `Write()` `:57`. Output order: `// comment` lines, `#define`s, blank line, then each declaration with its comment above, `;` terminated. `HIDLObject` (`:90`): `Add(key, value, comment)` overloads (string/float/bool/IRuntimeValue/nested/list), `Note(comment)` (free comment line), `ToValue()`. Used for Hex settings, keys (`hex/Hex.Dawn/Shell/DawnScene.Keys.cs:108`), workspaces (`hex/Hex.Core/Workspace/HexWorkspace.cs:191`).
- `HIDLSerializer` (`HIDLSerializer.cs`): `Document(name, obj)` `:44`, `ToObject` `:47`, `ToValue` `:61`, `Populate(target, value)` `:104`, `TryRead(value, type, current, out read)` `:131`, `SnakeCase` `:264`. Public settable fields/properties by snake_case name; attributes `[HidlName]`, `[HidlComment]` (written as `//` above the key), `[HidlIgnore]` (`:13-28`). Handles text, number, bool, enums (as names), vectors, lists/arrays/sets, text-keyed dictionaries, nested objects. `Populate` leaves unmentioned members untouched and fills non-settable collections in place. Reflection based: AOT/trim needs `[DynamicDependency(PublicFields|PublicProperties|PublicConstructors, typeof(T))]` on the caller (see `HexTasks.cs:89`).
- What survives a round-trip: values and key ORDER (dictionary insertion order). What does NOT: formatting, hand-written comments, `@doc`, includes/spreads/templates/arrows (evaluated away), `#if` blocks, numeric spelling. The only comments in written files are the ones the caller supplies (`HIDLDocument.Comment`, `HIDLObject.Add(..., comment)`, `[HidlComment]`). Re-saving a hand-edited layout in Hex's designer loses comments inside braces (`hex/docs/layouts-and-hidl.md`). Editors that need in-place edits use the source map (`TryGetKey` -> `Literal` span) and edit the text, not the writer.
- `runtime.ToText()` / `to_text()` / `to_text(value)` write values back as HIDL.
- Interop (`Interop/HIDLInterop.cs:48`): `HIDLInterop.Declare(runtime, "dotnet", baseDir)` adds `dotnet.load/type/new/can_load` (`ClrValue`, `AssemblyValue` as `IHostValue`). Opt-in, arbitrary .NET access, `load` cannot work under AOT (`can_load()` says). Hex wraps it as `HexInterop` (`hex/Hex.Dawn/Extensions/HexInterop.cs`).

## 6. Editor tooling

- `editors/vscode/`: `package.json` (language `hidl`, scope `source.hidl`, extensions `.hor .jab .hex-workspace`), `language-configuration.json`, `syntaxes/hidl.tmLanguage.json`. The same grammar file is embedded and used by Hex's TextMate highlighter (`horizon/Horizon.UI/Scripting/TextMateHighlighter.cs`). Edit once, both follow. No packaged .vsix.
- Token-level helpers for editors (no parse needed): `HIDLDeclarations.Declared/IsArrowParameter/OpensArrowParameters/OpensParameters/Parameters/LoopNames` (`Parsing/HIDLDeclarations.cs:16-151`); `HIDLDocs.Read`.
- Hex language service `hex/Hex.Core/Hidl/`: `HidlAnalysis.cs` (Complete, Hover, FindDeclaration, SyntaxErrors, `Check` = parse errors + run errors + lints, `IsLayout`, `IsDusk` `:412`, `Keywords` `:25`), `HidlSemantics`, `HidlInlays`, `HidlFunctions` (parameter inlays, signature help), `HidlLocals` (F12/peek), `HidlReferences`, `HidlComponents` (layout component kinds), `HidlServer` (LSP over stdio, `Hex --lsp`, `hex/Hex/*.cs:~46`; initialize, didOpen/Change/Close, completion, hover, definition, documentSymbol, publishDiagnostics, 300 ms debounce).

## 7. How to extend (step by step)

**A. New built-in function** (global)
1. Pick the file: general -> `Library/CoreLibrary.cs` (`Declare` at `:13`), text/number helpers -> `TextLibrary.cs:141`, maths -> inside the `math` object in `MathLibrary.cs:17` (add `("name", Natives.Function(...))` to the tuple list).
2. Add `scope.Declare("name", Natives.Function("name", args => new NumberValue(args.Number(0) * 2)), true);` (pattern `CoreLibrary.cs:15`). Use `args.Text/Number/...` for checked access; throw `args.Wrong("...")` for bad input. Needs the runtime (like `include`, `defined`)? take it from the `Declare(HIDLRuntime runtime, ...)` parameter (`CoreLibrary.cs:88`).
3. Script callbacks: `HIDLRuntime.Current!.Invoke(fn, args)`.
4. Completion/highlighting come from `GlobalScope.Variables` automatically (`HidlHighlighter.cs:71`). Add a row to the `docs/reference.md` library tables, and a test in `horizon/Horizon.Tests/Scripting/HidlLibraryTests.cs`.

**B. New text or list method**: add the name to `Methods` (`TextLibrary.cs:18`, `ListLibrary.cs:15`) and a `case` in that file's `Call` (`TextLibrary.cs:68`, `ListLibrary.cs:58`). `Everything` (for "Did you mean") derives from it. `Member` (value form) and `TryCall` (direct call) share `Call`. A new read-only property (like `count`) goes in the `Member` switch (`TextLibrary.cs:30`, `ListLibrary.cs:26`) and `Everything`.

**C. New syntax form or operator**
1. Lexer: new `TokenType` (`Lexing/TokenType.cs`) and rule in `Lexer.TokenizeInto` (two-char ops table `Lexer.cs:250`, single `:277`); keep lenient mode sane. New keyword -> `Lexer.Keywords` `:21` (it becomes reserved everywhere except as keys).
2. Parser: add to the right `ParseX` level (`Parser.cs:544-761`) or `ParsePrimary` (`:761`) / `ParseStatement` (`:187`); node class in `Parsing/Expressions/` or `Statements/`, `NodeType` entry, `SourceSpan` if errors/editors need it. A binary operator that is a plain string only needs a `NextOperator(...)` list entry plus a case in `Operate` (`HIDLInterpreter.cs:883`) or special handling in `EvaluateBinaryExpression` (`:819`) if it short-circuits.
3. Interpreter: case in `EvaluateNode` (`:80`) and an `Evaluate*` method. Respect `Flow`, depth and `Stopped()`.
4. Tooling: `editors/vscode/syntaxes/hidl.tmLanguage.json`, `HIDLDeclarations`/`HidlSemantics`/`HidlLocals` if it declares names, `HidlAnalysis.Keywords` if a keyword, `HIDLWriter` if writable, `docs/reference.md`.
5. Tests: `HidlShorthandTests.cs` / `HidlTests.cs`. Model case: arrows (`TokenType.Arrow`, `Parser.ParseArrow` `:444`).

**D. New host object/value**: for a thing with dynamic members implement `IHostValue` (`Get/Set/TypeName`); otherwise build `Natives.Object(...)` and `DeclareSystem` it. Update `Values.Text/IsTruthy/AreEqual/TypeName` (`Values.cs`) and `HIDLWriter.CanWrite` (`:90`) if it is value-ish. Remember `DeclareSystem` names survive `reset()` and cannot be assigned.

**E. New preprocessor directive**: add a `case` in `HIDLPreprocessor.Directive` (`:114`); mind `keeping` and the blank-the-line rule; test in `HidlPreprocessorTests.cs`.

**F. New persisted C# type**: give public settable members, optionally `[HidlComment]`; write with `HIDLSerializer.Document(...)`, read with `Populate`; add `[DynamicDependency]` on the reader for AOT.

## 8. Tests

HIDL has no test project; its tests live elsewhere.
- `horizon/Horizon.Tests/Scripting/` (xunit, `Horizon.Tests.csproj`): `HidlTests.cs`, `HidlShorthandTests.cs` (arrows, `in`, defaults, destructuring), `HidlLibraryTests.cs`, `HidlPreprocessorTests.cs`, `HidlDocTests.cs`, `HidlSourceTests.cs` (source map), `HidlInteropTests.cs`, `HIDLSerializerTests.cs`. Other files in that folder are for other languages. Style: `new HIDLRuntime { Output = null }.Run(src)` then `Assert.IsType<NumberValue>`, theories with `[InlineData(src, expected)]`.
- `hex/Hex.Tests/`: `HidlAnalysisTests.cs`, `HidlInlayTests.cs`, `HidlSemanticsTests.cs`, `HidlServerTests.cs`, `HexAutomationTests.cs`, `HexTasksFixTests.cs`.
- Hex in-app self tests: `hex/Hex.Dawn/Testing/`, `Hex.exe --selftest` (run suites from the real `hex/Hex/bin` output, copies miss `hex-extensions`).
- Run: `dotnet test horizon/Horizon.Tests/Horizon.Tests.csproj [--filter Hidl]`, `dotnet test hex/Hex.Tests/Hex.Tests.csproj`. (Not run while writing this.)

## 9. Gotchas and invariants

- Don't make undeclared names an error on plain read; only calls/member access error.
- Include'd files do not see the includer's variables (parent scope is GlobalScope). Passing data in = defines, `DeclareSystem` objects, or functions taking arguments.
- Numbers are float32; digits are ASCII only (a png opened as text once crashed the lexer). Whole numbers up to 7 digits go through a fast path (`Lexer.Whole`).
- `thing.x` where `thing` is a vector writes back a new vector (value semantics); lists and objects are references.
- `ParseAll`/`GenerateValue` ignore host `Defines`; `Run`/`Evaluate`/`RunFile` use them.
- Include parse cache is static and shared across runtimes/threads (lock-protected).
- Flow state and call depth are per interpreter instance: one runtime per thread. Hex gives each extension and each automation its own runtime.
- `HIDLRuntime.Current` is thread-static; use `Run`/`Invoke`, not `Interpreter` directly, or library callbacks and source maps lose their context.
- Preprocessor blanks lines instead of deleting them, keep it so error lines are right. A `#` at the start of a line inside a multi-line `"..."` is treated as a directive by the preprocessor (it works line by line).
- Lexer pools identifier strings per thread and reuses a thread-static token list; do not hold the `Token[]` buffer assumption (it is copied to an array at the end).
- Parser nodes are sealed classes. `docs/plan.md` still describes a Pratt parser, slots/frames and `ParseOptions.KeepTrivia`/`TokenizeLine`; none exist (grep finds none).
- Shapes (`docs/shapes.md`): designed only, no `shape` keyword anywhere. `@doc` from that design IS implemented.
- Comments are not in the AST (only doc text attaches); a formatter or comment-preserving rewrite is not possible from the tree alone, use tokens (`Lexer.Tokenize` keeps Comment tokens).
- Workflow: HIDL is its own repo. Commit in `horizon/Horizon.HIDL` on `development`, then the engine records the submodule pointer (`push-all` automation does this in order HIDL -> engine -> Hex -> Fighter2D -> workspace).

## 10. Where to look for X

| Need | Look at |
| --- | --- |
| Language rules, examples | `docs/reference.md` |
| Roadmap / perf plan / shapes design | `docs/plan.md`, `docs/shapes.md` (unimplemented) |
| Keywords, token kinds, escapes, templates, numbers | `Lexing/Lexer.cs`, `Lexing/TokenType.cs` |
| `#if` / `#define` | `Preprocessing/HIDLPreprocessor.cs` |
| Grammar, precedence, error recovery | `Parsing/Parser.cs` |
| AST node shapes | `Parsing/Expressions/`, `Parsing/Statements/`, `Parsing/NodeType.cs` |
| Evaluation, operators, loops, calls, member access | `Runtime/HIDLInterpreter.cs` |
| Scopes, constants, system vars | `Runtime/Environment.cs` |
| Value types, equality, truthiness, `HidlRuntimeException` | `Runtime/Values/*.cs` (`Values.cs`) |
| Value -> source location | `Runtime/HIDLSourceMap.cs` |
| Built-in functions and methods | `Library/CoreLibrary.cs, TextLibrary.cs, ListLibrary.cs, MathLibrary.cs`; `Natives.cs`, `Suggest.cs` |
| Run/include/parse entry points | `HIDLRuntime.cs` |
| Writing HIDL out | `HIDLWriter.cs`, `HIDLDocument.cs`, `HIDLSerializer.cs` |
| .NET reflection bridge | `Interop/HIDLInterop.cs` |
| `@doc` handling | `Parsing/HIDLDocs.cs` |
| Token-level editor helpers | `Parsing/HIDLDeclarations.cs` |
| TextMate grammar / VS Code | `editors/vscode/`, `HIDLGrammar.cs` |
| Engine content loading (packs) | `horizon/Horizon/Content/ContentScripts.cs` |
| UI layouts / compositor | `horizon/Horizon.UI/UIModule.Runtime.cs`, `Scripting/UIScript.cs`, `Skinning/UISkin.Loading.cs` |
| Hex language service / LSP | `hex/Hex.Core/Hidl/*`, `hex/docs/layouts-and-hidl.md` |
| tasks.hor / automations / extensions | `hex/Hex.Core/Tasks/`, `hex/Hex.Dawn/Tasks/`, `hex/Hex.Dawn/Extensions/`, `hex/docs/extensions.md`, root `tasks.hor` |
| Game-side reading of .hor | `Fighter2D/Content/HorReader.cs`, `Fighter2D/Simulation/Dummy/DummyScript.cs`, `Fighter2D/Assets/data/*.hor` |
| Tests | `horizon/Horizon.Tests/Scripting/Hidl*.cs`, `hex/Hex.Tests/Hidl*.cs` |

## 11. Appendix: token and node maps, recipes

**TokenType** (`Lexing/TokenType.cs`, a `byte` enum): Let Const Number Equals Dot If While Do Break Delete OpenParenthesis CloseParenthesis BinaryOperation Identifier Semicolon Comma EndOfFile Null OpenBracket(`{`) CloseBracket(`}`) OpenBrace(`[`) CloseBrace(`]`) Colon Function Quote TextLiteral Exclamation Equality NotEquality Vector Comment Else For In Return Continue Spread CompoundEquals(value is the operator without `=`) Question Unknown(lenient only) Directive(parser never sees it, `Over()` skips Comment and Directive) Boolean(value `true`/`false`) QuestionDot Template(value is the raw text between backticks) Arrow. `Token` = Type, Value, Line, Col, Index, Length, Number (`Lexing/Token.cs`). `SourceSpan(Line, Column, Index, Length)` with `Of(token)`, `Between(a, b)`, `IsKnown` (`Parsing/SourceSpan.cs`); `SourceLocation(File, Span, Value, Literal, Doc)` is what the source map returns (`SourceSpan.cs:28`).

**NodeType** (`Parsing/NodeType.cs`): Program NullLiteral VectorDeclaration VariableDeclaration FunctionDeclaration AnonymousFunctionDeclaration Assignment IfExpression WhileExpression DoWhileExpression DeleteStatement ForInStatement ReturnStatement BreakStatement ContinueStatement Property ObjectLiteral ListLiteral Spread Conditional Identifier NumericLiteral BinaryExpression MemberExpression CallExpression StringLiteral BooleanLiteral Template Destructuring. Unary `! - +` are `BinaryExpression` with `Right == null`; `??`, `&&`, `||`, `in` are `BinaryExpression` too, special-cased in `EvaluateBinaryExpression` (`HIDLInterpreter.cs:819`). Optional `?.` is the `Optional` flag on `MemberExpression`/`CallExpression`. Arrow vs `func(...)` is the `Arrow` flag on `AnonymousFunctionDeclarationExpression`.

**Values** (`Runtime/Values/`): `NumberValue`, `StringValue`, `BooleanValue` (`Values.True/False/Bool`), `NullValue` (`Values.Null`), `ListValue.Items`, `ObjectValue.Properties` (insertion-ordered dictionary), `Vector2/3/4Value` (System.Numerics), `FunctionValue` (named, has `Defaults`), `AnonymousFunctionValue`, `NativeFunctionValue(Func<IRuntimeValue[], Environment, IRuntimeValue>)`, `NativeValue` (`AccessorCallback()` get, setter), `IHostValue`. Helpers in `Values.cs`: `Text(value)` `:28` (how a value prints; numbers `0.######`), `TypeName` `:83`, `IsFunction` `:98`, `TryGetComponents` `:103`, `Vector(span)` `:128`. Functions hold their defining `Environment` (closures).

**Recipe: read a data file in a game**
1. `new HIDLRuntime().ReadingContent()` (or plain for disk files; set `Output` to your logger, or null).
2. `var (ok, msg) = rt.Evaluate(text)`; on failure show `msg` (it has line and file).
3. `rt.UserScope.Lookup("name") is ObjectValue o` and walk `o.Properties`; convert with `UIScript.ToNumber/ToText/ToTexts` or `HIDLSerializer.Populate(target, value)`.
4. Defaults: write them in the C# object, files then override only what they say. Shared + own data: `merge(include("shared.hor").moves, moves)` inside the file.

**Recipe: call a script handler from C#**: keep the `IRuntimeValue` the file declared (or stored in an object key), then `runtime.Invoke(fn, args)` on the SAME runtime, from the thread that owns it. Check `Values.IsFunction(v)` first, `null` and non-functions are common for optional handlers. Catch `HidlRuntimeException` and show it, a script error must not take the host down (Hex wraps this in `HexExtension.Invoke`).

**Recipe: debug a script problem**
1. Reproduce with `runtime.Evaluate(text)`, read `msg`: it names the line, file and often the nearest declared name.
2. Parse only: `HIDLRuntime.ParseAll(text)` gives every syntax error with line/col/length.
3. See what a value is: `print(to_text(x))` writes it back as HIDL; `typeof(x)`, `len(x)`.
4. Where a value came from: set `Sources` and ask `TryGetKey`/`TryGetDeclaration`.
5. In Hex: the Problems panel (`HidlAnalysis.Check`) and Values panel show both; Hex's MCP `hex_problems` reports the same.

**Common error texts to grep** (so you can find the throw site): "There is no 'x' to call, nothing of that name was declared" and "There is no 'x' to get 'y' from" (`HIDLInterpreter.cs:529,617`), "There is nothing to get 'k' from, the value is null" (`:532`), "A list has no 'x'" / "A text has no 'x'" (`ListLibrary.cs:32`, `TextLibrary.cs:34`), "There is a 'x' already" (`Environment.cs:~105`), "There is no 'x' to assign to, declare it with let first" and "'x' is a constant" and "belongs to the program" (`Environment.cs:151-157`), "Calls went 256 deep" (`HIDLInterpreter.cs:661`), "ran N times and showed no sign of stopping" (`:735,751`), "'f' includes itself, round and round" (`HIDLRuntime.cs:329`), "never closed" (lexer, `Lexer.cs:202,230`), "'x' is a word of the language" (`Parser.cs:309`, destructuring `:340`).

## 12. Appendix: UI layouts, the biggest consumer

- A layout is a `.hor` file with `#define layout` that calls `compositor.<kind>({ ...props })` and declares what it wants named. `UIModule.SetupRuntime` (`horizon/Horizon.UI/UIModule.Runtime.cs:~88`) does `Runtime.UserScope.DeclareSystem("compositor", new ObjectValue(factories))`, so `compositor` is a host object whose members are component factories (`compositor.textbox`, `.stack`, `.design`, ...). `compositor.design({ size: vec(1600, 900), fit: "contain" | "stretch" })` records the designed-for screen (`UIModule.Runtime.cs:~92-100`).
- Loading: `UILayout.Load(module, path, parent?, introDelay)` (`horizon/Horizon.UI/Layout/UILayout.cs:76`) and `LoadCode(module, code, ...)` (`:85`); the program is evaluated inside `module.Capture(parent, layout.roots, ...)` (`:273`) so components made but not parented land in the layout. Named declarations of the file (`let answer = compositor.textbox(...)`) are how code fetches parts of a layout.
- Values go through `UIScript.ToNumber/ToBoolean/ToText/ToTexts` (`horizon/Horizon.UI/Scripting/UIScript.cs:14-35`) which give typed errors with the key name.
- Handlers in layouts are script functions (`on_pressed: hex => {...}`); the UI invokes them with `module.Runtime.Invoke(...)` so library calls work inside.
- The engine's `Runtime` is shared across a module's layouts; each `include` of a layout is parsed once per text (static cache). Skins/themes use a fresh `new HIDLRuntime().ReadingContent()` per file (`UISkin.Loading.cs:54`).
- Hex's layout designer rewrites layouts with `HIDLDocument`/`HIDLWriter` (trailing commas), see `hex/docs/layouts-and-hidl.md`; hand comments inside braces are lost on a designer save.

## 13. Appendix: idioms (patterned on real files, short forms are illustrative)

```js
// defaults without ifs
let cancel = move.phases?.raise?.cancel ?? 0
// config over defaults
let moves = merge(shared.moves, { jab: { damage: 6 } })
// list work
let big = damage.filter(d => d > 10);  let total = damage.sum();  let names = items.map(i => i.name).join(", ")
// building text
hex.print(`{repo}: {n} changes`);  let line = format("{0} of {1}", 3, 5)
// guard clauses
func on_open(hex, file) { if (!hex.setting("x") || file != hex.file()) return; ... }
// optional handler
state.on_drop?.(start)
// computed keys and spreads for variants
let variant = { ...base, [slot]: art, name }
// iteration with index
for (i, move in moves) print(i, move)
// shell-ish work in automations
let result = hex.run("git", ["-C", repo, "status", "-sb"], ".", { quiet: true });   // { exit, output }
```
