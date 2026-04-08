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
