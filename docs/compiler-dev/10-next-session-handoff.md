# Next Session Handoff

Snapshot of project state as of the last clean session, for the next
session to pick up without replaying history.

## Current state

- **main** is at a clean commit, everything pushed to
  `github.com/Neftedollar/ll-lang`.
- **395 xUnit tests** pass (`dotnet test` from repo root).
- **Error positions are real** — E001..E008 report actual `line:col`
  instead of the historical `0:0`, threaded via a `PosMap` side-table
  keyed by reference equality on AST nodes.
- **Phases 1–6 + 7.1 + 7.2 + 7.3a + 7.3b + 7.3c + 7.4 + 7.5a + 7.5b + 7.5c + 7.5d + 7.5e + 7.6a + 7.6b + 7.6 integration + 7.7a done.**
  ll-lang now hosts a real lexer (`09-lexer-real.lll`), a real
  recursive-descent arithmetic parser (`11-parser-real.lll`), a real
  type-declaration parser (`12-typeparser-real.lll`), a real
  fn-declaration parser (`13-fnparser-real.lll`), a real full-expression
  parser (`14-exprparser-real.lll`), AND — as of Phase 7.4 / 7.5a /
  7.5b / 7.5c / 7.5d / 7.5e — a real full **module parser**
  (`15-moduleparser-real.lll`, 979 lines) that stitches the previous
  five slices into one recursive-descent front end consuming a whole
  `module M\n import ...\n tag ...\n type ...\n let ...\n export fn
  ...\n fn ... = ...` source end-to-end, including module-level `let`
  decls, `match`-with-explicit-scrutinee, `let name = e1 in e2` chains,
  `\x. body` lambdas, string literals, `[]` / `h :: t` cons patterns
  in match arms, **tagged literals** (`"x"[UserId]` as `ETagged`),
  **bracket-form parametric ctor args** (`Maybe[Int]` as `TAApp`), and
  **module-level `tag` / `import` / `export` decls** (`DTag` / `DImport`
  / `DExport`) — all **nine of nine** items from the Phase 7.5 backlog,
  closed across Phases 7.5a/7.5b/7.5c/7.5d/7.5e. "ll-lang has a full
  front-end in itself" is now one runnable program, not a story spread
  across five separate slices. The Phase 7.5 umbrella is closed; the
  heavier remaining items (multi-line fn bodies, multi-line type decls,
  `trait` / `impl` decls, list / tuple patterns) move to a separate
  Phase 7.6 slice before the self-hosting elaborator + codegen work
  begins.
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
- `lllc run spec/examples/valid/15-moduleparser-real.lll` prints
  ```
  module Examples.Bigger
  import Std.List
  import Std.Maybe
  tag UserId
  tag Email
  type Maybe (A) = Some(A) | None
  type Color = Red | Green | Blue
  type Container = MkBox(Maybe[Int])
  let answer = 42
  let zero = 0
  let uid = ("user-42"[UserId])
  export fn addOne (x: Int) -> Int = (x + 1)
  fn double (x: Int) -> Int = (x * 2)
  fn classify (x: Int) -> Int = (match x with | 0 -> 0 | _ -> 1)
  fn pickColor (x: Int) -> Color = (if x then Red else Green)
  fn shift (x: Int) -> Int = (let y = (x + 1) in (y * 2))
  fn applyDouble (x: Int) -> Int = ((fun y -> (y * 2)) x)
  fn greet () -> Str = "hello"
  fn classifyXs (xs: Int) -> Int = (match xs with | [] -> 0 | (h :: t) -> 1)
  ```
  (a whole module — header, two imports, two tags, three type decls,
  three module-level `let` decls, one exported fn decl, seven regular
  fn decls covering arithmetic, match-with-explicit-scrutinee, `if-
  then-else`, let-in chain, lambda-application, string literal, cons-
  pattern, tagged-literal, and parametric-ctor-arg bodies — round-
  tripped through tokenize → parseModule → showModule). The `let`
  decls and `match`-in-fn-body were added in Phase 7.5a on top of the
  Phase 7.4 baseline; `let-in` chains and `\x. body` lambdas were
  added in Phase 7.5b; string literals and `[]` / `h :: t` cons
  patterns were added in Phase 7.5c; tagged literals and parametric
  ctor args were added in Phase 7.5d; `tag` / `import` / `export`
  module-level decls were added in Phase 7.5e (closing the Phase 7.5
  umbrella 9/9).
