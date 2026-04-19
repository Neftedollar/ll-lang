# v2 Canonical Compiler Boundaries

**Status:** frozen specification — binding for v2 development  
**Audience:** implementation agents and maintainers  
**Closes:** #46 (M1.A), #47 (M1.B), #48 (M1.C), #49 (M1.D), #51 (M1.F)

## Summary

The repo contains two compiler realities:

1. the **stage0 bootstrap** in `src/LLLangCompiler/*.fs` — frozen, not the long-term owner
2. the **canonical self-hosted compiler** under `stdlib/src/*.lll` — the owner for v2

This document is the binding ownership map. Every new feature, refactor, and
migration decision must use this table as the authority. A subsystem "jointly
owned" by stage0 and self-hosted code is a temporary migration state only —
not a steady state.

## Boundary rules

For `v2`, each compiler subsystem has exactly one canonical owner in ll-lang.
Stage0 is a bootstrap mirror only; it is not an active source of truth for any
subsystem where a self-hosted module exists.

A subsystem is considered **migrated** only when all of the following are true:

- a canonical ll-lang module (or module group) owns it
- docs point to that module as authoritative
- tests exercise the ll-lang path directly
- stage0 is a bootstrap mirror or thin compatibility layer

## Frozen ownership table

| Subsystem | Stage0 bootstrap | Canonical v2 owner | Stage0 status | Migration state |
|-----------|------------------|--------------------|---------------|-----------------|
| Lexer | `src/LLLangCompiler/Lexer.fs` | `Compiler.Syntax.Lexer` (file: `stdlib/src/Lexer.lll`) | transitional bootstrap | self-hosted impl exists; needs feature-parity gate |
| Parser | `src/LLLangCompiler/Parser.fs`, `FParsecParser.fs` | `Compiler.Syntax.Parser` (file: `stdlib/src/Parser.lll`) | transitional bootstrap | self-hosted impl exists; parser contract must be defined |
| Elaborator | `src/LLLangCompiler/Elaborator.fs` | `Compiler.Frontend.Elaborator` (file: `stdlib/src/Elaborator.lll`) | transitional bootstrap | self-hosted impl exists; behavioral gap vs stage0 to close |
| Type representations | `src/LLLangCompiler/Types.fs` | `Compiler.Types` (file: `stdlib/src/CompilerTypes.lll`) | transitional bootstrap | ll-lang impl landed; covered by subsystem suite |
| Typed IR shapes | `src/LLLangCompiler/TypedAST.fs` | `Compiler.Typed` (file: `stdlib/src/CompilerTyped.lll`) | transitional bootstrap | ll-lang impl landed; covered by subsystem suite |
| HM inference | `src/LLLangCompiler/HMInfer.fs` | `Compiler.Infer` (file: `stdlib/src/CompilerInfer.lll`) | transitional bootstrap | ll-lang impl landed; Algorithm W active; covered by subsystem suite |
| Backend-neutral lowering | implicit in `Codegen*.fs` | `Compiler.Lower` (file: `stdlib/src/CompilerLower.lll`) | transitional bootstrap | ll-lang impl landed; BinOp desugaring active; full match/lambda lowering transitional |
| F# backend | `src/LLLangCompiler/Codegen.fs` | `Compiler.Backend.FSharp` (file: `stdlib/src/Codegen.lll`) | transitional bootstrap | self-hosted emitter exists; formalize as canonical |
| TypeScript backend | `src/LLLangCompiler/CodegenTS.fs` | `Compiler.Backend.TypeScript` (file: `stdlib/src/CodegenTS.lll`) | transitional bootstrap | self-hosted emitter exists; parity targets needed |
| Python backend | `src/LLLangCompiler/CodegenPy.fs` | `Compiler.Backend.Python` (file: `stdlib/src/CodegenPy.lll`) | transitional bootstrap | self-hosted emitter exists; parity targets needed |
| Java backend | `src/LLLangCompiler/CodegenJava.fs` | `Compiler.Backend.Java` (file: `stdlib/src/CodegenJava.lll`) | transitional bootstrap | self-hosted emitter exists; parity targets needed |
| C# backend | `src/LLLangCompiler/CodegenCSharp.fs` | `Compiler.Backend.CSharp` (file: `stdlib/src/CodegenCSharp.lll`) | transitional bootstrap | self-hosted emitter exists; parity targets needed |
| LLVM backend | `src/LLLangCompiler/CodegenLLVM.fs` | `Compiler.Backend.LLVM` (file: `stdlib/src/CodegenLLVM.lll`) | experimental bootstrap | self-hosted subset emitter exists; ownership is explicit but backend remains experimental |
| Project manifest | `src/LLLangCompiler/Manifest.fs` | `Compiler.Project.Manifest` | gap to fill — stage0 only | `Std.Toml` is the parser substrate; self-hosted resolver layer needed over it |
| Project graph loader | `src/LLLangCompiler/ProjectLoader.fs` | `Compiler.Project.Loader` | gap to fill — stage0 only | no canonical ll-lang owner; module graph loading to be self-hosted |
| CLI orchestration | `src/LLLangTool/Program.fs` | `Compiler.Cli` | gap to fill — stage0 first | ll-lang tool layer exists but is not canonical; stage0 wrapper must be demoted |
| Full pipeline entrypoint | `src/LLLangCompiler/Compiler.fs` | `Compiler.Main` (file: `stdlib/src/Compiler.lll`) | transitional bootstrap | self-hosted pipeline exists for core path; must own the full canonical flow |

