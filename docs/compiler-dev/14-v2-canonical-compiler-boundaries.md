# v2 Canonical Compiler Boundaries

**Status:** planning aid for Milestone 1  
**Audience:** implementation agents and maintainers  
**Purpose:** define which subsystems should be owned by the canonical ll-lang compiler in `v2`, what their current implementation status is, and what migration step closes each gap.

## Summary

The current repo contains two compiler realities at once:

1. the active stage0 compiler in `src/LLLangCompiler/*.fs`
2. a substantial self-hosted slice under `stdlib/src/*.lll`

This document turns that into an explicit ownership map. It is not a second
architecture spec. It is the concrete decomposition needed to implement
Milestone 1 of the `v2` roadmap.

## Boundary rules

For `v2`, each compiler subsystem must have exactly one canonical owner in
ll-lang. Stage0 may mirror it for bootstrap purposes, but may not remain the
active source of truth.

A subsystem is considered migrated only when all of the following are true:

- there is a canonical ll-lang module (or module group) that owns it
- docs point to that ll-lang module as authoritative
- tests exercise the ll-lang path directly
- stage0 is either a bootstrap mirror or a thin compatibility layer

## Current ownership map

| Subsystem | Current stage0 owner | Current ll-lang owner | `v2` canonical owner | Current state | Gap to close |
|-----------|----------------------|------------------------|----------------------|---------------|--------------|
| Lexer | `src/LLLangCompiler/Lexer.fs` | `stdlib/src/Lexer.lll` | `Std.Lexer` or `Compiler.Syntax.Lexer` | ll-lang implementation exists, but docs still describe stage0-first architecture | make ll-lang lexer the documented owner and ensure feature parity tests gate it |
| Parser | `src/LLLangCompiler/Parser.fs`, `FParsecParser.fs` | `stdlib/src/Parser.lll` | `Std.Parser` or `Compiler.Syntax.Parser` | ll-lang parser exists as recursive-descent self-host slice | define canonical parser contract and reduce stage0-specific parser assumptions |
| Elaborator / name checks | `src/LLLangCompiler/Elaborator.fs` | `stdlib/src/Elaborator.lll` | `Std.Elaborator` or `Compiler.Frontend.Elaborator` | ll-lang elaborator exists in simplified form | close behavioral gap vs stage0 and document exact invariants |
| HM inference / typed core | `src/LLLangCompiler/HMInfer.fs`, `Types.fs`, `TypedAST.fs` | no canonical ll-lang counterpart yet | `Compiler.Types`, `Compiler.Typed`, `Compiler.Infer` | still stage0-owned | design and land canonical ll-lang typed-core modules |
| Backend-neutral lowering | implicit in stage0 compiler internals | no canonical ll-lang layer | `Compiler.Lower` | missing as an explicit layer | introduce a distinct lowering boundary instead of backend emitters re-deriving semantics |
| F# backend | `src/LLLangCompiler/Codegen.fs` | `stdlib/src/Codegen.lll` | `Std.Codegen` or `Compiler.Backend.FSharp` | ll-lang emitter exists and is already used in self-host story | formalize it as canonical owner |
| TypeScript backend | `src/LLLangCompiler/CodegenTS.fs` | `stdlib/src/CodegenTS.lll` | `Std.CodegenTS` or `Compiler.Backend.TypeScript` | ll-lang emitter exists | formalize ownership and add parity targets |
| Python backend | `src/LLLangCompiler/CodegenPy.fs` | `stdlib/src/CodegenPy.lll` | `Std.CodegenPy` or `Compiler.Backend.Python` | ll-lang emitter exists | formalize ownership and add parity targets |
| Java backend | `src/LLLangCompiler/CodegenJava.fs` | `stdlib/src/CodegenJava.lll` | `Std.CodegenJava` or `Compiler.Backend.Java` | ll-lang emitter exists | formalize ownership and add parity targets |
| C# backend | `src/LLLangCompiler/CodegenCSharp.fs` | no canonical ll-lang emitter yet | `Compiler.Backend.CSharp` | still stage0-owned | add canonical ll-lang C# backend or explicitly defer it |
| LLVM backend | `src/LLLangCompiler/CodegenLLVM.fs` | `stdlib/src/CodegenLLVM.lll` | `Std.CodegenLLVM` or `Compiler.Backend.LLVM` | ll-lang emitter exists for subset path | keep experimental, but still make ownership explicit |
| Project manifest parsing | `src/LLLangCompiler/Manifest.fs` | `stdlib/src/Toml.lll` is a parser substrate, not the resolver | `Compiler.Project.Manifest` | stage0-owned | add self-hosted manifest/resolver layer over `Std.Toml` |
| Project graph / topo loading | `src/LLLangCompiler/ProjectLoader.fs` | no canonical ll-lang owner yet | `Compiler.Project.Loader` | stage0-owned | implement self-hosted module graph loader |
| CLI command orchestration | `src/LLLangTool/Program.fs` | `lllc self` delegates to ll-lang tool layer, but not yet canonical | `Compiler.Cli` or `Tool.Main` | still stage0-first | promote ll-lang CLI path to canonical and demote stage0 wrapper |
| Full pipeline entrypoint | `src/LLLangCompiler/Compiler.fs` | `stdlib/src/Compiler.lll` | `Std.Compiler` or `Compiler.Main` | ll-lang pipeline exists for core path | expand it to own the full canonical flow |

## Naming target for v2

Two naming strategies are acceptable. Pick one and use it consistently:

1. **Keep `Std.*` as the canonical compiler module namespace**
   Best if the self-hosted compiler continues to live under `stdlib/src`.
2. **Split into a dedicated `Compiler.*` namespace**
   Best if compiler implementation is separated from general-purpose stdlib.

`v2` should not ship with both as long-term parallel identities.

Recommended direction:

- keep `Std.*` for reusable library modules
- move canonical compiler implementation toward `Compiler.*`
- allow `Std.Compiler` only as a compatibility façade or high-level convenience entrypoint

## Required pass boundaries

The canonical compiler should be documented and implemented with these explicit
phase boundaries:

1. `Compiler.Syntax.Lexer`
2. `Compiler.Syntax.Parser`
3. `Compiler.Frontend.Elaborator`
4. `Compiler.Types`
5. `Compiler.Typed`
6. `Compiler.Infer`
7. `Compiler.Lower`
8. `Compiler.Backend.<Target>`
9. `Compiler.Project.Manifest`
10. `Compiler.Project.Loader`
11. `Compiler.Cli`

Not every phase must map to exactly one file, but each phase must have one
owner module group.

## Immediate documentation tasks for Milestone 1

To close the docs half of Milestone 1, the repo should next do all of:

- update [01-architecture-overview.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/compiler-dev/01-architecture-overview.md) so it no longer reads as stage0-only architecture
- update [stdlib-reference.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/stdlib-reference.md) to distinguish reusable stdlib from canonical compiler implementation modules
- document whether the canonical namespace will be `Std.*` or `Compiler.*`
- document how `lllc self` evolves into the canonical compiler path or gets folded into `lllc`

## Exit criteria for Milestone 1

Milestone 1 is done only when:

- each subsystem in the table has a canonical ll-lang owner
- docs consistently identify the same owners
- missing ll-lang-owned subsystems are either implemented or explicitly deferred
- stage0-only areas are visible and limited

## Validation targets

- architecture docs updated
- self-hosted subsystem matrix kept current
- direct tests for each ll-lang-owned subsystem
- at least one end-to-end self-hosted pipeline test that does not rely on undocumented stage0-only behavior