- **Phase 7.6a + 7.6b done** — the first two slices of the ll-lang
  elaborator live in [`16-elaborator-real.lll`](../../spec/examples/valid/16-elaborator-real.lll)
  (512 lines). 7.6a shipped the two-pass `collectDecls` → `checkDecls`
  pipeline over a minimal local AST, emitting `E002 UnboundVar <name>`
  for every free variable in any fn body. 7.6b extended it with a
  **constructor-coverage exhaustiveness** pass — walks each `DFn`
  whose body is a top-level clause-sugar `EMatch` and whose last
  param's declared type is a known sum type, then emits `E003
  NonExhaustiveMatch <type> missing <ctor>` for every ctor not in
  the arm list. Skips fns whose arms carry a catch-all (`PWild` /
  `PVar` / `PCons`), mirroring the F# host elaborator's
  `exhaustivenessCheck` in `Elaborator.fs` lines 481-548. Still in
  scope for future Phase 7.6 slices: E001 type checking (7.6c),
  E004 / E005 tag / unit checks, source positions, builtin env,
  parser integration, ctor-arg patterns.
- `lllc run spec/examples/valid/16-elaborator-real.lll` prints
  ```
  E002 UnboundVar undefinedName
  E002 UnboundVar otherMissing
  E003 NonExhaustiveMatch Shape missing Empty
  ```
  (`bad(x) = undefinedName + otherMissing` fires two E002s;
  `shapeBad(s Shape) = | Circle -> 1 | Rect -> 2` fires one E003
  for the missing `Empty` ctor; `add` / `useCtor` / `shapeGood`
  / `shapeWild` and the non-fn decls all elaborate cleanly).
- **Phase 7.6 integration done** — the parser from
  `15-moduleparser-real.lll` and the elaborator passes from
  `16-elaborator-real.lll` now live in a single program:
  [`17-pipeline-real.lll`](../../spec/examples/valid/17-pipeline-real.lll)
  (~1500 lines). Source string -> tokenize -> parseModule ->
  elaborate -> error list -> stdout. The elaborator half was
  adapted to walk 15's richer `Decl` / `Expr` / `Pat` / `Param` /
  `FnDecl` / `LetDecl` / `TypeDecl` AST (16's minimal local AST
  was dropped). Showcase milestone: first time two compiler
  layers authored in ll-lang share a single AST and run back-to-
  back. Deliberately narrow cut — 15's `Pat` has no `PCon`, so
  matches over sum types report every constructor as missing.
- `lllc run spec/examples/valid/17-pipeline-real.lll` prints
  ```
  E002 UnboundVar undefinedName
  E003 NonExhaustiveMatch Shape missing Circle
  E003 NonExhaustiveMatch Shape missing Rect
  ```
  (hardcoded source: `module M\ntype Shape = Circle | Rect\nfn
  good(x Int) Int = x + 1\nfn bad(x Int) Int = undefinedName\nfn
  shapeBad(s Shape) Int = match s with | 0 -> 1`).
- **Phase 7.7a done** — the first slice of **Hindley-Milner inference
  in ll-lang itself** lives in
  [`18-hminfer-real.lll`](../../spec/examples/valid/18-hminfer-real.lll)
  (287 lines). Defines a minimal `TypeExpr` (TyName / TyVar / TyFn),
  a parallel-list `Subst`, an `applyType` walker, and a
  `unify : TypeExpr -> TypeExpr -> Maybe[Subst]` mirroring the F# host's
  `HMInfer.unify` in `src/LLLangCompiler/HMInfer.fs` line 63 area.
  The unifier covers each shape arm-by-arm: `TyName` vs `TyName`,
  `TyVar` (bind) on either side, `TyFn` vs `TyFn` (recurse args
  then results, single-level subst, no compose). Deliberately tight
  scope: no `inferExpr`, no env, no fresh-var counter, no occurs
  check, no compose, no `TyApp` / `TyTagged`. Each `t1` arm of
  `unify` is split into its own helper (`unifyName` / `unifyVar` /
  `unifyFn` / `unifyResults`) to keep `match` nesting one level deep
  — three-level nested matches in ll-lang have ambiguous arm
  bleed-over because indentation rules can't tell which match a
  shallower `|` belongs to.
- `lllc run spec/examples/valid/18-hminfer-real.lll` prints
  ```
  t1 unify Int Int ok
  t2 unify Int Str mismatch
  t3 unify a Int ok bound a
  t4 unify (Int -> Str) (Int -> Str) ok
  t5 unify (Int -> Bool) (Int -> Str) mismatch
  ```
  (five hardcoded `unify` cases — three success / "ok bound" lines
  and two mismatch lines, covering each `unify` arm at least once).

## Immediate next task

**Phase 7.7b — extend the HM-inference-in-ll-lang slice with `Env`,
fresh-var counter, occurs check, and a toy `inferExpr`**. Phase 7.7a
shipped the `unify` spine; the next tick adds the algorithm-W loop
shape so the file can infer literal / variable / addition / app
expression types end-to-end.

Shape (Phase 7.7b):
1. Extend `18-hminfer-real.lll` in place with an `Env` (parallel
   `List[Str]` + `List[TypeExpr]`, same trick as `Subst`).
2. Add a `FreshState` carrier — either a mutable counter via a side
   `Int Ref`-like ADT, or thread `Int` through every `infer` call.
3. Add the `Expr` sum the file's header already documents: `EInt` /
   `EStr` / `EBool` / `EVar` / `EAdd` / `EApp`.
4. Implement `inferExpr (env Env)(fresh Int)(e Expr) (TypeExpr, Subst, Int)`
   covering every arm. EAdd unifies both children with `Int`; EApp
   unifies `f`'s type with `TyFn(arg_ty, beta)` and returns `beta`
   under the result subst.
5. Add `occurs : Str -> TypeExpr -> Bool` and wire it into `unify`'s
   `TyVar` arm (return `None` if the var occurs in the candidate
   type) — this is E008 in the F# host.
6. Add a `composeSubst : Subst -> Subst -> Subst` so `inferExpr`
   can thread substs across multi-binder expressions. The current
   `unifyFn` returns the head subst only; once `inferExpr` exists
   it has to compose for the result-subst case.

Keep scope still tight: no let-generalization, no `ELam` / `EIf` /
`EMatch` / `ELet`, no trait dispatch, no source positions, no
`TyApp` / `TyTagged`. Those land in 7.7c onwards.

### Backlog: heavier module-parser extensions in ll-lang

Not blocking 7.6b/c. Phase 7.6 also has heavier module-parser work
parallel to the elaborator-in-ll-lang track. Pick any one (or combine
two if they compose cleanly) and ship it the same way 7.5a-e shipped:
extend `15-moduleparser-real.lll` **in place**, update the runtime E2E
expectation first (TDD), commit as
`feat(corpus)` + `test(corpus)` + `docs(self-hosting)`.

Phase 7.6 candidates, roughly ordered by leverage for self-hosting:

1. **`trait` / `impl` module-level decls.** Add `TKwTrait` / `TKwImpl`
   tokens, `DTrait` / `DImpl` decls, `parseTraitDecl` / `parseImplDecl`
   helpers. Each needs multi-line signature parsing (trait bodies) and
   indented fn decl lists (impl bodies) — roughly twice the complexity
   of the 7.5e `tag` / `import` / `export` trio. Grammar in
   `spec/grammar.ebnf` lines 172-177.
2. **Multi-line `fn` bodies.** Layout-sensitive parsing: the body
   indentation level relative to the `=` determines whether a
   subsequent token still belongs to the body. The F# host compiler
   uses synthetic `Indent` / `Dedent` tokens; the ll-lang mirror would
   need the same. Touches the lexer (to emit layout tokens on newline
   boundaries) AND the parser (to consume them). Biggest single item.
3. **Multi-line type decls.** `type T =\n  | A\n  | B`. 15's
   `parseCtors` doesn't skip newlines between ctors. Fix: make
   `parseCtorsTail` / `parseCtor` newline-tolerant. Smaller than the
   fn-body layout item because type decls don't nest expressions.
4. **List-literal expression atoms + tuple patterns.** `[a b c]` as
   an `EList Expr*` atom in expression position, and `(a, b)` as a
   `PTuple Pat Pat` arm in `parsePrimaryPat`. The cons-only subset
   already shipped in 7.5c; these two round out the list / tuple
   coverage.
5. **Starting the elaborator-in-ll-lang slice (Stage D).** The first
   half of a new file — name resolution, unbound-var detection,
   declared-type checks — that consumes the `List[Decl]` AST
   `15-moduleparser-real.lll` already produces and emits an
   `ElaboratedModule`. Mirrors `src/LLLangCompiler/Elaborator.fs`.
   Largest item by total line count, but also the highest leverage
   for the self-hosting goal.

Test the result by round-tripping an actual existing corpus file
(e.g., `01-basics.lll`, `02-adts.lll`, or even a trimmed variant of
`hello.lll`) through tokenize → parseModule → pretty → compare.

Other backlog items worth picking up opportunistically:

- Fix `atom[Tag]` ambiguity (parser lookahead beyond `]`)
- Codegen polish: emit prelude block AFTER types but handle files
  that don't declare any types (currently the order is fragile)
- Mutually-recursive user type declarations: 14-exprparser-real.lll
  had to flatten `type MatchArm = MkArm Pat Expr` into two parallel
  lists inside `EMatch` because codegen emits each user type in
  isolation (no `type ... and ...` grouping). A grouping pass in
  codegen would let future corpus files use the more natural shape.

## Language gaps (backlog for Phase 7.5+)

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
