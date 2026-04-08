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
