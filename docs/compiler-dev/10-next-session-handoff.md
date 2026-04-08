# Next Session Handoff

Snapshot of project state as of the last clean session, for the next
session to pick up without replaying history.

## Current state

- **main** is at a clean commit, everything pushed to
  `github.com/Neftedollar/ll-lang`.
- **365 xUnit tests** pass (`dotnet test` from repo root).
- **Error positions are real** — E001..E008 report actual `line:col`
  instead of the historical `0:0`, threaded via a `PosMap` side-table
  keyed by reference equality on AST nodes.
- **Phases 1–6 + 7.1 + 7.2 + 7.3a + 7.3b done.** ll-lang now hosts a
  real lexer (`09-lexer-real.lll`), a real recursive-descent expression
  parser (`11-parser-real.lll`), a real type-declaration parser
  (`12-typeparser-real.lll`), AND a real fn-declaration parser
  (`13-fnparser-real.lll`) written in itself. Together they prove the
  language can express four of the five pieces of its own front end
  (lex, expression parse, type-decl parse, fn-decl parse); scaling up
  to a full ll-lang parser-in-itself is Phase 7.3c+.
- No stale worktrees, no stray branches, no uncommitted changes.
- `lllc run spec/examples/valid/hello.lll` prints `Hello, ll-lang!`.
- `lllc run spec/examples/valid/09-lexer-real.lll` prints
  `kw:fn id:add ( id:a ) ( id:b ) = id:a + id:b`.
- `lllc run spec/examples/valid/11-parser-real.lll` prints
  `(1 + (2 * 3))` (precedence-correct parse of `"1 + 2 * 3"`).
- `lllc run spec/examples/valid/12-typeparser-real.lll` prints
  ```
  type Maybe (A) = Some(A) | None
  type Result (A) (E) = Ok(A) | Err(E)
  type Shape = Circle | Rect | Empty
  type Wrapped = MkWrapped(Int)
  ```
  (four `type` decls round-tripped through tokenize → parse → pretty).
- `lllc run spec/examples/valid/13-fnparser-real.lll` prints
  ```
  fn add (a: Int) (b: Int) -> Int = (a + b)
  fn double (x: Int) -> ? = (x * 2)
  fn const (a: Int) (b: Int) -> Int = a
  fn answer () -> Int = 42
  ```
  (four `fn` decls with curried typed params and optional return
  types round-tripped through tokenize → parse → pretty).

## Immediate next task

**Phase 7.3c — full expression parser in ll-lang**. Extend the
arithmetic-subset body grammar from `11-parser-real.lll` (reused for
the body of `13-fnparser-real.lll`) to the full ll-lang expression
language: `let` / `let-in`, `if`-`then`-`else`, `match ... with`,
lambdas (`\x. body`), function application, cons `::` expressions,
list literals. After 7.3c, Phase 7.3d ties lexer + type-decl +
fn-decl + full-expression parsers together into the first ll-lang
front end written in ll-lang itself.

Before 7.3c the three language gaps surfaced by 7.3b are worth
fixing (see Phase 7.3b section in `09-self-hosting-roadmap.md`):

1. List-literal elements parse as atoms instead of applications, so
   `[TNum numVal]` becomes a two-element list. Needs `parseApp` or
   comma separators in the list-literal parser.
2. Multi-line `strConcat` chains trip application termination in
   `parseApp` under deeply-nested indentation.
3. Parenthesised type application as a ctor arg (`(Maybe TypeRef)`)
   rejects juxtaposition; only `Maybe[TypeRef]` bracket form works.

Other high-leverage backlog items can also be picked up first:

- Fix `atom[Tag]` ambiguity (parser lookahead beyond `]`)
- Codegen polish: emit prelude block AFTER types but handle files
  that don't declare any types (currently the order is fragile)

## Language gaps (backlog for Phase 7.3+)

Tracked in order of expected leverage for self-hosting work:

1. **No surface tuple literals**. `(a, b)` as an expression doesn't
   build `ETuple`. Workaround: named two-field ADT (`type Parsed =
   MkParsed Expr (List Token)`). Fix would require a small parser
   change + HMInfer for tuple construction.

2. **`atom[Tag]` ambiguity**. `foo [TPlus]` parses as
   `ETagged(foo, "TPlus")` instead of `EApp(foo, EList [TPlus])`.
   Workaround: parenthesize. Fix: lookahead beyond `]`.

3. **Codegen offside under deep nested `let`**. Long `let ... in (let
   ... in (if ... else ...))` chains inside indented function bodies
   emit F# that violates the offside rule. Workaround: split into
   top-level helpers. Fix: codegen should anchor nested `let` bodies
   on a known column.

4. **String interpolation**. Not a blocker, cosmetic. Today: chain
   `strConcat` calls.

5. **Prelude namespacing / collision with F# stdlib**. Our `exit`
   collides with F#'s `exit`. Today: forwarded through `int64`
   coercion in the prelude block. Fix: prefix all prelude bindings
   with something like `ll_` or emit them inside a nested F# module.

6. **No `::` in top-level `fn` param patterns**. Match-arm cons
   patterns work; destructuring a fn param via cons does not. Force
   users to take a plain list param and immediately `match` it.

## Architecture at a glance

```
Source (.lll)
  → Lexer      (layout-aware, synthetic INDENT/DEDENT)
  → Parser     (recursive descent, backtracking for let-pattern)
  → Elaborator (name resolution, exhaustiveness, declared-type checks E001-E005)
  → HMInfer    (Algorithm W, let-generalization, trait dispatch E006, occurs check E008)
  → Codegen    (F# source emission)
  → dotnet fsi (for `lllc run`)
```

