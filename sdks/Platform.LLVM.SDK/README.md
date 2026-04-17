# Platform.LLVM.SDK

**Status: MVP — native binaries work for a growing subset of ll-lang.**

The end-to-end pipeline (`tools/llvm-build.sh <file.lll>`) compiles `.lll` sources to native
executables by:

1. `lllc build --target llvm` emits `.ll`
2. `tools/llvm-add-declares.py` patches the IR (missing `declare`s, instruction-form GEPs, `void main`)
3. `sdks/Platform.LLVM.SDK/runtime/Makefile` builds the C runtime to `lllc_runtime.o`
4. `clang` links everything to a native binary

## Supported features

Validated against examples `spec/examples/valid/36-llvm-*.lll` through `39-llvm-*.lll`.

| Feature | Example | Status |
|---|---|---|
| `printfn` of string literal | `36-llvm-native-hello.lll` | works |
| Integer arithmetic + user-defined functions | `37-llvm-arith.lll` | works |
| `if`/`else` with integer comparison + `phi` | `38-llvm-conditional.lll` | works |
| `strConcat` of two string literals | `39-llvm-strings.lll` | works |
| `intToStr` | 37, 38 | works (via runtime `intToStr`) |
| ADTs / pattern matching | — | not exercised yet |
| `let`/`in` chained bindings | 37, 38, 39 | works (via `=` bindings) |
| Lists (`list_nil`/`list_cons`) | — | runtime stubs only |
| I/O (`read_line`, `read_file`) | — | runtime stubs only |
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
3. **`define void @main()`** — codegen emits `void` return but C runtime expects `int` (or the
   process inherits garbage from the return register). Script rewrites the signature to
   `i32 @main()` and swaps `ret void` for `ret i32 0`.

## Running the pipeline

```bash
tools/llvm-build.sh spec/examples/valid/37-llvm-arith.lll /tmp/arith
/tmp/arith   # -> 42
```

Requires: `dotnet`, `python3`, `clang`, `make`.

## CI

`.github/workflows/llvm.yml` builds and runs all four `36-39` examples on `ubuntu-latest` and
asserts stdout matches the expected output. Triggered on changes to the SDK, codegen source,
the build tools, or the example corpus.

## Scope / non-goals (yet)

Real GC, exceptions, FFI into arbitrary C libraries, sum-type codegen, and tail-call
optimization are all out-of-scope for the MVP. Add examples and extend
`tools/llvm-add-declares.py` + `runtime/lllc_runtime.c` as codegen grows into more language
features.
