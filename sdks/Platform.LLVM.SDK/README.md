# Platform.LLVM.SDK

**Status: MVP — native binaries work for a growing subset of ll-lang.**

## Quick start

```bash
# Build and run:
tools/lllc-native spec/examples/valid/36-llvm-native-hello.lll ./hello
./hello    # -> "Hello from native binary!"
```

`tools/lllc-native` is the canonical user-facing command (a thin wrapper around
`tools/llvm-build.sh`). Eventually this lives under `lllc build --native`; for
now it's a standalone launcher because the frozen bootstrap `lllc` (in
`initial-compiler/`) can't grow new subcommands.

### Prerequisites

| Platform | Install |
|---|---|
| macOS    | Xcode Command Line Tools (ships `clang`, `make`) + [.NET 10 SDK](https://dotnet.microsoft.com/download) + `python3` |
| Linux    | `sudo apt-get install -y clang make python3` + [.NET 10 SDK](https://dotnet.microsoft.com/download) |

Before the first run: `dotnet build initial-compiler/src/LLLangTool/LLLangTool.fsproj -c Debug`
so that `lllc.dll` exists.

## Pipeline

The wrapper performs four stages end-to-end:

1. `lllc build --target llvm` emits `.ll`
2. `tools/llvm-add-declares.py` patches the IR (missing `declare`s, instruction-form GEPs, `void main`)
3. `sdks/Platform.LLVM.SDK/runtime/Makefile` builds the C runtime to `lllc_runtime.o`
4. `clang` links everything to a native binary

## Supported features

Validated against examples `spec/examples/valid/36-llvm-*.lll` through `48-llvm-*.lll`,
plus a growing set of non-LLVM examples that exercise the same pipeline via post-processor
patches (see "Stretch-tested non-LLVM examples" below).

| Feature | Example | Status |
|---|---|---|
| `printfn` of string literal | `36-llvm-native-hello.lll` | works |
| Integer arithmetic + user-defined functions | `37-llvm-arith.lll` | works |
| `if`/`else` with integer comparison + `phi` | `38-llvm-conditional.lll` | works |
| `strConcat` of two string literals | `39-llvm-strings.lll` | works |
| `intToStr` | 37, 38, 40, 42, 43, 44, 45 | works (via runtime `intToStr`) |
| Recursion (`factorial`) | `40-llvm-recursion.lll` | works |
| ADT `Maybe A` + `match` (1-field ctor) | `41-llvm-adt-maybe.lll` | works |
| ADT `Shape` + `match` (0/1/2-field ctors) | `42-llvm-adt-shape.lll` | works (arms must order nullary before multi-field — codegen limitation, see example) |
| `let`/`in` chained bindings | 37, 38, 39, 40, 41, 42 | works (via named `=` bindings; `_ =` arms are silently dropped by frozen codegen) |
| List literal `[1; 2; 3]` + `match x :: rest` + recursion | `43-llvm-list-sum.lll` | works (cons-style ADT cell, tag `-1`, reuses `__ll_alloc`) |
| User-defined `mapList` + `showList` over lists, nested if in match arm | `44-llvm-list-map.lll` | works (post-processor repairs stale match-end phi predecessors) |
| `List[Maybe[Int]]` (nested ADT inside list, heterogeneous values via `ptrtoint`/`inttoptr`) | `45-llvm-list-of-maybe.lll` | works |
| `readFile` — read entire file into string | `46-llvm-readfile.lll` | works (open/fstat/read/close, malloc'd buffer; empty string on error) |
| `writeFile` — create/truncate file, write string | `47-llvm-writefile.lll` | works (open O_CREAT\|O_TRUNC, write, close; must use named binding `w = writeFile ...`) |
| Command-line arguments via `getArgs` | `48-llvm-cli-args.lll` | works (C runtime captures argv; post-processor wires the `printArgs []` entry into `call @ll_getArgs()` — `getArgs` alone is silently dropped by frozen codegen, see limitation below) |
| Higher-order `listMap` (functions as values) | — | unsupported — `ys = listMap double xs` is silently dropped by frozen codegen; use a recursive user-defined `mapList` instead (see example 44) |
| I/O (`read_line`) | — | runtime stub only |
| GC | — | stubbed (raw `malloc`) |

## Components

- `stdlib/src/CodegenLLVM.lll` — emits LLVM IR (frozen in `initial-compiler/CodegenLLVM.fs`)
- `src/Codegen.lll` — SDK-level codegen mirror
- `src/Runtime.lll` — declares runtime externals
- `src/FFI.lll` — ll-lang to LLVM external name mappings
- `src/Prelude.lll` — high-level wrappers (`printfn`, `intToStr`, `strConcat`)
- `runtime/lllc_runtime.c` — C runtime (I/O, strings, allocator stubs)
- `runtime/Makefile` — builds `lllc_runtime.o` via `clang -O2`

## Known post-processor patches (applied by `tools/llvm-add-declares.py`)

These compensate for codegen shortcuts in the frozen `CodegenLLVM.fs`. Each is idempotent.

1. **Missing `declare` lines** — codegen calls `@printfn`, `@strConcat`, `@intToStr`, etc. without
   declaring them. Script prepends declares using a hardcoded signature table (falls back to
   parsing the call site when unknown).
2. **Instruction-form GEP with extra parens** —
   `%t0 = getelementptr inbounds ([N x i8], ptr @.s, i64 0, i64 0)` is illegal as an instruction
   (only legal as a constant-expression). Script unwraps the parens.
3. **Rename `define <ret> @main()` → `define void @ll_main()`** — the C runtime owns the real
   `int main(int argc, char** argv)` so it can capture `argv` for `getArgs` and return a
   proper OS exit code. Previously the script rewrote `void @main` → `i32 @main` + `ret i32 0`;
   moving to renaming lets the runtime wrap user code cleanly. A `__attribute__((weak))`
   fallback in C keeps linking alive if a .ll ever lacks a `main`. The rename also handles
   value-returning mains (e.g. `define i32 @main()` or `i64`, emitted when the user's `main`
   body evaluates to a scalar — example 21's `main = rbSize m`). The signature is coerced
   to `void` and any `ret <ty> <val>` inside is rewritten to `ret void`; the OS exit code
   still comes from the C wrapper and the user-level return value is discarded.
4. **Duplicate `@__ll_alloc` definition** — ADT examples trigger codegen to emit a local
   definition of `@__ll_alloc`, which collides with the C runtime's version at link time
   (`duplicate symbol '___ll_alloc'`). Script strips the generated definition; the runtime
   remains the single source of truth.
5. **Stale match-end `phi` predecessors** — when a match arm contains nested control flow
   (e.g. `if`/`else`), the codegen writes `phi [ %v, %match_body_K ]` but the actual
   predecessor is the arm's `if_end_N` block, yielding `PHI node entries do not match
   predecessors`. The script walks the CFG per function and replaces each stale entry label
   with the unique real predecessor reachable from the original body block.
6. **`main() = printArgs []` → real argv list** — frozen codegen drops references to
   value-shaped prelude identifiers like `getArgs` (TEVar not in VarEnv). Workaround: user
   writes `main() = printArgs []`; the codegen emits `call void @printArgs(ptr null)` inside
   `@ll_main`, which the script rewrites to `%x = call ptr @ll_getArgs()` + `call void
   @printArgs(ptr %x)`. `@ll_getArgs` is provided by the C runtime and materialises a cons
   list from `argv[1..]` using the same `{ i64 tag, i64 payload, ptr tail }` node ABI as list
   literals. See example 48.
7. **Synthesise `strConcat` in match arms shaped `"lit" + payload`** — a match arm whose
   body contains exactly a literal-string GEP + a payload `inttoptr i64 %tN to ptr` + a
   branch to `match_end` is a frozen-codegen drop of the `+` operator for string concat.
   The match-end phi entry for that body is `null`. Script detects the signature tightly
   (one GEP of `[K x i8] @.strX`, one inttoptr, direct branch to `match_end_M`, phi entry
   `null`) and inserts `%cat_<body> = call ptr @strConcat(ptr %gep, ptr %payload)` before
   the branch, then rewrites the phi entry to use `%cat_<body>`. Example 10's
   `TIdent s -> "id:" + s` / `TNum s -> "num:" + s` arms depend on this patch.

## Stretch-tested non-LLVM examples

These examples predate the LLVM backend but now build end-to-end through the
pipeline (post-processor + C runtime). They were never written with LLVM in
mind, so any "works" here is pure reach.

| Example | Status | Output |
|---|---|---|
| `hello.lll` | works | `Hello, ll-lang!` |
| `06-stdlib.lll` | works (main only prints a literal; higher-order helpers are stubs — see limitations) | `stdlib example` |
| `10-multiline-sum.lll` | works (patch #7 wires `strConcat` into match arms) | `id:foo` |
| `02-adts.lll` | builds (no `main` → exit 0, no output — treated as a library) | _(none)_ |
| `21-multi-param-types.lll` | builds (value-returning `main` coerced to void via patch #3 generalisation) | _(none, exit 0)_ |
| `23-external-opaque.lll` | builds (no `main` → exit 0) | _(none)_ |

### Known non-LLVM blockers (documented; not yet patched)

- `07-text-processing.lll` — uses `listFold` over `strChars`; frozen codegen
  drops the higher-order `listFold` call (`countDigits` just calls `strChars`
  and returns `0`). Same class as the `listMap` limitation. Blocker: frozen
  codegen's higher-order-function handling.
- `03-tags.lll` — codegen produces an IR type mismatch (`ret ptr %t0` where
  `%t0` is a `double`). Not a post-processor patchable issue.
- `04-traits.lll` — trait `impl` generates `call @f` as if `f` were a global,
  yielding `Undefined symbols: _f` at link time. Blocker: trait / HOF codegen.
- `05-modules.lll` — compilation fails earlier (`E002 UnboundVar head`).
  Module resolution, not a codegen issue.
- `30-file-io-external.lll` — `external` declarations have no LLVM mapping
  (`E026 UnknownExternalMapping target:llvm name:fileReadAll`). Adding
  symbol mappings + runtime stubs for `fileReadAll`/`fileExists` would
  unblock this and similar `external`-heavy examples.
- `31-fixpoint-test.lll` — `E002 UnboundVar compileSingleFile` (imports
  not resolved).
- `33-io-sequence.lll` — IO monad example; pulls in `Std.IO` and emits
  duplicate `@.str0` globals (codegen bug when merging imported modules).
- `35-monad-module.lll` — heavy higher-order / monad code, same HOF-drop
  class as example 44 but at much larger scale; no single post-processor
  patch unlocks it.

## Running the pipeline

```bash
tools/lllc-native spec/examples/valid/37-llvm-arith.lll /tmp/arith
/tmp/arith   # -> 42
```

Requires: `dotnet` (.NET 10 SDK), `python3`, `clang`, `make`.

## CI

`.github/workflows/llvm.yml` builds and runs all ten `36-45` examples on `ubuntu-latest` and
asserts stdout matches the expected output. Triggered on changes to the SDK, codegen source,
the build tools, or the example corpus (path filter: `spec/examples/valid/[3-9][0-9]-llvm-*.lll`,
so new examples auto-enroll). Also runnable manually via `gh workflow run llvm.yml`.

## Scope / non-goals (yet)

Real GC, exceptions, FFI into arbitrary C libraries, and tail-call optimization are all
out-of-scope for the MVP. Add examples and extend `tools/llvm-add-declares.py` +
`runtime/lllc_runtime.c` as codegen grows into more language features.

### Known frozen-codegen limitations (workarounds required in example sources)

- **`_ =` bindings are silently dropped.** Use a named binding when sequencing side-effects:
  `r1 = printfn s1; r2 = printfn s2; r2` (not `_ = printfn s1`). Examples 41/42 use this idiom.
- **ADT match arms dereference tail pointers unconditionally.** When an ADT has both nullary
  and multi-field constructors, list the nullary arm *before* any multi-field arm so the match
  succeeds before the codegen tries to load from a null tail. Example 42 demonstrates.
- **Higher-order calls in `main` are silently dropped.** `ys = listMap double xs` produces IR
  that only allocates `xs` and returns — `listMap` never appears. Use a recursive user-defined
  wrapper instead (`mapList(xs List[Int]) List[Int] = match xs ...`). Example 44 demonstrates.
- **Match-end `phi` uses stale predecessor labels** when arms contain nested control flow. The
  post-processor repairs these via CFG analysis (see patch #5 above). No source-level workaround
  is required.
