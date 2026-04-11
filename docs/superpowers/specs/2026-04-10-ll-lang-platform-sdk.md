# Platform.*.SDK Package Architecture — Design Spec

**Date:** 2026-04-10
**Status:** Draft
**Depends on:** Phase 8 project system (2026-04-09-ll-lang-project-system.md)

## 1. Problem

ll-lang has four codegen backends (F#, TS, Python, Java) hardcoded into the
compiler. Adding a new target means editing Compiler.fs, adding another
`CodegenFoo.fs`, and recompiling `lllc`. The codegen modules already exist in
ll-lang itself (`stdlib/src/Codegen.lll`, `CodegenTS.lll`, etc.) but are not
used by the compiler — they're proof-of-concept.

Goal: each target becomes an installable **Platform SDK package** that carries
its own codegen, runtime helpers, and FFI declarations. The compiler discovers
and dispatches to them through the package system. New targets (WASM, CLR IL,
LLVM) can be added without modifying the compiler.

## 2. What a Platform SDK Contains

```
platform-fsharp/
  ll.toml
  src/
    Codegen.lll          -- AST → F# source emitter
    Runtime.lll          -- runtime helper type declarations
    FFI.lll              -- FFI bridge type declarations
    Keywords.lll         -- keyword/reserved-word table
  runtime/
    prelude.fs           -- F# prelude (listMap, strConcat, etc.)
  meta.toml              -- target metadata (extensions, build commands)
```

### 2.1 `ll.toml`

```toml
[project]
name = "platform-fsharp"
version = "0.1.0"

[sdk]
target   = "fsharp"              # canonical target name
aliases  = ["fs"]                # CLI shorthand
ext      = ".fs"                 # output file extension
host-ext = ".fsproj"             # project file extension (optional)
```

The `[sdk]` table is what distinguishes a platform package from a regular
library. When `lllc` loads a dep whose `ll.toml` has `[sdk]`, it registers that
package as a platform provider rather than treating it as source code to compile.

### 2.2 `src/Codegen.lll`

Already exists for all four targets. The emitter must export a function with
the signature:

```lll
export emitModule(m Module) Str
```

The compiler calls this function to produce target source code from a TypedModule.
Until ll-lang can run its own code (self-hosting), the F#-side codegen
(`Codegen.fs`, `CodegenTS.fs`, etc.) remains the actual emitter. The `.lll`
versions serve as the specification and will become the real emitters once the
interpreter or self-hosted compiler is ready.

### 2.3 `runtime/`

Static files copied or referenced during build. For F#, this is the prelude
block (stdlib bindings). For TypeScript, the runtime helpers. For Python, the
dataclass/typing imports and stdlib lambdas.

These are **not** `.lll` files — they are target-language source that the
generated code depends on at runtime. The SDK ships them as-is.

### 2.4 `meta.toml`

Target-specific build metadata:

```toml
[build]
# Command to compile the emitted output (optional)
compile = "dotnet build {project_file} -c Release"
# Command to run the compiled output (optional)
run     = "dotnet run --project {project_file}"
# Template for the project file (optional, inline or path)
project-template = "templates/project.fsproj.tmpl"
```

This replaces the hardcoded `.fsproj` generation in `Program.fs` line 68-77.

## 3. `ll.toml` Format for Platform Selection

### 3.1 User project manifest

```toml
[project]
name = "myapp"
version = "0.1.0"

[deps]
json = "https://github.com/user/ll-json#v0.1.0"

[platform]
use = ["fsharp", "typescript"]

[platform.fsharp]
sdk = "https://github.com/ll-lang/platform-fsharp#v0.1.0"

[platform.typescript]
sdk = "https://github.com/ll-lang/platform-typescript#v0.1.0"
```

### 3.2 Resolution rules

1. `[platform] use` lists target names (matching `[sdk] target` or `aliases`).
2. Each target listed in `use` must have either:
   - A corresponding `[platform.<target>]` section with an `sdk` URL, OR
   - A built-in SDK bundled with the compiler (for bootstrap targets).
3. If no `[platform]` section exists, default is `use = ["fsharp"]` with the
   built-in F# codegen.

### 3.3 Built-in vs external SDKs

For the initial implementation, all four existing targets remain **built-in**.
The compiler ships with `Codegen.fs`, `CodegenTS.fs`, `CodegenPy.fs`,
`CodegenJava.fs` as it does today. The `[platform] use` list controls which
targets `lllc build` emits for, but the codegen is still hardcoded.

External SDKs become possible once ll-lang can evaluate `.lll` code at compile
time (interpreter or self-hosting). At that point the compiler loads the SDK's
`Codegen.lll` and calls `emitModule` directly.

**Migration path:**
1. NOW: `[platform] use` controls multi-target dispatch, built-in codegens.
2. NEXT: SDKs installed as deps, compiler loads `meta.toml` for build commands.
3. LATER: compiler evaluates SDK's `Codegen.lll` instead of built-in F# codegen.

## 4. FFI / Interop Design

### 4.1 External declarations

```lll
module MyApp.Main

import Platform.TypeScript.FFI

-- Declare an external function available in the target environment
external console_log(msg Str) Unit
external fetch(url Str) Promise[Response]
external JSON_parse(s Str) Any
```

`external` declares a function that exists in the target runtime but has no
ll-lang body. The codegen emits a binding that references the native name.

### 4.2 Name mapping

External names use underscores; the codegen maps them to the target's naming
convention:

| ll-lang declaration          | F# emit                      | TS emit                  |
|------------------------------|------------------------------|--------------------------|
| `external console_log`       | `Console.WriteLine`          | `console.log`            |
| `external JSON_parse`        | `System.Text.Json...`        | `JSON.parse`             |

The mapping is defined in the SDK's `FFI.lll` as a table:

```lll
module Platform.TypeScript.FFI

-- FFI name mapping table
ffiMap = [
  ("console_log", "console.log"),
  ("JSON_parse", "JSON.parse"),
  ("fetch", "fetch")
]
```

### 4.3 Grammar addition

One new declaration form:

```
ExternalDecl ::= "external" Ident ParamList* TypeExpr
```

The elaborator records these as type signatures without bodies. The codegen
consults the SDK's FFI map to emit the correct native call. Unmatched externals
are emitted as-is (passthrough).

### 4.4 Type bridging

Each SDK defines how ll-lang types map to target types. This already exists
implicitly in each `emitType` function. Making it explicit:

```lll
module Platform.TypeScript.Types

typeMap = [
  ("Int",   "number"),
  ("Float", "number"),
  ("Str",   "string"),
  ("Bool",  "boolean"),
  ("Char",  "string"),
  ("Unit",  "void")
]
```

### 4.5 Opaque types

For target-specific types with no ll-lang equivalent:

```lll
-- Opaque type: exists in the target, not in ll-lang
opaque Promise[A]
opaque Response
opaque Any
```

Opaque types can be passed around and returned but not constructed or
destructured in ll-lang. The codegen emits the target's native type name.

## 5. Multi-Target Build Flow

```
lllc build
  1. Read ll.toml
  2. Parse [platform] use = ["fsharp", "typescript"]
  3. For each target:
     a. Resolve SDK (built-in or from .ll-deps/)
     b. Load runtime prelude from SDK
     c. Compile all .lll files (lex → parse → elaborate → infer)
        - Step (c) is shared across targets — TypedModule is target-agnostic
     d. Run target-specific codegen (emitModule / emitProjectModules)
     e. Write output to bin/<target>/
  4. If SDK has [build] compile command, optionally run it

Result:
  bin/
    fsharp/
      myapp.fs
      myapp.fsproj
    typescript/
      myapp.ts
      package.json       (from SDK template)
```

### 5.1 Shared compilation

Steps 1-3c (lex through inference) produce `TypedModule` values that are
**target-independent**. The same TypedModules are fed to every codegen.
This is the key architectural insight: the front-end runs once, codegens
fan out.

### 5.2 Output directory structure

Single-target (current behavior, backward compatible):
```
bin/myapp.fs
bin/myapp.fsproj
```

Multi-target:
```
bin/fsharp/myapp.fs
bin/fsharp/myapp.fsproj
bin/typescript/myapp.ts
bin/typescript/package.json
```

When `[platform] use` has exactly one entry, output goes directly into `bin/`
(no subdirectory) for backward compatibility.

## 6. What Needs to Change in the Compiler

### 6.1 NOW (can implement today)

| Component | Change | Effort |
|-----------|--------|--------|
| `Manifest.fs` | Parse `[sdk]` table in ll.toml | Small |
| `Compiler.fs` | `compileProjectTarget(proj, target)` dispatching to the right emitter | Small |
| `Program.fs` | `cmdBuildProject` reads `[platform] use`, loops over targets | Small |
| `Program.fs` | Multi-target output directory (`bin/<target>/`) | Small |
| `Codegen*.fs` | Add `emitProjectModules` to TS/Py/Java backends (only F# has it) | Medium |

Total: straightforward. The compiler already has `compileTarget` for single
files; extending `compileProject` to accept a target is mechanical.

### 6.2 NEXT (after project system is stable)

| Component | Change | Effort |
|-----------|--------|--------|
| `Manifest.fs` | Parse `[platform.<target>]` sub-tables | Small |
| `ProjectLoader.fs` | Load SDK packages from `.ll-deps/` | Medium |
| `AST.fs` + `Parser.fs` | Add `external` and `opaque` declaration forms | Medium |
| `Elaborator.fs` | Handle external decls (type-only, no body) | Small |
| `HMInfer.fs` | Infer external decls as given type schemes | Small |
| `Codegen*.fs` | Emit external calls using FFI map | Medium |
| Build templates | `.fsproj.tmpl`, `package.json.tmpl`, etc. in SDK packages | Small |

### 6.3 LATER (requires interpreter or self-hosting)

| Component | Change | Effort |
|-----------|--------|--------|
| `Compiler.fs` | Load and evaluate SDK `Codegen.lll` at compile time | Large |
| Runtime | ll-lang interpreter for compile-time evaluation | Large |
| All SDKs | Extract codegens from compiler into standalone packages | Medium |

## 7. Concrete File Layout: Platform.FSharp.SDK

```
platform-fsharp/
  ll.toml
  meta.toml
  src/
    Codegen.lll              # ← already exists as stdlib/src/Codegen.lll
    Keywords.lll             # isFsKeyword, safeIdent
    Runtime.lll              # prelude function type declarations
    FFI.lll                  # FFI name mappings for .NET interop
    Types.lll                # type mapping table (Int→int64, etc.)
  runtime/
    prelude.fs               # F# prelude block (currently inline in Codegen.fs)
  templates/
    project.fsproj.tmpl      # .fsproj template (currently inline in Program.fs)
```

### 7.1 `ll.toml`

```toml
[project]
name = "platform-fsharp"
version = "0.1.0"

[sdk]
target   = "fsharp"
aliases  = ["fs"]
ext      = ".fs"
host-ext = ".fsproj"
```

### 7.2 `meta.toml`

```toml
[build]
compile = "dotnet build {project_file} -c Release"
run     = "dotnet run --project {project_file}"
project-template = "templates/project.fsproj.tmpl"

[runtime]
prelude = "runtime/prelude.fs"
```

### 7.3 What moves where

| Currently in | Moves to SDK |
|---|---|
| `Codegen.fs` lines 405-460 (`fsharpPreludeCore/Maybe/Result`) | `runtime/prelude.fs` |
| `Codegen.fs` lines 21-39 (`fsKeywords`) | `src/Keywords.lll` |
| `Program.fs` lines 68-77 (`.fsproj` template) | `templates/project.fsproj.tmpl` |
| `stdlib/src/Codegen.lll` | `src/Codegen.lll` (same content) |

The F# compiler-side `Codegen.fs` stays as-is until self-hosting. The SDK
package is the **source of truth** for what the codegen should do; the F#
implementation is the **executable reality** until we can run `.lll` at
compile time.

## 8. Platform.TypeScript.SDK (sketch)

```
platform-typescript/
  ll.toml                    # [sdk] target = "typescript", aliases = ["ts"]
  meta.toml                  # compile = "tsc", project-template for tsconfig
  src/
    Codegen.lll              # ← stdlib/src/CodegenTS.lll
    Keywords.lll             # isTsKeyword, safeIdent
    Runtime.lll              # prelude function types
    FFI.lll                  # console.log, fetch, etc.
    Types.lll                # Int→number, Str→string, etc.
  runtime/
    prelude.ts               # TS runtime helpers
  templates/
    tsconfig.json.tmpl       # tsconfig template
    package.json.tmpl        # package.json template
```

## 9. Implementation Plan

### Phase A: Multi-target dispatch (NOW)

1. Extend `Manifest.fs` to parse `[sdk]` table (forward-compatible, ignored if absent).
2. Add `emitProjectModules` to `CodegenTS.fs`, `CodegenPy.fs`, `CodegenJava.fs`.
3. Add `compileProjectTarget : LLProject -> Target -> Result<string, LLError list>`.
4. Update `cmdBuildProject` to loop over `[platform] use` entries.
5. Multi-target output: `bin/<target>/` when >1 target.
6. Tests: project with `use = ["fsharp", "typescript"]` produces both outputs.

### Phase B: SDK package loading (NEXT)

1. Define SDK package conventions (`[sdk]` table, `meta.toml`, `runtime/`).
2. Load SDK metadata from `.ll-deps/platform-*/meta.toml`.
3. Use SDK's runtime prelude instead of hardcoded prelude strings.
4. Use SDK's build templates instead of hardcoded `.fsproj` generation.
5. Scaffold `platform-fsharp` and `platform-typescript` as packages.

### Phase C: FFI declarations (NEXT)

1. Add `external` keyword to lexer/parser.
2. Add `opaque` keyword to lexer/parser.
3. Elaborator: external decls recorded as type-only.
4. HMInfer: external decls as given type schemes.
5. Codegen: emit external calls, consult FFI map.

### Phase D: Self-hosted codegen (LATER)

1. Build ll-lang interpreter (eval TypedExpr).
2. Load SDK `Codegen.lll`, evaluate `emitModule`.
3. Remove hardcoded `Codegen.fs` etc. from compiler.
4. SDKs become truly external packages.

## 10. Design Decisions

**Why not make SDKs plugins/DLLs?** Plugin loading (.NET Assembly.LoadFrom) adds
complexity, versioning pain, and platform-specific headaches. ll-lang packages
are source-distributed. The SDK's codegen is ll-lang source that the compiler
evaluates — same as any other code.

**Why keep built-in codegens?** Bootstrap. The compiler is written in F# and
cannot evaluate ll-lang code yet. Built-in codegens are the bridge. Once
self-hosting works, they can be removed. The SDK packages are written in
parallel as the source-of-truth specification.

**Why `meta.toml` separate from `ll.toml`?** `ll.toml` is the standard
manifest that all ll-lang packages share. `meta.toml` carries SDK-specific
build metadata that a regular library would never have. Keeping them separate
means the manifest parser stays simple and SDK awareness is opt-in.

**Why not target-specific ll-lang syntax?** No `#if typescript` conditionals.
ll-lang source is target-agnostic. Target-specific behavior comes from:
(a) the SDK's codegen mapping types/patterns differently, (b) `external`
declarations for target-native APIs, (c) conditional deps in `ll.toml` (future).
