# Self-hosting roadmap

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