F# compile order in `LLLangCompiler.fsproj`:
`Token.fs → Lexer.fs → AST.fs → Parser.fs → Elaborator.fs → Types.fs →
TypedAST.fs → HMInfer.fs → Codegen.fs → Compiler.fs`.

The `lllc` CLI is a thin wrapper project: `src/LLLangTool/Program.fs`.
Commands: `lllc build <file.lll>` writes `<file>.fs`; `lllc run
<file.lll>` builds and executes via `dotnet fsi`.

## Built-in stdlib (via elaborator `builtinEnv`)

The elaborator injects ~50 function schemes and the codegen prepends an
F# prelude block with matching runtime bindings. Covered groups:

- Math: `abs`, `absf`, `sqrt`, `min`, `max`
- List: `listLen`, `listMap`, `listFilter`, `listFold`, `listHead`,
  `listTail`, `listReverse`, `listAppend`, `listConcat`, `listIsEmpty`,
  `listAt`
- Maybe: `maybeMap`, `maybeBind`, `maybeWithDefault`
- Result: `resultMap`, `resultBind`, `resultMapErr`
- Str: `strLen`, `strConcat`, `strTrim`, `strContains`, `strToInt`,
  `strChars`, `strFromChars`, `strSlice`, `strIndexOf`, `strSplit`,
  `strReverse`
- Char: `charToInt`, `intToChar`, `charIsDigit`, `charIsAlpha`,
  `charIsSpace`, `intToStr`
- File IO: `readFile`, `writeFile`, `fileExists`
- Process: `exit`
- IO: `printfn`, `print`

`Maybe` and `Result` type declarations are NOT built-in. Any file using
a Maybe/Result-returning stdlib fn (`listHead`, `strToInt`,
`maybeMap`, ...) must declare the type locally. The codegen prelude
emits Maybe/Result-dependent runtime helpers only when the user file
declares the corresponding type.

## Language features available (as of Phase 7.1.6)

- Significant indentation (INDENT/DEDENT layout)
- Literals: Int, Float, Str, Bool, Char (`'a'`, `'\n'`)
- Operators: `+ - * / == != < > <= >= ::`
- Lists: `[a b c]` literals (space-separated, no commas), `::` cons
  patterns and expressions
- Tuples: destructurable via patterns, NOT constructible via
  expression literals (use ADT wrappers)
- Functions: typed params, untyped params, inferred return types,
  pattern-match bodies, mutually recursive top-level fns
- Lambdas: `\x. body`, `\x y. body`
- `let name = expr`, `let name = expr in body`
- `let (a, b) = pair` / `let _ = discarded` — pattern destructuring
- Haskell-style indented `let` (no `in` required when body follows on
  next indented line)
- `if ... then ... else ...`
- `match` as fn body (implicit scrutinee = last param) AND as value-
  position expression (explicit `match <e> with | p -> e | ...`)
- Patterns: `PVar`, `PWild`, `PLit`, `PCon name args`, `PTuple`,
  `PCons` (cons destructure), `PList` (matching via `[a b c]` literal
  patterns — untested in practice but AST exists)
- Type declarations: `type T = A | B | C` single-line, multi-line
  `type T =\n  | A\n  | B`, records, type parameters, phantom types
- Tags: `tag UserId`, `"x"[UserId]`, numeric tags with unit algebra
  (partial — simple cases work, composite `[m/s]` not yet modeled)
- Traits and `impl` blocks (from Phase 3)

## Error codes

`E001..E008`, all documented in `spec/error-codes.md`. All of E001,
E002, E003, E004, E005, E006, E008 are emitted by the compiler; E007
is reserved for multi-target Platform mismatches and never fires.

## Testing convention

`tests/LLLangTests/` has one `.fs` per compiler stage, roughly:

- `LexerTests.fs`
- `ParserTests.fs`
- `ElaboratorTests.fs`
- `HMInferTests.fs` — also contains a `valid corpus infers ok`
  `[<Theory>]` that iterates over `spec/examples/valid/*.lll`
- `CodegenTests.fs` — includes E2E runtime tests via
  `System.Diagnostics.Process` launching `lllc run`
- `StdlibTests.fs`
- `RealLexerTests.fs`
- `ArithmeticParserTests.fs`
- `TypeParserTests.fs`
- `FnParserTests.fs`

Add any new corpus example to the `valid corpus infers ok` theory so
its inference is smoke-tested automatically.

## Commit hygiene

- **Conventional commits** required (pre-commit hook enforces).
- **No co-author trailers** (global user rule — `~/.claude/rules/no-coauthors.md`).
- Heredoc commit messages trip the hook; write to `/tmp/commit-msg-*.txt`
  and use `git commit -F`.
- Use worktrees (`git worktree add ../ll-lang-wt-<name> -b <branch> main`)
  for any non-trivial change; merge with `--ff-only`; remove worktree
  and delete branch after merge.

## Useful one-liners

```bash
# Repo state audit
git worktree list && git branch && git status --short

# Full test suite
dotnet test --nologo 2>&1 | tail -5

# Run a corpus example
dotnet run --project src/LLLangTool -- run spec/examples/valid/hello.lll

# Build a corpus example (writes .fs next to source)
dotnet run --project src/LLLangTool -- build spec/examples/valid/09-lexer-real.lll

# Shut down stale MSBuild/test servers before retrying a hung build
dotnet build-server shutdown
```
