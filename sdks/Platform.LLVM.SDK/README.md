# Platform.LLVM.SDK

**Status: scaffolding / experimental.** The LLVM backend produces `.ll` files but the
runtime layer is not yet wired into actual codegen output.

## What exists

- `stdlib/src/CodegenLLVM.lll` — emits LLVM IR for a subset of ll-lang (calls `@printfn` etc. without declarations)
- `src/Codegen.lll` — SDK-level codegen mirror
- `src/Runtime.lll` — declares 22 runtime externals (str_concat, print_str, gc_alloc, adt_alloc, list_*, etc.) — **not yet referenced by codegen**
- `src/FFI.lll` — ll-lang↔LLVM external name mappings
- `src/Prelude.lll` — high-level wrappers (`printfn`, `intToStr`, `strConcat`)

## What's missing (tracked in #146)

1. `CodegenLLVM.lll` does not emit `declare` lines for externals it calls (e.g. `@printfn`)
2. Codegen does not use the new Runtime.lll names (`@print_str`, `@str_concat` etc.)
3. No actual C/Rust runtime implementation that exposes these symbols at link time
4. CI does not validate generated `.ll` with `llvm-as` or run with `lli`

## Path to working LLVM backend

1. Implement runtime in C/Rust (separate crate) exposing the 22 externals
2. Update `CodegenLLVM.lll` to emit `declare` lines and use new runtime names
3. Add CI step: `llvm-as <generated.ll>` to validate IR
4. Add example: `lllc build --target llvm hello.lll && clang hello.bc runtime.o -o hello`

For now, treat this SDK as a vocabulary/scaffolding for the future LLVM backend, not as a
production-ready compilation target.
