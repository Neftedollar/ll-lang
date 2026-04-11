# Architecture overview

**Status:** All 10 phases complete. Bootstrap fixpoint achieved (`compiler₁.fs == compiler₂.fs`).

The compiler is a straight pipeline: source text in, target source out. Each
stage is a pure function (modulo the small `InferState` used in HM) and
returns `Result<_, LLError list>`.

## Pipeline

### Single-file mode

```
.lll source (string)
        │
        ▼
   Lexer.tokenize                 Tok list with INDENT/DEDENT
        │
        ▼
   Parser.parseModuleWithPos      LLModule : AST + PosMap
        │
        ▼
   Elaborator.elaborate           (LLModule', TypeEnv) + E001..E005 checks
        │
        ▼
   HMInfer.infer                  TypedModule : typed AST + Env + dispatch map
        │
        ▼
   Codegen*.emit                  Target source string
   (Codegen.fs | CodegenTS.fs | CodegenPy.fs | CodegenJava.fs)
        │
        ▼
  .fs/.ts/.py/.java  →  dotnet fsi   (lllc run, F# only)
                     →  dotnet build (lllc build --target fs)
```

Entry points:
- `Compiler.compile : string -> Result<string, LLError list>` — single-file, F# target
- `Compiler.compileTarget : Target -> string -> Result<string, LLError list>` — single-file, any target

### Project mode (Phase 8)

```
ll.toml
        │
        ▼
   Manifest.parseManifest         LLManifest (name, version, deps, platform)
        │
        ▼
   ProjectLoader.loadProject      LLProject (topo-sorted LoadedFile list)
        │   ▼ glob src/*.lll
        │   ▼ parse each file header (module path + imports)
        │   ▼ validate module paths (E020)
        │   ▼ Kahn's topological sort (E024 on cycle)
        ▼
   Compiler.compileProject        for each file: compile → TypedModule
        │
        ▼
   Codegen*.emitProjectModules    prelude once + all modules concatenated
        │   (per-target; loops over [platform] use list)
        ▼
  bin/<name>.fs + bin/<name>.fsproj       (single target)
  bin/fsharp/<name>.fs                    (multi-target)
  bin/typescript/<name>.ts
```

A `PosMap` side-table (see `AST.fs`) is populated by the parser and
threaded through the elaborator and inference passes so that every
error carries a real `line:col` from the source (instead of the historic
`0:0` placeholder).

## Module-by-module

### `Token.fs` — token types

Enum-like discriminated union with cases for keywords, identifiers, literals,
operators, and the three synthetic layout tokens (`Indent`, `Dedent`,
`Newline`). A `Tok` carries source `Line` and `Col`.

### `Lexer.fs` — tokenizer

Character-by-character scan producing a `Tok list`. Responsible for:

- Keyword recognition via a `Map<string, Token>`.
- INDENT/DEDENT synthesis from leading whitespace changes.
- String literal escape handling.
- Distinguishing identifiers by case: `[A-Z]...` → `TypeId`, lowercase →
  `Ident`.

See [02-lexer](02-lexer.md).

### `AST.fs` — untyped surface AST

Discriminated unions for types (`TypeExpr`), literals (`Literal`), patterns
(`Pattern`), expressions (`Expr`), and declarations (`Decl`). An `LLModule`
is a record of `Path`, `Imports`, and `Decls`.

Type variables are `TyVar string`; type applications are `TyApp(outer, arg)`
built left-to-right; units are a separate `UnitExpr` tree nested inside
`TyTagged`.

### `Parser.fs` — recursive descent

Mutable `Ctx` cursor over a `Tok array`. Hand-written precedence climbing:
`parseAtom < parseTagged < parseApp < parseMul < parseAdd < parseCons <
parseCmp < parsePipe`. Higher-level constructs (`parseExprInner`,
`parseBlockExpr`) handle `let`, `if`, `match`, and lambda. The parser
exposes two top-level entries: `parseModule` (plain) and
`parseModuleWithPos` (same, but also returns the populated `PosMap`).

See [03-parser](03-parser.md).

### `Elaborator.fs` — declared-type checking pass

Four sub-passes:

1. `rewriteTagsInModule` — rewrites `TyApp(T, TyVar t)` to
   `TyTagged(T, UName t)` for every declared `tag` so that HMInfer sees
   the tagged form directly.
2. `collectDecls` — walks decls, populates `TypeEnv : Map<string, TypeExpr>`
   with a large `builtinEnv` (arith/cmp ops plus the Phase 6 stdlib:
   math, list, maybe, result, str, char, file IO, process), let-binding
   types (from literal inspection), function signatures, and sum-type
   constructors.
3. `checkDecls` — traverses function bodies calling `typeOf`, which walks
   expressions and compares declared vs actual types for each application.
   Emits E001/E002/E004/E005 via `classifyMismatch` and carries positions
   from the parser's `PosMap`.
4. `exhaustivenessCheck` — for every `DFn` whose first param type is a
   named sum type, verifies that every `EMatch` branch list covers every
   constructor. Emits E003 for missing constructors.

