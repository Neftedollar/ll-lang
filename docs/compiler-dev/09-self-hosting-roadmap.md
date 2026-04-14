# Self-hosting roadmap

> **Status (2026-04-13): COMPLETE.** Phase 7.10 bootstrap fixpoint achieved.
> `compiler₁.fs == compiler₂.fs` (602 lines, 264 bindings, byte-identical).
> See `docs/compiler-dev/fixpoint-snapshots/compiler1-latest.fs`.

Phase 7 of the project goal: rewrite the ll-lang compiler in ll-lang
itself. The current F# compiler ("compiler₀") becomes a bootstrap
artifact, and after one fixpoint cycle the language is self-sufficient
on .NET.

## Why self-host?

Two reasons:

1. **Proof of expressiveness.** If ll-lang can compile itself, it has
   enough power for non-trivial systems work (string handling, file
   IO, recursive data structures, error reporting). Until then it's
   not credible as a general-purpose language.
2. **Single source of truth.** Right now the F# compiler is the spec
   in code form. After self-hosting, the canonical compiler is in
   ll-lang and the F# version is frozen as a bootstrap-only relic.

## Bootstrap sequence

Classical three-step:

```
1.  compiler₀  =  src/LLLangCompiler/*.fs    (the current F# compiler)
2.  compiler.lll  =  the same compiler, hand-translated to ll-lang
3.  compiler₁  =  compiler₀ compiler.lll     (binary built by F# compiler)
4.  compiler₂  =  compiler₁ compiler.lll     (binary built by ll-lang)
5.  assert  compiler₂ == compiler₁           (fixpoint achieved)
```

Once `compiler₂ == compiler₁`, the F# source is no longer needed for
correctness. We keep it as a build-time bootstrap and as documentation.

## What's missing today

The ll-lang surface currently does not have enough features to
implement its own compiler. Concrete gaps, in priority order:

### 1. Strings as data

Today only `Str` literals and `printfn` work. Self-hosting needs:

- `String.length`, indexing, slicing
- `String.split` / `String.join`
- `String.toCharList` / `String.fromCharList`
- Char operations (`isDigit`, `isLetter`, `isUpper`, ...)
- StringBuilder-equivalent for the codegen output

These come in via `Std.Str` and a small `Std.Char` module — both
backed by F# `String`/`Char` for the .NET target.

### 2. File IO

The compiler reads `.lll` files and writes `.fs` files. Without IO,
it can't run. We need at minimum:

- `File.readAllText : Str -> IO[Str]`
- `File.writeAllText : Str -> Str -> IO[Unit]`
- `Console.print : Str -> IO[Unit]`
- `Console.error : Str -> IO[Unit]`

These belong in `Platform.IO.File` and `Platform.IO.Console`. The
`IO[A]` type is a phantom-tagged wrapper around the underlying value;
the .NET backend implements it as identity.

### 3. List operations

Inference and elaboration use `List` everywhere. We need real
`Std.List` with:

- `map`, `filter`, `fold`, `foldBack`
- `head`, `tail`, `cons`, `append`
- `length`, `reverse`, `zip`, `unzip`
- `tryFind`, `contains`, `distinct`

The current parser already understands `[1 2 3]` as a list literal,
and inference assigns it `List[A]`. Codegen emits F# `list`.

### 4. `Map` / `Set` collections

The compiler is full of `Map<string, _>` for environments. Without
`Std.Map`, we'd have to use association lists (`List[(K, V)]`) which
is fine for correctness but quadratic. A real `Std.Map` is part of
the bootstrap stdlib.

### 5. Mutable state, or a robust state-monad pattern

The current F# compiler uses small mutable cells (`Ctx.Pos`,
`InferState.Errors`). In ll-lang we'd either:

- Add a controlled `Ref` primitive (`ref A`, `:=`, `!`).
- Or thread state explicitly via `State` / `Result` monad combinators
  (verbose but pure).

The decision is open. `ref` is simpler for bootstrap; the monadic
form is purer but requires `do`-notation sugar to be tolerable.

### 6. Pattern match completeness

The current elaborator's exhaustiveness check has a known gap with
wildcard `_` patterns. Self-hosting requires the bootstrap compiler
to be correct on its own source — so the wildcard fix and any other
known limitations have to land before we can ship `compiler.lll`.

### 7. Source position tracking

Many errors today emit `0:0`. The bootstrap compiler should produce
real positions because debugging compiler bugs by line number matters
a lot. This means:

- Threading `Line`/`Col` through every AST node, not just `Tok`.
- Storing position in `LLError` correctly.
- Updating the compact error format to always carry real numbers.

### 8. Trait dispatch resolution

Today, automatic resolution of trait method calls is incomplete — the
user often has to call `map_Maybe` directly or pass through a
constrained generic. Self-hosting needs the dispatch tables to fully
work, otherwise the bootstrap compiler will be cluttered with manual
dispatch.

## Staging plan

### Stage A — Bootstrap stdlib (Phase 6)

Implement `Std.List`, `Std.Maybe`, `Std.Result`, `Std.Str`,
`Std.Math`, `Std.Map`, `Std.Set`. Each module gets:

- `.lll` source under `stdlib/`
- F# implementation that ll-lang code can call (resolved at link
  time via the import system)
- Tests in `tests/LLLangTests/StdlibTests.fs`

Add the multi-file linker so `import Std.List` actually resolves to a
real module.

### Stage B — Platform.IO (Phase 6.5)

`Platform.IO.File`, `Platform.IO.Console` with .NET-only impls.
Surface as `IO[A]` phantom-tagged values.

### Stage C — Bug fixes for completeness

- Wildcard `_` exhaustiveness.
- Position tracking through every AST node.
- Trait dispatch automatic resolution.

### Stage D — Translate compiler (Phase 7.0)

Hand-translate `src/LLLangCompiler/*.fs` to `src/compiler.lll` (or
`compiler/*.lll` if we split modules). Expected breakdown:

| F# file        | ll-lang file              | Estimated LOC |
|----------------|---------------------------|---------------|
| `Token.fs`     | `Compiler.Token`          | ~50           |
| `Lexer.fs`     | `Compiler.Lexer`          | ~200          |
| `AST.fs`       | `Compiler.AST`            | ~80           |
| `Parser.fs`    | `Compiler.Parser`         | ~700          |
| `Elaborator.fs`| `Compiler.Elaborator`     | ~400          |
| `Types.fs`     | `Compiler.Types`          | ~120          |
| `TypedAST.fs`  | `Compiler.TypedAST`       | ~80           |
| `HMInfer.fs`   | `Compiler.HMInfer`        | ~500          |
| `Codegen.fs`   | `Compiler.Codegen`        | ~250          |
| `Compiler.fs`  | `Compiler`                | ~30           |

Translation is mostly mechanical — discriminated unions become
ll-lang `type Foo = Ctor1 ... | Ctor2 ...`, function definitions
become `fn`, mutable state becomes either `ref` or threaded
explicitly.

### Stage E — Fixpoint (Phase 7.1)

```bash
# build compiler₁ via F# bootstrap
lllc-bootstrap build compiler.lll
fsc compiler.fs -o compiler1.exe

# use compiler₁ to build compiler₂
mono compiler1.exe build compiler.lll
fsc compiler.fs -o compiler2.exe

# diff
diff compiler1.exe compiler2.exe
```

