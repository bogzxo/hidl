# HIDL for VS Code

The TextMate grammar of the language, `syntaxes/hidl.tmLanguage.json`, and the little that VS Code needs round it to
colour a `.hor` file (and a `.jab`, and Hex's `.hex-workspace`). The same grammar file is what Hex colours HIDL with,
it is built into the language's assembly as a resource (`HIDLGrammar`) and read by the engine's TextMate highlighter,
so what you see in one editor is what you see in the other. Change it here and both follow.

To try it in VS Code without packaging anything, copy or link this folder into your extensions folder,
`%USERPROFILE%\.vscode\extensions\bogzxo.hidl-0.1.0` on Windows, `~/.vscode/extensions/bogzxo.hidl-0.1.0` elsewhere,
and reload the window. `vsce package` in here makes a .vsix of it when that day comes.

What the grammar knows. Line and block comments, the preprocessor's lines (`#define`, `#if defined(x)`, `#ifdef`,
`#ifndef`, `#elif`, `#else`, `#endif`, `#undef`), texts with their escapes and the markup a label reads in them
(`[b]`, `[color:red]`, `[icon:pad_a]`, `$(save)`), a text that is a key of a game's language (`"@main_menu.quit"`),
templates in backticks with `{expressions}` inside, numbers, `true`, `false` and `null`, the keywords, the names made
with `let`, `const` and `func` (and the ones `let { a, b }` takes out of something), the names a `for` goes through
with (`for (key, value in object)` too), a function's parameters and an arrow's (`x =>`, `(a, b = 2) =>`), the kind
after `compositor.`, what the language comes with (`print`, `include`,
`vec` and the rest), calls, members after a dot, the keys of objects, the operators (`??`, `?.` and the `=>` of an
arrow among them) and the brackets. What a name is beyond that (made in this file, a parameter, the library's) Hex
works out on top of the grammar with the language's own lexer, the way VS Code lays semantic tokens over TextMate.
