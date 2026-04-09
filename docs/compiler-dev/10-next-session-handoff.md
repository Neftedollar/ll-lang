# Next Session Handoff

Snapshot of project state as of the last clean session, for the next
session to pick up without replaying history.

## Current state

- **main** is at a clean commit, everything pushed to
  `github.com/Neftedollar/ll-lang`.
- **398 xUnit tests** pass (`dotnet test` from repo root).
- **Error positions are real** — E001..E008 report actual `line:col`
  instead of the historical `0:0`, threaded via a `PosMap` side-table
  keyed by reference equality on AST nodes.
- **Phases 1–6 + 7.1 + 7.2 + 7.3a + 7.3b + 7.3c + 7.4 + 7.5a + 7.5b + 7.5c + 7.5d + 7.5e + 7.6a + 7.6b + 7.6 integration + 7.7a + 7.7b + 7.7c + 7.7d + 7.8a + 7.8b + 7.8c + 7.8d + 7.8e done. Phase 7.7 is COMPLETE. Phase 7.8 is COMPLETE.**
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
- **Phase 7.7a + 7.7b + 7.7c + 7.7d done — Phase 7.7 is COMPLETE** —
  the full **Hindley-Milner inference spine in ll-lang itself** lives
  in [`18-hminfer-real.lll`](../../spec/examples/valid/18-hminfer-real.lll)
  (~1010 lines). 7.7a shipped a minimal `TypeExpr` (TyName / TyVar /
  TyFn), a parallel-list `Subst`, an `applyType` walker, and a
  `unify : TypeExpr -> TypeExpr -> Maybe[Subst]` mirroring the F# host's
  `HMInfer.unify` in `src/LLLangCompiler/HMInfer.fs` line 63 area.
  7.7b extended it in place with an `Env` (parallel-list like `Subst`),
  a dumb list-concat `composeSubst`, an explicit `Int`-threaded
  `freshVar`, an `Expr` AST (`EInt` / `EStr` / `EBool` / `EVar` /
  `EAdd` / `EApp`), an `InferResult = MkInferResult TypeExpr Subst Int`
  three-field carrier, and `inferExpr env n e` covering every arm
  (literal / var lookup / EAdd unify-both-with-Int / EApp fresh-beta
  + unify-with-TyFn). 7.7c added three structural `Expr` variants —
  `ELam Str Expr`, `ELet Str Expr Expr`, `EIf Expr Expr Expr` — plus
  an `applyEnv : Subst -> Env -> Env` helper. **7.7d closes out all
  four remaining HM closers**: (1) occurs check — `unifyVar` now
  calls `occursIn v t` before binding and emits `E008 InfiniteType`
  on circular substitutions; (2) Result-threaded errors — new
  `type Outcome A = OkR A | ErrR Str` carrier replaces the `TyName
  "ERROR"` sentinel, so `unify` returns `Outcome[Subst]` and
  `inferExpr` returns `Outcome[InferResult]` with real E001/E002/E008
  diagnostics; (3) `EMatch` inference — new `EMatch Expr List[Pat]
  List[Expr]` constructor and `Pat = PInt Int | PVar Str | PWild`,
  with an `inferMatch` helper family that walks parallel pat/body
  lists, derives patTy per pattern, unifies with scrutinee, infers
  body, unifies with shared β; (4) let-generalization — new
  `type TypeScheme = MkScheme List[Str] TypeExpr`, `Env` now stores
  schemes, `inferLet` calls `generalize env t1` before extending,
  `inferVar` calls `instantiate n sch` on lookup. New helpers:
  `ftvType` / `ftvScheme` / `ftvEnv` / `generalize` / `instantiate` /
  `freshSubstFor` / `applyScheme` / `substRemove*` /
  `listContainsStr` / `listDiffStr` / `listDedupStr`. Deliberately
  still out of scope after 7.7d: multi-param lambda, TypedAST
  round-trip, trait dispatch, wiring into `17-pipeline-real.lll`,
  tagged types (TyTagged) and E004/E005, flex/rigid TyVar split.