### Reading the table

- **transitional bootstrap**: stage0 is a mirror only. The canonical owner is the ll-lang module. Do not add features to stage0 in this subsystem.
- **gap to fill — stage0 only**: stage0 is the only implementation. A canonical ll-lang module must be designed and landed. Stage0 remains until the gap is closed.
- **gap to fill — missing as a layer**: the phase doesn't exist anywhere yet. Must be introduced as an explicit ll-lang module.
- **experimental bootstrap**: stage0 mirrors an experimental subset. Feature parity is intentionally deferred; ownership is explicit.

## Namespace split (frozen)

- **`Std.*`** — reusable library modules only: `Std.List`, `Std.Maybe`, `Std.Result`, `Std.Map`, `Std.Str`, `Std.State`, `Std.Parsec`, `Std.Lazy`, `Std.Json`, `Std.Toml`, `Std.Test`.
- **`Compiler.*`** — canonical self-hosted compiler implementation. All phases listed in the table above belong here.

`Std.Compiler`, `Std.Lexer`, `Std.Parser`, `Std.Elaborator`, `Std.Codegen*` are **temporary compatibility names** for the duration of the bootstrap. They must not remain as the long-term identity of any compiler subsystem.

## Typed-core ownership (M1.B)

The typed-core area currently has three separate responsibilities blended in stage0. For v2 these are frozen as three distinct modules:

| Module | Responsibility | Must own | Must not own |
|--------|---------------|----------|--------------|
| `Compiler.Types` | type representations and substitutions | `TypeExpr`, `TypeScheme`, `Subst`, `Env`, `FreshState`, `applyType`, `unify` | inference algorithm, IR shapes |
| `Compiler.Typed` | typed IR shapes | `TypedExpr`, `TypedDecl`, `TypedModule`, `ExprId`, `DispatchInfo` | type operations, inference algorithm |
| `Compiler.Infer` | HM inference and typed-core construction | Algorithm W, generalization, instantiation, dispatch resolution | type representation definitions, IR shapes |

No code that lives in `Compiler.Types` may depend on `Compiler.Infer`. The dependency direction is: `Compiler.Infer` → `Compiler.Typed` → `Compiler.Types`.

## Lowering as an explicit phase (M1.C)

`Compiler.Lower` is a **required phase** between elaboration/inference and backend codegen. It is not optional.

**What lowering must make explicit before any backend sees the IR:**

- match compilation (match → decision tree or if-chain)
- closure captures (lambda lifting or explicit closure records)
- operator desugaring (infix → application)
- unit elimination (expressions of type `Unit` that must still sequence side effects)
- tag wrapping/unwrapping (bracket expressions to constructor applications)

**What backends must not do:**

- re-derive semantics that lowering should have made explicit
- re-implement match compilation or operator desugaring
- carry per-target elaboration logic

