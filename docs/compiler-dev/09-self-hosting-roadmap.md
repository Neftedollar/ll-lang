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

### 2026-04 — Phase 7.2: arithmetic expression parser (IN PROGRESS)

A WIP `11-parser-real.lll` was drafted (164 lines, recursive-descent
arithmetic parser with `MkParsed` ADT wrapper) but hit an `E003
NonExhaustiveMatch` in the elaborator's exhaustiveness pass — the match
arms covering sub-patterns of `Expr` aren't being recognized as
exhaustive. The draft is preserved at `/tmp/11-parser-real-wip.lll` for
the next session. Likely root cause: the match arms destructure on
constructor-pattern shapes that the exhaustiveness check conservatively
flags as incomplete even when they actually cover every variant via
mutually-exclusive guards. Debug path for next session: reproduce with
a minimal test, trace through `exhaustivenessCheck` in
`Elaborator.fs`, decide whether to relax the check or restructure the
parser to satisfy it.