- `lllc run spec/examples/valid/18-hminfer-real.lll` prints
  ```
  t1 unify Int Int ok
  t2 unify Int Str mismatch
  t3 unify a Int ok bound a
  t4 unify (Int -> Str) (Int -> Str) ok
  t5 unify (Int -> Bool) (Int -> Str) mismatch
  t6 infer 42 : Int
  t7 infer (1 + 2) : Int
  t8 infer x in env : Int
  t9 infer (double 5) in env : Int
  t10 infer (double "x") in env : ERROR E001 TypeMismatch Int vs Str
  t11 infer (\x. x) : ($0 -> $0)
  t12 infer (\x. x + 1) : (Int -> Int)
  t13 infer (let x = 5 in x + 1) : Int
  t14 infer (if true then 1 else 2) : Int
  t15 infer (if true then 1 else "x") : ERROR E001 TypeMismatch Int vs Str
  t16 infer (\f. \x. f x) : (($1 -> $2) -> ($1 -> $2))
  t17 unify a (a -> Int) infinite
  t18 infer (match 1 | 0 -> "zero" | _ -> "other") : Str
  t19 infer (match 1 | 0 -> "zero" | 1 -> 42) : ERROR E001 TypeMismatch Str vs Int
  t20 infer (match 1 | x -> x + 1) : Int
  t21 infer (let id = \x. x in id 5) : Int
  t22 infer (let id = \x. x in let i = id 5 in id "hi") : Str
  ```
  (five `unify` cases t1-t5; ten basic `inferExpr` cases t6-t15;
  higher-order lambda t16; occurs-check E008 t17; EMatch success
  t18 + branch mismatch t19 + PVar-binding t20; basic let-bound
  lambda t21 + polymorphic double-use of `id` t22 — the canonical
  let-gen demo that only type-checks when `id` is generalized to
  `∀ $0. $0 -> $0` and each use instantiates fresh).
- **Phase 7.8a+b+c+d+e done — Phase 7.8 is COMPLETE** — **F# codegen
  written in ll-lang itself** lives in
  [`19-codegen-real.lll`](../../spec/examples/valid/19-codegen-real.lll)
  (926 lines; 212 after 7.8a, 341 after 7.8b, 546 after 7.8c, 683
  after 7.8d, 926 after 7.8e). Phase 7.8a added a tiny `TExpr` /
  `TDecl` AST (TEInt / TEStr / TEVar / TEAdd / TEApp / TELet; `TDFn
  Str List[Str] TExpr`) and a `showTExpr` / `showDecl` family that
  walks it and emits F# source strings, mirroring the F# host's
  [`Codegen.fs`](../../src/LLLangCompiler/Codegen.fs) `emitExpr` /
  `emitDecl`. Phase 7.8b added the three control-flow shapes
  (`TELam` / `TEIf` / `TEMatch`) with a `Pat = PInt Int | PVar Str |
  PWild` ADT; the match form is **single-line** to dodge F# offside-
  rule trouble. Phase 7.8c added `TDType` sum-type decl emission
  (`TypeArg` / `Ctor` ADTs, `showTypeDecl` / `showCtors` /
  `showCtorArgs` helpers, `Int → int64 / Str → string / Bool → bool`
  primitive-name mapping). Phase 7.8d added the module header + F#
  stdlib prelude block (`Module = MkModule Str List[TDecl]` ADT,
  `fsharpPrelude` 5-binding subset, `showModule` / `showModuleBody`
  walkers, `joinBlocks` "\n\n"-fold). **Phase 7.8e closes the
  umbrella with mutually-recursive `let rec ... and ...` grouping
  and `[<EntryPoint>]` emission** on the zero-param `main` fn: new
  `isMainFn` / `showMainDecl` / `showFnDeclPlain` / `showFnDeclFirst`
  / `showFnDeclCont` / `showFnGroup` / `splitAndShowDecls` helpers
  mirror the host's `emitDeclGroup` / `isMainFn` / `TDFn isMainFn`
  arm. The walker uses a simplified **"always rec for 2+" rule** —
  every consecutive run of 2+ non-main TDFns becomes a single
  `let rec ... and ... and ...` block unconditionally (F# accepts
  `let rec` on non-recursive bindings, and skipping the host's
  `containsVar` walker keeps the impl tiny). `main` grows from 10
  to 11 decls, adding a zero-param `TDFn "main" [] (TEApp (TEVar
  "print") (TEStr "hello"))` at the end to exercise the new
  `[<EntryPoint>]` branch. **Milestone**: all four compiler stages
  (lex → parse → elaborate → HM-infer → codegen) now have ll-lang
  representations for **every shape the bootstrap compiler will
  need**, and the codegen side emits a complete, compilable F#
  source file for every supported input. Deliberately still out
  of scope after 7.8e: proper recursion-detection walker (host's
  `containsVar`), mutual-recursion dependency splitting within
  groups, record / wrapped type bodies, parametric type applications
  in ctor args, multi-line match emission, keyword-safe ident
  rewriting, `TypeScheme`-carrying TypedAST, conditional
  Maybe/Result prelude sections, the full ~30-binding
  `fsharpPreludeCore`, and consuming the output of
  `18-hminfer-real.lll`.