When the diff is empty, we have fixpoint. Cache `compiler1.exe` as
the canonical bootstrap binary (or rebuild from F# whenever needed).

### Stage F — Retire F# source

Move `src/LLLangCompiler/` to `bootstrap/` and stop maintaining it.
The ll-lang source becomes the only place compiler changes happen.
F# bootstrap is regenerated only when the language gains a feature
the bootstrap can't lex/parse.

## Risks

- **Translation drift.** Manual translation introduces subtle bugs.
  Mitigation: extensive corpus tests, ideally a property test that
  verifies `compiler₀(src) == compiler₁(src)` on every corpus file.
- **Cycle in feature dependencies.** Some features (e.g. trait
  dispatch) need themselves to be implemented in the compiler.
  Mitigation: add features to compiler₀ first, then to compiler.lll.
- **Missing optimizations.** A naively-translated compiler may be
  slow. Acceptable for the bootstrap milestone — optimize later.
- **Memory model differences.** F# has GC, value types,
  CLR-specific patterns. ll-lang's runtime model has to match closely
  enough that the translation is straightforward.

## Out of scope for self-hosting

These are explicitly *not* required for the Phase 7 milestone:

- Multi-target backends (TS, Python, JVM, LLVM). Phase 8+.
- IDE integration / language server.
- Incremental compilation.
- Optimizer passes beyond what F# already does for free.

## Tracking

The self-hosting milestone is a single GitHub issue
(`Neftedollar/ll-lang#7-self-hosting`) with sub-issues for each stage.
Progress is measured by which `.fs` files have a working `.lll`
counterpart.

## Progress log

### 2026-04 — Phase 7.1: real lexer in ll-lang (DONE)

The first concrete piece of `Compiler.Lexer` is now expressible in
ll-lang and lives in
[`spec/examples/valid/09-lexer-real.lll`](../../spec/examples/valid/09-lexer-real.lll).
It strictly subsumes the `08-lexer-poc.lll` toy: it groups multi-char
identifiers and multi-digit integers, recognises a small keyword set
(`let` / `fn` / `if` / `then` / `else`) and the single-char operators /
parens, and emits a flat `List Token`. The accompanying tests live in
`tests/LLLangTests/RealLexerTests.fs`. Test count: 281 → 284.

Things this proves the language can already do, in idiomatic form:

- ADTs with 17+ constructors (`type Token = ...`).
- Recursive top-level fns operating on `List Char`.
- `Maybe`-returning destructuring helpers (`listHead` / `listTail`)
  factored into thin wrappers — workable but verbose.
- Multi-fn mutual recursion across `lexChars`, `lexId`, `lexNum`,
  `lexOp` — codegen wraps the lot in a `let rec ... and ...` block.

Things this surfaced as language gaps for Phase 7.2+:

1. **No `::` cons in patterns or expressions.** All list deconstruction
   has to go through `listHead` / `listTail` plus a Maybe-unwrapping
   helper, which is much noisier than a `c :: rest` pattern. Adding
   `::` to the parser, the AST (or sugaring it as `PCon "::"`),
   inference, and codegen is the highest-leverage next change.
2. **`atom[TypeId]` ambiguity.** A bracketed type-constructor expression
   that immediately follows another atom (`listAppend [TPlus] xs`) is
   parsed as a tagged literal instead of a list literal applied as an
   argument. The lexer works around this with a `pre` helper, but the
   parser should disambiguate using lookahead beyond `]`.
3. **No `match ... with` in expression position.** `EMatch` is only
   reachable as a fn body, so every nested case-split has to be lifted
   into its own top-level helper. For a real compiler we want
   expression-form `match`.
4. **No `let` patterns.** `let (a, b) = ...` would let the lexer thread
   `(taken, leftover)` tuples through `takeWhile` in a single pass,
   instead of running two passes (`takeWhilePred` + `dropWhilePred`).
5. **Codegen indentation under deep nesting.** Naive emission of
   `let ... in <body>` chains inside an `if`-ladder produces F# that
   trips the offside rule. The lexer was restructured into smaller
   top-level fns to dodge this; the better fix is for codegen to emit
   normalised newlines and re-indent each `let` body from a known
   anchor.

These gaps drove the Phase 7.1.5 and 7.1.6 work plans (below).

### 2026-04 — Phase 7.1.5: cons patterns + match-as-expression (DONE)

Closed gaps #1 and #3 from the Phase 7.1 list:

1. `::` cons was added in both pattern and expression position. Right-
   associative (`1 :: 2 :: rest` → `ECons(1, ECons(2, rest))`). Parser
   precedence sits between `==` and `+` (so `1 + 2 :: xs` = `(1+2) :: xs`
   and `a == 1 :: rest` = `a == (1 :: rest)`). Codegen emits native F#
   `::` — no transformation. HMInfer.patternType for PCons threads the
   element-var unification back into bindings so `match xs with | x :: _
   -> x` infers to `(List A) -> A` correctly.
2. `match <expr> with | p -> e | ...` as a value-position expression
   landed as the additive `EMatchOf` variant — the existing implicit-
   scrutinee `EMatch` form used by fn bodies is untouched. Codegen emits
   single-line `(match scrut with | p1 -> e1 | p2 -> e2)` to dodge F#
   offside rule issues. Branch type-mismatch correctly yields E001.

Along with the features, `09-lexer-real.lll` was rewritten to use
`c :: rest` patterns directly and to inline keyword classification via
`match s with | "let" -> TLet | ... | _ -> TIdent s`, dropping the
`unwrapCharOrSpace` / `unwrapTailOrEmpty` / `headChar` / `tailChars`
helpers. Token output unchanged.

Test count: 284 → 307.

### 2026-04 — Phase 7.1.6: parser prep (DONE)

Closed gaps #4 and the multi-line-sum issue:

1. **`let` pattern destructuring**: `let (a, b) = pair`, `let h :: t =
   xs`, `let _ = sideEffect` all work in both top-level `DLet` position
   and expression-level `ELet`. Implemented via additive AST variants
   (`DLetPat of Pattern * Expr`, `ELetPat of Pattern * Expr * Expr
   option`) so existing `PVar x` fast paths stay untouched. Codegen
   emits native F# `let (a, b) = expr` reusing the existing
   `emitPattern`.
2. **Multi-line sum type declarations**: `type T =\n  | A\n  | B\n  | C`
   now parses to the same `TBSum` AST as the single-line form. Enables
   compact multi-constructor types like the Phase 7.2 parser AST.

Added corpus example `spec/examples/valid/10-multiline-sum.lll`.

Test count: 307 → 325.

Remaining gaps after 7.1.6 (drive Phase 7.2 parser work):

- **No surface tuple-literal expressions**: `(a, b)` as an expression
  doesn't build an `ETuple`. `ETuple` is only inhabited by fn-parameter
  destructuring. Workaround for parser authors: use a named two-field
  ADT variant like `type Parsed = MkParsed Expr (List Token)` and
  destructure via `match | MkParsed e rest -> ...`.
- **`atom[TypeId]` ambiguity**: still unfixed. Parenthesize around
  bracketed lists when applying to a function.
- **Codegen indentation under deep nesting**: still a lurking hazard;
  the workaround remains "split into top-level helpers".

### 2026-04 — Phase 7.2: arithmetic expression parser (DONE)

`spec/examples/valid/11-parser-real.lll` (164 lines) is a working
recursive-descent expression parser written in ll-lang itself. For
input `"1 + 2 * 3"` it prints `(1 + (2 * 3))` end-to-end via `lllc
run`. Together with `09-lexer-real.lll`, ll-lang now hosts both a real
lexer and a real parser written in itself — the first end-to-end
proof toward the Phase 7 self-hosting milestone.

The parser uses every Phase 7.0/7.1 language feature: cons patterns
(`c :: rest`), match-as-expression with explicit scrutinee, multi-line
sum types (`Token`/`Expr`/`DigitRun`/`Parsed` ADTs), `let .. in`
chains, and mutually recursive top-level fns (`parseExpr` ↔
`parseExprTail`, `parseTerm` ↔ `parseTermTail`).

The surface-tuple gap is worked around with named two-field ADT
wrappers (`MkParsed Expr List[Token]`, `MkDigitRun List[Char]
List[Char]`) and destructured via match arms.

Three compiler bugs were uncovered and fixed in flight:

1. **`exhaustivenessCheck` over-eagerness.** The pass was scanning the
   FIRST fn parameter (clause sugar actually scrutinizes the LAST) and
   recursing into nested matches, treating every match as if it
   scrutinized the outer fn's parameter type. Fixed by narrowing the
   check to top-level clause-sugar bodies only and using the LAST
   parameter as the scrutinee. Nested matches and explicit
   `match ... with` are skipped — full exhaustiveness across arbitrary
   match expressions belongs in HMInfer (Phase 4) where types are
   actually known.

2. **F# offside violation on nested `let-in`.** TELet was emitting the
   `in` body on a new indented line, which landed left of the
   enclosing context column when nested inside another expression and
   produced FS0058. Fixed by emitting `(let x = e in body)` inline.

3. **Curried application of multi-arg ADT constructors.** ll-lang
   surface syntax curries constructor applications (`MkPair x y`),
   which the AST stores as `TEApp(TEApp(TECon "MkPair", x), y)`. F#
   treats DU constructors with multiple fields as taking a tuple, so
   the old curried codegen produced FS0001 type errors. Fixed by
   gathering all args along the chain and emitting tuple form
   `(MkPair (x, y))` when the leftmost head is a `TECon` and arity is
   2 or more.

Test count: 328 → 331 (corpus theory + 2 new ArithmeticParserTests).

### 2026-04 — Phase 7.3a: type-declaration parser (DONE)

`spec/examples/valid/12-typeparser-real.lll` (217 lines) is a working
recursive-descent **type-declaration** parser written in ll-lang
itself. Third self-hosting milestone after the real lexer (7.1) and
the arithmetic parser (7.2). Together they cover lexing, expression
parsing, and type-decl parsing — three pieces of the eventual
ll-lang-in-ll-lang front end.

The parser handles the single-line form
`type Name P1 P2 = Ctor1 Arg1 Arg2 | Ctor2 | ...` for the input

```
type Maybe A = Some A | None
type Result A E = Ok A | Err E
type Shape = Circle | Rect | Empty
type Wrapped = MkWrapped Int
```

and prints each declaration on its own normalised line via `lllc run`:

```
type Maybe (A) = Some(A) | None
type Result (A) (E) = Ok(A) | Err(E)
type Shape = Circle | Rect | Empty
type Wrapped = MkWrapped(Int)
```

Type params print one-per-paren so the param/ctor boundary is visually
obvious; nullary ctors print bare; ctors with args wrap them in
`(a, b)` form. Single-letter uppercase identifiers are classified as
type variables, longer ones (`Int`, `Maybe`, ...) as type constructors.

The parser exercises:

- Mutually-recursive top-level fns (`parseTypeDecls` ↔ `parseDeclsClean`
  ↔ `consDecl`, plus `parseCtors` ↔ `parseCtorsTail`)
- Surface tuple-literal returns from helper fns
  (`parseTypeDecl : List Token -> (TypeDecl, List Token)`) without
  needing the old `MkParsed` ADT wrapper
- Cons patterns + clause sugar on token streams (`TKwType :: rest`,
  `TBar :: rest`, `TUpper s :: rest`)
- ASCII range checks on `charToInt` for an inline `isUpperChar`
  predicate (ll-lang lacks `&&`, so two nested `if`s suffice)

Two language quirks surfaced (added to the Phase 7.3b backlog):

1. **Clause-sugar bodies with multi-line `let-in` arms get parsed
   wrong.** A wildcard arm whose body is `let (a, b) = expr in
   useA useB` ends up reading `a`/`b` as out-of-scope. Workaround:
   split the multi-line let into a separate single-line helper fn.
2. **List-literal arms in clause sugar are silently dropped.** Mixing
   `| TEnd :: _ -> []` and `| [] -> []` and `| _ -> ...` in a single
   clause-sugar body codegens to a single-arm match (only the first
   cons arm survives), producing a runtime `MatchFailure`. Workaround:
   use a positive-only cons pattern (`| TKwType :: _ -> ...`) and let
   the wildcard handle empty + EOF together.
3. **`params` is reserved in F#.** A param named `params` in ll-lang
   round-trips through codegen and triggers FS0046. Workaround: rename
   to `prms`. Long-term fix: codegen should rewrite reserved names.

Test count: 354 → 357 (corpus theory + 2 new TypeParserTests).

Next slice: **Phase 7.3b — fn-decl parser** (`fn add(a Int)(b Int) Int
= a + b`), then 7.3c — full expressions (richer than the arithmetic
subset), then tying lexer + type-decl + fn-decl + expression parsers
together into a real ll-lang front end written in ll-lang itself.

### 2026-04 — Phase 7.3b: fn-declaration parser (DONE)

`spec/examples/valid/13-fnparser-real.lll` (270 lines) is a working
recursive-descent **fn-declaration** parser written in ll-lang itself.
Fourth self-hosting milestone after the real lexer (7.1), the
arithmetic parser (7.2), and the type-decl parser (7.3a). Together
they cover four of the five front-end pieces needed for an
ll-lang-in-ll-lang compiler — only the full expression parser (7.3c,
adding `if`/`let`/`match`/lambdas on top of the arithmetic subset)
remains before every surface form the F# front end parses has a
mirror implementation in ll-lang.

The parser handles the single-line form
`fn Name(p1 T1)(p2 T2) ... RetTy? = bodyExpr` for the input

```
fn add(a Int)(b Int) Int = a + b
fn double(x Int) = x * 2
fn const(a Int)(b Int) Int = a
fn answer() Int = 42
```

and prints each declaration on its own normalised line via `lllc run`:

```
fn add (a: Int) (b: Int) -> Int = (a + b)
fn double (x: Int) -> ? = (x * 2)
fn const (a: Int) (b: Int) -> Int = a
fn answer () -> Int = 42
```

Curried param groups print space-separated as `(name: Type)`; nullary
fns print `()`; missing return types print as `?`; binary body ops are
fully parenthesised; leaves (variables, literals) print bare. The
body-expression grammar is a strict subset of 11-parser-real.lll's:
integer literals, variable references, and `+` / `-` / `*` / `/` with
the usual precedence. `if` / `let` / `match` / lambdas are Phase
7.3c's job.

The parser exercises:

- A 7-way mutually-recursive fn group (`parseFnDecls` ↔
  `parseDeclsClean` ↔ `consDecl` ↔ `parseFnDecl`, plus the
  five-way expression parser `parseExpr`/`parseExprTail`/`parseTerm`/
  `parseTermTail`/`parseFactor` reused from 11-parser-real.lll with
  a TLower-variable leaf added to `parseFactor`)
- A four-field ADT (`FnDecl`) carrying `Str`, `List[Param]`,
  `Maybe[TypeRef]`, and `Expr` — the first corpus file that uses
  the bracket syntax `Maybe[TypeRef]` inside a constructor signature
  (parens `(Maybe TypeRef)` don't parse as a single ctor arg; see
  gap 4 below)
- Optional return-type parsing by looking for a `TUpper` token
  immediately before `=` — a clean example of "Maybe from lookahead"
- Surface tuple returns from every parser helper
  (`parseFnDecl : List Token -> (FnDecl, List Token)`)

Three language gaps surfaced (these become Phase 7.3c
prerequisites):

1. **List-literal elements parse as atoms, not applications.** Writing
   `[TNum numVal]` in an expression position parses as a two-element
   list `[TNum, numVal]` rather than the one-element list
   `[TNum numVal]`. Workaround: let-bind the application first
   (`let tok = TNum numVal in listAppend [tok] ...`). The list-literal
   parser in `Parser.fs` calls `parseTagged` per element, which stops
   at `parseAtom`, so juxtaposition-application is lost. Fix: switch
   the list-literal element parser to `parseApp`, or require commas.
2. **Deeply-nested multi-line `strConcat` chains confuse `parseApp`.**
   A `strConcat` applied across multiple newline-indented arguments
   loses the application shape and elaborates as if `strConcat` were
   used unapplied, producing `TyFn(Str, TyFn(Str, Str)) vs Str`.
   Workaround: use single-line `strConcat` calls or pipe through
   `let` intermediates. Likely root cause: indentation-sensitive
   application termination in `parseApp`.
3. **Parenthesised type application as a ctor arg rejects juxtaposition.**
   `type FnDecl = MkFn ... (Maybe TypeRef) ...` treats the inside of
   the parens as `parseTypeExprTop`, which only applies types via
   `[…]` brackets, so `Maybe TypeRef` inside parens errors out and
   the ctor silently drops the arg. Workaround: write `Maybe[TypeRef]`
   without parens. Fix: inside `parseBase`'s paren branch, accept
   the surface `List`-style application grammar instead of the
   stricter `parseTypeExprTop`.

Test count: 362 → 365 (corpus theory + 2 new FnParserTests).

Next slice: **Phase 7.3c — full expression parser** (adds `if` /
`let` / `match` / lambda on top of the arithmetic subset), then
tying lexer + type-decl + fn-decl + full-expression parsers together
into the first ll-lang front end written in ll-lang itself.

### 2026-04 — Phase 7.3c: full expression parser (DONE)

[`spec/examples/valid/14-exprparser-real.lll`](../../spec/examples/valid/14-exprparser-real.lll)
(425 lines) is a working recursive-descent **full expression** parser
written in ll-lang itself. Fifth self-hosting milestone after the real
lexer (7.1), the arithmetic parser (7.2), the type-decl parser (7.3a),
and the fn-decl parser (7.3b). Together the five corpus files cover
every front-end piece needed for an ll-lang-in-ll-lang compiler —
Phase 7.4 (stitching lexer + type-decl + fn-decl + full-expression
parsers into one file that can read a full ll-lang module) is the
only piece left before the bootstrap can begin in earnest.

Supported expression forms:

- integer and string literals, lowercase variable references
- parenthesised grouping
- curried application via juxtaposition (`f x y` → left-associative)
- binary `+ - * /` with classical precedence
- `let x = e1 in e2` (single-line)
- `if c then a else b`
- `match e with | pat -> body | pat -> body ...` where `pat` is an
  integer literal, string literal, variable, or wildcard
- `\x. body` lambdas (single parameter; nest `\` for multi-arg)

Out of scope (do not appear in 14-exprparser but remain open work for
the eventual full compiler): cons / constructor patterns in match
arms, tagged literals, list / tuple literals in expression position,
multi-arg lambdas at the surface, type annotations, pipes, multi-line
`let-in` bodies.

The parser walks five driver inputs:

```
let x = 1 in (x + 2)
if x then 1 else 2
match x with | 0 -> "zero" | _ -> "other"
\y. (y + 1)
f x y
```

and pretty-prints each in unambiguous, fully-parenthesised form via
`lllc run`:

```
(let x = 1 in (x + 2))
(if x then 1 else 2)
(match x with | 0 -> "zero" | _ -> "other")
(fun y -> (y + 1))
((f x) y)
```

The parser exercises every tool in the current ll-lang toolbox: a
13-arm `Expr` sum type, a dispatch-on-first-token `parseExpr` that
fans out to special-form helpers (`parseLet` / `parseIf` /
`parseMatch` / `parseLam`), a three-level precedence cascade
(`parseAddSub` → `parseMulDiv` → `parseApp` → `parseAtom`), a
juxtaposition-as-application layer that consults an `isAtomStart`
predicate to know when to stop, multi-token operator lexing for `->`,
and string-literal lexing with a small `takeStrBody` helper.

Match arms are represented as two parallel `List[Pat]` / `List[Expr]`
fields inside `EMatch` rather than a mutually-recursive
`type MatchArm = MkArm Pat Expr`. The current F# codegen lowers each
user type independently (no `type ... and ...` grouping), so mutually-
recursive type declarations cannot cross-reference each other. The
parallel-list representation keeps the two components in lockstep by
appending to both in the same recursive step of `parseArms`.

Test count: 378 → 381 (corpus theory + 2 new ExprParserTests).

Next slice: **Phase 7.4 — full module parser**, combining the lexer
(09), the type-decl parser (12), the fn-decl parser (13), and the
full-expression parser (14) into a single file that can read an
entire ll-lang module end-to-end. This is the last step before the
self-hosting translation work (Stage D in the plan above) can
begin — the bootstrap compiler needs a real front end that handles
every surface form the F# compiler parses.

### 2026-04 — Phase 7.4: full ll-lang module parser (DONE)

[`spec/examples/valid/15-moduleparser-real.lll`](../../spec/examples/valid/15-moduleparser-real.lll)
(506 lines) is the **showcase milestone** for Phase 7: a single
runnable ll-lang program that stitches the lexer (09), type-decl
parser (12), fn-decl parser (13), and full-expression parser (14)
into one recursive-descent module-level front end. After this lands,
"ll-lang has a full front-end in itself" stops being a story spread
across four separate corpus slices and becomes one file that consumes
a whole `module M\n type ...\n fn ... = ...` source end-to-end and
emits a `List[Decl]` AST.

The parser handles the module-level form
`module M.N\n type T = ...\n type U = ...\n fn f(p T) R = expr\n ...`
for the driver input

```
module Examples.Toy
type Maybe A = Some A | None
type Color = Red | Green | Blue
fn double(x Int) Int = x * 2
fn pickColor(x Int) Color = if x then Red else Green
fn answer() Int = 42
```

and prints the reconstructed module deterministically via `lllc run`:

```
module Examples.Toy
type Maybe (A) = Some(A) | None
type Color = Red | Green | Blue
fn double (x: Int) -> Int = (x * 2)
fn pickColor (x: Int) -> Color = (if x then Red else Green)
fn answer () -> Int = 42
```

Six pretty-printed decls in total: the module header with
dot-separated TypeId segments, two `type` decls (one parametric
`Maybe A = Some A | None`, one nullary enum `Color = Red | Green |
Blue`), and three `fn` decls demonstrating three body shapes — int
literal (`42`), arithmetic (`x * 2`), and `if-then-else` with ctor
references as expression leaves (`if x then Red else Green`).

The parser unifies every prior self-hosting slice:

- **Token set** — a single 20-constructor `Token` ADT covering every
  token any of 09/12/13/14 needed, plus `TKwModule` and `TDot` for the
  module header.
- **Lexer** — same shape as 09, with `\n` emitted as `TNewline` (so
  the module loop can see decl boundaries) and keyword classification
  extended to the Phase 7.4 keyword set (`module`/`type`/`fn`/`if`/
  `then`/`else`).
- **Type-decl parser** — lifted wholesale from 12, same
  `parseTypeDecl`/`parseCtors`/`parseTypeArgs` helpers.
- **Fn-decl parser** — lifted from 13, same `parseFnDecl`/
  `parseParamGroups`/`parseReturnType`.
- **Expression parser** — a **constrained subset** of 14: int lit, var
  ref (`TLower` or `TUpper` for ctor refs), parens, `+ - * /`,
  `if-then-else`, curried application. No `let-in`, no `match`, no
  lambdas — those stay in 14 and will come back in Phase 7.5's
  extended body grammar. Dropping them kept the file under 700 lines
  and focused on the module-level structure.
- **Top-level driver** — `parseModule` reads the header, then
  `parseDecls` dispatches on the first token of each line (`TKwType`/
  `TKwFn`), consing onto a `List[Decl]` via `DType`/`DFn` wrappers
  around the reused type-decl / fn-decl ASTs. `Module` is modeled as
  `MkModule Str List[Decl]` — the name is a pre-joined `Str`.

**Explicit out-of-scope** (becomes Phase 7.5's feature backlog):

1. **`let` decls at module level** — only `type` and `fn` currently.
   The Phase 7.5 top-level dispatcher needs a `TKwLet :: _ ->` arm.
2. **`tag Name`, `unit`, `trait`/`impl`, `import`/`export`** — all
   the other module-level forms the F# `Parser.fs` accepts are missing.
3. **Multi-line fn bodies with indented `let-in` chains** — 15 only
   handles single-line bodies. Real corpus files (01-basics.lll,
   etc.) routinely declare multi-line bodies; a Phase 7.5 version
   needs layout-sensitive body parsing that either tracks indentation
   or requires an explicit `end` delimiter.
4. **`match` / lambda / `let-in` in fn bodies** — dropped for line
   budget. 14-exprparser-real.lll already implements each of these;
   re-merging them into the module-level parser is mostly a copy-
   paste job plus harmonising the `isAtomStart` token set and adding
   `TKwMatch`/`TKwWith`/`TBackslash`/`TDot`/`TArrow`/`TUnder` back to
   the unified Token ADT.
5. **Cons / tuple / list patterns in `match`** — same story. The
   surface forms exist in the compiler; 15 doesn't parse them.
6. **Parametric type application in ctor args** — e.g., `Maybe[A]`
   inside `Some (Maybe A)` doesn't round-trip. The `parseTypeArgs`
   helper only accepts bare `Upper` tokens.
7. **Multi-line type declarations** — the multi-line `type T =\n |
   A\n | B` form the compiler already supports (see `10-multiline-
   sum.lll`) is not handled by 15's single-line-only parser.
8. **Tagged literals (`"x"[UserId]`)** — not recognised by the lexer
   and not part of the expression grammar.
9. **String literals in bodies** — dropped to avoid the
   `takeStrBody` helper overhead; would be trivial to add back from
   14-exprparser-real.lll.

Test count: 381 → 384 (corpus theory + 2 new ModuleParserTests).

Next slice: **Phase 7.5 — extended module parser**. Extend 15 (or
fork it into 16-moduleparser-full-real.lll) to cover the Phase 7.4
out-of-scope list: `let` decls, `tag`/`trait`/`impl`/`import`,
multi-line bodies with proper layout, and a full-expression body
grammar including `let-in` / `match` / lambdas / cons+list patterns.
After that the bootstrap compiler's front end is expressible in
ll-lang and the self-hosting translation work (Stage D in the plan
above — elaborator and codegen in ll-lang) can begin.

### 2026-04 — Phase 7.5a: let decls + match in fn body (DONE)

First sub-tick of Phase 7.5. Extends
[`spec/examples/valid/15-moduleparser-real.lll`](../../spec/examples/valid/15-moduleparser-real.lll)
**in place** (no sibling file) with the two highest-leverage items
from the Phase 7.4 out-of-scope list:

1. **`let name = expr` decls at module level.** `Decl` gains a `DLet
   LetDecl` arm where `LetDecl = MkLet Str Expr`. `parseDecls`
   dispatches on `TKwLet :: _` and calls the new `parseLetDecl`
   helper, which consumes `TKwLet TLower TEq` and then reuses the
   full `parseExpr` driver for the right-hand side (so any expression
   form the fn-body grammar accepts is also valid on the RHS of a
   `let`).
2. **`match scrut with | pat -> body | pat -> body ...` in fn bodies.**
   Lifts the match machinery wholesale from
   14-exprparser-real.lll: `parseMatch` uses `parseExpr` for the
   scrutinee (which stops at `TKwWith` because `with` isn't an
   atom-starter), then `parseArms` pulls two parallel lists (pats,
   bodies) out of the arm loop. `EMatch Expr List[Pat] List[Expr]`
   uses the same parallel-list trick 14 relies on to dodge
   mutually-recursive `type MatchArm = MkArm Pat Expr` which the
   current codegen cannot emit. A fresh `Pat = PInt Int | PVar Str |
   PWild` sum covers the three arm-pattern shapes in this slice
   (constructor / cons / list / tuple / string patterns all stay in
   Phase 7.5+).

Token ADT additions: `TKwLet`, `TKwMatch`, `TKwWith`, `TArrow`,
`TUnder`. Lexer additions: `let`/`match`/`with` keywords in
`classifyIdent`, `_` → `TUnder`, and a 2-char lookahead `lexMinusOrArrow`
helper that splits `-` from `->` (same shape as the helper in
14-exprparser-real.lll).

Pretty printer: `showPat`/`showArm`/`showArms` emit `| pat -> body`
chunks joined by single spaces, `showExpr EMatch` wraps them as
`(match <scrut> with | p -> e | ...)`, `showLetDecl` emits `let <name>
= <e>`, and `showDecl` gains the `DLet` arm.

The driver now parses

```
module Examples.Bigger
type Maybe A = Some A | None
type Color = Red | Green | Blue
let answer = 42
let zero = 0
fn double(x Int) Int = x * 2
fn classify(x Int) Int = match x with | 0 -> 0 | _ -> 1
fn pickColor(x Int) Color = if x then Red else Green
```

and prints

```
module Examples.Bigger
type Maybe (A) = Some(A) | None
type Color = Red | Green | Blue
let answer = 42
let zero = 0
fn double (x: Int) -> Int = (x * 2)
fn classify (x: Int) -> Int = (match x with | 0 -> 0 | _ -> 1)
fn pickColor (x: Int) -> Color = (if x then Red else Green)
```

Eight pretty-printed lines — module header, two type decls, two
module-level `let` decls, three fn decls with arithmetic,
match-with-scrutinee, and `if-then-else` bodies.

File size: 506 → 666 lines. Test count unchanged at 384 (the two
existing ModuleParserTests absorbed the extended driver — the runtime
E2E test got an updated `expected` list, the inference round-trip
re-reads straight from disk).

Phase 7.5 backlog, remaining 7 of 9 items (unchanged — the other
seven still drive future work):

- Multi-line fn bodies with layout-sensitive parsing
- `let-in` chains / lambdas inside fn bodies (already in 14; re-merge
  into 15 with the token-level `|` disambiguation)
- Cons / list / tuple patterns in `match` arms
- Tagged literals (`"x"[UserId]`)
- Other module-level forms — `tag Name`, `trait ... with ...`, `impl
  Trait for Type = ...`, `import Foo.Bar`, `export ...`
- Multi-line type declarations (`type T =\n | A\n | B`)
- Parametric ctor args (`Some (Maybe A)`, `Some Maybe[A]`)

Next tick: **Phase 7.5b** — pick any two of the above remaining seven
and ship them the same way (in-place extension of 15, TDD for the
runtime pretty-print expectation, commit-split feat / test / docs).

### 2026-04 — Phase 7.5b: lambdas + let-in chains in fn bodies (DONE)

Second sub-tick of Phase 7.5. Extends
[`spec/examples/valid/15-moduleparser-real.lll`](../../spec/examples/valid/15-moduleparser-real.lll)
**in place** (no sibling file) with two more fn-body expression forms
from the Phase 7.5 backlog:

1. **`\x. body` lambdas.** Single-param lambda expression. Multi-param
   is expressed by nesting (`\x. \y. body`). The body is parsed at
   full `parseExpr` level so it can contain any expression form the
   grammar accepts, including further lambdas and let-in chains.
2. **`let name = e1 in e2` let-in chains.** Single-binder expression
   let-in. Chains are right-recursive — `let y = ... in let z = ... in
   body` parses as two nested `ELetIn` nodes. Both `e1` and `e2` go
   through the full `parseExpr`, so nested lambdas, match, if-then-else,
   and arithmetic all work.

Token ADT additions: `TKwIn`, `TBackslash`. Lexer additions: `"in"`
in `classifyIdent`, and `\` → `TBackslash` in `lexChars` (`.` → `TDot`
was already present from Phase 7.5a's match-arrow support). Note the
`\\` double-escape in the ll-lang source for `c == '\\'` — char
literals use the same escaping rules as strings.

Expression AST additions: `ELam Str Expr` and `ELetIn Str Expr Expr`.
Both dispatch from `parseExpr`:

```
fn parseExpr(toks List[Token]) =
  | TKwIf :: rest -> parseIf rest
  | TKwMatch :: rest -> parseMatch rest
  | TKwLet :: rest -> parseLetIn rest         -- Phase 7.5b
  | TBackslash :: rest -> parseLam rest       -- Phase 7.5b
  | _ -> parseAddSub toks
```

Important: `TKwLet` is now consumed by two independent dispatchers —
`parseDecls` peels it off at module level for `let name = expr` decls
(no `in`), and `parseExpr` peels it off at expression level for
`let name = e1 in e2` chains. Context disambiguates cleanly because
the two entry points are independent — `parseDecls` never calls
`parseExpr` with a leading `TKwLet`, and `parseExpr` never sees tokens
from a module-level decl.

Pretty printer: `showExpr` gains two new arms — `ELam` prints as
`(fun <n> -> <body>)` (F#-ish shape, same as 14-exprparser-real.lll)
and `ELetIn` prints as `(let <n> = <e1> in <e2>)`.

The driver now parses two new fn decls on top of the Phase 7.5a
baseline:

```
fn shift(x Int) Int = let y = x + 1 in y * 2
fn applyDouble(x Int) Int = (\y. y * 2) x
```

and prints

```
fn shift (x: Int) -> Int = (let y = (x + 1) in (y * 2))
fn applyDouble (x: Int) -> Int = ((fun y -> (y * 2)) x)
```

Ten pretty-printed lines total now — module header, two type decls,
two module-level `let` decls, five fn decls covering arithmetic,
match-with-scrutinee, `if-then-else`, let-in chains, and lambda
application bodies.

File size: 666 → 721 lines. Test count unchanged at 384 (the two
existing ModuleParserTests absorbed the extended driver — the runtime
E2E test got an updated `expected` list, the inference round-trip
re-reads straight from disk).

Phase 7.5 progress: 4 of 9 items done (module-level let decls + match
in fn body from 7.5a, lambdas + let-in chains from 7.5b). Phase 7.5
backlog, remaining 5 of 9 items:

- Multi-line fn bodies with layout-sensitive parsing
- Cons / list / tuple patterns in `match` arms
- Tagged literals (`"x"[UserId]`)
- Other module-level forms — `tag Name`, `trait ... with ...`, `impl
  Trait for Type = ...`, `import Foo.Bar`, `export ...`
- Multi-line type declarations (`type T =\n | A\n | B`)
- Parametric ctor args (`Some (Maybe A)`, `Some Maybe[A]`)
- String literals in bodies

Next tick: **Phase 7.5c** — pick another two or three items from the
remaining five and ship them the same way.

### 2026-04 — Phase 7.5c: string literals + cons patterns in match arms (DONE)

Third sub-tick of Phase 7.5. Extends
[`spec/examples/valid/15-moduleparser-real.lll`](../../spec/examples/valid/15-moduleparser-real.lll)
**in place** (no sibling file) with two more items from the Phase 7.5
backlog:

1. **String literals in fn bodies.** `parseAtom` gains a
   `TStr s :: rest -> (EStr s, rest)` arm; the lexer recognises
   `"..."` via the `takeStrBody` helper lifted from
   14-exprparser-real.lll. The pretty printer renders `EStr s` as
   `"<s>"` with surrounding quotes.
2. **`[]` and `h :: t` cons patterns in match arms.** `parsePat` is
   now recursive: a new `parsePrimaryPat` handles the existing leaf
   patterns plus `[]` (`PNil`); the wrapper `parsePat` peeks for
   `TColonColon` and folds the result into a right-associative
   `PCons` so chains like `a :: b :: rest` parse as `PCons a (PCons b
   (PVar "rest"))`. The pretty printer renders `PNil` as `[]` and
   `PCons h t` as `(h :: t)`.

Token ADT additions: `TStr Str`, `TLBrack`, `TRBrack`, `TColonColon`.
Lexer additions: `"` triggers `lexStr` (which calls `takeStrBody` on
the post-quote tail), `[` and `]` map directly to `TLBrack` /
`TRBrack`, and `:` peeks the next char via `lexColonOrCons` to emit
`TColonColon` only when it sees `::` (a bare `:` falls through
silently — the grammar has no use for it).

Pattern AST additions: `PNil` and `PCons Pat Pat`. Expression AST
addition: `EStr Str`. The recursive `parsePat`:

```
fn parsePrimaryPat(toks List[Token]) =
  | TInt n :: rest -> (PInt n, rest)
  | TUnder :: rest -> (PWild, rest)
  | TLBrack :: TRBrack :: rest -> (PNil, rest)
  | TLower s :: rest -> (PVar s, rest)
  | _ -> (PWild, toks)

fn parsePat(toks List[Token]) =
  let (p, rest) = parsePrimaryPat toks in
  match rest with
    | TColonColon :: rest2 ->
      let (tail, rest3) = parsePat rest2 in
      (PCons p tail, rest3)
    | _ -> (p, rest)
```

The driver now parses two new fn decls on top of the Phase 7.5b
baseline:

```
fn greet() Str = "hello"
fn classifyXs(xs Int) Int = match xs with | [] -> 0 | h :: t -> 1
```

and prints

```
fn greet () -> Str = "hello"
fn classifyXs (xs: Int) -> Int = (match xs with | [] -> 0 | (h :: t) -> 1)
```

Twelve pretty-printed lines total now — module header, two type decls,
two module-level `let` decls, seven fn decls covering arithmetic,
match-with-scrutinee, `if-then-else`, let-in chains, lambda
application, string literal, and cons-pattern bodies. Note that the
`xs` parameter in `classifyXs` is annotated as `Int` rather than
`List Int` only because the test checks pretty-print output, not
semantic types — the parser doesn't care.

File size: 721 → 793 lines. Test count unchanged at 384 (the two
existing ModuleParserTests absorbed the extended driver — the runtime
E2E test got an updated `expected` list with two more lines, the
inference round-trip re-reads straight from disk).

Phase 7.5 progress: 6 of 9 items done (module-level let decls + match
in fn body from 7.5a, lambdas + let-in chains from 7.5b, string
literals + cons patterns from 7.5c). Phase 7.5 backlog, remaining 3 of
9 items, grouped by theme:

- **Multi-line surface forms**: multi-line fn bodies with
  layout-sensitive parsing, multi-line type declarations
  (`type T =\n | A\n | B`), and parametric ctor args
  (`Some (Maybe A)`, `Some Maybe[A]`).
- **Tagged literals**: `"x"[UserId]` post-atom lookahead.
- **Other module-level forms**: `tag Name`, `trait ... with ...`,
  `impl Trait for Type = ...`, `import Foo.Bar`, `export ...`. List-
  literal `[a b c]` and tuple `(a, b)` patterns are deferred under the
  same umbrella since the cons-only subset already shipped in 7.5c.

Next tick: **Phase 7.5d** — pick another two or three items from the
remaining backlog and ship them the same way.

### 2026-04 — Phase 7.5d: tagged literals + parametric ctor args (DONE)

Fourth sub-tick of Phase 7.5. Extends
[`spec/examples/valid/15-moduleparser-real.lll`](../../spec/examples/valid/15-moduleparser-real.lll)
**in place** (no sibling file) with two more items from the Phase 7.5
backlog:

1. **Tagged literal expressions.** `parseAtom` gains two new 4-token
   cons-pattern arms that run BEFORE the plain 1-token `EInt` / `EStr`
   arms:
   ```
   | TInt n :: TLBrack :: TUpper ty :: TRBrack :: rest -> (ETagged (EInt n) ty, rest)
   | TStr s :: TLBrack :: TUpper ty :: TRBrack :: rest -> (ETagged (EStr s) ty, rest)
   ```
   Dispatching inline in parseAtom (instead of a separate
   `maybeTagged` post-pass) sidesteps a lingering issue with multi-arg
   helper fns in the host parser's clause form. Mirrors the host
   compiler's Phase 7.2.2 rule: only `EInt` and `EStr` literal atoms
   can take a `[Tag]` suffix — other atom shapes (var refs, parens)
   never try to match the `[Tag]` tail so a stray bracket stays on the
   token stream and would later error out. The pretty printer renders
   `ETagged e t` as `(<show e>[<t>])`.
2. **Parametric ctor args in type decls.** `parseTypeArgs` now
   delegates to a new `parseOneTypeArg` helper, which after matching a
   `TUpper` head calls `parseBrackArgs` to collect zero or more
   `[typeArg]` bracket groups. Multiple groups (`Result[A][E]`) flatten
   into one `TAApp head args` ctor with a multi-element arg list;
   nested bracket-form args (`Foo[Bar[Baz]]`) recurse via
   `parseOneTypeArg` on the inner slot. A bare `TUpper` with no
   trailing bracket group stays as `TAVar` / `TACon` exactly like
   earlier slices. Picked the bracket form `Maybe[Int]` over the
   juxtaposition form `(Maybe Int)` because bracket delimiters need
   no lookahead.

AST additions: `Expr` gains `ETagged Expr Str`; `TypeArg` gains
`TAApp Str List[TypeArg]`. The pretty printer grows a `showBrackArgs`
helper that folds a `List[TypeArg]` into `[a1][a2]...`, used from the
new `TAApp head args -> strConcat head (showBrackArgs args)` arm in
`showTypeArg`.

Lexer: no changes. `TLBrack` / `TRBrack` / `TUpper` were already lexed
in Phase 7.5c (for `[]` / `h :: t` patterns). Bracket-form type
applications and tagged literals reuse the same three tokens — the
parser's dispatch context disambiguates.

The driver now parses two new decls on top of the Phase 7.5c baseline:

```
type Container = MkBox Maybe[Int]
let uid = "user-42"[UserId]
```

and prints

```
type Container = MkBox(Maybe[Int])
let uid = ("user-42"[UserId])
```

Fourteen pretty-printed lines total now — module header, three type
decls, three module-level `let` decls (one carrying a tagged string
literal), seven fn decls. The `UserId` tag doesn't need to be declared
anywhere in the module: the in-ll-lang parser does no name resolution,
it just builds an AST and pretty-prints it. The parametric ctor arg
lands inside `MkBox(...)` because the outer `showArgs` wraps the
ctor-arg list in `(...)` regardless of which `TypeArg` ctor produced
the rendered string.

File size: 793 → 866 lines. Test count unchanged at 384 (the two
existing ModuleParserTests absorbed the extended driver — the runtime
E2E test got an updated `expected` list with two more lines, the
inference round-trip re-reads straight from disk).

Phase 7.5 progress: 8 of 9 items done (module-level let decls + match
in fn body from 7.5a, lambdas + let-in chains from 7.5b, string
literals + cons patterns from 7.5c, tagged literals + parametric ctor
args from 7.5d). Phase 7.5 backlog, remaining 1 of 9 items, carrying
the rest of the multi-line-and-module-forms umbrella as a single
follow-up tick:

- **Multi-line surface forms + module-level forms**: multi-line fn
  bodies with layout-sensitive parsing, multi-line type declarations
  (`type T =\n | A\n | B`), and the remaining module-level decls
  `tag Name`, `trait ... with ...`, `impl Trait for Type = ...`,
  `import Foo.Bar`, `export ...`. List-literal `[a b c]` expression
  atoms and tuple `(a, b)` patterns tag along under the same umbrella
  since the cons-only subset already shipped in 7.5c.

Next tick: **Phase 7.5e** — close out Phase 7.5 by shipping whatever
the remaining backlog item resolves to (likely multi-line fn bodies +
the cluster of `tag` / `trait` / `impl` / `import` / `export` module-
level decls in one sweep).

### 2026-04 — Phase 7.5e: tag + import + export decls (DONE)

Fifth and final sub-tick of Phase 7.5. Extends
[`spec/examples/valid/15-moduleparser-real.lll`](../../spec/examples/valid/15-moduleparser-real.lll)
**in place** (no sibling file) with three module-level decl forms, the
lightweight subset of the Phase 7.5 umbrella's remaining "other
module-level forms" item:

1. **`tag Name` decls.** Bare semantic tag declarations — no body, no
   params, just the keyword and a single `TUpper`. Mirrors the host
   compiler's `KwTag` arm in `parseDecl`. Added `TKwTag` to the
   `Token` union, a `"tag" -> TKwTag` arm to `classifyIdent`, a
   `DTag Str` variant to `Decl`, and a new `parseTagDecl` helper
   matching `TKwTag :: TUpper name :: rest -> (DTag name, rest)`.
2. **`import Foo.Bar.Baz` decls.** Dotted module imports. Added
   `TKwImport` to the token union, `"import" -> TKwImport` to
   `classifyIdent`, `DImport Str` to `Decl`, and a new
   `parseImportDecl` helper that reuses `parseModuleNameTail` — the
   exact same dotted-path walker `parseModuleHeader` already uses —
   so the qualified path string is built identically on both sides
   of the module header / import boundary.
3. **`export` modifier prefix.** A keyword that turns any existing
   decl into a publicly-visible `DExport Decl` wrapper. `DExport`
   is a self-recursive variant on `Decl` — same shape as the
   existing `EAdd Expr Expr` etc. on `Expr` — chosen over an
   `exported: Bool` field because wrapping doesn't touch any
   existing variant shape. Added `TKwExport` to the token union,
   `"export" -> TKwExport` to `classifyIdent`, `DExport Decl` to
   `Decl`, and a `TKwExport :: rest -> DExport inner :: ...` arm in
   `parseDecls` that delegates the inner decl parse to a new
   `parseOneDecl` helper and wraps the result.

`parseOneDecl` is a local helper that dispatches on the same keyword
ladder as `parseDecls` but returns exactly one `(Decl, leftover)` pair
instead of driving the full decl-list recursion. Lets the `export`
modifier compose with every decl shape without duplicating the
dispatch ladder.

Pretty printer: `showDecl` grows three new arms:

```
| DTag name -> strConcat "tag " name
| DImport path -> strConcat "import " path
| DExport inner -> strConcat "export " (showDecl inner)
```

The `DExport` arm recurses back into `showDecl` so the inner decl's
canonical pretty form stays untouched and the `export ` modifier
stacks in front of any shape.

The driver now parses five new decls on top of the Phase 7.5d
baseline:

```
import Std.List
import Std.Maybe
tag UserId
tag Email
export fn addOne(x Int) Int = x + 1
```

and prints

```
import Std.List
import Std.Maybe
tag UserId
tag Email
export fn addOne (x: Int) -> Int = (x + 1)
```

Nineteen pretty-printed lines total now — module header, two imports,
two tags, three type decls, three module-level `let` decls, one
exported fn decl, and seven regular fn decls.

File size: 866 → 979 lines (+113). Test count unchanged at 386 (the
two existing ModuleParserTests absorbed the extended driver — the
runtime E2E test got an updated `expected` list with five more lines,
the inference round-trip re-reads straight from disk).

Phase 7.5 progress: 9 of 9 items done. The Phase 7.5 umbrella is
closed — the `trait` / `impl` module-level decls, multi-line fn
bodies, multi-line type decls, and list-literal / tuple patterns all
deliberately move to a separate **Phase 7.6** slice, because each of
those is much more complex than a flat single-line dispatch arm
(multi-line signatures, indented fn bodies, layout-sensitive parsing).

Next tick: **Phase 7.6** — pick between multi-line fn bodies + type
decls, `trait` / `impl` module-level decls, list-literal `[a b c]`
expression atoms / tuple `(a, b)` patterns, or bootstrapping the
elaborator-in-ll-lang slice (Stage D in the roadmap).

### 2026-04 — Phase 7.6a: elaborator slice A — name resolution + E002 (DONE)

First tick of Phase 7.6 and the very first bite of the
**elaborator-in-ll-lang** half of the self-hosting roadmap. The
front-end half (Phases 7.1..7.5e) now produces a `List[Decl]` AST
inside [`15-moduleparser-real.lll`](../../spec/examples/valid/15-moduleparser-real.lll);
Phase 7.6 starts walking that AST with the same semantic passes the
F# host elaborator runs in
[`src/LLLangCompiler/Elaborator.fs`](../../src/LLLangCompiler/Elaborator.fs).

This slice targets the **simplest and most obvious** elaborator
function — **name resolution / free-variable check**. For each `fn`
body, walk the expression and report `E002 UnboundVar <name>` for
every `EVar name` whose name isn't in the collected top-level env
and isn't bound by a local `let` / lambda / fn param / pattern
binder.

New file: [`16-elaborator-real.lll`](../../spec/examples/valid/16-elaborator-real.lll)
(328 lines). Pipeline mirrors `Elaborator.fs`:

```
collectDecls env0 decls   -- pass 1: gather every top-level name
checkDecls   env  decls   -- pass 2: walk each fn body under that env
```

Uses a **minimal local AST** (not reusing `15-moduleparser-real.lll`'s
full `Decl` shape) so the slice stays self-contained and the
hardcoded test-module literal stays compact:

```lll
type Pat  = PInt Int | PVar Str | PWild | PNil | PCons Pat Pat
type Expr = EInt Int | EStr Str | EVar Str | EAdd Expr Expr | EApp Expr Expr
          | ELet Str Expr Expr | ELam Str Expr | EIf Expr Expr Expr
          | EMatch Expr List[Pat] List[Expr]
type Decl = DFn Str List[Str] Expr | DLet Str Expr
          | DType Str List[Str]   | DTag Str
type Env  = MkEnv List[Str]
```

`EMatch` stores match arms as two parallel lists (same trick as
`15-moduleparser-real.lll`) to sidestep the codegen limitation on
mutually-recursive user type decls.

The hardcoded test module has three fns, one tag, one type, and one
let:

```
tag UserId
type Maybe = Some | None
let answer = 42
fn add(a b) = a + b
fn bad(x) = undefinedName + otherMissing
fn useCtor(y) = match y with | 0 -> Some | _ -> None
```

`bad` intentionally references two undeclared names; the elaborator
emits one `E002` per reference. The other two fns are clean: fn
params `a` / `b` / `x` / `y` are locals; `Some` / `None` are ctors
collected from the `DType`; `answer` is collected from the `DLet`
but not used (the slice doesn't warn on unused bindings).

Running the file prints:

```
E002 UnboundVar undefinedName
E002 UnboundVar otherMissing
```

**Deliberately out of scope** (feature backlog for 7.6b/c/+):

1. **E001 type checking** — no TypeEnv, no `typeOf`, no type
   annotations at all. Name resolution only. Phase 7.6b.
2. **E003 exhaustiveness** — no match-arm completeness check. Phase
   7.6c.
3. **E004 / E005 unit / tag checks** — no unit algebra, no tag
   propagation. Phase 7.6+.
4. **Source positions** — errors emitted as `E002 UnboundVar <name>`
   (no `line:col` prefix). Position tracking on AST nodes is a
   separate Phase 7.6+ item.
5. **Builtin env** — the test input only references names the user
   declares locally, so the env starts empty. Wiring into the real
   `builtinEnv` lives in a later integration slice.
6. **Parser integration** — the input module AST is hardcoded in
   `main`, not read from a `.lll` file and threaded through the
   parser. Phase 7.6 integration tick, not this slice.

Tests: 386 -> 389.
  * `tests/LLLangTests/ElaboratorRealTests.fs` — two facts:
    inference round-trip + runtime E2E asserting both E002 lines on
    stdout.
  * `tests/LLLangTests/HMInferTests.fs` — `16-elaborator-real.lll`
    added to the `valid corpus infers ok` theory.

Next tick: **Phase 7.6b** — extend the elaborator-in-ll-lang with
**E003 NonExhaustiveMatch** constructor-coverage detection (keeping
E001 as a separate Phase 7.6c tick). Add a second collect pass
building a `TypeName → List[CtorName]` index, walk each `DFn` whose
body is a top-level clause-sugar `EMatch` whose last-param type is
in the index, and emit one `E003 NonExhaustiveMatch <type> missing
<ctor>` per missing ctor — or skip if the fn's arms carry a catch-
all `PWild` / `PVar` / `PCons`. Mirror the scope limitation the F#
host elaborator uses at `Elaborator.fs` line 503 area: nested and
value-position matches are out of scope, same as the host.

### 2026-04 — Phase 7.6b: elaborator slice B — E003 exhaustiveness (DONE)

Second tick of Phase 7.6. Extends [`16-elaborator-real.lll`](../../spec/examples/valid/16-elaborator-real.lll)
**in place** (328 → 512 lines, +184) with **constructor-coverage
exhaustiveness detection** over top-level clause-sugar fn bodies.
Mirrors the F# host elaborator's `exhaustivenessCheck` in
[`src/LLLangCompiler/Elaborator.fs`](../../src/LLLangCompiler/Elaborator.fs)
(lines 481-548), including its deliberately-narrow scope: only the
*direct* top-level `EMatch` of a `DFn` whose last curried param is a
named sum type is checked.

AST changes:
- `type Pat` gains `PCon Str` (0-arity ctor pattern; argument patterns
  are modelled in a later slice).
- `type Decl` updates `DFn` from `DFn Str List[Str] Expr` to `DFn Str
  List[Str] Str Expr`, carrying the **last curried param's type name**
  (empty string `""` = no declared type, skips the check). All
  existing `DFn` call sites in the hardcoded test module are updated
  accordingly.

New helpers added alongside `collectDecls` / `checkDecls`:

```lll
type TypeCtors = MkTypeCtors Str List[Str]

fn collectTypes(decls) -> List[TypeCtors]      -- walks DType bodies
fn lookupCtors(types)(name) -> List[Str]       -- linear scan by type name
fn patIsCatchAll(p) -> Bool                    -- PWild / PVar / PCons
fn armsHaveCatchAll(pats) -> Bool              -- short-circuit over a list
fn coveredCtors(pats) -> List[Str]             -- collects PCon names
fn missingCtorErrs(ty)(required)(covered)      -- diff → List[Str]
fn exhaustivenessDecl(types)(d) -> List[Str]   -- per-decl check
fn exhaustivenessCheck(types)(decls)           -- top-level entry
```

The top-level `elaborate` now runs **both** passes (name-resolution
AND exhaustiveness) and concatenates their error lists — same
two-pass shape the F# host elaborator uses.

Hardcoded test module extended with:

```
type Shape = Circle | Rect | Empty
fn shapeGood(s Shape) = | Circle -> 1 | Rect -> 2 | Empty -> 3  -- clean
fn shapeBad(s Shape)  = | Circle -> 1 | Rect -> 2               -- E003
fn shapeWild(s Shape) = | Circle -> 1 | _ -> 0                  -- catch-all
```

Running the file now prints:

```
E002 UnboundVar undefinedName
E002 UnboundVar otherMissing
E003 NonExhaustiveMatch Shape missing Empty
```

**Deliberately out of scope** (feature backlog for 7.6c/+):

1. **E001 type checking** — still Phase 7.6c. Adding full declared-
   type checking requires a `TypeExpr` sum and a `typeOf` walker.
2. **E004 / E005 unit / tag checks** — Phase 7.6+.
3. **Nested / value-position matches** — skipped to avoid cascading
   false positives. Real support needs H-M inference to know nested
   scrutinee types.
4. **Ctor arg patterns** — `PCon Str List[Pat]` instead of `PCon Str`.
   Binders would recurse into arg pats. Not needed for 0-arity ctor
   coverage.
5. **Source positions** — errors still emitted without `line:col`.
6. **Builtin env + parser integration** — still hardcoded.

Tests: 389 (no count change — the existing `ElaboratorRealTests.fs`
runtime E2E fact was updated in place to assert the new E003 line
alongside the two E002 lines). The inference round-trip fact is
unchanged because the extended file still parses + elaborates + HM-
infers cleanly through the F# host pipeline.

Next tick: **Phase 7.6c** — extend the elaborator-in-ll-lang with
**E001 type mismatch detection** OR jump straight to full H-M
inference. Add a minimal `TypeExpr` sum, thread declared types
through fn param collection via pair-shaped param lists, add a
`typeOf` walker, and flag every `EApp` whose argument's inferred
type doesn't match the function's parameter type. Keep scope
minimal — no inference, just checking against explicit annotations.

### 2026-04 — Phase 7.6 integration: parser + elaborator pipeline (DONE)

Third tick of Phase 7.6. **Showcase milestone** — the first time two
compiler layers authored in ll-lang share a single AST and run
back-to-back on a real source string. New file:
[`17-pipeline-real.lll`](../../spec/examples/valid/17-pipeline-real.lll)
(~1500 lines). Copies the full module parser from
`15-moduleparser-real.lll` verbatim (lexer + recursive-descent
parser + `List[Decl]` AST) and grafts the elaborator passes from
`16-elaborator-real.lll` on top — adapted to walk 15's richer
`Decl` / `Expr` / `Pat` / `Param` / `FnDecl` / `LetDecl` / `TypeDecl`
shapes instead of 16's minimal local AST.

The `main` driver hardcodes a small ll-lang source, runs
`tokenize` -> `parseModule` -> `elaborate`, and prints the resulting
error list:

```
module M
type Shape = Circle | Rect
fn good(x Int) Int = x + 1
fn bad(x Int) Int = undefinedName
fn shapeBad(s Shape) Int = match s with | 0 -> 1
```

produces:

```
E002 UnboundVar undefinedName
E003 NonExhaustiveMatch Shape missing Circle
E003 NonExhaustiveMatch Shape missing Rect
```

**Adaptation notes**:

* `checkExpr` handles every 15 `Expr` variant: `EInt` / `EStr` /
  `EVar` (the only leaf that resolves names) / `ETagged` / all four
  arithmetic arms / `EApp` / `ELam` / `ELetIn` / `EIf` / `EMatch`.
* `collectDecl` / `checkDecl` / `exhaustivenessDecl` unwrap 15's
  `DType TypeDecl` / `DFn FnDecl` / `DLet LetDecl` / `DTag Str` /
  `DImport Str` / `DExport Decl` variants. `DExport inner` recurses
  into the inner decl so an exported fn still registers its top-
  level name AND gets its body walked.
* `DImport` contributes nothing to the env in this slice — real
  module resolution is a much later phase. `DLet`'s RHS is walked
  now (16 skipped it).
* Small wrapper functions (`fnName`, `letName`, `typeDeclCtors`,
  `paramName`, `paramNames`, `typeRefName`, `paramTypeName`,
  `checkFnBody`, `checkLetBody`, `exhaustivenessFn`) destructure
  each inner type one level at a time. This works around the host
  codegen's current limitation around nested constructor patterns
  (`PCon(c, [p])` emits `ctor p` without wrapping the inner
  pattern in parens, which F# rejects when `p` is itself a
  multi-arg ctor pattern like `MkFn n ps ty b`). Avoiding nested
  patterns via thin helpers is cleaner than touching `Codegen.fs`.
* 15's `Pat` has no `PCon` (named constructor patterns are a later
  slice), so `coveredCtors` is the constant empty list — a match
  over a declared sum type with no catch-all arm reports every
  constructor as missing. Deliberately narrow: the integration's
  goal is proving the pipeline plumbing, not full coverage analysis.

**Deliberately out of scope** (same as 7.6b, plus):

1. **Realistic corpus of test inputs** — only one hardcoded source
   in `main`. A broader integration would scan multiple `.lll`
   files through the same pipeline.
2. **Error formatting with source positions** — still bare `E002` /
   `E003` codes without `line:col` prefixes.
3. **Reading a file at runtime** — source is still a string literal
   in `main`. File-reading builtins (`fileRead`, etc.) would let
   the pipeline read real `.lll` files, but aren't wired yet.

Tests: 392 (+3 vs 389). New `PipelineRealTests.fs` adds an
inference round-trip fact and a runtime E2E fact; a new row in
`HMInferTests.fs`'s `valid corpus infers ok` theory brings the
inference smoke coverage to three self-host files (15 / 16 / 17).

Next tick: **Phase 7.6c** — E001 type mismatch in ll-lang, OR
jump ahead to **Phase 7.7** — start porting H-M inference into
ll-lang itself.

### 2026-04 — Phase 7.7a: HM inference slice A — TypeExpr + Subst + unify (DONE)

First tick of **Phase 7.7** — the H-M middle of the compiler, written
in ll-lang itself. After Phase 7.6 closed out the front-end half
(lex / parse / elaborate, all in ll-lang), this slice starts the
type-inference half. New file:
[`18-hminfer-real.lll`](../../spec/examples/valid/18-hminfer-real.lll)
(287 lines). Defines a minimal `TypeExpr` AST, a parallel-list `Subst`,
an `applyType` walker, and a `unify : TypeExpr -> TypeExpr -> Maybe[Subst]`
mirroring the F# host's `HMInfer.unify` in
`src/LLLangCompiler/HMInfer.fs` line 63 area.

**Shape (deliberately tight)**:

```lll
type TypeExpr =
  | TyName Str           -- concrete: "Int", "Str", "Bool"
  | TyVar Str            -- type variable: "a", "b", "$0"
  | TyFn TypeExpr TypeExpr  -- arrow type

type Subst = MkSubst List[Str] List[TypeExpr]

fn unify(t1 TypeExpr)(t2 TypeExpr) Maybe[Subst]
```

`unify`'s arms mirror the F# host arm-by-arm:

* `TyName a` vs `TyName b`     — `Some empty` if equal, else `None`
* `TyVar a`  vs `TyVar b`      — `Some empty` if same name, else bind
                                 `a := TyVar b`
* `TyVar a`  vs t (non-var)    — `Some (a := t)`
* t (non-var) vs `TyVar b`     — symmetric: `Some (b := t)`
* `TyFn a1 r1` vs `TyFn a2 r2` — recurse: unify args, apply subst to
                                 results, unify results, return head
                                 subst (no compose — see scope below)
* anything else                — `None`

The `main` driver runs five hardcoded `unify` cases and joins their
result lines into a single `printfn` (ll-lang's `fn main` body is one
expression — there's no statement sequence):

```
t1 unify Int Int ok
t2 unify Int Str mismatch
t3 unify a Int ok bound a
t4 unify (Int -> Str) (Int -> Str) ok
t5 unify (Int -> Bool) (Int -> Str) mismatch
```

**Implementation notes**:

* Parallel-list `Subst` (`List[Str]` keys + `List[TypeExpr]` vals)
  rather than `List[(Str, TypeExpr)]`. Same trick as
  `16-elaborator-real.lll`'s `EMatch` arms — sidesteps the codegen's
  current limitation around tuples-in-list patterns.
* Each `t1` arm of `unify` is split into its own helper
  (`unifyName` / `unifyVar` / `unifyFn` / `unifyResults`) to keep
  every `match` one level deep. Three-level nested matches in ll-lang
  have ambiguous arm bleed-over because indentation rules can't tell
  which match a shallower `|` belongs to — e.g.,
  `match t1 with | TyName a -> match t2 with | ... | _ -> None`
  parses the trailing `_` as a `t1` arm, not a `t2` arm.
* `emptySubst` takes a dummy `Int` parameter — zero-arg fns in
  ll-lang require a discard arg at the call site (same trick as
  `16-elaborator-real.lll`'s `testModule` at line 426).
* `applyType` does single-level lookup, not chain-following. Sufficient
  for this slice because `singletonSubst` only ever produces
  `var -> non-var` bindings, so a chain would never form.
* `unifyFn` returns the head subst directly without composing the
  result subst into it. Composition only matters once an `inferExpr`
  walker (Phase 7.7b) needs to thread substs across multi-binder
  expressions; the five test cases here never produce a non-empty
  arg subst that the result subst would need to be threaded through.

**Deliberately out of scope** (carved out for Phase 7.7b and later):

1. **`inferExpr`** — no algorithm-W loop. The `Expr` AST that 7.7b
   will walk is left as a doc-only stub in the file's header so the
   diff against the F# host stays trivial.
2. **`Env`** — no name-to-type mapping. `inferExpr`'s var lookup
   needs an `Env`; this slice has only `unify`, which is
   position-agnostic and env-agnostic.
3. **`FreshState` / fresh-var counter** — no algorithm-W means no
   fresh-var allocation. Phase 7.7b adds a counter (either via a
   threaded `Int` or a mutable `Ref`-like ADT).
4. **Occurs check** — `unify` happily binds `a := TyFn(a, b)` today.
   Phase 7.7b adds the check, mirroring the F# host's `e008
   OccursCheck` in `HMInfer.fs` line 52 area.
5. **`composeSubst`** — only matters for `inferExpr`. The current
   `unifyFn` returns the head subst directly. 7.7b adds compose.
6. **Let-generalization, polymorphism, type schemes** — much later.
7. **Trait dispatch (E006)** — even later.
8. **`TyApp` / `TyTagged`** — record types, parametric types, and
   tagged unit types are all out of scope. The F# host's `TypeExpr`
   has them; this slice does not.
9. **Source positions on errors** — `unify` returns `None`, not an
   `Err` carrying a code + position. Error codes / positions land in
   a later slice once `inferExpr` needs them.

Tests: 395 (+3 vs 392). New `HMInferRealTests.fs` adds an inference
round-trip fact and a runtime E2E fact (asserting all five `t<N>
unify ...` lines appear in stdout); a new row in `HMInferTests.fs`'s
`valid corpus infers ok` theory brings the inference smoke coverage
to four self-host files (15 / 16 / 17 / 18).

Next tick: **Phase 7.7b** — extend `18-hminfer-real.lll` with `Env`,
fresh-var counter, occurs check, `composeSubst`, the documented
`Expr` AST, and a toy `inferExpr` covering literal / var / EAdd /
EApp arms.

### 2026-04 — Phase 7.7b: HM inference slice B — Env + fresh vars + inferExpr (DONE)

Second tick of **Phase 7.7**. Extends
[`18-hminfer-real.lll`](../../spec/examples/valid/18-hminfer-real.lll)
in place from 287 to 490 lines (+203). Phase 7.7a shipped the `unify`
spine; this slice adds the algorithm-W loop shape so the file can
infer literal / variable / addition / application expression types
end-to-end.

**New shape (on top of Phase 7.7a)**:

```lll
type Env = MkEnv List[Str] List[TypeExpr]    -- parallel-list like Subst

type Expr =
  | EInt Int
  | EStr Str
  | EBool Bool
  | EVar Str
  | EAdd Expr Expr
  | EApp Expr Expr

type InferResult = MkInferResult TypeExpr Subst Int

fn freshVar(n Int) = (TyVar (strConcat "$" (intToStr n)), n + 1)

fn composeSubst(s1 Subst)(s2 Subst) Subst    -- dumb list-concat compose

fn inferExpr(env Env)(n Int)(e Expr) InferResult
```

`inferExpr`'s arms mirror (a tiny slice of) the F# host's
`HMInfer.inferExpr` in `src/LLLangCompiler/HMInfer.fs` line 172 area:

* `EInt _`  / `EStr _` / `EBool _` — literal types, empty subst, n unchanged
* `EVar name` — env lookup; miss returns `TyName "ERROR"` sentinel
  (this slice skips real `E002 UnboundVar` reporting — sentinel is
  enough for the deterministic test output)
* `EAdd l r` — infer both children, unify both with `TyName "Int"`,
  return `TyName "Int"` under the composed subst (ERROR on either
  unify failure)
* `EApp f a` — infer both children, allocate a fresh `β`, unify
  `applyType s_a τf` against `TyFn(τa, β)`, return `applyType s β`
  under the composed subst (ERROR on unify failure)

Each arm is split into its own helper (`inferVar` / `inferAdd` /
`inferAddR` / `inferAddUnify` / `inferAddUnify2` / `inferApp` /
`inferAppArg` / `inferAppFresh`) to keep `match` nesting one level
deep — same trick as Phase 7.7a's `unify` spine, for the same
indentation-ambiguity reason.

The `main` driver appends five hardcoded `inferExpr` cases to the
existing five `unify` cases:

```
t6 infer 42 : Int
t7 infer (1 + 2) : Int
t8 infer x in env : Int
t9 infer (double 5) in env : Int
t10 infer (double "x") in env : ERROR
```

t8 / t9 / t10 use a pre-populated env (`{x : Int}` and
`{double : Int -> Int}`). t10 intentionally exercises the ERROR
path — unifying `TyFn Int Int` against `TyFn Str β` fails at the
argument position.

**Implementation notes**:

* `composeSubst` is deliberately dumb — just `listAppend` the two
  parallel-list substs head-to-tail. No "apply s1 to s2's vals"
  step, which is safe only for monomorphic types (no chained
  bindings). Proper compose lands once polymorphism / let-
  generalization does in 7.7c.
* `unifyResults` changed: now threads the result-side subst back
  into the head subst via `composeSubst`, so bindings emitted while
  unifying `TyFn` result types survive. Phase 7.7a discarded them;
  they only mattered once `inferExpr` asked for `applyType s β` on
  the fresh var after an `EApp`. The five Phase 7.7a `unify` tests
  are unaffected because their result substs are all empty.
* `freshVar` is purely functional: takes `Int`, returns `(TyVar "$n",
  n + 1)` via a literal tuple. The F# host uses a mutable ref cell;
  threading the counter explicitly is verbose but matches the rest
  of this file's pure style.
* `InferResult` is a three-field ADT (`MkInferResult TypeExpr Subst
  Int`) so `inferExpr` can return type + subst + new fresh counter
  without tuple-in-tuple gymnastics that currently have codegen
  friction.
* Env is `MkEnv List[Str] List[TypeExpr]` — identical shape to
  `Subst`, and `envLookup` / `envLookupLists` / `envExtend` read
  identically to `lookupSubst` / `lookupSubstLists`. A future slice
  could factor them into a shared helper but for now duplication
  is cheaper than the abstraction.

**Deliberately still out of scope** (carved out for Phase 7.7c):

1. **Let-generalization, polymorphism, type schemes** — no
   `generalize` / `instantiate`, no type schemes. The stdlib's
   `List[A]` / `Maybe[A]` / `Result[A, E]` rely on all three.
2. **Occurs check (`e008`)** — `unify` still happily binds
   `a := TyFn(a, b)`. The five new inference tests don't trigger
   it; 7.7c adds the check.
3. **`ELam` / `ELet` / `EIf` / `EMatch` inference** — those need
   polymorphism + pattern-type checking, both blocked on 7.7c.
4. **Real error reporting** — `inferExpr` returns `TyName "ERROR"`
   sentinel instead of `Result[TypeExpr, LLError]`. The F# host's
   `E001..E008` machinery doesn't land in the ll-lang mirror until
   `Result`-threading is on the table.
5. **Applying s1 to s2 in compose** — the current compose is pure
   list-concat. Fine for mono types; broken for chains.
6. **Wiring into `17-pipeline-real.lll`** — the HM inference slice
   stays standalone for now. Phase 7.8+ integrates it alongside the
   parser + elaborator pipeline.

Tests: 395 (unchanged vs 7.7a). The runtime E2E fact in
`HMInferRealTests.fs` updated to assert all ten lines appear in
stdout; the inference round-trip fact still passes unchanged (the
new types and helpers all infer cleanly through the host compiler).

Next tick: **Phase 7.7c** — extend `inferExpr` with `ELam`, `ELet`
(mono, no let-generalization yet), and `EIf` so the HM middle covers
most fn body shapes. Let-generalization / polymorphism / occurs
check / `Result`-based errors still land in Phase 7.7d+.

### 2026-04 — Phase 7.7c: HM inference slice C — ELam + ELet + EIf (DONE)

Third tick of **Phase 7.7**. Extends
[`18-hminfer-real.lll`](../../spec/examples/valid/18-hminfer-real.lll)
in place from 490 to 616 lines (+126). Phase 7.7b shipped the
algorithm-W loop over `EInt` / `EStr` / `EBool` / `EVar` / `EAdd` /
`EApp`; this slice adds the three structural Expr variants so
`inferExpr` can walk most mono-typed fn bodies end-to-end.

**New shape (on top of Phase 7.7b)**:

```lll
type Expr =
  | EInt Int | EStr Str | EBool Bool | EVar Str
  | EAdd Expr Expr | EApp Expr Expr
  | ELam Str Expr              -- new
  | ELet Str Expr Expr         -- new
  | EIf Expr Expr Expr         -- new

fn applyEnv(s Subst)(e Env) Env         -- new, walks env types via applyType
fn applyTypeList(s Subst)(ts ...) ...    -- new, inner recursive listMap
```

`inferExpr`'s three new arms mirror (mono slices of) the F# host's
corresponding cases in `src/LLLangCompiler/HMInfer.fs` line 260 /
276 / 315 area:

* `ELam name body` — fresh `α` for the param, extend env with
  `(name : α)`, infer body → `(τbody, sBody)`, return
  `TyFn (applyType sBody α) τbody` under `sBody`.
* `ELet name e1 e2` — infer `e1` → `(τ1, s1)`, extend
  `applyEnv s1 env` with `(name : τ1)`, infer `e2` under that env
  → `(τ2, s2)`, return `τ2` under `compose s2 s1`. **No
  generalization** — `name` binds to the raw monomorphic `τ1`, so
  `let id = \x. x in (id 5, id "a")` polymorphism doesn't work yet.
  That lands in Phase 7.7d alongside type schemes.
* `EIf cond thn els` — infer `cond`, unify with `TyName "Bool"`,
  infer both branches under the updated env, unify `τt ~ τe`,
  return `applyType sAll τt` under the composed subst. ERROR
  sentinel on any unify failure (cond not `Bool`, or branches
  mismatch).

Each arm is split into its own helper chain (`inferLam`; `inferLet`
/ `inferLetBody`; `inferIf` / `inferIfCondBool` / `inferIfThen` /
`inferIfElse` / `inferIfUnify`) to keep `match` nesting one level
deep — same trick as Phase 7.7a/7.7b's `unify` and `inferExpr`
spines, for the same indentation-ambiguity reason.

The `main` driver appends five hardcoded cases to the ten existing
ones:

```
t11 infer (\x. x) : ($0 -> $0)
t12 infer (\x. x + 1) : (Int -> Int)
t13 infer (let x = 5 in x + 1) : Int
t14 infer (if true then 1 else 2) : Int
t15 infer (if true then 1 else "x") : ERROR
```

t11 proves lambda + fresh-var round-trip (`$0` comes from the first
`freshVar 0` call). t12 proves lambda body unifies against `EAdd`'s
`TyName "Int"` constraint. t13 proves the mono `ELet` threads the
RHS type through the body env. t14 proves `EIf` unifies both
branches against each other. t15 proves the branch-mismatch ERROR
path is deterministic.

**Deliberately still out of scope** (carved out for Phase 7.7d):

1. **Let-generalization, polymorphism, type schemes** — no
   `generalize` / `instantiate`; no `TypeScheme` in `Env`.
2. **Occurs check (`e008`)** — `unify` still happily binds
   `a := TyFn(a, b)`.
3. **`EMatch` inference** — needs pattern-type checking.
4. **Real error reporting** — `inferExpr` still returns `TyName
   "ERROR"` sentinel.
5. **Multi-param lambda** — `\x y. body` maps to nested `ELam`; the
   AST variant takes a single `Str`, not a list.
6. **Wiring into `17-pipeline-real.lll`** — still standalone.

Tests: 395 (unchanged vs 7.7b). The runtime E2E fact in
`HMInferRealTests.fs` updated to assert all fifteen lines appear in
stdout; the inference round-trip fact still passes unchanged (the
new `ELam` / `ELet` / `EIf` cases and their helpers all infer
cleanly through the host compiler).

Next tick: **Phase 7.7d** — add let-generalization, `Result`-based
error reporting, and the occurs check (`e008`). After 7.7d the HM
spine is feature-complete enough to host polymorphic stdlib
functions like `listMap` / `maybeMap` in later slices.

### 2026-04 — Phase 7.7d: HM inference slice D — occurs + Result + EMatch + let-gen (DONE) — Phase 7.7 COMPLETE

Final tick of **Phase 7.7**. Extends
[`18-hminfer-real.lll`](../../spec/examples/valid/18-hminfer-real.lll)
in place from 616 to 1010 lines (+394). Lands all four outstanding
HM closers in a single slice and marks **Phase 7.7 complete**.

**New shape (on top of Phase 7.7c)**:

```lll
type Outcome A = OkR A | ErrR Str      -- Result-like error carrier
type Pat = PInt Int | PVar Str | PWild  -- EMatch pattern AST
type TypeScheme = MkScheme List[Str] TypeExpr  -- ∀ vars. body

type Expr =
  | ... (EInt/EStr/EBool/EVar/EAdd/EApp/ELam/ELet/EIf)
  | EMatch Expr List[Pat] List[Expr]    -- new

type Env = MkEnv List[Str] List[TypeScheme]    -- was List[TypeExpr]
```

**The four features**:

1. **Occurs check** — new `occursIn v t` walker called from
   `unifyVar`'s bind arm, so `unify a (a -> Int)` now emits an
   `E008 InfiniteType` diagnostic instead of silently constructing a
   circular substitution. Mirrors the F# host's `occurs` in
   [`HMInfer.fs`](../../src/LLLangCompiler/HMInfer.fs) line 52 area.

2. **Result-threaded errors** — `unify` now returns `Outcome[Subst]`
   (was `Maybe[Subst]`), and `inferExpr` returns `Outcome[InferResult]`
   (was raw `InferResult` with `TyName "ERROR"` sentinel). Every
   helper threads errors via nested `match ... with | OkR ... | ErrR
   m -> ErrR m`. t10/t15/t19 now print the real
   `E001 TypeMismatch Int vs Str` instead of just `ERROR`.

   Named `Outcome` (not `Result`) because the codegen prelude
   auto-emits `resultMap` / `resultBind` / `resultMapErr` bindings
   whenever a module declares `type Result` — and those assume a
   two-param `type Result A E = Ok A | Err E`, whereas this slice
   only needs a one-param error-is-always-Str carrier.

3. **EMatch inference** — new `EMatch Expr List[Pat] List[Expr]`
   constructor (parallel-list branches, same trick as `Subst` /
   `Env`) and a `Pat` AST with `PInt` / `PVar` / `PWild`. The
   `inferMatch` helper family walks pat/body pairs in lockstep:
   derives `patTy` per pattern (`PInt -> Int`, `PVar -> fresh α +
   env extend`, `PWild -> fresh`), unifies with the scrutinee type,
   infers the body under the extended env, and unifies each branch
   type with a shared `β`. Returns `applyType sFinal β`.

4. **Let-generalization** — introduces `TypeScheme` and wires it
   into `Env`, `envLookup`, `envExtend`, `applyEnv`, `inferVar`, and
   `inferLet`. Key pieces:

   * `mono t` wraps a raw type as `MkScheme [] t`. Used by
     `envExtend` (which still takes `TypeExpr` for the common
     lambda-param / PVar case) and the Env-in-`main` helper calls.
   * `envExtendScheme` takes a full scheme. Used only by `inferLet`.
   * `applyEnv` now walks schemes via `applyScheme`, which removes
     the scheme's quantified vars from the substitution's domain
     before applying. Built on a new `substRemoveAll` /
     `substRemove` / `substRemoveLists` helper family.
   * `ftvType` / `ftvScheme` / `ftvEnv` compute free type variables.
     All `TyVar` names are treated as free in this slice (no
     flex/rigid split).
   * `generalize e t` = `MkScheme (dedup(ftvType t) - ftvEnv e) t`.
   * `instantiate n sch` allocates one fresh `$k` per quantifier via
     `freshSubstFor` and applies the resulting subst to the scheme
     body. Threads the counter.
   * `inferVar` now instantiates the looked-up scheme.
   * `inferLetBody` calls `generalize env2 t1` before extending, so
     polymorphic bindings like `let id = \x. x in ...` get scheme
     `∀ $0. $0 -> $0` and each use instantiates fresh.

**New test cases** (on top of t1-t15):

```
t16 infer (\f. \x. f x) : (($1 -> $2) -> ($1 -> $2))
t17 unify a (a -> Int) infinite
t18 infer (match 1 | 0 -> "zero" | _ -> "other") : Str
t19 infer (match 1 | 0 -> "zero" | 1 -> 42) : ERROR E001 TypeMismatch Str vs Int
t20 infer (match 1 | x -> x + 1) : Int
t21 infer (let id = \x. x in id 5) : Int
t22 infer (let id = \x. x in let i = id 5 in id "hi") : Str
```

* **t16** exercises the compose-subst chain through a nested lambda
  (higher-order function) without needing let-gen.
* **t17** is the occurs-check demo — `unify a (TyFn a Int)` fires
  `E008`; `runTest` peeks at the `ErrR` message prefix and prints
  `infinite` instead of the generic `mismatch`.
* **t18** is a simple `EMatch` on an `Int` scrutinee returning `Str`
  uniformly across both arms.
* **t19** demonstrates the Result-threaded error shape: branch
  mismatch (Str in arm 0, Int in arm 1) surfaces the real E001.
* **t20** exercises the `PVar` binder — the branch env is extended
  with `x : fresh α`, and the body `x + 1` pins α to `Int`.
* **t21** is the basic let-bound lambda (works with or without
  let-gen; included as a sanity check).
* **t22** is the canonical let-gen demo — without generalization,
  `id`'s type gets pinned at `Int -> Int` after the first use, and
  `id "hi"` fails E001; with generalization, each use instantiates a
  fresh scheme var so the whole expression types at `Str`.

**Out of scope** (later slices or deferred):

1. Multi-param lambda — still single-`Str` param; curry on the call
   site.
2. TypedAST round-trip — `inferExpr` still returns a triple
   `(TypeExpr, Subst, Int)` wrapped in `Outcome`, not a full
   `TypedExpr` walk.
3. Trait dispatch / type classes.
4. Wiring into `17-pipeline-real.lll` — still standalone.
5. Tagged types (`TyTagged`) and unit-mismatch E004/E005 errors.
6. Rigid-var flex/rigid split — all `TyVar`s are treated as flex.

Tests: 398 (unchanged vs Phase 7.7c / 7.8a). The runtime E2E fact in
`HMInferRealTests.fs` updated to assert all twenty-two lines appear
in stdout; the inference round-trip fact still passes unchanged.

**Phase 7.7 closes out here.** The self-host HM inference spine now
covers: unify with occurs check, algorithm-W over every basic Expr
shape (including EMatch), Result-threaded diagnostics (E001 / E002 /
E008), and let-generalization. Next umbrella: **Phase 7.9** —
assemble `bootstrap/compiler.lll` from the lex / parse / elaborate /
HM / codegen slices, or continue Phase 7.8 with more codegen work.

### 2026-04 — Phase 7.8a: codegen slice A — TExpr + showTExpr + showDecl (DONE)

First tick of **Phase 7.8** — the **back end** of the compiler,
written in ll-lang itself. After Phase 7.7 closed out the HM middle
(18-hminfer-real.lll: unify + inferExpr over every basic Expr shape),
this slice starts `Codegen.lll`: a tiny `TExpr` / `TDecl` AST —
a stand-in for the host's TypedAST — plus a `showTExpr` family of
walkers that emit F# source strings, mirroring the F# host's
[`Codegen.fs`](../../src/LLLangCompiler/Codegen.fs) `emitExpr` /
`emitDecl`.

**Milestone**: After this slice lands, **all four compiler stages
have ll-lang representations** (lex → parse → elaborate → HM-infer →
codegen), so the next umbrella (`bootstrap/compiler.lll`) can stitch
them into a single end-to-end program.

New file:
[`19-codegen-real.lll`](../../spec/examples/valid/19-codegen-real.lll)
(212 lines).

**Shape**:

```lll
type TExpr =
  | TEInt Int | TEStr Str | TEVar Str
  | TEAdd TExpr TExpr
  | TEApp TExpr TExpr
  | TELet Str TExpr TExpr

type TDecl = TDFn Str List[Str] TExpr

fn showTExpr(e TExpr) Str          -- dispatcher, one arm per TExpr ctor
fn showInt(n Int) Str              -- "<n>L" (F# int64 literal)
fn showStr(s Str) Str              -- "\"<s>\"" (no escaping)
fn showAdd(a TExpr)(b TExpr) Str   -- "(<a> + <b>)"
fn showApp(f TExpr)(a TExpr) Str   -- "(<f> <a>)"
fn showLet(n Str)(e1 TExpr)(e2 TExpr) Str  -- "(let n = e1 in e2)"
fn showParams(ps List[Str]) Str    -- space-separated idents
fn showDecl(d TDecl) Str           -- dispatch to showFnDecl
fn showFnDecl(name Str)(ps List[Str])(body TExpr) Str
  -- empty ps  -> "let name = body"
  -- non-empty -> "let name p1 p2 ... = body"
```

Every recursive arm is split into its own helper so `match` nesting
stays one level deep — same trick as `18-hminfer-real.lll`'s `unify`
and `inferExpr` spines, for the same indentation-ambiguity reason.

`main` hardcodes four `TDFn`s, one per supported TExpr shape:

```
let inc x = (x + 1L)
let greet = "hello"
let addOne x = (let y = (x + 1L) in y)
let callInc x = (inc x)
```

`inc` exercises TEAdd + TEVar + TEInt; `greet` exercises TEStr and
the empty-params `TDFn` branch; `addOne` exercises TELet (with a
nested TEAdd inside the binding); `callInc` exercises TEApp. Output
is joined via the same `joinLines` helper as
`18-hminfer-real.lll` — `fn main` has a single-expression body so all
four emitted lines have to funnel through a single `printfn` call.

Emission mirrors the host's offside-safe inline forms from
`Codegen.fs` line 162 area:
* `TELet` renders as single-line `(let n = e1 in e2)` to dodge F#'s
  offside-rule in nested contexts — same as the host's `emitExpr`
  `TELet(_, _, _, Some body)` arm.
* `TEApp` and `TEAdd` always parenthesise, same as the host.
* `TDFn` zero-params drops the param segment entirely (`let greet =
  "hello"`), matching the host's `emitFnClause` behaviour.

**Deliberately out of scope** (carved out for Phase 7.8b+):

1. **TELam / TEIf / TEMatch** — multi-line match emission needs
   indentation tracking that this slice dodges.
2. **F# prelude block** — the stdlib shim in `Codegen.fs`
   `fsharpPrelude*` constants is a separate concern.
3. **`[<EntryPoint>]` on `main`** — the host's special-case branch
   for zero-arg `fn main`.
4. **Mutual-recursion grouping** (`let rec ... and ...`) — the host's
   `groupDecls` logic is a separate slice.
5. **Keyword-safe ident rewriting** — the host's `safeIdent` table;
   every hardcoded test name in this slice is already a non-keyword.
6. **Real `TypeScheme`-carrying TypedAST** — this slice walks a plain
   `TExpr`/`TDecl` without touching type info. Slice B adds the
   `TypeScheme` payload once integration with 18-hminfer-real starts.
7. **Consuming the output of `18-hminfer-real.lll`** — integration is
   a separate tick after both sides stabilise.

Tests: 395 → 398 (+3: 1 new corpus theory row in `HMInferTests.fs` +
2 new facts in `CodegenRealTests.fs` — a dedicated inference round-
trip fact and a runtime E2E fact). The runtime E2E asserts each of
the four emitted F# source lines appears in stdout.

Next tick: **Phase 7.8b** — extend `19-codegen-real.lll` in place
with the remaining TExpr shapes (TELam, TEIf, TEMatch) and optional
`TDType` emission, OR **Phase 7.9** — assemble `bootstrap/compiler.lll`
by stitching 09 / 15 / 17 / 18 / 19 into a single end-to-end program
that consumes source text and emits F# source. Pick one; slice B
keeps the codegen spine evolving, 7.9 proves the pipeline composes.

### 2026-04 — Phase 7.8b: codegen slice B — TELam + TEIf + TEMatch (DONE)

Second tick of Phase 7.8. Extends `19-codegen-real.lll` in place with
the three control-flow expression shapes `18-hminfer-real.lll`
already handles on the front-end side, so the ll-lang codegen has
parity with HM inference over every basic `Expr` variant.

Extended file:
[`19-codegen-real.lll`](../../spec/examples/valid/19-codegen-real.lll)
(212 → 341 lines, +129).

**New shapes**:

```lll
type Pat =
  | PInt Int | PVar Str | PWild

type TExpr =
  | ...                                   -- Phase 7.8a shapes
  | TELam Str TExpr                       -- NEW: (fun x -> body)
  | TEIf TExpr TExpr TExpr                -- NEW: (if c then t else e)
  | TEMatch TExpr List[Pat] List[TExpr]   -- NEW: parallel pats/bodies

fn showLam(x Str)(body TExpr) Str        -- "(fun <x> -> <body>)"
fn showIf(c TExpr)(t TExpr)(e TExpr) Str -- "(if <c> then <t> else <e>)"
fn showMatch(scr TExpr)(ps List[Pat])(bs List[TExpr]) Str
  -- "(match <scr> with | <p1> -> <b1> | <p2> -> <b2>)"  -- single line
fn showPat(p Pat) Str                    -- "<n>L" / "<x>" / "_"
fn showArms(ps List[Pat])(bs List[TExpr]) Str
  -- walks parallel lists in lockstep via showArmsCons helper
```

**Design notes**:

* **Parallel pat/body lists** for `TEMatch` mirror
  `18-hminfer-real.lll`'s `EMatch Expr List[Pat] List[Expr]` encoding
  — same reason: the ll-lang surface has no tuples so you can't pair
  `(Pat, Expr)` at construction time. `showArms` / `showArmsCons`
  walk both lists in lockstep, same trick as
  `18-hminfer-real.lll`'s `inferMatchBranches`.
* **Single-line match emission** joins all arms with spaces
  (`| p1 -> b1 | p2 -> b2`) — mirrors the host's `TEMatchOf` arm in
  `Codegen.fs` line 201 area. Multi-line match would need indentation
  tracking this slice still dodges; single-line form works in any
  expression position at the cost of long lines.
* **`TELam` as a top-level value**: `double` is emitted as
  `let double = (fun x -> (x + x))` rather than `let double x = ...`
  — the `showFnDecl` zero-params branch and `showLam` compose
  naturally. The F# output is equivalent (both curry to
  `int64 -> int64`) and this exercises `TELam` without needing a new
  `TDecl` variant.
* Every new `showTExpr` arm is still split into its own helper so the
  dispatcher `match` stays one level deep — same trick as the rest
  of the file.

`main` gains three new `TDFn`s, one per new form:

```
let choose b = (if b then 1L else 2L)
let double = (fun x -> (x + x))
let classify x = (match x with | 0L -> "zero" | _ -> "other")
```

`choose` exercises `TEIf` with an if-expression body; `double`
exercises `TELam` bound as a zero-params `TDFn` value; `classify`
exercises `TEMatch` with a `PInt` literal branch, a `PWild` wildcard
branch, and parallel-list construction (list-literal elements that
start with a TypeId ctor app are let-bound individually to dodge
the `[PInt 0 PWild]` curried-ctor-app parse ambiguity — same
workaround `18-hminfer-real.lll`'s t18 uses).

**Still deliberately out of scope** (carved out for 7.8c+):

1. **Constructor-application (`TECon`) emission** — the host's
   multi-arg tuple-form path for ctor apps is a separate slice.
2. **`TDType` emission** — Phase 7.8c.
3. **F# prelude block** — Phase 7.8d.
4. **`let rec ... and ...` grouping / `[<EntryPoint>]` on `main`** —
   Phase 7.8e.
5. **Multi-line match emission** — still dodged; single-line form
   covers every test case and embeds cleanly in any parent position.
6. **Integration with `18-hminfer-real.lll`'s TypedAST** — still a
   separate tick once both sides stabilise.

Tests: 398 → 398 (no new facts — the existing `runs and emits F#
source lines for each TDFn` fact in `CodegenRealTests.fs` grows its
expected-substring list from 4 to 7 to cover the three new shapes).

Next tick: **Phase 7.8c** — extend `19-codegen-real.lll` with
`TDType` (sum-type) emission, OR **Phase 7.9** — assemble
`bootstrap/compiler.lll` by stitching the lex / parse / elaborate /
HM / codegen slices into a single end-to-end program. All four
compiler stages now have ll-lang representations covering every
basic control-flow shape; 7.9 finally proves the pipeline composes.

### 2026-04 — Phase 7.8c: codegen slice C — TDType sum-type emission (DONE)

Third tick of Phase 7.8. Extends `19-codegen-real.lll` in place with
top-level **sum-type declaration** emission, so the ll-lang codegen
now emits F# discriminated unions alongside the existing fn decls.

Extended file:
[`19-codegen-real.lll`](../../spec/examples/valid/19-codegen-real.lll)
(341 → 546 lines, +205).

**New shapes**:

```lll
type TypeArg =
  | TAName Str    -- concrete: "Int", "Str", "Bool", or user ADT name
  | TAVar Str     -- single-letter type var: "A", "B"

type Ctor = MkCtor Str List[TypeArg]

type TDecl =
  | TDFn Str List[Str] TExpr           -- Phase 7.8a/b
  | TDType Str List[Str] List[Ctor]    -- NEW: sum-type decl

fn showTypeParam(p Str) Str                -- "'A"
fn showTypeArg(a TypeArg) Str              -- Int -> int64, A -> 'A, ...
fn showTypeArgName(n Str) Str              -- primitive name mapping
fn showCtorArgs(args List[TypeArg]) Str    -- " * "-joined arg body
fn showCtor(c Ctor) Str                    -- one "    | Name of ..." line
fn showCtors(cs List[Ctor]) Str            -- newline-joined arms
fn showTypeParams(ps List[Str]) Str        -- "<'A, 'B>" header segment
fn showTypeDecl(name Str)(tps List[Str])(ctors List[Ctor]) Str
```

**Design notes**:

* **Primitive name mapping** mirrors the host's `emitType` in
  `Codegen.fs` line 51 area: `Int` → `int64`, `Str` → `string`,
  `Bool` → `bool`; every other `TAName` passes through verbatim.
  `Float`/`Unit`/`Char` are still out of scope — none of the
  hardcoded test cases use them.
* **Multi-line emission** is safe for type decls because they sit at
  module level — F# has no offside-rule trouble between sibling
  `    | ctor` lines once they're left-anchored. This is the first
  time the file emits multi-line output; `TEMatch` still uses the
  single-line form because it can be embedded in any parent
  expression position.
* **`showDecl` becomes a dispatcher** with two arms (`TDFn` →
  `showFnDecl`, `TDType` → `showTypeDecl`), so the outer match stays
  one level deep. Same trick as the rest of the file.
* **List-literal construction workaround** — the three new TDType
  decls in `main` need the same let-bind-each-element dance as the
  existing `classifyPats` / `classifyBodies` do, because list-literal
  elements that start with a TypeId ctor app parse as a single
  curried ctor call. Every `MkCtor`, `TAVar`, and `TAName` is let-
  bound before being placed in a `[...]` literal.

`main` gains three new TDType decls:

```
type Maybe<'A> =
    | Some of 'A
    | None
type Shape =
    | Circle
    | Rect of int64 * int64
    | Empty
type Pair<'A, 'B> =
    | MkPair of 'A * 'B
```

`Maybe` covers the parametric-header branch (`<'A>`) and mixes an
arg-bearing ctor with a nullary ctor. `Shape` covers the monomorphic
branch (no `<>`) and exercises the `" * "` join in `showCtorArgs` via
the two-arg `Rect` ctor. `Pair` covers the two-type-param header
branch (`<'A, 'B>`), exercising the `", "` join in
`showTypeParamsBody` plus the `TAVar` arm of `showTypeArg`.

**Still deliberately out of scope** (carved out for 7.8d+):

1. **Record (`TBRecord`) and wrapped (`TBWrapped`) type bodies** —
   only sum bodies are supported in 7.8c.
2. **Parametric type applications in ctor args** — `Some (Maybe A)`
   is deliberately out; only flat `TAName` / `TAVar` single-segment
   args are supported.
3. **F# prelude block** — Phase 7.8d.
4. **`let rec ... and ...` grouping / `[<EntryPoint>]` on `main`** —
   Phase 7.8e.
5. **Constructor-application (`TECon`) emission** — still a separate
   slice.
6. **Integration with `18-hminfer-real.lll`'s TypedAST** — still a
   separate tick once both sides stabilise.

Tests: 398 → 398 (no new facts — the existing `runs and emits F#
source lines for each TDFn` fact in `CodegenRealTests.fs` grows its
expected-substring list from 7 to 16 to cover the three new type
decls and their header / arm lines).

Next tick: **Phase 7.8d** — add the F# prelude block to the codegen
output (the stdlib shim in `Codegen.fs` `fsharpPrelude*`), OR
**Phase 7.9** — assemble `bootstrap/compiler.lll` by stitching the
five self-host slices into a single end-to-end program. 7.8c's
sum-type emission is now in place, so a trimmed `module M\ntype T =
...\nfn main() = ...` example can round-trip through the ll-lang
codegen without needing a host fallback for type decls.

### 2026-04 — Phase 7.8d: codegen slice D — module header + F# prelude (DONE)

Fourth tick of Phase 7.8. Extends `19-codegen-real.lll` in place with
a `module <path>` header and a minimal F# stdlib prelude block, so the
ll-lang codegen now emits something that's structurally a complete F#
source file instead of a loose sequence of `let` / `type` decls.

Extended file:
[`19-codegen-real.lll`](../../spec/examples/valid/19-codegen-real.lll)
(546 → 683 lines, +137).

**New shapes**:

```lll
type Module = MkModule Str List[TDecl]

fn fsharpPrelude(_ignored Int) Str      -- 5-binding subset of fsharpPreludeCore
fn showModule(m Module) Str             -- full-file emitter
fn showModuleBody(path Str)(decls List[TDecl]) Str  -- split helper
fn joinBlocks(xs List[Str]) Str         -- "\n\n"-joined fold
```

**Design notes**:

* **Prelude content** mirrors a 5-binding subset of the host's
  `fsharpPreludeCore` in `Codegen.fs` line 380 area: `listMap`,
  `listLen`, `strLen`, `strConcat`, `print`. Wrapped in the canonical
  `// --- ll-lang stdlib prelude (auto-generated) ---` /
  `// --- end prelude ---` banner so the output looks like a shrunken
  version of the host's prelude block. Phase 7.8e+ grows this to the
  full ~30-binding core plus conditional Maybe/Result sections.
* **`fsharpPrelude` takes a dummy `Int` arg** (and callers pass `0`)
  because the module parser doesn't yet accept `fnName ()` as a call
  expression. A constant binding would be cleaner but ll-lang doesn't
  have `let` at module level for arbitrary expressions in this file's
  dialect, and a zero-param fn would need the `()` call syntax to
  invoke.
* **`showModule` dispatches through `showModuleBody`** to keep its
  outer match one level deep, same trick as the rest of the file. The
  body helper emits `module <path>` + prelude + decl block glued with
  blank-line separators via the new `joinBlocks` fold (same shape as
  `joinLines`, `"\n\n"` separator instead of `"\n"`).
* **`main` reorders decls** — types first (Maybe, Shape, Pair), then
  fns (inc, greet, addOne, callInc, choose, double, classify) — so
  the output mirrors the host's `emitModule` layout. The ll-lang
  `showModule` itself doesn't sort by kind; it trusts the caller.
  Phase 7.8e may add an automatic kind-aware split once mutual-
  recursion grouping lands.
* **Flat prelude (no conditional sections)** — the host's
  `assemblePrelude` only emits `fsharpPreludeMaybe` / `fsharpPreludeResult`
  when the user declares `Maybe` / `Result` types. This slice always
  emits the same 5-line core because the test cases don't exercise
  Maybe/Result-dependent bindings and because conditional emission
  would need the decl list scanned twice.

`main` now wraps its ten hardcoded decls in a single `MkModule
"Examples.Generated" ...` value and `printfn`s its `showModule`
rendering.

**Still deliberately out of scope** (carved out for 7.8e+):

1. **Conditional Maybe/Result prelude sections** — the host's
   `assemblePrelude` emits these only when the user declares `Maybe`/
   `Result` types. This slice keeps the prelude unconditional and
   always 5-line.
2. **Full ~30-binding `fsharpPreludeCore`** — math / char / file IO /
   stringly helpers are out of scope for this minimum-viable slice.
3. **`[<EntryPoint>]` attribute on `main`** — Phase 7.8e.
4. **Mutually-recursive `let rec ... and ...` grouping** — Phase 7.8e.
5. **Type-decl-first reordering inside `showModule`** — Phase 7.8e
   (currently the caller has to hand-order types before fns).
6. **Integration with any pipeline** — standalone demo only.

Tests: 398 → 398 (no new facts — the existing `runs and emits F#
source lines for each TDFn` fact in `CodegenRealTests.fs` grows its
expected-substring list from 16 to 24 to cover the `module` header,
the prelude banner lines, and the five prelude body bindings).

Next tick: **Phase 7.8e** — `let rec ... and ...` mutual-recursion
grouping + `[<EntryPoint>]` attribute on `main`, OR **Phase 7.9** —
assemble `bootstrap/compiler.lll` by stitching the five self-host
slices into a single end-to-end program. The codegen side now emits
a complete F# file shape (module header + prelude + types + fns),
so a trimmed `module M\ntype T = ...\nfn main() = ...` example can
round-trip through the ll-lang codegen and compile standalone
without any host-side assembly.

### 2026-04 — Phase 7.8e: codegen slice E — let-rec grouping + `[<EntryPoint>]` (DONE)

Fifth and final tick of Phase 7.8. Extends `19-codegen-real.lll` in
place with mutually-recursive `let rec ... and ...` grouping for 2+
consecutive non-main TDFns and `[<EntryPoint>]` emission on the zero-
param `main` fn. Both mirror the host `Codegen.fs` behavior, using a
simplified "always rec for 2+" rule instead of the host's
`containsVar` dependency walker.

Extended file:
[`19-codegen-real.lll`](../../spec/examples/valid/19-codegen-real.lll)
(683 → 926 lines, +243).

**New shapes**:

```lll
fn isMainFn(name Str)(ps List[Str]) Bool
fn showMainDecl(body TExpr) Str                    -- [<EntryPoint>] form
fn showFnDeclPlain(name Str)(ps List[Str])(body TExpr) Str   -- singleton `let`
fn showFnDeclFirst(name Str)(ps List[Str])(body TExpr) Str   -- `let rec` head
fn showFnDeclCont (name Str)(ps List[Str])(body TExpr) Str   -- `and` tail
fn showFnGroup(fns List[TDecl]) Str                -- 2+ run → let rec block
fn splitAndShowDecls(decls List[TDecl]) Str        -- accumulator walker
```

**Design notes**:

* **"Always rec for 2+" rule** — the host `Codegen.fs` runs a
  `containsVar` walker to decide whether a 2+ fn run is actually
  recursive and only emits `let rec` when it is. This slice skips
  that walker entirely: any consecutive run of 2+ non-main TDFns
  becomes a single `let rec ... and ... and ...` block unconditionally.
  F# accepts `let rec` on non-recursive bindings — it's stricter,
  not wrong — so the output stays valid in every test case at a
  fraction of the implementation cost.
* **`isMainFn` predicate** matches zero-param fns named `"main"`
  exactly, mirroring `isMainFn` in `Codegen.fs` line 268 area minus
  the `TypedFnSig` wrapping. Non-zero-param `main` fns fall through
  to the normal `showFnDeclPlain` path.
* **`showMainDecl` wraps the body** with a fixed header/trailer:
  `[<EntryPoint>]\nlet main (argv: string[]) =\n    <body>\n    0`.
  F# requires the entry point to have signature `string[] -> int`,
  so the dummy `argv` parameter and `0` exit code are mandatory.
* **Four `showFnDecl*` variants** — `Plain` for singleton runs,
  `First`/`Cont` for grouped runs, and `Dispatch` to route TDFn
  through either `Plain` or `Main`. Each one keeps its own
  `match ps with` so the "one-level-deep match" discipline holds
  across the whole file.
* **`splitAndShowDecls` walker** carries two accumulators through
  a left-to-right traversal: `pending` (reversed list of non-main
  TDFns currently being collected into a potential rec group) and
  `acc` (output string built so far). On every TDType or main
  boundary it flushes `pending` via `flushPendingFns` — which
  reverses the list back to source order and dispatches on length
  (singleton → `showDecl` plain `let`, 2+ → `showFnGroup` rec
  block) — then emits the boundary decl as its own block.
* **Flushing on end-of-list** — the loop's base case hits
  `flushPendingFns` too, so any trailing non-main fn run gets its
  own block even when no TDType or main follows it.
* **`if ... then EXPR` must stay on one line** — the parser rejects
  `if cond\n  then EXPR` layouts, so `splitAndShowStepFn` extracts
  its main-boundary branch into a separate `splitAndShowStepMain`
  helper. Same one-line-branch rule as `showTypeArgName`.

`main` now grows from 10 to 11 decls, adding `TDFn "main" [] (TEApp
(TEVar "print") (TEStr "hello"))` at the end. The seven existing
non-main fns now collapse into a single `let rec inc ... and greet
... and addOne ... and callInc ... and choose ... and double ... and
classify ...` block, and the new `main` decl prints with the
`[<EntryPoint>]` header, dummy `argv` parameter, and `0` exit-code
trailer.

**Still deliberately out of scope** (carved out for Phase 7.9+):

1. **Proper recursion-detection walker** (host's `containsVar`) —
   this slice uses "always rec for 2+" to keep the impl tiny.
2. **Mutual-recursion dependency splitting** within groups — every
   contiguous non-main fn run stays as one group, even if the fns
   don't actually reference each other.
3. **Full ~30-binding `fsharpPreludeCore`** — still 5-line subset
   from Phase 7.8d.
4. **Integration with the 18-hminfer-real.lll HM side** — still a
   standalone demo; bootstrap stitching lands in Phase 7.9.
5. **Keyword-safe ident rewriting** (`safeIdent` in `Codegen.fs`) —
   every hardcoded test name is already a non-keyword.

Tests: 398 → 398 (no new facts — the existing `runs and emits F#
source lines for each TDFn` fact in `CodegenRealTests.fs` grows its
expected-substring list from 24 to 29 to cover the `let rec inc`
head, six `and <name>` continuation lines, `[<EntryPoint>]`,
`let main (argv: string[]) =`, `(print "hello")`, and `    0`).

With this slice in place, **Phase 7.8 is complete**: all four
compiler stages (lex / parse / elaborate / HM-infer / codegen) now
have ll-lang self-host representations for every shape the
bootstrap compiler will need, and the codegen side emits a complete,
compilable F# source file for every supported input.

Next tick: **Phase 7.9** — assemble `bootstrap/compiler.lll` by
stitching the five self-host slices (`15-lexer-real.lll`,
`16-parser-real.lll`, `17-elab-real.lll`, `18-hminfer-real.lll`,
`19-codegen-real.lll`) into a single end-to-end program that
takes ll-lang source on stdin and emits F# source on stdout.

### 2026-04 — Phase 7.9a: 3-stage bootstrap compiler — parser + elab + HM (DONE)

First tick of **Phase 7.9** — the **bootstrap assembly** umbrella.
Where Phase 7.8 finished the codegen back end as a standalone slice,
Phase 7.9a takes the first step toward stitching the five self-host
slices into one program: it combines parser + elaborator + a minimal
HM-style type checker into a **single file**, running all three
stages back-to-back on the same shared AST.

New file: **`spec/examples/valid/20-bootstrap-compiler.lll`** (1598
lines). Starts from `17-pipeline-real.lll` verbatim (the existing
parser + elaborator integration, 1320 lines) and adds ~280 lines of
HM pass code at the end, plus an updated driver.

**What the HM pass does**:

  * Adds a minimal `TypeExpr` ADT:
    ```
    type TypeExpr =
      | TyName Str
      | TyVar Str
    ```
  * Adds a structural `typeEq : TypeExpr -> TypeExpr -> Bool` that
    treats `TyVar "?"` as a wildcard matching anything — mirrors the
    host elaborator's `tyEqual`.
  * Adds `inferExprType : TypeEnv -> Expr -> TypeExpr` that assigns:
      - `EInt _` -> `TyName "Int"`
      - `EStr _` -> `TyName "Str"`
      - `EVar name` -> lookup in `TypeEnv`, falling back to `TyVar "?"`
      - arithmetic (`EAdd/ESub/EMul/EDiv`) -> `TyName "Int"`
      - `EIf _ thn _` -> inferred type of the `then` branch
      - everything else -> `TyVar "?"` (punted shape)
  * Adds `typeCheck : TypeEnv -> Expr -> List[Str]` that walks the
    expression and emits `E001 TypeMismatch <l> vs <r>` at each
    arithmetic or if-branch mismatch.
  * Adds a `TypeEnv` seeded from a fn's declared params:
    `MkParam name (TR tyName)` -> `name -> TyName tyName`.
  * Extends `elaborate` to run the HM pass after name-resolution and
    exhaustiveness, concatenating all three error lists in order:
    E002 first, then E003, then E001.

**Driver delta**: `main`'s hardcoded source gains a fourth fn
`badType(x Int) Int = x + "y"` so the HM pass has something to fire
on. Expected stdout is four lines:

```
E002 UnboundVar undefinedName
E003 NonExhaustiveMatch Shape missing Circle
E003 NonExhaustiveMatch Shape missing Rect
E001 TypeMismatch Int vs Str
```

**Deliberately out of scope** (carved out for Phase 7.9b+):

  * Full HM with `unify` / `Subst` / fresh vars — 18-hminfer-real.lll
    has the reference implementation, but it works on a minimal
    local AST, not the parser's richer one. Phase 7.9b (or later)
    bridges the two.
  * Pattern-type checking inside `EMatch` arms.
  * Let-generalization for `ELetIn` bindings.
  * Constructor-arity checking for `ECon` applications.
  * Codegen integration — Phase 7.9b folds in `19-codegen-real.lll`
    so the bootstrap compiler can emit F# source.

Tests: 398 -> 401 (one new corpus theory row in `HMInferTests.fs`
plus two new facts in `BootstrapCompilerTests.fs`):

  * `20-bootstrap-compiler.lll parses, elaborates, and infers
    without errors` — inference round-trip smoke test.
  * `20-bootstrap-compiler.lll runs and emits E002 + E003 + E001
    for the hardcoded source` — runtime E2E via `lllc run`,
    substring-contains assertions on all four expected error
    lines.

With this slice in place, three of the five compiler stages (lex +
parse + elab + minimal HM) run back-to-back inside a single
ll-lang program on a real source string. The Phase 7.6 integration
proved two-stage stitching; Phase 7.9a proves three-stage stitching.

Next tick: **Phase 7.9b** — add codegen to the stitched pipeline.
Either inline `19-codegen-real.lll`'s TExpr/TDecl walker verbatim
(minimal bridge, matches the Phase 7.9a simplicity) or extend the
HM pass first so the bootstrap compiler can emit F# source for the
hardcoded driver module end-to-end.

### 2026-04 — Phase 7.9b: 4-stage bootstrap compiler — + F# codegen (DONE)

Second tick of **Phase 7.9** — turns 20-bootstrap-compiler.lll from a
3-stage error reporter (parse + elab + HM) into a true **4-stage
compiler** (parse + elab + HM + **F# emission**). After this slice
the file takes ll-lang source as input and produces F# source as
output — not just a list of errors.

Updated file: **`spec/examples/valid/20-bootstrap-compiler.lll`**
(1598 -> 2100 lines, +502). Adds an `emit*` family of helpers that
walks the parser's `Decl` / `Expr` / `Pat` / `TypeArg` AST and
produces an F# source string. Helpers are flat ports of the
`showTExpr` / `showDecl` family from `19-codegen-real.lll`,
adapted to the parser's richer AST shape.

**What the codegen pass does**:

  * `emitExpr : Expr -> Str` — covers every shape the hardcoded
    driver source needs (`EInt` / `EStr` / `EVar` / `ETagged` /
    `EAdd` / `ESub` / `EMul` / `EDiv` / `EApp` / `ELam` / `ELetIn` /
    `EIf` / `EMatch`). Arithmetic arms share a single 3-arg
    `emitBin l r op` helper. Match arms emit on a single line via
    `emitArms` / `emitArmsCons` (offside-rule-safe in any expression
    position).
  * `emitPat : Pat -> Str` — covers all five parser pattern shapes
    (`PInt` / `PVar` / `PWild` / `PNil` / `PCons`).
  * `emitTypeArg` / `emitCtorOne` / `emitCtorList` /
    `emitTypeParamsHeader` — sum-type emission with primitive name
    mapping (`Int -> int64`, `Str -> string`, `Bool -> bool`),
    type-param `'A` rendering, and the `T1 * T2` ctor-arg join.
  * `emitDeclStandalone` / `emitFnPlain` / `emitFnFirst` /
    `emitFnCont` / `emitMainDecl` — fn-decl emission with
    `[<EntryPoint>]` wrapping for the zero-param `main` and
    `let rec ... and ...` grouping for 2+ consecutive non-main fns.
  * `emitGroupedDecls` — left-to-right walker that partitions a
    `List[Decl]` into groups (DType / DLet boundaries flush a
    pending non-main-fn run; main fn flushes too) and emits each
    group as its own blank-line-joined block. Uses the same
    "always rec for 2+" simplification as `19-codegen-real.lll`.
  * `emitPrelude` — minimal 5-binding subset of the F# host's
    `fsharpPreludeCore` (listMap, listLen, strLen, strConcat,
    print) wrapped in the canonical comment banner.
  * `emitModule` — top-level entry point. Stitches the
    `module <path>` header, prelude block, and decl blocks with
    `\n\n` separators via `emitJoinBlocks`.

**Driver delta**: the hardcoded source is now **clean** (no
unbound vars, no non-exhaustive matches, no type mismatches), so
`elaborate` returns an empty error list and `main` proceeds to the
codegen pass. The error-reporter path is preserved as a fallback.

New driver source:
```
module Examples.Clean
type Maybe A = Some A | None
fn inc(x Int) Int = x + 1
fn greet() Str = "hello"
fn main() Int = inc 1
```

Expected stdout (F# source for the clean module):
```
module Examples.Clean

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

let rec inc x = (x + 1L)
and greet = "hello"

[<EntryPoint>]
let main (argv: string[]) =
    (inc 1L)
    0
```

**Deliberately out of scope** (carved out for Phase 7.10+):

  * **Bootstrap fixpoint** — compiling 20-bootstrap-compiler.lll
    through itself via the emitted F# source and verifying the
    output is byte-identical. That's Phase 7.10's job.
  * **`dotnet fsi` invocation** of the emitted F# — the runtime
    test asserts substrings of the emitted string only.
  * **Full expression coverage** — only what the hardcoded test
    module needs is wired through. Records, `DTag` / `DImport`
    decls, spread patterns, real string escape handling, and
    parametric `TAApp` ctor-args (`Maybe[Int]`) all work, but
    other shapes (`TBRecord`, `TBWrapped`, `Float` / `Char` /
    `Unit` literals) are still un-emitted.
  * **Recursion-detection walker** — Phase 7.9b uses the "always
    rec for 2+" simplification (F# accepts `let rec` on
    non-recursive bindings).

Tests: 401 -> 401 (no new fact; the existing runtime E2E test was
**rewritten** to assert substrings of the emitted F# source rather
than the four `E001/E002/E003` lines from the 7.9a hardcoded
source). Test name updated from `... emits E002 + E003 + E001 for
the hardcoded source` to `... emits F# source for the clean
hardcoded module`. Inference round-trip test unchanged.

With this slice in place, four of the five compiler stages (lex +
parse + elab + minimal HM + codegen) run back-to-back inside a
single ll-lang program on a real source string. Phase 7.6 proved
two-stage stitching; Phase 7.9a proved three-stage stitching;
Phase 7.9b proves four-stage stitching with F# source output.

Next tick: **Phase 7.10** — bootstrap fixpoint. Compile the
4-stage bootstrap compiler `20-bootstrap-compiler.lll` through
itself: feed the file to its own codegen pass, run the emitted F#
through `dotnet fsi`, and verify the resulting program reproduces
the same F# source for the same input — closing the self-hosting
loop.

### 2026-04 — Phase 7.9c: bootstrap compiler reads source from file (DONE)

Third tick of **Phase 7.9** — flips the driver from a hardcoded
string literal to a real `readFile` call. First concrete step
toward `compiler₁ = compile(compiler.lll)`: the bootstrap
compiler is now a file-reading tool rather than a fixed-input
demo.

**Changes:**

* New corpus file `spec/examples/valid/20a-bootstrap-input.lll`
  (5 lines) — the exact same "clean" ll-lang module that was
  previously a hardcoded string literal inside `main`:
  ```
  module Examples.Clean
  type Maybe A = Some A | None
  fn inc(x Int) Int = x + 1
  fn greet() Str = "hello"
  fn main() Int = inc 1
  ```
* `main` in `20-bootstrap-compiler.lll` (+17 / -11) now reads
  the source via `readFile "spec/examples/valid/20a-bootstrap-input.lll"`
  and pipes it through the existing `tokenize → parseModule →
  elaborate → emitModule` chain.
* `tests/LLLangTests/BootstrapCompilerTests.fs` (+86 / -31):
  pinned the child process `WorkingDirectory` to the repo root
  (derived from `__SOURCE_DIRECTORY__`), extracted a `runBootstrap`
  helper, and added a **new file-reading-path test** that
  temporarily renames `20a-bootstrap-input.lll` to `.bak`, asserts
  the bootstrap run fails with no codegen markers, and restores
  the file in a `finally` block — proving the source genuinely
  flows through `readFile` rather than a stale hardcoded fallback.

**Why it matters:**

Before 7.9c, the 4-stage bootstrap compiler only ever compiled
one hardcoded input. "Point it at a different .lll file" meant
editing and rebuilding the source. Now any .lll file on disk
can be the input — the minimum shape of a real CLI compiler
tool. The next tick can progressively expand
`20a-bootstrap-input.lll` (or point at other corpus files) to
probe what the bootstrap compiler can handle and where the
parser / elaborator / HM / codegen gaps live. That feature-gap
list is the raw material for Phase 7.10.

`readFile : Str -> Str` was already wired end-to-end
(`Elaborator.fs:127` + `Codegen.fs:410` prelude), so no host
compiler changes were needed.

**Deliberately out of scope** (carved out for Phase 7.9d+):

* **argv / stdin** — the input path is still hardcoded in the
  ll-lang source. A true CLI with `argv` needs ll-lang support
  for command-line args, which doesn't exist yet.
* **Feature-gap expansion** — `20a-bootstrap-input.lll` is still
  the minimal 5-line clean module. Pointing the bootstrap
  compiler at larger corpus files (e.g. `17-pipeline-real.lll`,
  `18-hminfer-real.lll`) will surface parser / elab / HM / codegen
  gaps that Phase 7.9d / 7.10 will have to close.

Tests: 401 → 402 (+1 for the file-reading-path regression test).

With 7.9c in place, the bootstrap compiler is now a real file
consumer rather than a fixed-input demo. Every subsequent slice
can change behaviour by editing `20a-bootstrap-input.lll` alone.

Next tick: **Phase 7.9d** — expand the input file toward
`20-bootstrap-compiler.lll`'s own shape and fix the first class
of gaps that surface.

### 2026-04 — Phase 7.9d: bracket-form types in fn signatures (DONE)

Fourth tick of **Phase 7.9**. Probing the bootstrap compiler with
a fn that uses a parametric return type (`fn wrap(v Int) Maybe[Int]
= Some v`) surfaced a cascading parser bug: `parseParamGroups`
and `parseReturnType` only accepted bare `Upper` type names, so
the head `Maybe` was consumed but `[Int] = Some v` was left in
the token stream. The expression parser fell through to `(EInt
0, toks)` on `TLBrack` in `parseAtom`, the outer decl loop
desynched on the stray tokens, and the subsequent `main` decl
was **silently dropped** from the emitted F# source.

**Changes:**

* New helper `parseSkipBrackType` in `20-bootstrap-compiler.lll`
  consumes any trailing `[...]` bracket groups after a head
  `TUpper` has been read. Handles nested (`Foo[Bar[Baz]]`) and
  chained (`Result[A][E]`) forms.
* `parseParamGroups` + `parseReturnType` both call the helper.
  The bracket payload is discarded at parse time (kept out of
  `TypeRef = TR Str`) — enough to unblock parsing without
  extending the AST.
* New fixture `20b-bootstrap-input-maybe.lll` exercises
  `fn wrap(v Int) Maybe[Int] = Some v` + main.
* New regression test in `BootstrapCompilerTests.fs` swaps the
  20a input for 20b, runs the bootstrap compiler, and asserts
  the emitted F# contains `wrap`, `[<EntryPoint>]`, and `let main`
  — proving main is no longer dropped.

**Deliberately out of scope:**

* **Bracket-form type payload in `TypeRef`** — the brackets are
  consumed and discarded. A future slice can extend `TypeRef`
  to carry the full type application and feed it to
  `typeCheck` / HM for real bracket-aware inference.
* **`(Upper Upper)` juxtaposition form** — the host parser
  accepts both `Maybe[Int]` and `(Maybe Int)`; the bootstrap
  parser only accepts the bracket form for now.

Tests: 402 → 403 (+1 for the bracket-form regression test).

Next tick: **Phase 7.9e** — seed the elaborator env with stdlib
builtin names, so fn bodies that call `strConcat` / `listMap`
don't fire `E002 UnboundVar`.

### 2026-04 — Phase 7.9e: stdlib builtins in elaborator env (DONE)

Fifth tick of **Phase 7.9**. Probing with `fn greet(n Str) Str =
strConcat "hi " n` surfaced the next gap: `elaborate` started
with an empty `env0 = MkEnv []`, so any fn body that called a
stdlib builtin fired `E002 UnboundVar <name>`. Blocks any input
program that uses `strConcat` / `listMap` / `printfn` / `readFile`
— which is **most** of the ll-lang corpus files.

**Changes:**

* New helper `stdlibNames(_ignored Int) List[Str]` in
  `20-bootstrap-compiler.lll` returns the list of stdlib builtin
  names the bootstrap compiler itself relies on:
  `["strConcat" "strLen" "print" "printfn" "listMap" "listLen"
  "listAppend" "listIsEmpty" "listFold" "readFile"]`. Uses the
  `_ignored Int` dummy-arg convention already used by
  `emitPrelude` to dodge ll-lang's lack of zero-param fn-call
  surface syntax at expression position.
* `elaborate` now calls `MkEnv (stdlibNames 0)` instead of
  `MkEnv []`, so every input program starts with the 10 builtin
  names already in scope.
* New fixture `20c-bootstrap-input-stdlib.lll` exercises
  `strConcat "hi " n` in a fn body.
* New regression test in `BootstrapCompilerTests.fs` swaps the
  20a input for 20c, runs the bootstrap compiler, and asserts
  (a) no `E002` in the combined output, (b) emitted F# contains
  `let greet n` + `strConcat` + `[<EntryPoint>]`.

**Deliberately out of scope:**

* **Full stdlib coverage** — 10 names only (the ones the
  bootstrap compiler itself uses). Other host builtins like
  `charToStr` / `strFromChar` / `intToStr` can be added in a
  later slice once an input program needs them.
* **Stdlib types** — the env is still a flat `List[Str]`. HM
  still skips unknowns on stdlib calls. Real stdlib type
  plumbing is a much bigger slice.

Tests: 403 → 404 (+1 for the stdlib-env regression test).

Next tick: **Phase 7.9f** — `==` operator only, so
`if 1 == 1 then 0 else 1` can round-trip. New lexer token,
new `Expr` variant, new parser precedence layer, new codegen
arm — `<` / `>` / `!=` / `&&` / `||` punted to 7.9g+.

### 2026-04 — Phase 7.9f: == operator (DONE)

Sixth tick of **Phase 7.9**. Probing with
`fn main() Int = if 1 == 1 then 0 else 1` surfaced the next
gap: the bootstrap lexer only emitted `TEq` for a single `=`
and had no two-char `==` token, so the parser could not
handle equality comparison. Blocks any input program that
uses the `==` operator — which includes most ll-lang
branching on integer equality.

**Changes:**

* New lexer token `TEqEq` in `20-bootstrap-compiler.lll`'s
  `Token` sum, alongside the existing `TEq`.
* New helper `lexEqOrEqEq` mirroring `lexColonOrCons` /
  `lexMinusOrArrow`: the main loop consumes the first `=`
  and hands the tail to the helper, which peeks for a second
  `=`; if found, emits `TEqEq` and recurses past both, else
  emits `TEq` and re-lexes starting at the non-`=` head.
* New `EEq Expr Expr` variant in `Expr`.
* New `parseCompare` / `parseCompareTail` layer in the
  precedence cascade, slotted between `parseExpr` and
  `parseAddSub` (comparison is looser than `+/-`). Left
  associative, same shape as `parseAddSubTail`. `parseArmBody`
  also promoted from `parseAddSub` to `parseCompare` so match
  arm bodies can use `==`.
* `checkExpr` / `inferExprType` / `typeCheck` / `showExpr` /
  `emitExpr` each get one new `EEq` arm. `inferExprType`
  returns `TyName "Bool"` — just a string tag in this slice,
  no Bool-type machinery (no `typeEq Bool Bool` arm, no
  `checkIfBranches` refinement).
* `emitExpr` uses `emitBin l r " = "` — F# uses a single `=`
  for equality, so `EEq (EInt 1) (EInt 1)` emits `(1L = 1L)`
  (the `L` suffix comes from `emitInt`).
* New fixture `20d-bootstrap-input-eqeq.lll` exercises
  `fn main() Int = if 1 == 1 then 0 else 1`.
* New regression test in `BootstrapCompilerTests.fs` swaps
  the 20a input for 20d, runs the bootstrap compiler, and
  asserts (a) no `E002` / no `error` in combined output,
  (b) emitted F# contains `let main` + `[<EntryPoint>]` +
  `(1L = 1L)`.

**Deliberately out of scope:**

* **`<` / `>` / `<=` / `>=` / `!=`** — only `==` in this
  slice. The new `parseCompare` layer is structured as a
  precedence tier so more comparison operators can be added
  with a second pattern arm in `parseCompareTail` (and new
  lexer tokens) without restructuring the cascade.
* **`Bool` type machinery** — `TyName "Bool"` is just a
  string tag. No `typeEq Bool Bool` refinement, no
  `checkIfBranches` narrowing on `EEq` guards, no Bool
  literals (`true` / `false`). An input program that tries
  to bind a comparison result to a variable and then pass
  it to a fn expecting `Int` will still parse and emit — HM
  just rubber-stamps the `TyVar "?"` mismatch.
* **Boolean connectives** — `&&` / `||` / `not` all stay in
  Phase 7.9g+.

Tests: 404 → 405 (+1 for the `==` regression test).

Next tick: **Phase 7.9g** — `<` / `>` comparison operators,
so `if n < 0 then 0 - n else n` can parse. Reuses the
`parseCompare` layer shipped in 7.9f — only new lexer
tokens, new `Expr` variants, and new emit arms needed.

### 2026-04 — Phase 7.9g: < and > operators (DONE)

Seventh tick of **Phase 7.9**. Probing with
`fn abs(n Int) Int = if n < 0 then 0 - n else n` surfaced
the next gap: the bootstrap lexer silently dropped `<` and
`>` via the `lexChars` fall-through (`else lexChars rest`),
so `n < 0` tokenised as `n 0` and parsed as juxtaposition
application `(n 0L)` — a silent mis-parse that reached
codegen and emitted invalid F#. Blocks any input program
that uses ordered integer comparison — which includes every
`abs` / `max` / `min` / bounds-check shape.

**Changes:**

* New lexer tokens `TLt` and `TGt` in
  `20-bootstrap-compiler.lll`'s `Token` sum, alongside the
  existing `TEq` / `TEqEq`.
* Two new single-char arms in `lexChars` — `else if c == '<'
  then listAppend [TLt] (lexChars rest)` and the `>` mirror.
  No `lexLtOrX` helper needed: this slice has no two-char
  forms like `<=` / `>=` / `<<` / `>>`. The standalone `>`
  arm does NOT collide with `lexMinusOrArrow`'s `->` handling
  because `lexMinusOrArrow` is only called from the `c ==
  '-'` arm, so a bare `>` (not preceded by `-`) falls through
  to the new arm as intended.
* New `ELt Expr Expr` / `EGt Expr Expr` variants in `Expr`,
  siblings of `EEq`.
* Two new arms in `parseCompareTail` — `| TLt :: r -> let
  (rhs, rest2) = parseAddSub r in parseCompareTail (ELt lhs
  rhs) rest2` and the `TGt` / `EGt` mirror. Reuses the 7.9f
  `parseCompare` precedence layer unchanged; no new tier
  needed.
* `checkExpr` / `inferExprType` / `typeCheck` / `showExpr` /
  `emitExpr` each get two new arms (`ELt` / `EGt`) mirroring
  the existing `EEq` arms. `inferExprType` returns `TyName
  "Bool"` for both, same as `EEq`.
* `emitExpr` uses `emitBin l r " < "` / `emitBin l r " > "`
  — F# uses `<` / `>` verbatim for integer comparison, so
  `ELt (EVar "n") (EInt 0)` emits `(n < 0L)` (the `L` suffix
  comes from `emitInt`).
* New fixture `20e-bootstrap-input-ltgt.lll` exercises both
  operators together via `fn neg(n Int) Int = if n < 0 then
  0 - n else n` and `fn pos(n Int) Int = if n > 0 then n
  else 0 - n`.
* New regression test in `BootstrapCompilerTests.fs` swaps
  the 20a input for 20e, runs the bootstrap compiler, and
  asserts (a) no `E002` / no `error` in combined output,
  (b) emitted F# contains `let rec neg` + `and pos` +
  `let main` + `[<EntryPoint>]` + `(n < 0L)` + `(n > 0L)`.
  The `let rec` / `and` spine is an incidental consequence
  of the 7.8e mutual-recursion grouping — any run of two or
  more consecutive non-`main` fn decls gets wrapped in a
  single `let rec ... and ...` block.

**Deliberately out of scope:**

* **`<=` / `>=` / `!=`** — only `<` and `>` in this slice.
  The two-char forms each need their own `lexLtOrLte` /
  `lexGtOrGte` / `lexBangOrNeq` helpers (same shape as
  `lexEqOrEqEq` from 7.9f) plus new `Expr` variants. Trivial
  extensions, just not in scope here.
* **`Bool` type machinery** — `TyName "Bool"` is still just
  a string tag. Comparison results infer to `TyName "Bool"`
  but nothing consumes them; an `if` guard on a comparison
  short-circuits via `inferExprType`'s `EIf _ thn _ ->
  inferExprType env thn` arm (returns the `then` branch's
  type), never actually checking that the guard is `Bool`.
  An input program that binds a comparison result to a
  variable and then tries to use it as an `Int` will still
  parse and emit — HM rubber-stamps the `TyVar "?"`
  mismatch.
* **Boolean connectives** — `&&` / `||` / `not` stay in
  Phase 7.9h+.
* **`true` / `false` literals** — Bool literals still
  unavailable; comparisons are the only way to produce a
  "truthy" value.

Tests: 405 → 406 (+1 for the `< / >` regression test).

**Observation worth noting** — the pre-fix behaviour was a
silent mis-parse, not a crash: `n < 0` tokenised as `n` `0`
(unknown chars dropped by the lexer fall-through) and
parsed as `EApp (EVar "n") (EInt 0)`, which then reached
codegen and emitted `(n 0L)` as juxtaposition application.
The fsharpc check in the test pipeline caught it as `FS0026
This rule will never be matched` (the spurious app made a
subsequent `if` arm unreachable), but from the bootstrap
compiler's own perspective everything looked fine. This is
exactly the class of bug that future `unknown char` lexer
diagnostics (Phase 7.10+) should surface loudly.

Next tick: **Phase 7.9h** — `Bool` as a proper type +
`true` / `false` literals. The HM layer currently treats
`Bool` as just a string tag (`TyName "Bool"`) and nothing
consumes it — comparisons return `TyName "Bool"` that never
gets checked against anything. Adding real `Bool` support
unlocks `checkIfBranches` narrowing on comparison guards,
enables `&&` / `||` connectives in Phase 7.9i+, and lets
fns declare explicit `Bool` parameters and return types.

### 2026-04 — Phase 7.9h: true/false literals (DONE)

Eighth tick of **Phase 7.9**. Probing with
`fn flag() Int = if true == false then 1 else 0` surfaced
the next gap: the bootstrap compiler's lexer already
produces `TLower "true"` / `TLower "false"`, `parseAtom`
already wraps them in `EVar "true"` / `EVar "false"`,
`emitExpr`'s `EVar x -> x` arm already emits them as valid
F# boolean literals, and `inferExprType`'s fall-through
returns `TyVar "?"` for unknown names (which `typeEq`
short-circuits on) — so the ENTIRE pipeline was already
wired to handle `true` / `false`. The only gap was the
elaborator's name-scope check: `"true"` / `"false"` were
not in `stdlibNames` (the seed env introduced in 7.9e), so
`checkExpr`'s `EVar name` arm fired `E002 UnboundVar true`
/ `E002 UnboundVar false` before the expression could
reach codegen. Blocks any input program that uses bare
`true` / `false`, including the bootstrap compiler's own
source (`isUpperChar`, `isLowerChar`, `isIdStart` all
return `true` / `false` internally).

**Changes:**

* Two new strings appended to `stdlibNames` in
  `20-bootstrap-compiler.lll`: `"true"` and `"false"`.
  One-line edit, zero lexer / parser / AST / HM / codegen
  changes needed — the other layers were already cleared
  for boolean literal names by the existing `EVar`
  pipeline. `EVar "true"` emits `true` (a valid F#
  boolean literal) via the `EVar x -> x` arm.
* New fixture `20f-bootstrap-input-bool-lits.lll`
  exercises both literals via
  `fn flag() Int = if true == false then 1 else 0`
  alongside a `choose` fn that smoke-tests the 7.9g `>`
  operator under the expanded stdlib env.
* New regression test in `BootstrapCompilerTests.fs` swaps
  the 20a input for 20f, runs the bootstrap compiler, and
  asserts (a) no `E002` / no `E001` / no `error` in
  combined output, (b) emitted F# contains `let rec
  choose` + `and flag` + `let main` + `[<EntryPoint>]` +
  `true` + `false`. The mutual-recursion spine is the
  same incidental shape as 7.9g — any run of two or more
  consecutive non-`main` fn decls gets wrapped in a
  single `let rec ... and ...` block by the 7.8e grouping
  pass.

**Deliberately out of scope:**

* **Proper `Bool` type promotion in HM** — this is a
  NAME-SCOPE-ONLY fix. `true` and `false` are treated as
  ordinary stdlib names; neither has a `TyBinding` in the
  `TypeEnv`, so `inferExprType` still returns `TyVar "?"`
  for `EVar "true"` / `EVar "false"` and `typeEq`
  silently accepts any comparison against them. A program
  like `if 1 then 0 else 1` (int guard where a Bool is
  expected) still parses and emits without complaint.
  Promoting `Bool` to a proper type in HM/typeCheck stays
  in Phase 7.9i — the groundwork is already in
  `inferExprType` (comparisons `EEq` / `ELt` / `EGt`
  already return `TyName "Bool"`), but nothing consumes
  that result yet.
* **Dedicated `EBool` Expr variant** — no new AST variant
  in this slice. `true` / `false` stay as `EVar "true"` /
  `EVar "false"`. A proper `EBool Bool` variant (plus
  `TKwTrue` / `TKwFalse` keyword tokens) is cleaner but
  requires touching the AST, parser, elaborator, HM, and
  codegen — deferred to 7.9i as part of the Bool-type
  promotion work.
* **Boolean connectives** — `&&` / `||` / `not` stay in
  Phase 7.9i+.
* **`!=` / `<=` / `>=`** — these still need their own
  lexer helpers (same shape as `lexEqOrEqEq` from 7.9f)
  plus new `Expr` variants; trivial extensions, not in
  scope here.

Tests: 406 → 407 (+1 for the `true` / `false` regression
test).

**Observation worth noting** — smallest slice in Phase
7.9 so far: a single two-element addition to one list
literal (`stdlibNames`), no other source changes. The
rest of the pipeline was already cleared for boolean
literal names as a side-effect of the `EVar x -> x`
pass-through shape of `emitExpr` and the `TyVar "?"`
fall-through in `inferExprType`. A testament to how the
bootstrap compiler's narrow scope and "punt on unknowns"
shape composes — most of the pipeline simply doesn't
care what the name resolves to.

Separate parser bug discovered in diagnosis: the
bootstrap `parseLetIn` doesn't tolerate a `TNewline`
between `in` and the body expression, so multi-line
let-in chains like
```
let cond = expr in
  body
```
silently mis-parse (the body becomes `EInt 0` and the
real body tokens flow back to `parseDecls`, producing
spurious `E002` errors on fn parameters). All existing
fixtures are single-line, which is why it hadn't surfaced
before. Not in scope for 7.9h — fixture was adapted to
single-line form instead. Logged as a separate Phase
7.10+ cleanup candidate.

Next tick: **Phase 7.9i** — basic char literal (`'c'`)
support. Bool type promotion moves to Phase 7.9k; char
literals are much higher impact because the bootstrap
compiler's own source uses `'='` / `'"'` / `'('` / `')'`
/ `'|'` / `'.'` / `':'` / `'_'` and dozens of other single
chars in `lexChars` — without char literal support, the
bootstrap compiler can't parse a single real ll-lang
source file.

### 2026-04 — Phase 7.9i: char literals (DONE)

Ninth tick of **Phase 7.9**. Pivoted from the originally
planned "Bool type promotion in HM" slot because char
literals are strictly higher impact: the bootstrap
compiler's own source uses single-char literals (`'='`,
`'"'`, `'('`, `')'`, `'['`, `']'`, `'|'`, `'.'`, `'\\'`,
`'_'`, `':'`, `'+'`, `'-'`, `'*'`, `'/'`, `'>'`, `'<'`,
`'\n'`) literally dozens of times in `lexChars` and
friends. Without char literals, the bootstrap compiler
silently drops `'` as an unknown char (falls through to
the catch-all `else lexChars rest` in `lexChars`) and
mangles any source that uses them. Bool type promotion
just tightens an existing feature; char literals unblock
a primitive that every non-trivial lexer needs.

**Changes:**

* New lexer token variant `TChar Char` in `type Token`,
  placed next to `TInt Int` / `TStr Str`.
* New lexer helper `fn lexCharLit(cs List[Char]) List[Token]`
  consumes one content char, expects the closing `'`, and
  emits `TChar ch`. If the closing quote is missing
  (unterminated literal), silently falls through and
  re-lexes from the saved position — same leniency as the
  catch-all `else lexChars rest` arm in `lexChars`.
* New arm in `lexChars`:
  `else if c == '\'' then lexCharLit rest` — placed right
  after the `'"'` / `lexStr` arm to mirror the string
  literal shape.
* New AST variant `EChar Char` in `type Expr`, placed next
  to `EInt Int` / `EStr Str`.
* New arm `TChar c :: rest -> (EChar c, rest)` in
  `parseAtom`, as a sibling of the `TInt n :: rest` and
  `TStr s :: rest` arms.
* New `TChar _ -> true` arm in `isAtomStart` so char
  literals can start an atom at juxtaposition position
  (`f 'x'`).
* New `EChar _ -> []` noop arms in `checkExpr` (name
  resolution) and `typeCheck` (exhaustive walker), plus
  `EChar _ -> TyName "Char"` in `inferExprType`.
* New `EChar c -> strConcat (strConcat "'" (strFromChars [c])) "'"`
  arms in `showExpr` and `emitExpr`. F# accepts `'c'`
  directly for `char`, so the emitted source is valid F#
  verbatim. Uses `strFromChars [c]` since the bootstrap
  compiler has no `strFromChar` helper — a single-element
  char list through `strFromChars` is the equivalent.
* New fixture `20g-bootstrap-input-char-lit.lll` exercises
  `'='` in a conditional:
  `fn sym(n Int) Int = if '=' == '=' then n else 0`
  alongside a `main` that calls `sym 5`.
* New regression test in `BootstrapCompilerTests.fs`
  swaps the 20a input for 20g, runs the bootstrap
  compiler, and asserts (a) no `E002` / `E001` / `error`
  in combined output, (b) emitted F# contains `let sym`
  or `let rec sym`, `let main`, `[<EntryPoint>]`, and the
  `'='` char literal substring. The test is the same
  swap/restore pattern as the 7.9h bool literal test.

**Deliberately out of scope:**

* **Escape sequences** (`'\n'`, `'\\'`, `'\''`) deferred
  to Phase 7.9j. Escape handling adds a lexer state
  machine (recognise the leading `\`, consume the next
  char as the escape marker, decode `n` / `\\` / `'`
  back to the literal char) which doubles the slice size.
  This 7.9i slice is enough to prove the pipeline works
  end-to-end for a new primitive literal; escapes are
  next.
* **Unicode** (non-ASCII chars) deferred beyond 7.9j. The
  `Char` type in ll-lang is currently single-byte ASCII
  and the F# host compiler's char codegen doesn't
  distinguish ASCII from UTF-8 yet — proper unicode is a
  much later phase.
* **Unterminated char lit diagnostic** — currently the
  lexer silently skips a lone `'` or a malformed `'c`
  (no closing quote). Matches the leniency of every
  other `else lexChars rest` arm in the bootstrap lexer.
  A proper `E010 UnterminatedCharLit` diagnostic is a
  later phase when the bootstrap lexer grows a proper
  diagnostic channel.
* **Multi-char char lits** — `'ab'` stays a silent skip
  (neither the host nor the bootstrap accepts it).
* **`Char` type as a proper HM type operand** — `EChar`
  infers to `TyName "Char"` in `inferExprType`, but no
  arm in `typeCheck` actually consumes a `Char` type
  (there are no char-specific arithmetic / comparison
  ops in the bootstrap grammar — `==` just checks
  reference equality through the punted `EEq` arm).
  Char-typed mismatches stay silent for now.
* **Bool type promotion in HM** — moved from the
  originally planned 7.9i slot to Phase 7.9k.

Tests: 407 → 408 (+1 for the char literal regression
test).

**Observation worth noting** — this slice is the first
time the bootstrap compiler has added a genuinely new
**primitive literal** (vs 7.9f/g/h which added operators
or promoted existing identifiers). The pattern is the
same as 7.9f's `EEq` operator slice — nine change sites
across lexer, AST, parser, elaborator, HM, and codegen —
but the lexer change is more interesting: a new multi-
char token (opening `'`, content, closing `'`) that
doesn't fit the single-char symbol shape of any earlier
bootstrap token. `lexCharLit` handles the tiny state
machine as a plain recursive helper, same shape as
`takeStrBody` / `lexStr` but simpler (fixed-length: one
content char, one closing quote).

Next tick: **Phase 7.9j** — char escape sequences
(`'\n'`, `'\\'`, `'\''`), needed to lex the bootstrap
compiler's own source which uses `'\n'` on line 452 of
the `lexChars` newline arm. The shape is a lexer state-
machine extension to `lexCharLit`: peek the first content
char, if it's `\` then consume and decode the next char
(n → `'\n'`, `\\` → `'\\'`, `'` → `'\''`), otherwise use
the raw char as today. Roughly doubles the size of
`lexCharLit` and adds a small escape-decoding helper.

### 2026-04 — Phase 7.9j: char escape sequences (DONE)

Tenth tick of **Phase 7.9**. Direct follow-up to 7.9i's
basic char literal (`'c'`) slice: the bootstrap
compiler's own source `20-bootstrap-compiler.lll` uses
`'\n'` on the `lexChars` newline arm and `'\\'` on the
backslash arm, so without escape handling the bootstrap
compiler cannot lex its own source — a hard blocker for
the self-host fixpoint. This slice extends `lexCharLit`
to recognise a leading `\` and dispatch to an escape
decoder that maps the four needed escapes (`\n`, `\t`,
`\\`, `\'`) back to their raw char values.

**Changes:**

* New helper `fn decodeEscape(c Char) Char` maps the four
  escape content chars to their raw char values: `n` →
  `'\n'`, `t` → `'\t'`, `\\` → `'\\'`, `'` → `'\''`.
  Unknown escapes pass through raw (lenient — mirrors the
  catch-all leniency elsewhere in `lexChars`).
* New helper `fn lexCharEsc(cs List[Char]) List[Token]`
  consumes one escape content char after `\`, expects the
  closing `'`, and emits `TChar (decodeEscape esc)`.
  Missing closing quote silently falls through, matching
  the leniency of `lexCharLit`.
* `lexCharLit` extended: after binding the first content
  char `ch`, checks `if ch == '\\' then lexCharEsc rest`
  before the plain-char path. The plain-char path is
  unchanged.
* New helper `fn emitChar(c Char) Str` re-encodes a `Char`
  as a valid F# char literal by inverting the escape
  mapping: `'\n'` → `"'\\n'"`, `'\t'` → `"'\\t'"`, `'\\'`
  → `"'\\\\'"`, `'\''` → `"'\\''"`, and everything else
  wrapped verbatim as before. Without this, 7.9i's raw
  `'<c>'` emission would inject a literal newline /
  backslash into the generated F# source and break the
  host compile.
* `emitExpr`'s `EChar c` arm now delegates to `emitChar c`
  instead of the inline `strConcat` wrap.
* New fixture `20h-bootstrap-input-char-esc.lll` exercises
  `'\n'` and `'\\'` in two conditionals:
  `fn nl(n Int) Int = if '\n' == '\n' then n else 0` and
  `fn bs(n Int) Int = if '\\' == '\\' then n else 0`
  plus a `main` that calls `nl 5`.
* New regression test in `BootstrapCompilerTests.fs`
  swaps the 20a input for 20h, runs the bootstrap
  compiler, and asserts (a) no `E002` / `E001` / `error`,
  (b) emitted F# contains `let nl`/`let rec nl`, `and bs`
  or `let bs`, `and main` or `let main`, `[<EntryPoint>]`,
  and the `'\n'` + `'\\'` char literal substrings. Same
  swap/restore pattern as 7.9h / 7.9i.

**Deliberately out of scope:**

* **`\r` / `\0` / `\u....`** — only the four escapes
  actually used in the bootstrap compiler's own source
  (`\n`, `\t`, `\\`, `\'`) are supported. Adding the
  others is a one-line extension to `decodeEscape` when
  the bootstrap or fixtures grow a need.
* **String-literal escapes** — `lexStr` / `takeStrBody`
  still copy string bodies verbatim, so `"foo\nbar"`
  stays a raw 4-char-then-newline-then-3-char sequence.
  No bootstrap source currently needs string escapes; a
  separate slice can extend `takeStrBody` symmetrically
  later.
* **Unterminated escape diagnostic** — `'\` with no
  follow-up char or `'\n` with no closing quote silently
  drops, matching the leniency of plain `lexCharLit`.
* **Unicode-aware escapes** — `Char` stays single-byte
  ASCII. Proper unicode is deferred beyond the self-host
  fixpoint.

Tests: 408 → 409 (+1 for the char escape regression
test).

**Observation worth noting** — the GREEN step turned
out to require a *second* change site beyond the lexer:
`emitExpr`'s `EChar c` arm had been emitting the raw
char wrapped in single quotes, which worked fine for
printable ASCII (7.9i's `'='` test) but silently injects
a literal newline / backslash into the generated F#
source when the char IS newline / backslash. Without the
new `emitChar` helper re-escaping on emission, the
generated F# is syntactically invalid
(`'<literal newline>'` is not a char literal in F#).
This is the mirror image of the lexer's `decodeEscape` —
the round trip `source → token → AST → emit` must
preserve the escape encoding at the boundaries. Good
reminder that new primitive literals aren't done after
just the lexer extension; the emitter has to symmetric-
ally re-escape whenever it crosses back into source
form.

Next tick: **Phase 7.9l** — constructor patterns in
match arms. Without `PCon` the bootstrap compiler
silently turns every `| Some n ->` / `| None ->` arm
into a wildcard, blocking any program that match-
destructures a tagged union. Direct blocker for self-
compiling any lexer / parser / elaborator written in
ll-lang itself, every one of which pattern-matches over
sum types.

### 2026-04 — Phase 7.9l: constructor patterns in match arms (DONE)

Eleventh tick of **Phase 7.9**. Before this slice, the
bootstrap compiler's `parsePrimaryPat` had no
`TUpper`-headed arm, so constructor patterns like
`| Some n -> ...` / `| None -> ...` / `| Cons h t -> ...`
silently fell through to the `PWild` catch-all, losing
the constructor name entirely. The emitted F# turned
every ctor arm into a wildcard — visible in the fixture
as `let unwrap m = 0L` (the match collapsed to its
implicit default branch). This blocks compilation of
any program that match-destructures a tagged union and
is therefore a direct blocker for self-compiling any
lexer / parser / elaborator written in ll-lang itself.

**Changes:**

* New AST variant `PCon Str List[Pat]` in `type Pat`,
  holding the constructor name plus a list of argument
  sub-patterns.
* New parser arm `| TUpper name :: rest -> parseCtorArgs
  name rest` in `parsePrimaryPat`, before the catch-all.
* New helper `fn parseCtorArgs(name Str)(toks) =
  let (args, rest) = parsePatArgs toks in (PCon name
  args, rest)`.
* New helper `fn parsePatArgs(toks)` — eagerly consumes
  a sequence of atomic sub-patterns (`TLower _` /
  `TUnder` / `TInt _` / `TLBrack TRBrack`) until the
  next token doesn't start a pattern (like `TArrow` /
  `TBar` / `TColonColon` / `TRParen`). Deliberately
  does NOT recurse into nested `TUpper` ctors without
  parentheses — nested `Cons (Some 1) None` requires
  parens around `Some 1`.
* `showPat` / `emitPat` extended with a `PCon name args`
  arm rendering `(Name arg1 arg2)` (F# DU pattern shape,
  parens unconditional, space before each arg). New
  helper `showPatArgs` / `emitPatArgs` walks the arg
  list.
* `patBinders` extended with `PCon _ args` recursing
  via new `patBindersList` helper.
* `patIsCatchAll` extended with `PCon _ _ -> false`
  (a ctor pattern matches only its own tag).
* `coveredCtors` — previously hardwired to `[]` with a
  comment noting `Pat` had no `PCon` — now walks the
  arm pattern list and collects the name from every
  `PCon`, enabling real exhaustiveness feedback.
* New fixture `20i-bootstrap-input-ctor-pat.lll`
  exercises both `Some n` (ctor with one arg binder)
  and `None` (nullary ctor) in
  `fn unwrap(m Maybe[Int]) Int = match m with | Some n
  -> n | None -> 0` plus a `fn main() Int = unwrap
  (Some 5)`. Single-line match (all arms on one line)
  because the bootstrap's `parseArms` doesn't yet skip
  `TNewline` between arms — multi-line match is a
  separate concern tracked below.
* New regression test in `BootstrapCompilerTests.fs`
  swaps 20a → 20i, runs the bootstrap, asserts no
  `E002` / `E001` / `error`, and checks the emitted F#
  contains `let unwrap` (or `let rec unwrap`),
  `[<EntryPoint>]`, `| (Some n) ->`, and `| (None) ->`.

**Deliberately out of scope:**

* **Nested ctor patterns without parens** — the greedy
  `parsePatArgs` stops on `TUpper`, so `Cons (Some 1)
  None` needs parens around `Some 1`. The bootstrap
  doesn't need unparenthesised nested ctors today.
* **Multi-line match arms** — `parseArms` still peeks
  for `TBar :: _` without skipping intervening
  `TNewline`, so the fixture uses a single-line match
  body. Extending `parseArms` to skip newlines would be
  a clean follow-up but is not required for this slice.
* **Full exhaustiveness feedback for nested ctors** —
  `coveredCtors` only collects the head ctor name, not
  the sub-patterns. Good enough for flat sum types.
* **Constructor type unification in HM** — the minimal
  HM in the bootstrap doesn't know that `PCon "Some" n`
  binds `n` at the `Maybe`'s type-arg instantiation.
  `n` just becomes a fresh var and the body type
  carries it, so round-trip emission works without the
  type-system machinery.

Tests: 409 → 410 (+1 for the constructor-pattern
regression test).

**Surprises:**

1. The first RED run came out to exactly the shape the
   test expected — `| Some n ->` fell through to
   `PWild`, the arm body became bogus, and `main`
   silently dropped because the `| _ ->` in the match
   body had a TNewline right before the next `| None`
   arm, which `parseArms` rejected (no `TBar :: _`
   match once the newline got there from the preceding
   arm body's parse). Net effect: `let unwrap m = 0L`
   with no match at all and no `main`. Clean proof
   that the parser loses everything when PCon is
   missing.
2. GREEN uncovered a second change site: `coveredCtors`
   was hard-wired to `[]` with an explicit comment
   saying "no PCon, so no covered ctors". Adding `PCon`
   required replacing that stub with a real walker,
   otherwise the match-exhaustiveness check fires
   `E003 NonExhaustiveMatch Maybe missing Some` /
   `missing None` for a fully-exhaustive match.
3. Multi-line match arms don't parse in the bootstrap.
   `parseArms` has `| TBar :: _` without skipping
   intervening `TNewline`, so the fixture had to use a
   single-line match. Host compiler accepts both
   shapes. Follow-up: extend `parseArms` with a leading
   `skipNewlines`.

Next tick: **Phase 7.9k** — Bool type promotion in HM /
`typeCheck`. Deferred from the original 7.9i slot and
still pending. Promotes `Bool` from an inference-only
phantom type to a real consumed type, unblocking
rejection of non-`Bool` guards (`if 1 then ... else ...`)
and enforcement of `Int`-only comparison operands.

### Phase 7.9k (planned): Bool type promotion in HM

Deferred from the original 7.9i slot. Currently
`inferExprType` returns `TyName "Bool"` from `EEq` /
`ELt` / `EGt` but nothing consumes it; promoting `Bool`
means `typeCheck` can reject `if 1 then 0 else 1`
(non-Bool guard) and comparisons must refuse non-`Int`
args. Unblocks `EBool` as a dedicated AST variant,
`TKwTrue` / `TKwFalse` keyword tokens, and the `&&` /
`||` / `not` connectives that need Bool operands.

### 2026-04 — Phase 7.9.newlines: layout-tolerant parsers (DONE)

Twelfth tick of **Phase 7.9**. Bundles two sibling
bugs — Neftedollar/ll-lang#8 (`parseLetIn` newline
intolerance) and Neftedollar/ll-lang#12 (`parseArms`
newline intolerance) — into a single GREEN commit
because the root cause is identical: a parser peeks at
the next token without first calling `skipNewlines`, so
any intervening `TNewline` silently mis-parses.

Before this slice, every bootstrap fixture had to cram
match bodies and let-in chains onto a single line (see
the explicit "multi-line match arms" caveat in the
7.9l section above). That cramping is a direct blocker
for any fixture that exercises real-world ll-lang
layout — including every future self-hosting
milestone where the bootstrap eats one of the host
modules verbatim.

**Bugs closed:**

* **#8 — `parseLetIn`**: a multi-line `let x = rhs
  in\n  body` fell into the wildcard arm of the
  helper's body parse because `parseExpr` was called
  on `TNewline :: ...`, whose `parseCompare` cascade
  bottomed out at `parseAtom`'s `_ -> (EInt 0, toks)`
  default, returning a bogus `EInt 0` and leaving the
  whole body unconsumed. The decl that followed the
  orphaned `in body` then got re-parsed as if it were
  the body, corrupting the module-level decl list.
* **#12 — `parseArms`**: a multi-line match
  `match m with\n  | Some n -> n\n  | None -> 0`
  silently dropped every arm after the first.
  `parseArm`'s body parse stopped cleanly at the
  trailing `TNewline`, but then `parseArms` recursed
  on `TNewline :: TBar :: _`, which fell through the
  `| TBar :: _` guard into the `_ -> (([], []), toks)`
  terminator. Downstream, the leftover `| None ->
  fallback` got re-parsed as a fresh module-level
  construct and either desynced the decl list or got
  silently discarded along with `main`.

**Root cause:** identical in both. Any parser that
peeks at `TTok :: _` needs to `skipNewlines` the token
list first — the lexer emits `TNewline` as a
first-class separator and the parser needs to be
tolerant of whitespace between tokens that can legally
appear at layout boundaries.

**Changes (all in `20-bootstrap-compiler.lll`):**

* `parseLetIn` now calls `skipNewlines` twice — once
  after `TEq` (before parsing the RHS), once after
  `TKwIn` (before parsing the body). Both handle the
  multi-line shapes `let x =\n  rhs in ...` and
  `let x = rhs in\n  body`.
* `parseArms` now calls `skipNewlines` at the very
  top and peeks at `toks2` instead of the raw `toks`.
  Consumed leading newlines are safe to drop — they
  only appear between arms or at a decl boundary, and
  the module-level `parseDecls` always re-skips
  newlines before every decl.
* `parseFnDecl` calls `skipNewlines` after `TEq`
  before `parseExpr` on the body — the fixture puts
  the fn body on a fresh line, so the body parse
  would otherwise hit `TNewline` immediately.
* `parseLetDecl` calls `skipNewlines` after `TEq` for
  symmetry with `parseFnDecl`. Module-level `let x =
  \n  expr` decls can now span multiple lines the same
  way fn decls already do.

**Fixture:** `20j-bootstrap-input-layout.lll` exercises
BOTH bugs in a single module:

```
module Examples.Clean
type Maybe A = Some A | None
fn describe(m Maybe[Int]) Int =
  let fallback = 0 in
  match m with
    | Some n -> n
    | None -> fallback
fn main() Int = describe (Some 5)
```

The `let fallback = 0 in` + newline + `match m with`
shape exercises `parseLetIn`'s tolerance after `in`.
The multi-line match arms exercise `parseArms`'s
tolerance between arms. The `fn describe(...) Int =\n
  let ...` shape exercises `parseFnDecl`'s tolerance
after `TEq`.

**Test:** new `[<Fact>]` in
`BootstrapCompilerTests.fs` swaps 20a → 20j, runs the
bootstrap via `runBootstrap ()`, asserts no `E002` /
`E001` / `error`, and checks the emitted F# contains
`let describe` (or `let rec describe`), `let main`
(or `and main`), `[<EntryPoint>]`, `| (Some n) ->`
(proving the first arm survived), `| (None) ->`
(proving the second arm survived), and `fallback`
(proving the let-in body made it through).

**RED shape:** the initial failing run emitted
`let describe m = 0L` with a `0L` body (parseLetIn
returned the default `EInt 0`) and `let fallback = 0L`
as a stray orphan module-level decl (the inner `let
fallback = 0 in` got re-parsed as a module-level let
because the fn body had been truncated). No `main`
decl at all. Classic cascade from a desynced parser.

**Deliberately out of scope:**

* **Full layout sensitivity in every parser** — only
  the specific sites needed to round-trip the new
  fixture got `skipNewlines` calls. `parseIf`,
  `parseMatch`'s scrutinee, lambda bodies, and
  parenthesised expressions still require their
  sub-expressions to start on the same line as their
  surrounding keyword. Follow-up if future fixtures
  hit this.
* **Leading newline in `parseExpr`** — a tempting
  one-line fix at the top of `parseExpr` was rejected
  to keep the change strictly additive: adding
  `skipNewlines` to `parseExpr` could subtly change
  the semantics of `parseCompareTail`, which stops at
  newlines as a deliberate arm-body terminator. The
  per-site fixes are narrower and easier to reason
  about.
* **String literal escape sequences** — tracked as
  Phase 7.9m via issue #10, covering `\n` / `\t` /
  `\\` / `\"` inside string literals (currently the
  bootstrap's `lexStr` stops at the first `\` and
  emits a broken token).

Tests: 410 → 411 (+1 for the bundled layout
regression test).

**Surprises:**

1. The initial fix scoped only to `parseLetIn` and
   `parseArms` wasn't sufficient — the fn body itself
   started with a `TNewline` after the `TEq`, so
   `parseExpr` on the body hit its `parseCompare`
   default cascade and returned `EInt 0` before ever
   reaching the `TKwLet :: rest -> parseLetIn rest`
   arm. The minimal fix required a third
   `skipNewlines` call inside `parseFnDecl` (and, for
   symmetry, `parseLetDecl`). The fixes are strictly
   additive, so no existing single-line fixtures
   regressed — 410 → 411 with no tests changing
   status except the new one.
2. The `parseArmBody` parser already tolerates the
   arm-ending `TNewline` correctly — it cascades
   through `parseCompare` → `parseAddSub` → `parseApp`
   → `parseAtom`, each of which stops cleanly on any
   non-operator / non-atom-starter token. The only
   site that needed fixing was `parseArms` at the
   recursion boundary between arms.
3. No regression in any of the prior 7.9f/g/h/i/j/l
   single-line fixtures — the `skipNewlines` calls
   are no-ops when no `TNewline` is present.

Next tick: **Phase 7.9m** — string literal escape
sequences, per issue #10. The bootstrap's `lexStr`
currently has no escape handling, so a literal `\n`
inside a string breaks the tokenizer. Unblocks richer
error messages (which need embedded newlines) and any
fixture that stringifies ll-lang source with
backslashes. Bool type promotion (Phase 7.9k, still
pending) remains deferred.

### 2026-04 — Phase 7.9m: string literal escapes (DONE)

Thirteenth tick of **Phase 7.9**. Direct follow-up to
7.9j's char literal escape slice, which explicitly
deferred the string-literal analogue. Before this
slice, the bootstrap compiler's `takeStrBody` copied
string bodies verbatim until the first unescaped `"`,
so `"say \"hi\""` terminated at the first `\"`
leaving `hi\"\n"` as a mangled tail that the outer
lexer re-tokenised as stray identifiers + operators.
And even if the lexer had decoded escapes, `emitStr`
was just `strConcat "\"" (strConcat s "\"")` — it
injected the raw decoded bytes (e.g. a literal
newline) directly into the emitted F# source,
breaking the host compile. Both sites needed to move
in lockstep. Hard blocker for the self-host fixpoint
because the bootstrap's own source composes output
via `strConcat` calls that contain `"\n"` / `" in "`
/ `"let "` fragments with embedded escapes.

**Changes:**

* `takeStrBody` extended: detects `\` inside the
  string body and delegates to a new helper
  `takeStrBodyEsc`, which consumes the next char,
  decodes it via `decodeEscape` (reused from 7.9j),
  and recurses back into `takeStrBody` to keep
  collecting. The plain-char path and the closing
  `"` termination are unchanged.
* `decodeEscape` extended with one new arm:
  `else if c == '"' then '"'`, closing the fourth
  escape needed for string literals. The other four
  cases (`n`, `t`, `\\`, `'`) are reused unchanged
  from 7.9j's char-escape slice.
* New helper `fn encodeStrEscape(c Char) Str` mirrors
  7.9j's `emitChar` but for string-context escape
  set (uses `\"` instead of `\'`): `'\n'` → `"\\n"`,
  `'\t'` → `"\\t"`, `'\\'` → `"\\\\"`, `'"'` →
  `"\\\""`, everything else wrapped verbatim via
  `strFromChars`.
* New helper `fn emitStrBody(cs List[Char]) Str`
  walks a decoded char list via `listFold` and
  concatenates each char's `encodeStrEscape`
  encoding. Same `listFold` pattern as `showParams`,
  `showCtors`, etc.
* `emitStr` rewritten to route through `emitStrBody`:
  `strConcat "\"" (strConcat (emitStrBody (strChars
  s)) "\"")`. Before this slice it was a raw verbatim
  wrap; the 7.9b comment admitting "a real
  implementation would escape `\"` / `\\` / `\n`" is
  now redundant and was replaced with the 7.9m
  round-trip note.
* New fixture `20k-bootstrap-input-str-esc.lll`
  exercises all three critical escapes in one
  literal: `fn greet() Str = "say \"hi\"\n"`. The
  escaped quotes force the lexer's escape path (else
  the string terminates early → `E002 UnboundVar hi`)
  and the `\n` forces the emitter's re-encoding path
  (else a raw newline gets injected mid-literal).
* New regression test in `BootstrapCompilerTests.fs`
  swaps the 20a input for 20k, runs the bootstrap
  compiler, and asserts (a) no `E002` / `E001` /
  `error`, (b) emitted F# contains `let greet` /
  `let rec greet`, `[<EntryPoint>]`, and the literal
  substring `"say \"hi\"\n"`. Same swap/restore
  pattern as 7.9h / 7.9i / 7.9j.
* `showExpr`'s `EStr s` arm (debug pretty-printer)
  was left unchanged — it's dead code at runtime
  (`main` calls `emitModule`, not `showModule`), and
  updating it would expand the diff without
  functional benefit.

**Deliberately out of scope:**

* **`\r` / `\0` / `\u....`** — only the four escapes
  actually used in bootstrap source strings (`\n`,
  `\t`, `\\`, `\"`) are supported. Adding the others
  is a one-line extension to `decodeEscape` +
  `encodeStrEscape` when the bootstrap or fixtures
  grow a need.
* **Multi-line raw strings** — no `"""..."""` raw
  literal support; the only mechanism for embedding a
  newline is the `\n` escape.
* **Unterminated string diagnostic** — an unclosed
  `"` still silently drops off the end of the
  stream, matching the leniency of the rest of the
  lexer.
* **`showStr` symmetric update** — the 20-file's
  `showExpr` pretty-printer has an `EStr s ->
  strConcat (strConcat "\"" s) "\""` arm that now
  suffers the same raw-injection bug as pre-7.9m
  `emitStr`. Deliberately skipped because `showExpr`
  is never called at runtime (dead code kept for
  reference alongside `showDecl` / `showModule`).
  Would be a one-liner to fix if the debug printer
  path is ever reintroduced.

Tests: 411 → 412 (+1 for the string escape
regression test).

**Surprise:** the initial RED fixture used
`"hello\nworld"`, and the test still **passed**
against the unfixed bootstrap. Reason: the
unfixed `takeStrBody` was lenient enough to pass the
raw `\` + `n` pair through verbatim, and the unfixed
`emitStr` then wrapped it in quotes, so the F# output
literally contained `"hello\nworld"` — the exact
string the test was asserting on. The assertion was
true for the wrong reason. The fix required a
fixture that uses an escaped `"` (which the unfixed
lexer can't handle — it terminates the string at the
first unescaped `"` and re-lexes the rest as garbage
identifiers). `"say \"hi\"\n"` produces `E002
UnboundVar hi` pre-fix and round-trips cleanly
post-fix. Good reminder that a RED fixture has to
actually trip a **behavioural** difference between
pre- and post-fix, not just be syntactically
distinct.

Next tick: **Phase 7.9n** — unknown-char lexer
diagnostic, per issue #7. The bootstrap's `lexChars`
catch-all arm silently drops any char it doesn't
recognise, so typos like `let x = @y` or `(x + $)`
produce no diagnostic and parse as though the offending
char weren't there. This is the last of the known
lexer leniency bugs worth fixing before the self-host
fixpoint (Phase 7.10). Bool type promotion (Phase
7.9k, issue #6) remains deferred — it's an inference
pass change, not a lexer/parser/codegen slice, and
fits better into the Phase 7.10 fixpoint pass.

### 2026-04 — Phase 7.9n: line comments (DONE)

Fourteenth tick of **Phase 7.9**. The 7.9n slot
originally scoped the unknown-char lexer diagnostic
(#7), but a fresh fixpoint probe flipped priority:
running the bootstrap compiler on its own source
produced an empty output (just module header +
prelude) because a single `--` line comment desynced
`parseDecls` and silently dropped every subsequent
decl. The bootstrap's own source begins with ~190
lines of `-- header comment` before the first real
`fn` — so without line-comment support, the fixpoint
pass (Phase 7.10) couldn't even read the first decl.
Issue #13 was filed and routed into this slot.

**The proof was a 3-line fixture:**

```
module Examples.Clean
-- this is a comment
fn main() Int = 42
```

Pre-fix, `lllc run` on this file emitted 465 bytes
of F# (module header + prelude + nothing). Post-fix,
it emits the `main` fn plus `[<EntryPoint>]` wrapper.
The lexer was hitting the `-` arm, calling
`lexMinusOrArrow`, emitting a floating `TMinus`, and
then re-entering `lexChars` on the comment body as
though it were real tokens. The first char after
`--` varies (letter → identifier, digit → int lit,
etc.), so the desync was silent but total.

**Changes:**

* New top-level fn `skipLineComment(cs List[Char])
  List[Token]` in 20-bootstrap-compiler.lll, placed
  immediately before `lexMinusOrArrow`. Walks the
  remaining char list, discarding every char until
  it hits `\n`. When it finds the newline it emits
  a `TNewline` (preserving the decl-terminator
  semantics that `parseDecls` relies on) and
  recurses into `lexChars rest`. EOF inside a
  comment emits `[TEnd]`, matching the rest of the
  lexer's lenient EOF handling.
* `lexMinusOrArrow` extended with one new arm: if
  the char after `-` is also `-`, hand off to
  `skipLineComment rest`. The existing `-> vs -`
  branch and the EOF tail are unchanged. No changes
  to `lexChars` itself — all the wiring happens
  inside the `-` helper.
* New fixture `20l-bootstrap-input-comments.lll`
  exercises three comment positions in one file:
  a full-line comment immediately after the module
  header, a trailing comment after a decl body
  (`fn greet() Str = "hi"  -- trailing comment`),
  and another full-line comment between the two
  decls. All three must be skipped for the `greet`
  and `main` decls to round-trip cleanly.
* New regression test in `BootstrapCompilerTests.fs`
  follows the same 20a-backup / copy-fixture /
  restore pattern as 7.9h–7.9m. Asserts (a) no
  `E002` / `E001` / `error` in output, (b) emitted
  F# contains `let greet` or `let rec greet`,
  (c) contains `let main` or `and main`,
  (d) contains `[<EntryPoint>]`, (e) contains the
  literal `"hi"` substring — proving the `greet`
  body survived past the trailing comment.

**Deliberately out of scope:**

* **Block comments `{- -}`** — not used anywhere in
  the bootstrap source or corpus. Nested-comment
  tracking would roughly double the comment-handling
  surface area for zero current benefit. Defer until
  a fixture or the bootstrap itself grows a need.
* **Doc-comment markers (`---`, `|||`, etc.)** —
  with a `--` line comment, a third `-` is just a
  regular char inside the comment body. No structured
  doc-comment extraction.
* **Comment tokens in the AST** — comments vanish
  during lexing. The parser never sees them and the
  codegen can't round-trip them into the emitted F#.
  Formatters that need to preserve comments would
  need a `TLineComment Str` token and a different
  parser strategy; out of scope for bootstrap.
* **Unknown-char lexer diagnostic (#7)** — the
  original 7.9n scope, now pushed out to 7.9o or
  later. Strictly a diagnostic-quality fix, not a
  fixpoint blocker.

Tests: 412 → 413 (+1 for the line-comment regression
test).

**Fixpoint-probe confirmation:** post-fix, swapping
`20a-bootstrap-input.lll` for the bootstrap compiler's
own source and running `lllc run` produces 531 bytes
of emitted F# — the prelude plus a `type Maybe<'A>`
declaration plus the start of `type Token`, which is
where the *next* blocker surfaces (an unknown/
placeholder ctor the emitter renders as `| ?`). One
blocker down, one to go; each fixpoint tick peels one
more layer off the self-host onion.

**Surprise — trailing-comment edge case worked:**
the trailing-comment path
`fn greet() Str = "hi"  -- trailing\nfn main...`
was the one I was most nervous about, because the
decl-terminating `TNewline` is consumed *inside* the
comment. The fix handles this correctly: when
`skipLineComment` finds the `\n`, it emits a
`TNewline` itself (via `listAppend [TNewline]
(lexChars rest)`) before recursing, so the token
stream seen by `parseDecls` is identical whether the
newline arrived via `lexChars` or `skipLineComment`.
No special-casing in the parser was needed.

Next tick: **Phase 7.9o** — the next concrete blocker
surfaced by the fixpoint probe. Most likely Bool type
promotion (#6) in HM/typeCheck, which 7.9k deferred,
or the unknown-ctor case visible in the `| ?` output
above. A fresh probe after 7.9n will pick the
definitive next step.

### 2026-04 — Phase 7.9o: type decl layout (DONE)

Fifteenth tick of **Phase 7.9**. The Phase 7.9n
fixpoint probe had shown the `| ?` placeholder under
`type Token =` — revealing that the `| ?` was not an
"unknown ctor name" emission but a *real* parse
failure. The bootstrap compiler's own `type Token`
uses the multi-line form:

```
type Token =
  | TKwModule
  | TKwType
  | TKwFn
  | TKwExport
  ...
```

and `parseTypeDecl` / `parseCtors` / `parseCtorsTail`
had zero newline tolerance around the constructor
list. Two compounding bugs:

1. **No `skipNewlines` after `TEq`.** `parseTypeDecl`
   passed `TNewline :: TBar :: TUpper "TKwModule" :: ...`
   straight to `parseCtors`, which passed it to
   `parseCtor`, whose `TUpper name :: rest` arm never
   matched because the head was `TNewline`. Fell
   through to the `MkCtor "?" []` catch-all.
2. **No acceptance of optional leading `|`.** Even if
   the newlines were stripped, the multi-line form
   starts with `TBar` before the *first* ctor — and
   `parseCtors` called `parseCtor` directly with no
   `TBar`-eating step. Would have produced the same
   `MkCtor "?" []` placeholder.

The single-line form `type Maybe A = Some A | None`
has NO leading `|`, so the pre-fix code happened to
work for it (the 7.9l fixture and regression tests
rely on this). Both forms must work.

**The proof was a 10-line fixture:**

```
module Examples.Clean
type Color =
  | Red
  | Green
  | Blue
fn label(c Color) Int =
  match c with
    | Red -> 1
    | Green -> 2
    | Blue -> 3
fn main() Int = label Red
```

Pre-fix, `lllc run` on this file emitted a `type Color =`
header followed by `    | ?` and stopped — every ctor
became the `?` placeholder and `parseCtorsTail` never
consumed more than one. Post-fix, all three ctors
round-trip and the `label` match body walks them
cleanly.

**Changes in `20-bootstrap-compiler.lll`:**

* `parseTypeDecl` (~line 693) — `TEq :: r -> r`
  becomes `TEq :: r -> skipNewlines r`. The `| _ ->
  rest2` fallback for headerless/error shapes stays
  unchanged.
* `parseCtors` (~line 714) — no longer passes `toks`
  straight to `parseCtor`. Two-step strip: first
  `skipNewlines` at the head, then match the leading
  `TBar :: r` and `skipNewlines r` (or fall through
  with the unchanged token list). The result feeds
  `parseCtor` and `parseCtorsTail`.
* `parseCtorsTail` (~line 718) — peeks at
  `skipNewlines toks` instead of `toks` directly, so
  inter-ctor newlines (`\n  | Green`) don't prematurely
  terminate the list via the `| _ -> (acc, toks)`
  fall-through. After consuming a `TBar`, also
  `skipNewlines` before the next `parseCtor` call
  so a line-wrapped ctor name parses. **Important:**
  the fall-through still returns the *original* `toks`
  (not the skipped one), so the caller
  (`parseTypeDecl`) sees the inter-decl `TNewline`
  that `parseDecls` needs as a decl-terminator.
* New fixture `20m-bootstrap-input-type-layout.lll`
  — 10 lines, exercises `type Color =\n  | Red\n
  | Green\n  | Blue` and a `label` fn whose body is
  a multi-line match over all three ctors. The
  multi-line match already works since Phase
  7.9.newlines, so only the type-decl path is
  exercising new behavior.
* New regression test in `BootstrapCompilerTests.fs`
  follows the same 20a-backup / copy-fixture /
  restore pattern as 7.9h–7.9n. Asserts (a) no
  `E002` / `E001` / `error`, (b) the emitted output
  does NOT contain `| ?` (the placeholder that
  signals broken ctor parsing), (c) each of `| Red`
  / `| Green` / `| Blue` (or `Red of` / etc.) is
  present in the emitted F#, (d) `let rec label`
  or `let label`, (e) `[<EntryPoint>]`.

**Backward-compat with single-line form:**

The fixes are strictly additive. `skipNewlines` over
zero newlines is a no-op, and the optional
`TBar :: r -> skipNewlines r` match doesn't fire on
a single-line decl that starts with `TUpper name`
(first-ctor name directly after `=`). The existing
20b Maybe / 20i ctor-pat tests (which use the
single-line form) still pass without modification.

**Deliberately out of scope:**

* **Dedicated error messages for malformed type
  decls** — the `| _ -> (MkTypeDecl "?" [] [], toks)`
  fall-through still silently emits a placeholder
  instead of a real error. Fine for bootstrap; the
  host F# compiler catches anything the bootstrap
  lets slide.
* **GADT / existential / record-like ctors** — the
  bootstrap only needs nullary / positional sum-type
  ctors to self-compile. `type Result A E = Ok A
  | Err E` already works; `{ x: Int; y: Int }` and
  `type Expr where ...` are out of scope forever.

Tests: 413 → 414 (+1 for the multi-line type-decl
regression test).

**Fixpoint-probe confirmation:** post-fix, swapping
`20a-bootstrap-input.lll` for the bootstrap compiler's
own source and running `lllc run` produces a **26-byte
stdout** containing `E002 UnboundVar charToInt`. This
is a huge forward step — the parser now successfully
consumes the *entire* bootstrap source (all
`type Token | ... ` constructors, all multi-line
helper fns, ~1800 lines of real ll-lang), reaches the
elaborator's 7.9a error-reporter path, and the error
list is short enough that it short-circuits the
codegen pass. Pre-fix, the parser died at line ~200
and emitted a 403-byte stub; post-fix, parsing
completes and the next blocker is an **elaborator**
issue, not a parser issue.

**Surprise — the fix composed cleanly with 7.9l.**
The 7.9l ctor-pattern parser (which uses
`TUpper name :: rest -> parseCtorArgs name rest`)
had no interaction with the type-decl ctor list —
they share a `Ctor` type and a `parseCtor` function
name but the parser flows are independent. The fix
to `parseCtors` did not require any tweaks to the
match-arm pattern parser.

Next tick: **Phase 7.9p** — `charToInt` is the
immediate blocker. Either add it to `stdlibNames`
(one-line fix), OR — if the bootstrap source uses
several more stdlib functions beyond `charToInt` —
audit `20-bootstrap-compiler.lll` for all calls to
functions not in `stdlibNames` and batch-seed them.
A single `E002` is the fastest unblock, but given
the bootstrap's complexity, more unbound-var errors
are likely hiding just behind `charToInt`. The
fixpoint probe after 7.9p should reveal the next
layer.

### 2026-04 — Phase 7.9p: stdlib audit (DONE)

**Problem:** After 7.9o unblocked the parser, the
elaborator's name-resolution pass became the next
blocker. Running the bootstrap on its own source
(the fixpoint probe) produced a 26-byte stdout
containing exactly `E002 UnboundVar charToInt`.
The bootstrap's `stdlibNames` list — the seed
environment the elaborator uses to satisfy calls
to built-ins that aren't declared via `fn` — was
missing names the bootstrap source itself uses.

**Audit method:** The probe already enumerates the
full `E002 UnboundVar` set in one shot (the
bootstrap's `checkDecls` walks all decls and
collects every error; `showErrs` joins them with
`\n`). So the probe output is the ground truth —
no regex grep heuristic needed. The entire
`UnboundVar` cascade turned out to be a *single*
name: `charToInt`. Every other stdlib call the
bootstrap uses (`strConcat`, `strLen`, `printfn`,
`listMap`, `listLen`, `listAppend`, `listIsEmpty`,
`listFold`, `readFile`, `true`, `false`, `print`)
was already in the list; every other name the
bootstrap references resolves via its own
top-level `fn` declarations (which `collectDecls`
adds to the env alongside the stdlib seed).

**Fix:** Extend `stdlibNames` with a single name
— `charToInt`. New list has 13 entries:

```
["strConcat" "strLen" "print" "printfn" "listMap"
 "listLen" "listAppend" "listIsEmpty" "listFold"
 "readFile" "true" "false" "charToInt"]
```

**Fixture:** New `20n-bootstrap-input-stdlib-full.lll`
— a minimal module with `fn demo(c Char) Int =
charToInt c` + a zero-arg `main`. Before the fix,
running the bootstrap on this fixture surfaces
`E002 UnboundVar charToInt`; after the fix, the
elaborator accepts the name and the codegen pass
emits `let demo` + the `[<EntryPoint>]` main wrapper.

**Test:** New `Phase 7.9p` regression in
`BootstrapCompilerTests.fs` follows the same
20a-backup / copy-fixture / restore pattern as
7.9h–7.9o. Asserts (a) no `E002 UnboundVar
charToInt`, (b) no `E002 UnboundVar` at all,
(c) `let demo` or `let rec demo` in emitted F#,
(d) `[<EntryPoint>]` present.

Tests: 414 → 415 (+1 for the stdlib-audit
regression test).

**Fixpoint-probe confirmation:**

| Metric | Before 7.9p | After 7.9p |
|---|---|---|
| stdout bytes | 26 | 2253 |
| content | `E002 UnboundVar charToInt` | ~100 lines of emitted F# |
| pipeline stage reached | elaborator (name check) | codegen (partial) |

Post-fix, the bootstrap's codegen pass emits the
module header, stdlib prelude, *all thirteen*
top-level `type` decls (Maybe, Token, TypeArg,
Ctor, TypeDecl, TypeRef, Param, Pat, Expr,
LetDecl, FnDecl, Decl, Module), and then the
first `fn` — `isUpperChar` — as a single malformed
`let` line:

```
let isUpperChar c = (let n = (charToInt c) in
  (if (n < 65L) then false else 0L))
```

The emitted body mixes `Bool` (`false`) and `Int`
(`0L`) in the `if` branches — the bootstrap's
codegen pass for `EIf` doesn't unify the two arms
into a common type, and the entire pass halts
after this one fn (exit 0, no stack trace). That
means the remaining fns never get emitted, and
there's no `let rec`, no `[<EntryPoint>]` main
wrapper.

**Deliberately out of scope:** this codegen
halting is *not* a name-resolution issue, so it's
not 7.9p's slice. The E002 cascade is clear.

**Next tick:** **Phase 7.9q** — **codegen
continuation / if-branch typing**. The bootstrap's
`emitExpr` pass for `EIf` needs to either (a)
unify the two arm types via the minimal HM and
coerce the mismatched branch, or (b) emit a type
annotation on the if expr so F# accepts it. More
importantly, whatever is halting the emission
walker after the first fn needs investigation:
exit 0 + no error + truncated stdout means the
walker is quietly running out of something
(recursive depth? an exception swallowed by a
catch-all?). Either way, the next probe slice is
a **codegen** issue, not elaboration. The E002
UnboundVar cascade is fully clear.

### 2026-04 — Phase 7.9q: parseIf newline tolerance (DONE)

**Problem:** 7.9p's probe surfaced what looked like
a codegen walker halt: stdout ended cleanly after a
single malformed `let isUpperChar c = (... if ...
then false else 0L)` line, with no `let rec`, no
further fns, no `[<EntryPoint>]`. Exit 0. No stack
trace. The visible symptom pointed at `emitExpr` /
`emitGroupLoop` quietly failing on the first fn.

**Diagnosis:** Before writing any fix, I reproduced
the halt with a 6-fn fixture (all trivial bodies)
— the bootstrap emitted all 6 fns correctly. That
ruled out the walker-short-circuit hypotheses. I
then wrote a second fixture with one `if a then x\n
else if b then y\n else z` body followed by four
more trivial fns. That fixture reproduced the halt:
only the first fn emitted, with a truncated if body
`(if (c < 65L) then 0L else 0L)`, exactly matching
the symptom on the bootstrap's own `isUpperChar`.
Root cause was not in codegen at all — it was in
the bootstrap's `parseIf`:

```
fn parseIf(toks List[Token]) =
  let (c, rest) = parseExpr toks in
  let rest2 = match rest with
    | TKwThen :: r -> r
    | _ -> rest
  in
  ...
  let rest4 = match rest3 with
    | TKwElse :: r -> r
    | _ -> rest3
  in
  ...
```

The two `TKwThen :: r` / `TKwElse :: r` match arms
have no newline tolerance. On a multi-line if
like `if c < 65 then false\n  else if c > 90 then
false\n  else true`, the token stream between arms
is `... TKwThen (false) TNewline TKwElse ...`.
After parsing the `then` branch body, `parseIf`
checks for `TKwElse` but the head is `TNewline` —
the wildcard fires, leaving `rest4 = rest3`, and
`parseExpr` is called on `TNewline :: TKwElse :: ...`.
That bottoms out in `parseAtom`'s `| _ -> (EInt 0,
toks)` wildcard which returns `EInt 0` **without
consuming anything**. So `parseIf` returns
`EIf cond thenE (EInt 0)` with the stray `TKwElse`
(and everything after it — the rest of the
bootstrap's 2000+ lines) still sitting on the
token stream.

Back up in `parseDecls` after `parseFnDecl`
returns, `skipNewlines` eats the newline but the
head is now `TKwElse`, which matches none of the
`TKwType`/`TKwLet`/`TKwFn`/`TKwTag`/`TKwImport`/
`TKwExport` arms. The wildcard `| _ -> []` fires
and **every remaining decl is silently dropped**.
The pipeline sees a truncated decl list with one
non-main fn and emits it via `emitFnPlain`
(singleton `let`, not `let rec`) — which is
exactly what the probe showed.

The codegen concern about heterogeneous `EIf`
arms (`false` vs `0L`) is real but a downstream
symptom: with the `else` arm dropped, the parser
synthesised an `EInt 0` defensively and that's
why the emitted body has `0L` where the source
has `true`. Once `parseIf` consumes the newline,
the body parses cleanly and the heterogeneous-if
concern vanishes for this particular fn. (A more
robust Bool-vs-Int unification in codegen stays
on the backlog.)

**Fix:** Call `skipNewlines` four times inside
`parseIf`: before and after the `TKwThen` match,
and before and after the `TKwElse` match. Mirrors
the host parser's `skipNewlines c` calls at
exactly the same sites (`Parser.fs` lines ~383 and
~391 in the `KwIf` arm). Single-site change,
~15 lines of `parseIf` touched, no ripple into
`emitExpr` / `emitGroupLoop` / anywhere else.

**Fixture:** `20o-bootstrap-input-multifn.lll` — a
minimal repro with one multi-line `if-then-else-if-
then-else` body followed by four trivial fns and
a zero-arg `main`. Before the fix, only the first
fn emits (as a singleton `let`, truncated body);
after the fix, all five non-main fns collapse into
a `let rec one ... and two ... and five ...` block
plus the `[<EntryPoint>]` main wrapper.

**Test:** New `Phase 7.9q` regression in
`BootstrapCompilerTests.fs` follows the same
20a-backup / copy-fixture / restore pattern as
7.9h–7.9p. Asserts `let one` / `let rec one`,
`let five` / `and five`, `let main` / `and main`,
and `[<EntryPoint>]` in the emitted F#.

Tests: 415 → 416 (+1 for the parseIf-layout
regression test).

**Fixpoint-probe confirmation:**

| Metric | Before 7.9q | After 7.9q |
|---|---|---|
| stdout bytes | 2253 | 86 |
| pipeline stage reached | codegen (partial, 1 fn) | elaborator (error) |
| decls parsed | ~14 (13 types + 1 fn) | all ~75 |
| content | truncated F# | 4 E002 UnboundVar lines |

The byte count *drops* because the fix exposes
the next blocker: with the parser now consuming
the full source, the elaborator sees four
previously-hidden unbound names — `charIsDigit`,
`p`, `cs`, `List` — in fns that were silently
dropped before. The error path short-circuits
before codegen, hence the smaller output. This is
correct: all decls are now parsed, and the probe
faithfully reports the first un-resolved names
reachable from a well-formed decl list. The 2253-
byte partial codegen from 7.9p was a *lie* — most
of the bootstrap's decls weren't being parsed at
all.

**Next tick:** **Phase 7.9r** — unbound-name
cleanup. The four surfaced names split into two
kinds: (a) `charIsDigit` is a stdlib builtin the
host elaborator knows about; the fix is the same
one-line extension to `stdlibNames` that 7.9p did
for `charToInt`. (b) `p`, `cs`, `List` are more
interesting — `p`/`cs` are lambda/fn params that
the bootstrap's minimal name-scoping pass isn't
threading through correctly, and `List` is a type
constructor used in a position the elaborator's
kind-checker doesn't recognize. The probe report
will say which is which and whether 7.9r collapses
into one slice or needs two.

## Phase 7.10a — List literal expressions

**Slice:** add list literal expression support
(`[]`, `[x]`, `[x y z]`) to the bootstrap compiler,
mirroring how `PNil` / `PCons` work on the pattern
side. Two new `Expr` variants (`ENil`, `ECons`)
plus parser + elaborator + HM + codegen plumbing.

Pre-fix, `parseAtom` in the bootstrap had no arm
for `TLBrack` in expression position — every list
literal silently fell through to the wildcard
`(EInt 0, toks)` fallback *without consuming the
`[` token*, desyncing the token stream. The
bootstrap's own source uses list literals in ~30
places (`listAppend [c] ...`, `listAppend [TKwFoo]
...`, `[]` as the empty-case return in lex/parse
helpers), so this was a hard blocker for
self-hosting.

**Change sites (7):**
1. `type Expr` — two new variants `ENil` / `ECons
   Expr Expr`.
2. `parseAtom` — two new arms before the wildcard:
   `TLBrack :: TRBrack :: rest -> (ENil, rest)`,
   `TLBrack :: rest -> parseListLit rest`. New
   `parseListLit` helper reads atoms until
   `TRBrack`, folding right-associatively into
   `ECons`. Elements parsed via `parseAtom` (not
   `parseExpr`) — sufficient for every list literal
   in the bootstrap source.
3. `isAtomStart` — new `TLBrack -> true` arm so
   the application layer picks up `head [1 2 3]`
   as `EApp head [1 2 3]`.
4. `checkExpr` — noop arms threading child errors
   (`ENil -> []`, `ECons h t -> listAppend
   (checkExpr env h) (checkExpr env t)`).
5. `inferExprType` — `ENil` / `ECons _ _` both
   return `TyName "List"`. Minimal — no element
   type checking in this slice.
6. `typeCheck` (exhaustive walker) — noop arms
   (`ENil -> []`, `ECons h t -> listAppend
   (typeCheck env h) (typeCheck env t)`).
7. `showExpr` — `ENil -> "[]"`, `ECons h t ->
   "(h :: t)"`.
8. `emitExpr` — `ENil -> "[]"`, `ECons h t -> "(h
   :: t)"`. F# uses the same cons syntax as
   ll-lang, so the emit side is literally the
   same as `showExpr`.

**Fixture + test:** `20r-bootstrap-input-lists.lll`
exercises `fn main() Int = head [1 2 3]` over a
`head(xs List[Int])` match helper. The regression
test asserts the emitted F# contains `(1L :: (2L
:: (3L :: [])))`. Pre-fix: the list literal was
silently dropped, `head` emitted as a bare ref +
stray `0L`. Post-fix: full cons chain emitted,
compiles + runs.

**Fixpoint-probe confirmation:**

| Metric | Before 7.10a | After 7.10a |
|---|---|---|
| stdout bytes | 2726 | 2764 |
| stdout lines | 124 | 126 |
| errors (E00) | 0 | 0 |
| halt point | `takeIdCont` body | `takeIdCont` body |
| new content | — | `type Expr` gets `\| ENil` + `\| ECons of Expr * Expr` rows |

The probe output grew by exactly 2 lines / 38
bytes — the new `ENil` / `ECons` rows inside the
emitted `type Expr` sum-type decl. This is
ground-truth proof that the bootstrap is now
parsing + elaborating + emitting the two new
variants end-to-end without errors. The bootstrap
compiles its own modified source cleanly.

**Surprise finding:** the probe still halts at
`takeIdCont`'s body with `(match cs with | (c ::
rest) -> 0L)` — the *same* halt point as before
7.10a. Diagnosing the halt in isolation revealed
the blocker has nothing to do with list literals:
minimal fixture
```
fn takeIdCont(cs List[Int]) Int =
  match cs with | c :: rest -> if c then 1 else 0
                | _ -> 0
```
reproduces the identical `-> 0L` halt. Root cause:
`parseArmBody` in the bootstrap uses `parseCompare`
(for documented "safe" reasons — "15's match
bodies never contain bare let/match/lambdas"), but
`if-then-else` is ALSO a `parseExpr`-level special
form that `parseCompare` doesn't dispatch to. An
`if` token inside a match arm body falls through
`parseCompare → parseAddSub → ... → parseAtom`
wildcard, producing `(EInt 0, toks)` and corrupting
the arm body. Pre-existing bug, unrelated to the
7.10a scope. Verified by git-stashing 7.10a and
re-running the minimal fixture — identical output.

**Next tick:** **Phase 7.10b** — lift
`parseArmBody` from `parseCompare` to `parseExpr`
(or add explicit `TKwIf :: _` dispatch). Once the
arm body can be an `if-then-else`, `takeIdCont` /
`dropIdCont` / `takeDigit` / `dropDigit` should
all emit real bodies and the probe should jump
significantly past the 126-line wall.

## Phase 7.10b — If/let in match arm bodies

**Slice:** fix `parseArmBody` in the bootstrap
compiler to dispatch `TKwIf` and `TKwLet` at the
top before falling through to `parseCompare`. This
is a surgical, one-function change that unblocks
the `takeIdCont` / `dropIdCont` / `takeDigit` /
`dropDigit` family of lex helpers whose arm bodies
are `if isIdCont c then listAppend [c] (takeIdCont
rest) else []`.

Pre-fix, `parseArmBody` was a straight alias for
`parseCompare`, which SKIPS the special-form
dispatch (`parseIf` / `parseLetIn` / `parseMatch`
/ `parseLam`) that lives in `parseExpr`. An arm
body starting with `if` hit `parseCompare →
parseAddSub → ... → parseAtom`, which has no
`TKwIf` arm, fell into the wildcard `(EInt 0,
toks)`, and the token stream desynced — the body
became literal `0` and every subsequent decl
silently dropped out.

The original comment on `parseArmBody` justified
the `parseCompare` constraint as protection
against nested `match` in arm bodies accidentally
grabbing a sibling `| ...` arm — but explicitly
noted the constraint was "safe because 15's match
bodies never themselves contain bare `let` / `match`
/ lambdas (those are Phase 7.5+)". By 7.10b the
bootstrap's own source routinely uses `if` and
`let` inside arm bodies (the lex helper family
above), so the constraint had to be relaxed. The
**surgical fix** dispatches `TKwIf` and `TKwLet`
explicitly at the top of `parseArmBody` while
leaving `parseMatch` / `parseLam` behind
`parseCompare` — preserving the nested-match
safety guarantee and keeping scope tight to the
two forms actually needed.

**Change site (1):**

```
fn parseArmBody(toks List[Token]) =
  | TKwIf :: rest -> parseIf rest
  | TKwLet :: rest -> parseLetIn rest
  | _ -> parseCompare toks
```

**Fixture + test:**
`20s-bootstrap-input-arm-if.lll` exercises

```
fn describe(n Int) Int =
  match n with
    | 0 -> if 1 == 1 then 10 else 20
    | _ -> 30
fn main() Int = describe 0
```

Pre-fix, `describe` emitted as `(match n with | 0L
-> 0L)` — the `if` body was dropped to `EInt 0`,
and the `| _` wildcard arm was silently consumed
by the desync. Post-fix, the emitted F# contains
the proper `if ... then 10L else 20L` form and
both match arms are preserved. Regression test
`20-bootstrap-compiler.lll emits if expression in
match arm body (Phase 7.10b)` asserts the emitted
F# contains `if `, `10L`, `20L`, `describe`, and
`[<EntryPoint>]`.

**Fixpoint-probe confirmation:**

| Metric | Before 7.10b | After 7.10b |
|---|---|---|
| stdout bytes | 2764 | 153 |
| stdout lines | 126 | 5 |
| errors reported | 0 | 1 (`E002 UnboundVar strToInt`) |
| halt point | `takeIdCont` body emits as `-> 0L` (silent garbage) | bootstrap's own elaborator flags real unbound var |
| main branch taken | `printfn (emitModule mod)` | `printfn (showErrs errs)` |

The byte count SHRANK from 2764 to 153, but this
is actually the single most significant step
forward in Phase 7: the bootstrap is now
functioning as a real compiler. Pre-fix, the
parser silently emitted 126 lines of malformed F#
because desynced arm bodies produced `EInt 0`
ASTs that the elaborator couldn't even see — so
`errs` came back empty and `main` took the
`printfn (emitModule mod)` branch. Post-fix, the
parser correctly consumes every arm body in the
lex helper family, the elaborator now has a
well-formed AST, and IT (correctly) flags
`strToInt` — a genuinely undefined reference in
the bootstrap's own source — as unbound. `main`
takes the `printfn (showErrs errs)` branch and
stops.

**Next blocker:** **Phase 7.10c** — the bootstrap
references `strToInt` in `parseIntStr` (line 378
of `20-bootstrap-compiler.lll`) but `strToInt` is
not declared anywhere in the bootstrap's stdlib.
The host stdlib provides it via runtime.fs, but
the bootstrap needs an explicit extern/declared
binding (or a self-written implementation) so the
elaborator recognises it. The simplest fix is
probably to add a stub `fn strToInt(s Str)
Maybe[Int] = None` to the bootstrap's prelude
(it's only used defensively — the lexer guards
the call with an all-digits check) OR to declare
it as a builtin like the other stdlib names
already threaded through `elaborate`'s initial
environment. After 7.10c lands, the elaborator
should return empty and `main` will flip back to
the emit branch — hopefully producing a
significantly larger output than the pre-7.10a
2726-byte baseline, with emission flowing into
the `parseIntStr` / `classifyId` / `lex*` family.


## Phase 7.10c — strToInt in stdlibNames

**Slice:** add `"strToInt"` to the bootstrap's
`stdlibNames` flat list so the minimal HM pass
accepts the name as a builtin, mirroring the
7.9p (`charToInt`) and 7.9r (`charIsDigit`)
slices. The host elaborator already knows
`strToInt` as a builtin; the bootstrap's mirror
list was the only gap.

**Change site (1):**

```
fn stdlibNames(_ignored Int) List[Str] =
  [... "charToInt" "charIsDigit" "strToInt"]
```

**Fixture + test:**
`20t-bootstrap-input-strtoint.lll`:

```
module Examples.Clean
fn useIt(s Str) Int = let x = strToInt s in 0
fn main() Int = useIt "42"
```

Regression test `20-bootstrap-compiler.lll
resolves strToInt via stdlibNames (Phase 7.10c)`
asserts NO `E002 UnboundVar strToInt`, and the
emitted F# contains `let useIt` / `let rec useIt`
plus `[<EntryPoint>]`.

**Fixpoint-probe confirmation:**

| Metric | Before 7.10c | After 7.10c |
|---|---|---|
| stdout bytes | 153 | 3338 |
| stdout lines | 5 | 131 |
| errors reported | 1 (`E002 UnboundVar strToInt`) | 0 |
| halt point | elaborator flags `strToInt` unbound | emit succeeds; main prints types + fn bodies |
| main branch taken | `printfn (showErrs errs)` | `printfn (emitModule mod)` |

The byte count jumped from 153 to 3338 — `main`
flipped back to the emit branch, and emission now
flows through the type decls and the lex-helper
family (`isUpperChar`, `isLowerChar`, `isIdStart`,
`isIdCont`, `takeIdCont`, `dropIdCont`,
`takeDigit`, `dropDigit`, `parseIntStr`,
`classifyIdent`). This is the first time the
bootstrap's output has exceeded the pre-7.10a
2726-byte baseline.


## Phase 7.10d — PStr string literal patterns

**Slice:** extend the bootstrap's `Pat` type with
a `PStr Str` variant so match arms like
`| "module" -> TKwModule` parse and render
correctly. Previously, `parsePrimaryPat` had no
`TStr _` arm — string literal patterns fell
through to the catch-all `| _ -> (PWild, toks)`
which silently turned every literal arm into
a `PWild`, collapsing all subsequent arms into a
single wildcard. The 7.10c probe exposed this
directly: `classifyIdent` emitted a one-arm
match (`| _ -> "module"`) instead of the full
14-keyword dispatch.

**Change sites (6):**

1. `type Pat` — new `PStr Str` variant
2. `parsePrimaryPat` — `| TStr s :: rest ->
   (PStr s, rest)` between the `TInt` and
   `TUnder` arms
3. `showPat` — `PStr s -> "\"" ^ s ^ "\""`
4. `patBinders` — `PStr _ -> []`
5. `patIsCatchAll` — `PStr _ -> false`
6. `emitPat` — `PStr s -> "\"" ^ s ^ "\""`
   (F# accepts string-literal patterns verbatim)

**Fixture + test:**
`20u-bootstrap-input-str-pat.lll`:

```
module Examples.Clean
fn kind(s Str) Int =
  match s with
    | "one" -> 1
    | "two" -> 2
    | _ -> 0
fn main() Int = kind "two"
```

Regression test `20-bootstrap-compiler.lll
supports string literal patterns (Phase 7.10d)`
asserts NO `E002`/`E001`/`error`, and the emitted
F# contains `| "one"` and `| "two"` plus
`[<EntryPoint>]`.

**Fixpoint-probe confirmation:**

| Metric | Before 7.10d | After 7.10d |
|---|---|---|
| stdout+stderr bytes | 3338 | 1609 |
| stdout+stderr lines | 131 | 11 |
| errors reported | 0 (but truncated emit) | 2 (`E002 UnboundVar strChars`, `E002 UnboundVar s`) |
| halt point | `classifyIdent` emitted `\| _ -> "module"` (single wildcard; all 13 keyword arms silently dropped) | elaborator flags `strChars` unbound in `classifyIdent` wildcard body |
| main branch taken | emit branch (truncated output) | `printfn (showErrs errs)` |

The byte count appears to shrink (3338 → 1609)
because the bootstrap now fails faster at
elaboration rather than silently emitting
truncated F#. This is **forward progress**: the
bootstrap now successfully parses all 14
keyword arms of `classifyIdent`, reaches the
previously-unreached wildcard-arm body
`let cs = strChars s in ...`, and correctly
flags `strChars` as an unbound name. The earlier
3338-byte "success" was a false positive —
arms after `"module"` were all being dropped
into a single `PWild`.

**Next blocker:** **Phase 7.10e** — the bootstrap
references `strChars` (and `strFromChars`) at
lines 409/417/579/1995 of
`20-bootstrap-compiler.lll`, but `strChars` is
not in the bootstrap's `stdlibNames` mirror list.
Same shape as 7.9p / 7.9r / 7.10c — add the bare
name(s) so the minimal HM pass accepts them as
builtins. The `s` unbound is a likely cascade
from the failed `let cs = strChars s in` binding.

## Phase 7.10e — small stdlib audit (strChars et al.)

**Slice:** audit the bootstrap's `stdlibNames`
mirror list against the host elaborator's
`builtinEnv` for names the bootstrap itself
calls. Found five missing: `strChars`,
`strFromChars`, `intToStr`, `charIsSpace`,
`listReverse`. All five are referenced from
bootstrap fn bodies (lexer whitespace skip,
string construction, int formatting, pending
decl reversal) but were not in the flat name
list, so the bootstrap's minimal HM pass flagged
them as `E002 UnboundVar` when elaborating its
own source.

**Change site (1):**

`fn stdlibNames(_ignored Int) List[Str] =` at
line 1889 — extend the flat literal list with
the five missing names, grouped logically
(str* / list* / char*).

**Fixture + test:**
`20v-bootstrap-input-strchars.lll`:

```
module Examples.Clean
fn chars(s Str) List[Char] = strChars s
fn rebuilt(s Str) Str = strFromChars (strChars s)
fn rev(xs List[Char]) List[Char] = listReverse xs
fn showInt(n Int) Str = intToStr n
fn isWs(c Char) Bool = charIsSpace c
fn main() Str = rebuilt "hi"
```

Regression test `20-bootstrap-compiler.lll
resolves strChars/strFromChars/intToStr/
charIsSpace/listReverse via stdlibNames
(Phase 7.10e)` asserts NO `E002 UnboundVar`
for any of the five names and the emitted F#
contains `rebuilt s =` plus references to all
five builtins plus `[<EntryPoint>]`.

**Fixpoint-probe confirmation:**

| Metric | Before 7.10e | After 7.10e |
|---|---|---|
| stdout+stderr bytes | 1609 | 146 |
| stdout+stderr lines | 11 | 5 |
| errors reported | 2 (`strChars`, `s`) | 1 (`s` only) |
| halt point | elaborator flags `strChars` unbound in `classifyIdent` wildcard body | elaborator flags `s` — a single-letter local/param name leaked by parser desync downstream of the now-resolved `strChars`/`strFromChars` sites |
| main branch taken | `printfn (showErrs errs)` | `printfn (showErrs errs)` |

The byte count shrank 10.8x (1609 → 146) because
the elaborator now flags only one `E002` instead
of two, and the second error's message is short.
The remaining `s` is **not** a stdlib — it's a
downstream cascade from parser desync somewhere
past the resolved builtins; probe-driven
investigation in the next phase will pinpoint it.

**Next blocker:** investigate the lone
`E002 UnboundVar s`. Likely a parser-layer
issue in a body expression that references `s`
before its binder is visible — common suspects
are `let ... in` visibility inside lambdas or
a missing case in `patBinders` / environment
extension. Start with a printf-debug at
`checkDecls` to find which fn's body owns the
stray `s` reference.

## Phase 7.10f — skipNewlines in parseArmBody

**Slice:** the residual `E002 UnboundVar s`
was not a scoping bug at all — it was a
**parser-layer desync**. A multi-line match arm
body in the form

```
| _ ->
  let cs = strChars s in
  <body>
```

tokenises as `TBar TUnder TArrow TNewline
TKwLet TLower cs TEq ...`. `parseArmBody`
pattern-matched on `TKwLet` / `TKwIf` directly,
so the leading `TNewline` fell through to
`parseCompare`, which bottomed out at
`(EInt 0, toks)` with zero tokens consumed. The
arm body became `EInt 0`, the enclosing match
returned, and the orphaned `let cs = ... in
<body>` was then re-parsed by the top-level
`parseDecls` loop as a standalone `DLet` —
losing the outer fn param scope entirely. The
`<body>` (`if isUpperChar c then TUpper s
else TLower s`) ran at top-level where `s`
was not defined, producing the phantom
`E002 UnboundVar s`. Pinpointed in
`classifyIdent` at lines 393–412.

**Change site (1):**

`fn parseArmBody(toks List[Token]) =` at
line 1016 of `20-bootstrap-compiler.lll`:
convert the clause-sugar match to the
`let toks2 = skipNewlines toks in match toks2
with` shape already used by `parseArms`, so the
`TKwIf` / `TKwLet` special-form dispatch runs
on the post-newline-skip tokens. Only the
dispatch sees `toks2`; the fallthrough to
`parseCompare toks2` also uses the skipped list
so the compare chain resumes cleanly. 15-line
diff (one let + one match wrap).

**Regression test:** the existing 23 bootstrap
tests (filtered via
`FullyQualifiedName~Bootstrap|bootstrap`) all
stay green — the fix is a pure `parseArmBody`
tightening and doesn't touch any other parser
slice, elaborator pass, or codegen.

**Fixpoint-probe confirmation:**

| Metric | Before 7.10f | After 7.10f |
|---|---|---|
| stdout+stderr bytes | 146 | 178 |
| stdout+stderr lines | 5 | 6 |
| errors reported | 1 (`s`) | 2 (`lexChars` x2) |
| halt point | `classifyIdent` wildcard arm body ejects `let cs = strChars s in ...` to top-level, loses `s` scope | parser now correctly keeps the let-in inside the arm body; `s` is resolved fine, and the SURVIVING errors are the next blocker below |

The byte count grew 146 → 178 because the
bootstrap now flags two fresh errors instead of
the one phantom `s`. This is **progress** — the
`s` phantom was masking the real next issue,
which only surfaces once the parser stops
mis-rebasing arm-body let-ins into top-level
decl position.

## Phase 7.10g — clause-sugar fn-body desugaring (DONE)

This blocker is now closed.

`parseFnDecl` now routes bodies through `parseFnBody`, which:

1. `skipNewlines` on body entry
2. dispatches `TBar :: _` to `parseArms`
3. desugars to `EMatch (lastParamVar parms) pats bodies`
4. falls back to `parseExpr` for non-clause bodies

This matches the host rule: clause-sugar scrutinee is the **last**
curried parameter.

Evidence:

- Source: `spec/examples/valid/20-bootstrap-compiler.lll` (`parseFnBody`,
  `lastParamVar`).
- Regression: `20-bootstrap-compiler.lll desugars clause-sugar fn bodies
  (Phase 7.10g)` in `tests/LLLangTests/BootstrapCompilerTests.fs`.

## Phase 7.10q — tuple literals in bootstrap frontend/backend (DONE)

Tuple literals are now first-class in the bootstrap path:

1. lexer emits `TComma`
2. parser recognises `(e1, e2)` and builds `ETuple2`
3. let-in tuple destructuring uses `TComma`
4. checker/infer/emitter thread `ETuple2` through

Evidence:

- Fixture: `20x-bootstrap-input-tuple.lll`
- Regression: `20-bootstrap-compiler.lll emits tuple literal expressions
  (Phase 7.10q)`

## Phase 7.10r — parity blockers closed (DONE)

The three historical blocker slices marked as 7.10r are now covered by
active regression tests (not skipped):

1. Prelude completeness (`listFold`, `listReverse`, and related builtins)
2. Mutual-recursive grouping parity (`let rec ... and ...`)
3. Multi-line formatting parity of grouped function emission

Evidence (all in `tests/LLLangTests/BootstrapCompilerTests.fs`):

- `20y-bootstrap-input-prelude.lll: bootstrap prelude contains listFold and listReverse (Phase 7.10r blocker 1)`
- `20y-bootstrap-input-mutrec.lll: mutually recursive fns across type boundary produce correct let-rec grouping (Phase 7.10r blocker 2)`
- `20y-bootstrap-input-fmt.lll: each fn in a multi-fn module is emitted on its own separate line (Phase 7.10r blocker 3)`

## Post-7.10 hardening — lexer EOF/unknown-char paths (DONE)

Two bootstrap lexer correctness bugs closed in the same test suite:

1. Bug #7: unknown chars are surfaced as `TError` (no silent drop)
2. Bug #14: `lexCharLit` / `lexCharEsc` return `TEnd` at EOF (no empty-token desync)

Evidence:

- `20-bootstrap-compiler.lll emits TError for unknown chars instead of silently dropping (Bug #7)`
- `20-bootstrap-compiler.lll lexCharLit and lexCharEsc return TEnd at EOF instead of empty list (Bug #14)`

## Post-7.10 hardening — reverse/transpile boomerang parity matrix (DONE)

Cross-target boomerang parity is now pinned by a dedicated matrix test that
asserts this contract for core source slices on every supported backend:

1. `compileTarget target src` succeeds
2. `reverseToLll target emitted` succeeds
3. reversed source parses (`parseModuleWithPos`)
4. `compileTarget target reversed` succeeds on the same backend

Matrix coverage:

- all targets (`FSharp`, `TypeScript`, `Python`, `Java`, `CSharp`, `LLVM`):
  - arithmetic fn slice (`sampleFunctionSrc`)
  - float literal lets (`sampleSrcWithFloats`)
- non-LLVM targets:
  - bool/string lets (`sampleSrcWithBoolsAndStrings`)
- typed targets (`FSharp`, `Java`, `CSharp`, `LLVM`):
  - char literal lets (`sampleSrcWithChars`)

Additional guard:

- C# reverse now strips host-only wrappers (`checked` / `unchecked` and
  primitive cast prefixes like `(int)`), validated by a dedicated regression
  test:
  `reverse parser strips host-only checked and cast wrappers in CSharp expressions`

Evidence:

- `reverse boomerang matrix keeps core source slices recompilable per target`
  in `tests/LLLangTests/ReverseTranspilerTests.fs`

## Post-7.10 hardening — fixpoint metrics + diagnostics contract (DONE)

Fixpoint parity is now guarded by an additional **snapshot-independent**
contract layer in `BootstrapCompilerTests`:

1. Self-compile output structural metrics are pinned:
   - line count: `602`
   - byte/char count (normalized newlines): `56438`
   - top-level `let` count: `24`
   - top-level `and` count: `240`
2. Emitted output must not contain placeholder constructors (`| ?`).
3. Exactly one `[<EntryPoint>]` marker is present.
4. Semantic-failure diagnostics surface a stable payload for unbound names:
   `E002 UnboundVar <name>`.

Evidence:

- `20-bootstrap-compiler.lll self-compile output satisfies structural fixpoint metrics contract`
- `20-bootstrap-compiler.lll diagnostics contract: unbound var includes stable E002 payload`

## Post-7.10 hardening — backend match payload parity (Python done, C# follow-up)

Closed one backend drift where constructor-payload branches in `match`
could lose bound values:

- **Python backend** now emits payload binds via lambda parameters in ternary
  branches (e.g. `(lambda n: ...)(v._0)`), avoiding unresolved bare names.

Evidence:

- `Py: match with constructor payload binds branch variable`

Follow-up and current C# scope:

- C# now supports a **safe expression-match subset** for constructor payload
  extraction in `Int`-returning matches with nominal constructors and simple
  `PVar` payloads.
- Full C# expression-match parity (nested patterns, tuple/list payloads, and
  non-`Int` return types) remains deferred to a dedicated typed pattern-binder
  pass.

Additional evidence:

- `CSharp: match with constructor payload emits typed guarded accessor flow`

## Post-7.10 hardening — reverse boomerang richer trait slices (DONE)

Expanded boomerang parity beyond scalar/core slices with a trait-heavy module:

- Added `trait Show` + `impl Show Box` round-trip coverage for all
  non-LLVM targets in the reverse boomerang matrix.
- Hardened reverse normalization to avoid two drift patterns that blocked
  richer slices:
  1. **TypeScript** plain template literals are normalized into ll string
     literals (`"..."`) when no interpolation is present.
  2. **F# entrypoint bodies** with duplicated numeric tail (`0L` + `0`) are
     collapsed to a single numeric expression.

Evidence:

- `reverse boomerang matrix keeps core source slices recompilable per target`
- `reverse parser normalizes plain TypeScript template literals into ll strings`
- `reverse parser collapses duplicate numeric tail in FSharp entrypoint bodies`

## Post-7.10 hardening — Java reverse main-wrapper recovery (DONE)

Closed a Java-only reverse gap where modules with `external` declarations and
literal-only `main` bodies could fail reverse recovery because the Java backend
emits:

- `public static void main(String[] args) { var _ll_unused = <expr>; }`

while reverse previously only matched `return`-style static methods.

Hardening applied:

1. Java reverse parser now recovers a safe subset of the backend `void main`
   wrapper by extracting numeric-literal `_ll_unused` payloads.
2. Boomerang matrix now includes a Java `external + main` slice to keep this
   path pinned.

Evidence:

- `reverse boomerang matrix keeps core source slices recompilable per target`
  (case: `external_console_log_main`)

## Post-7.10 hardening — reverse FFI console-wrapper normalization (DONE)

Reduced reverse/transpile drift for `external console_log` modules by mapping
host wrapper calls back into ll-compatible builtin calls:

- `System.Console.WriteLine(...)` (F# / C#) → `print(...)`
- `console.log(...)` (TypeScript) → `print(...)`
- `System.out.println(...)` (Java wrapper recovery path) → `print(...)`

Additional recovery coverage:

- C# and Java reverse now recover `static void` single-statement wrapper
  methods (the shape emitted for external wrappers) instead of dropping them.

Evidence:

- `reverse boomerang matrix keeps core source slices recompilable per target`
  (case: `external_console_log_main` across non-LLVM targets)
- `reverse parser recovers Java external wrapper and literal main wrapper`

## Post-7.10 hardening — LLVM i32 main wrapper recovery (DONE)

Closed an LLVM reverse recoverability gap for backend-emitted entrypoint shape:

- `define i32 @main() { ... trunc i64 <n> to i32 ... ret i32 ... }`

Reverse parser now recovers this wrapper into ll `main(...) = <n>` instead of
failing with "could not recover ... declarations" when no other recoverable
LLVM `i64` function forms are present.

Evidence:

- `reverse boomerang matrix keeps core source slices recompilable per target`
  (case: `main_i32_wrapper` on LLVM)

## Post-7.10 hardening — ADT constructor reverse normalization (DONE)

Closed a cross-backend reverse drift for ADT constructor emissions in OO /
dynamic targets where generated host code used backend-native wrappers:

- `new Maybe.None()` / `new None<T>()` / `new Maybe.Some(...)` /
  `new Some<T>(...)` are normalized back to ll constructors
  (`None`, `Some (...)`).
- Python integer division emitted as `//` in host code is normalized back to
  ll `/` in recovered expressions.
- When recovered declarations/functions use `Some`/`None` but target reverse
  cannot recover a concrete type declaration, reverse now synthesizes the
  canonical fallback:
  `Maybe A = Some A | None`

Evidence:

- `reverse boomerang matrix keeps core source slices recompilable per target`
  (case: `maybe_ctor_if` across non-LLVM targets)
- `reverse parser normalizes host Maybe constructor wrappers across OO backends`

## Post-7.10 hardening — ADT match reverse normalization (ALL TARGETS DONE)

Extended reverse normalization for host-lowered `match` shapes over `Some/None`
so recovered ll code remains parseable and boomerang-compilable across all
targets:

- TypeScript IIFE match lowering with `_tag` checks is recovered into
  `match m | Some(n) -> n | None -> ...`.
- Python and Java host ternary/`if` forms over `_tag`/`instanceof` are
  normalized to the same ll match form.
- C# lifted expression-bodied match wrappers (`Func`/`is Some<T>`/`is None<T>`)
  are collapsed back to ll `match` expressions.
- Python ternary order (`then if cond else else`) is now recognized in reverse
  normalization.
- LLVM `phi`-based lowering over `Some/None` tag checks is recovered into
  canonical ll match form (`match m | Some(n) -> n | None -> ...`) instead
  of leaking backend SSA temporary names into reversed code.
- `None` branch recovery now accepts both literal and SSA-parameter `phi`
  inputs (for example `%arg1`), so recovered ll keeps source-level fallback
  variables (`| None -> fallback`) instead of backend temporaries.

Evidence:

- `reverse boomerang matrix keeps core source slices recompilable per target`
  (case: `maybe_match_unpack` for `FSharp`, `TypeScript`, `Python`, `Java`,
  `CSharp`, `LLVM`)
  and `maybe_match_unpack_fallback` for the same target set.
- `reverse parser normalizes host Maybe match wrappers for TS Py Java`
- `reverse parser normalizes CSharp lifted Maybe match wrapper`

## Current state and next work

Bootstrap self-hosting blocker chain from 7.10d → 7.10r is now closed
and guarded by regression tests. The next iterations should focus on:

1. reducing bootstrap/host behavioural drift in corner-case parser recovery
2. expanding boomerang parity from core slices to richer ADT/trait/FFI-heavy cases
3. growing diagnostics parity beyond `E002` to positional/typed error classes