Uses structural type equality with `TyVar` as a wildcard. Returns both
the rewritten `LLModule` and the enriched `TypeEnv`:
`elaborate : PosMap -> LLModule -> Result<LLModule * TypeEnv, LLError list>`.

See [04-elaborator](04-elaborator.md).

### `Types.fs` — type-scheme plumbing

Defines `TypeScheme`, `Subst` (flex-var-only), `Env`, `FreshState`. Exports
`applyType`, `applyEnv`, `compose`, `ftvType`, `generalize`, `instantiate`.
Rigid vars (no `$` prefix) are quantifiers; flex vars (`$N`) are
unification variables. The bridge `fromElaboratorEnv` converts
Elaborator's raw `TypeEnv` into H-M's `Env` by collecting rigid vars into
scheme quantifier lists.

### `TypedAST.fs` — typed AST

A parallel AST where every node has an `Id : ExprId`, a `Type : TypeExpr`,
and an `Expr : TypedExprKind`. Declarations carry a `TypeScheme` alongside
the body. `TypedModule` also tracks the final `Env` and a
`Dispatch : Map<ExprId, DispatchInfo>` (the trait dispatch table, populated
during inference).

### `HMInfer.fs` — Algorithm W

Walk the untyped `LLModule` producing a `TypedModule`. Key operations:

- `unify : TypeExpr -> TypeExpr -> Result<Subst, LLError>`
- `inferExpr : Env -> InferState -> Expr -> Subst * TypeExpr * TypedExpr`
- `inferDecl : Env -> InferState -> Decl -> bool -> (TypedDecl * bool) * Env`

Generalizes at every `let` and `fn`. Emits E001, E002, E004, E005, E006
(reserved), E008.

See [05-hm-inference](05-hm-inference.md).

### `Codegen.fs` — F# emitter

Walks the `TypedModule` producing a big string of F# source. Each
`TypedExprKind` case has a corresponding emit rule. Binary operators are
detected as `TEApp(TEApp(TEVar op, a), b)` and rendered infix. `fn main`
with no params becomes `[<EntryPoint>] let main (argv: string[]) = ... 0`.

See [06-codegen](06-codegen.md).

### `CodegenTS.fs` — TypeScript emitter

Emits TypeScript source. Sum types become discriminated unions with a `_tag` field. Curried functions emit as nested arrow functions. See [11-multi-target-codegen](11-multi-target-codegen.md).

### `CodegenPy.fs` — Python emitter

Emits Python source. Sum types become `@dataclass` classes under a `Union` alias. Curried functions emit as nested `def`. Pattern matching emits as ternary chains (Python `match` is not an expression).

### `CodegenJava.fs` — Java 21 emitter

Emits Java 21 source. Sum types become `sealed interface` + `record` hierarchies. Functions emit as static methods. See [11-multi-target-codegen](11-multi-target-codegen.md).

### `Manifest.fs` — TOML subset parser (Phase 8)

Hand-written parser for `ll.toml` project manifests. Supports `[table]` headers,
`key = "value"` strings, and `key = ["a","b"]` string arrays. No NuGet
dependencies. Entry point: `parseManifest : string -> Result<LLManifest, string>`.

### `ProjectLoader.fs` — multi-file project driver (Phase 8)

1. Calls `Manifest.parseManifest`.
2. Globs `src/**/*.lll` using `Directory.GetFiles`.
3. Parses each file to extract its `module` header and `import` list.
4. Validates module paths against file locations (E020).
5. Topological sort via Kahn's algorithm (E024 on cycle).
6. Returns `LLProject { Manifest; RootDir; Files: LoadedFile list }`.

Entry point: `loadProject : string -> Result<LLProject, LLError list>`.

### `Compiler.fs` — pipeline glue

Two entry points:

- `compile : string -> Result<string, LLError list>` — single-file pipeline.
- `compileProject : LLProject -> Result<string, LLError list>` — multi-file
  pipeline: runs `compile` on each topo-sorted file, concatenates via
  `Codegen.emitProjectModules`.

### `src/LLLangTool/Mcp.fs` — MCP server (Phase 9)

An in-process Model Context Protocol server that exposes the ll-lang
compiler to LLM clients (Claude Code, Cursor, Zed) via 8 structured
tools over stdio:

| Tool | What it does |
|------|-------------|
| `compile_file` | Full pipeline → `{ok, errors[], fsharp?}` |
| `check_file` | Lex→parse→elaborate→infer (no codegen) → `{ok, errors[]}` |
| `run_file` | Compile + `dotnet fsi` → `{exit_code, stdout, stderr, errors[]}` |
| `list_errors` | All E001–E025 codes with names and descriptions |
| `lookup_error` | One code → description + minimal repro from `spec/examples/invalid/` |
| `stdlib_search` | Substring search over ~50 stdlib entries → `[{name, signature, module, scope}]` |
| `grammar_lookup` | Rule name → EBNF production from `spec/grammar.ebnf` |
| `project_info` | Walk to `ll.toml` → `{root, manifest, modules[], deps[], platform_use[]}` |

Uses `FsMcp.Core` + `FsMcp.Server` (same packages as the `age-mcp` server
in this repo). Entry: `Mcp.runServer ()` blocks on stdio until the client
disconnects.