Until `Compiler.Lower` exists as a self-hosted ll-lang module, backends may continue to inline lowering logic, but every such inline is **explicitly transitional** — tracked under issue #48.

## Project and CLI as first-class phases (M1.D)

The project loader and CLI are compiler phases, not shell glue. Their ownership is frozen:

| Phase | Canonical owner | Responsibility | Stage0 counterpart |
|-------|----------------|----------------|--------------------|
| Manifest parsing | `Compiler.Project.Manifest` | parse `lll.toml` into a structured manifest value | `src/LLLangCompiler/Manifest.fs` |
| Module graph loading | `Compiler.Project.Loader` | glob sources, validate module paths, topological sort | `src/LLLangCompiler/ProjectLoader.fs` |
| CLI orchestration | `Compiler.Cli` | command dispatch, error formatting, exit codes | `src/LLLangTool/Program.fs` |

These phases sit **outside** the language pipeline but **inside** the compiler architecture. Their input/output contracts are in [15-v2-pass-contracts.md](15-v2-pass-contracts.md).

## Duplication audit and migration notes (M1.F)

The following definitions are currently duplicated between stage0 and self-hosted trees. Each entry is classified: **intentional mirror** (bootstrap copy, will be deleted when stage0 is retired) or **drift risk** (must be consolidated).

| Definition | Stage0 location | Self-hosted location | Classification | Migration note |
|------------|-----------------|----------------------|----------------|----------------|
| `Maybe A = Some A \| None` | `src/LLLangCompiler/AST.fs` (as F# DU) | `stdlib/src/Maybe.lll` | intentional mirror | stage0 F# DU is bootstrap; ll-lang module is canonical; no action until stage0 is retired |
| `List` / `Cons` / `Nil` | `src/LLLangCompiler/AST.fs` (builtinEnv) | `stdlib/src/List.lll` | intentional mirror | same as above |
| `LLError` / error format | `src/LLLangCompiler/` (multiple files) | not yet in ll-lang | drift risk | issue #21 tracks structured error fields; ll-lang `Compiler.Diagnostics` module needed |
| `Token` / `Tok` | `src/LLLangCompiler/Token.fs` | `stdlib/src/Lexer.lll` (partial) | drift risk | Lexer.lll must own the full token type; stage0 `Token.fs` becomes a bootstrap mirror |
| `AST` node types | `src/LLLangCompiler/AST.fs` | `stdlib/src/Parser.lll` (partial) | drift risk | Parser.lll must own surface AST; `AST.fs` becomes a bootstrap mirror |
| `TypeExpr` / `TypeScheme` | `src/LLLangCompiler/Types.fs` | not yet in ll-lang | gap — must fill | `Compiler.Types` must be created; no duplication until then |

**Rule for new code:** never copy a type definition from stage0 into a new ll-lang file. Use `import` from the appropriate module. If the module does not exist yet, create it rather than copying.

## Validation targets for Milestone 1

- [x] this document is the binding ownership spec (closes M1.A)
- [x] typed-core module responsibilities are separated (closes M1.B docs)
- [x] `Compiler.Lower` is defined as a required phase (closes M1.C docs)
- [x] project/CLI phases are first-class architecture (closes M1.D docs)
- [x] duplication is inventoried with migration notes (closes M1.F)
- [ ] `01-architecture-overview.md` updated to not read as stage0-only
- [ ] `stdlib-reference.md` updated to distinguish reusable stdlib from compiler impl modules
- [ ] pass fixture shapes defined per phase (issue #50 / M1.E)
- [x] `Compiler.Types` implemented in ll-lang (`stdlib/src/CompilerTypes.lll`, covered by subsystem suite)
- [x] `Compiler.Typed` implemented in ll-lang (`stdlib/src/CompilerTyped.lll`, covered by subsystem suite)
- [x] `Compiler.Infer` implemented in ll-lang (`stdlib/src/CompilerInfer.lll`, covered by subsystem suite)
- [x] `Compiler.Lower` implemented in ll-lang (`stdlib/src/CompilerLower.lll`, covered by subsystem suite)
- [ ] `Compiler.Project.Manifest`, `Compiler.Project.Loader`, `Compiler.Cli` implemented in ll-lang
- [ ] direct tests for each ll-lang-owned subsystem
