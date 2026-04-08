# Next Session Handoff

Snapshot of project state as of the last clean session, for the next
session to pick up without replaying history.

## Current state

- **main** is at a clean commit, everything pushed to
  `github.com/Neftedollar/ll-lang`.
- **381 xUnit tests** pass (`dotnet test` from repo root).
- **Error positions are real** — E001..E008 report actual `line:col`
  instead of the historical `0:0`, threaded via a `PosMap` side-table
  keyed by reference equality on AST nodes.
- **Phases 1–6 + 7.1 + 7.2 + 7.3a + 7.3b + 7.3c done.** ll-lang now
  hosts a real lexer (`09-lexer-real.lll`), a real recursive-descent
  arithmetic parser (`11-parser-real.lll`), a real type-declaration
  parser (`12-typeparser-real.lll`), a real fn-declaration parser
  (`13-fnparser-real.lll`), AND a real full-expression parser
  (`14-exprparser-real.lll`) written in itself. Together they prove
  the language can express every front-end piece needed for an
  ll-lang-in-ll-lang compiler — Phase 7.4 (tying the four parsers
  plus the lexer into one module-level front end) is the only step
  before the elaborator / codegen rewrites.
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
- `lllc run spec/examples/valid/14-exprparser-real.lll` prints
  ```
  (let x = 1 in (x + 2))
  (if x then 1 else 2)
  (match x with | 0 -> "zero" | _ -> "other")
  (fun y -> (y + 1))
  ((f x) y)
  ```
  (five expression kinds — let-in, if-then-else, match-as-expression,
  lambda, curried application — round-tripped through tokenize →
  parse → fully-parenthesised pretty).

## Immediate next task

**Phase 7.4 — full ll-lang module parser in ll-lang**. Combine the
real lexer (09), the type-decl parser (12), the fn-decl parser (13),
and the full-expression parser (14) into one module-level front end
that can read an entire `module M\n type ... \n fn ... = ...` file
end-to-end and produce a `List[Decl]` AST. This is the last step
before the self-hosting translation work (Stage D in the roadmap)
can begin in earnest — the bootstrap compiler needs a front end that
handles every surface form the F# `Parser.fs` parses.

Sketch of work:

1. Unify the four parsers' token types into one `Token` sum. Most
   constructors already agree (`TKwFn`, `TKwType`, `TUpper`, `TLower`,
   `TInt`, `TLParen`, etc.); a few renames will harmonise the set.
2. Extend the expression parser (14) to the body-expression grammar
   the fn-decl parser (13) actually needs: fn-declaration bodies use
   a richer expression language than 14 currently supports. Specifically,
   cons `::` patterns and list literals in expression position are
   out-of-scope in 14 and will need to come back.
3. Add a `Decl` sum (`DType TypeDecl | DFn FnDecl | DLet name expr`)
   and a top-level `parseModule` driver that walks the token stream,
   dispatching on `TKwType` / `TKwFn` / `TKwLet` for each decl.
4. Test with a small real corpus file — e.g., round-trip
   `hello.lll` through tokenize → parseModule → pretty → compare.

Other backlog items worth picking up opportunistically:

- Fix `atom[Tag]` ambiguity (parser lookahead beyond `]`)
- Codegen polish: emit prelude block AFTER types but handle files
  that don't declare any types (currently the order is fragile)
- Mutually-recursive user type declarations: 14-exprparser-real.lll
  had to flatten `type MatchArm = MkArm Pat Expr` into two parallel
  lists inside `EMatch` because codegen emits each user type in
  isolation (no `type ... and ...` grouping). A grouping pass in
  codegen would let future corpus files use the more natural shape.

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
- `ExprParserTests.fs`

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
