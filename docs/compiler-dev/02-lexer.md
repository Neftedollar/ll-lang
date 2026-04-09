# Lexer

File: `src/LLLangCompiler/Lexer.fs` (~185 lines).
Token definitions: `src/LLLangCompiler/Token.fs`.

The lexer is a single-pass character scanner that produces a
`Tok list` plus synthetic layout tokens. No external state between calls.

## Token shape

```fsharp
type Token =
    | KwFn | KwLet | KwIn | KwType | KwTag | KwUnit
    | KwTrait | KwImpl | KwImport | KwExport | KwModule
    | KwIf | KwThen | KwElse | KwTrue | KwFalse
    | KwMatch | KwWith
    | Ident of string
    | TypeId of string
    | IntLit of int64 | FloatLit of float | StrLit of string
    | CharLit of char
    | Arrow | Backslash | Dot | Comma | Colon | ColonColon
    | Eq | Bar | LBrack | RBrack | LParen | RParen
    | Plus | Minus | Star | Slash | Caret
    | Lt | Gt | Le | Ge | EqEq | Neq
    | Underscore
    | Indent | Dedent | Newline
    | Eof

type Tok = { Token: Token; Line: int; Col: int }
```

`Line`/`Col` track 1-based source position; `Col` is computed from
`pos - lineStart + 1`.

## Keyword table

Keywords are literal strings recognized after identifier tokenization
via a small `Map<string, Token>`:

```fsharp
let private keywords =
    Map.ofList [
        "fn", KwFn; "let", KwLet; "in", KwIn; "type", KwType
        "tag", KwTag; "unit", KwUnit; "trait", KwTrait; "impl", KwImpl
        "import", KwImport; "export", KwExport; "module", KwModule
        "if", KwIf; "then", KwThen; "else", KwElse
        "true", KwTrue; "false", KwFalse
        "match", KwMatch; "with", KwWith
    ]
```

## Case-based identifier split

After reading a run of `[A-Za-z0-9_]` starting with a letter or `_`, the
lexer checks `Char.IsUpper source[start]` to distinguish uppercase-leading
identifiers (types, constructors, module segments → `TypeId`) from
lowercase (`Ident`). This keeps the parser free of case checks.

## Two-character operators

Two-character operators (`->`, `<=`, `>=`, `==`, `!=`, `::`) are checked
with `peek()` before single-character operators. Order matters: `<` must
fall through to `<=` being tried first.

```fsharp
| '-' when peek() = '>' -> let t = mk Arrow in advance(); advance(); result.Add(t); scan()
| '<' when peek() = '=' -> let t = mk Le in advance(); advance(); result.Add(t); scan()
| ':' when peek() = ':' -> let t = mk ColonColon in advance(); advance(); result.Add(t); scan()
...
| '-' -> add Minus; advance(); scan()
| '<' -> add Lt; advance(); scan()
```

Note: `Arrow` is used for both the type arrow (`Int -> Bool`) and the pipe
operator (`e -> f`). Disambiguation happens in the parser based on
context. `ColonColon` is the list-cons operator used both in expressions
(`x :: xs`) and patterns.

## String literals

Standard escape handling: `\n`, `\t`, `\"`, `\\` are unescaped inline.
Unrecognized escapes (`\x`) pass the escaped character through as-is.
Unterminated string throws inside `scan()`, which the top-level `try`
converts into `Error ex.Message`.

## Char literals

Single-quoted char literals (`'a'`, `'\n'`) produce `CharLit of char`.
The escape set is: `\n`, `\t`, `\r`, `\\`, `\'`, `\"`, `\0`. Any other
backslash sequence raises an `Invalid char escape` error. An
unterminated or missing-closing-quote char literal also errors out.

```fsharp
| '\'' ->
    advance()  // consume opening '
    let ch =
        if source[pos] = '\\' then
            advance()
            match source[pos] with
            | 'n' -> '\n' | 't' -> '\t' | 'r' -> '\r'
            | '\\' -> '\\' | '\'' -> '\'' | '"' -> '"'
            | '0' -> '\000'
            | other -> failwith $"Invalid char escape '\\{other}'"
        else source[pos]
    ...
```

## Line comments

`--` starts a comment that runs to the end of the line. The scanner
consumes characters until the next `\n` without emitting a token:

```fsharp
| '-' when peek() = '-' ->
    while pos < source.Length && source[pos] <> '\n' do advance()
    scan()
```

Comment-only lines are also treated as non-significant by the indent
handler so they do not fire spurious DEDENTs inside a block.

## Layout tokens (INDENT / DEDENT)

This is the only non-obvious part of the lexer. ll-lang is
indentation-sensitive — but rather than leaking whitespace into the
parser, the lexer synthesizes `Indent` and `Dedent` tokens at significant
indentation changes.

### Algorithm

An `indentStack : Stack<int>` tracks the currently-open indent levels.
Starts as `[0]`.

On each newline:

1. The `Newline` token is emitted.
2. `measureAndHandleIndent()` counts leading spaces on the new line
   (treating `\t` as 4 spaces).
3. Determine if the line is "significant": non-empty, non-comment-only.
4. If the count is greater than the stack top: push it and emit
   `Indent`.
5. If the count is less than the stack top: pop levels while the top
   is greater than the new count, emitting `Dedent` for each pop.
6. If the count equals the top: do nothing.

At end-of-input, any remaining levels above `0` are drained as `Dedent`
tokens. Then `Eof` is appended.

### Why only significant lines matter

Blank lines and comment-only lines must not trigger DEDENT — otherwise a
blank line in the middle of an indented block would prematurely close it.
The check:

```fsharp
let significant =
    pos < source.Length &&
    source[pos] <> '\n' && source[pos] <> '\r' &&
    not (source[pos] = '-' && pos + 1 < source.Length && source[pos + 1] = '-')
```

### Tab policy

Tabs are silently treated as 4 spaces. Mixing tabs and spaces in the same
file is not recommended but is not a hard error.

## Error model

`tokenize` returns `Result<Tok list, string>`. The only source of errors
is an unterminated string literal, which bubbles up as a `failwith` and
is caught by the top-level `try/with`.

## Test coverage

See `tests/LLLangTests/LexerTests.fs`. Key cases:

- Basic token recognition for each operator and keyword.
- INDENT/DEDENT synthesis across multiple nesting levels.
- Blank-line tolerance inside blocks.
- String escape handling.
- `-->` vs `->` (second `-` is unary minus / binary minus after arrow).

Run:

```bash
dotnet test --filter LexerTests
```