**Client config** (add to `~/.config/claude/mcp.json` or Cursor settings):
```json
{
  "mcpServers": {
    "ll-lang": { "command": "lllc", "args": ["mcp"] }
  }
}
```

### `src/LLLangTool/Program.fs` — CLI

The `lllc` driver. Seven commands:

- `build <file.lll>` — single-file mode (default target: F#).
- `build --target ts|py|java <file.lll>` — single-file, named target.
- `build [dir]` — project mode (reads `ll.toml`, writes `bin/<name>.fs`; multi-target via `[platform] use`).
- `run <file.lll>` — writes a temp `.fsx` (stripping `module` and
  `[<EntryPoint>]`, appending `main [||] |> exit`) and shells out to
  `dotnet fsi`.
- `check <file.lll>` — lex → parse → elaborate → infer without emitting output. Fast type-check.
- `new <name>` — scaffold project directory structure.
- `install` — fetch source-based dependencies declared in `ll.toml` into `.ll-deps/`.
- `mcp` — launch MCP stdio server (blocks until stdin closes).

## F# compile order

F# requires forward declarations through `<Compile Include="...">` order
in the `.fsproj`. The current order is significant:

```xml
<Compile Include="Token.fs" />
<Compile Include="Lexer.fs" />
<Compile Include="AST.fs" />
<Compile Include="Parser.fs" />
<Compile Include="Elaborator.fs" />
<Compile Include="Types.fs" />
<Compile Include="TypedAST.fs" />
<Compile Include="HMInfer.fs" />
<Compile Include="Codegen.fs" />
<Compile Include="CodegenTS.fs" />
<Compile Include="CodegenPy.fs" />
<Compile Include="CodegenJava.fs" />
<Compile Include="Manifest.fs" />
<Compile Include="ProjectLoader.fs" />
<Compile Include="Compiler.fs" />
```

Dependency chain:

- `Token.fs` → `Lexer.fs`
- `AST.fs` is independent; `Parser.fs` depends on `Token.fs` + `AST.fs`
- `Elaborator.fs` depends on `AST.fs` only
- `Types.fs` depends on `AST.fs` and (for `fromElaboratorEnv`) on `Elaborator.fs`
- `TypedAST.fs` depends on `AST.fs` + `Types.fs`
- `HMInfer.fs` depends on all of the above
- `Codegen.fs`, `CodegenTS.fs`, `CodegenPy.fs`, `CodegenJava.fs` each depend on `AST.fs` + `Types.fs` + `TypedAST.fs`. Do not `open` more than one in `Compiler.fs` — all four export `emit`, which would shadow. Use fully-qualified names: `Codegen.emit`, `CodegenTS.emit`, etc.
- `Manifest.fs` depends only on `System` (no compiler module deps)
- `ProjectLoader.fs` depends on `Manifest.fs` + `Elaborator.fs` + `Lexer.fs` + `Parser.fs`
- `Compiler.fs` glues everything together; dispatches to the right codegen via `Target` DU

If you add a new file, slot it in where its dependencies are satisfied.
Adding it at the end will usually work unless it is depended on by
`Codegen.fs` or `Compiler.fs`.

## Multi-target dispatch

`Compiler.fs` exposes `compileTarget`:

```fsharp
type Target = FSharp | TypeScript | Python | Java

let compileTarget (target: Target) (src: string) : Result<string, LLError list> =
    match target with
    | FSharp      -> compile src           // Codegen.emit
    | TypeScript  -> compileToTS src       // CodegenTS.emit
    | Python      -> compileToPy src       // CodegenPy.emit
    | Java        -> compileToJava src     // CodegenJava.emit
```

The front-end (lex → parse → elaborate → infer) runs once per file regardless of target count. Codegens fan out from the shared `TypedModule`. In multi-target project builds, `compileProject` runs the front-end once and then calls each target's `emitProjectModules`.

Output layout:
- Single target: `bin/<name>.fs` (backward-compatible)
- Multi-target: `bin/fsharp/<name>.fs`, `bin/typescript/<name>.ts`, etc.

See [11-multi-target-codegen](11-multi-target-codegen.md) for per-backend details.

## Package system

Source-based dependencies. `ll.toml` declares them:

```toml
[deps]
std = { path = "../stdlib" }
json = "https://github.com/user/ll-json#v0.1.0"
```

`lllc install` copies dep sources into `.ll-deps/`. `ProjectLoader.fs` resolves imports by searching `src/` then `.ll-deps/*/src/`.

Platform SDKs are a future extension (`[sdk]` table in a dep's `ll.toml`). See `docs/superpowers/specs/2026-04-10-ll-lang-platform-sdk.md` for the design.

## No external parser / inference libraries

Everything is hand-written. The rationale:

- Bootstrap (Phase 7) translates the compiler into ll-lang itself. Avoiding external libraries keeps the translation mechanical.
- H-M is small (~200 lines) and the explicit form is easier to audit than an FParsec or FsLexYacc pipeline.
- Error messages are fully under our control — no generated parser spew.