- `lllc run spec/examples/valid/19-codegen-real.lll` prints
  ```
  module Examples.Generated

  // --- ll-lang stdlib prelude (auto-generated) ---
  let listMap f xs = List.map f xs
  let listLen (xs: 'a list) : int64 = int64 (List.length xs)
  let strLen (s: string) : int64 = int64 s.Length
  let strConcat (a: string) (b: string) = a + b
  let print (s: string) = System.Console.Write(s)
  // --- end prelude ---

  type Maybe<'A> =
      | Some of 'A
      | None

  type Shape =
      | Circle
      | Rect of int64 * int64
      | Empty

  type Pair<'A, 'B> =
      | MkPair of 'A * 'B

  let rec inc x = (x + 1L)
  and greet = "hello"
  and addOne x = (let y = (x + 1L) in y)
  and callInc x = (inc x)
  and choose b = (if b then 1L else 2L)
  and double = (fun x -> (x + x))
  and classify x = (match x with | 0L -> "zero" | _ -> "other")

  [<EntryPoint>]
  let main (argv: string[]) =
      (print "hello")
      0
  ```
  (a full compilable F# source file — module header, 5-binding
  stdlib prelude, three type decls (Maybe / Shape / Pair), a
  `let rec ... and ...` block holding all seven non-main fns
  (inc / greet / addOne / callInc / choose / double / classify),
  and an `[<EntryPoint>]`-wrapped `main` fn with `(print "hello")`
  body and `0` exit code. Phase 7.8a covers `inc` / `greet` /
  `addOne` / `callInc`; Phase 7.8b covers `choose` (TEIf),
  `double` (TELam bound as a top-level value), and `classify`
  (TEMatch with PInt + PWild + TEStr branches); Phase 7.8c covers
  `Maybe` / `Shape` / `Pair`; Phase 7.8d added the `module
  Examples.Generated` header and the stdlib prelude block; Phase
  7.8e collapses the fns into one rec group and adds the
  `[<EntryPoint>]` main fn).

## Immediate next task

**Phase 7.8 is now complete.** The ll-lang self-host codegen covers
every shape the bootstrap compiler will need: basic expressions
(TEInt / TEStr / TEVar / TEAdd / TEApp / TELet / TELam / TEIf /
TEMatch), sum-type declarations with parametric headers, a complete
F# module shell (header + stdlib prelude block), mutually-recursive
`let rec ... and ...` grouping for 2+ consecutive non-main fns, and
`[<EntryPoint>]` emission on the zero-param `main` fn. All four
compiler stages (lex → parse → elaborate → HM-infer → codegen) now
have ll-lang representations. The next natural step is **Phase
7.9** — assemble `bootstrap/compiler.lll` by stitching the five
self-host slices into a single end-to-end program.

### Option A: **Phase 7.9 — assemble `bootstrap/compiler.lll`**

Stitch the five existing self-hosted slices into a single end-to-end
program that reads source text, runs it through every stage, and
emits F# source:
  * `09-lexer-real.lll` — `tokenize`
  * `15-moduleparser-real.lll` — `parseModule`
  * `17-pipeline-real.lll` — `elaborate` (already wraps 15 + 16)
  * `18-hminfer-real.lll` — `inferExpr` (Phase 7.7d closed, so this
    prerequisite is unblocked; the HM spine now covers occurs check,
    Result-threaded errors, EMatch, and let-generalization)
  * `19-codegen-real.lll` — `showDecl`

A new `bootstrap/compiler.lll` imports the five modules and defines
`fn compile(src Str) Str = src |> tokenize |> parseModule |>
elaborate |> infer |> showModule`. First integration test: feeding
the compiler a minimal `module Hello\nfn main() = printfn "hi"`
source and asserting the emitted F# source compiles under the F#
host.

Recommended order: **Option A is the only unblocked path** — Phase
7.8 is complete in all five sub-slices (7.8a/b/c/d/e), so the HM
blocker, TDType blocker, prelude-block gap, `let rec` grouping gap,
and `[<EntryPoint>]` gap for `bootstrap/compiler.lll` are all gone.
The bootstrap assembly work can proceed without any further codegen
slices.

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
