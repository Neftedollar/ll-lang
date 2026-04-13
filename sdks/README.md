# Platform SDK Packages

This directory contains built-in `Platform.*.SDK` packages bundled with the compiler:

- `Platform.FSharp.SDK`
- `Platform.TypeScript.SDK`
- `Platform.Python.SDK`
- `Platform.Java.SDK`
- `Platform.CSharp.SDK`
- `Platform.LLVM.SDK`

Current host compiler still uses built-in F# codegen modules. These SDK packages
already carry package metadata/layout so runtime/template loading can move here
incrementally.
