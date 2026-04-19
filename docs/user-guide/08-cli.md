# The `lllc` CLI

`lllc` is the bootstrap binary entrypoint for single-file and multi-file project builds.
Stage0 `LLLangTool` is archived under `obsolete/stage0/src/LLLangTool` and is not the default runtime path.

1.0 note: `build/run/new/install/self/mcp` and `check [dir]` are stable CLI
surface. `reverse` is available as experimental tooling.

```
Usage:
  lllc build [--target fs|ts|py|java|cs|llvm] <file.lll>   compile single file
  lllc build [--target fs|ts|py|java|cs|llvm] [dir]        compile project (reads lll.toml)
  lllc check [--target fs|ts|py|java|cs|llvm] [dir]        type-check project (no codegen)
  lllc self check <file.lll>                                 canonical single-file type-check (LLL path)
  lllc check [--target fs|ts|py|java|cs|llvm] <file.lll>    legacy stage0 single-file check (compatibility path)
  lllc run   [--target fs|ts|py|java|cs|llvm] <file.lll>   compile and run single file
  lllc self  <cmd> <file> [arg]                             run self-hosted lllc tool layer
  lllc new   <name>         scaffold new project
  lllc install                                               sync deps into vendor/ + ll.sum
  lllc mod tidy                                              same as install
  lllc mod add <name>=<source>                              add dep and install
  lllc mod why <dep>                                         explain dependency chain
  lllc mcp                                                   run MCP server (stdio)
  lllc reverse --from <target> <file>   [experimental] recover minimal ll-lang from generated code
```

---

## `lllc build <file.lll>` — single file

```bash
lllc build hello.lll
```

1. Reads the `.lll` file from disk.
2. Runs the full pipeline: lex → parse → elaborate → infer → codegen.
3. On success, writes the emitted F# source next to the input (`.fs`).
4. Prints `Built <filename>.fs` and exits 0.
5. On error, prints each error to stderr and exits 1.

Example output for `01-basics.lll`:

```fsharp
module Examples.Basics

let pi = 3.14159
let greeting = "hello"

let add a b = (a + b)
let double x = (x * 2L)
...
```

Note: integer literals emit as `int64` (`2L` not `2`).

---

## `lllc build [dir]` — project build

```bash
lllc build           # finds lll.toml by walking up from cwd
lllc build ./myapp   # builds project rooted at ./myapp
```

Requires a `lll.toml` manifest at the project root (see [06-modules.md](06-modules.md)).

1. Reads `lll.toml` from the project root.
2. Globs all `*.lll` files under `src/` recursively.
3. Validates each file's `module` header matches its path (E020).
4. Topologically sorts by imports (E024 on cycle).
5. Compiles each file in dependency order using the selected target's external mapping validation.
6. Concatenates/emit modules into target output directory/file(s).
7. Generates `bin/<name>.fsproj` for `dotnet build` (F#/legacy single-target path).

```bash
# After lllc build:
dotnet build bin/myapp.fsproj     # compile to .dll / .exe
dotnet run   --project bin/myapp.fsproj
```

---

## `lllc self check <file.lll>` — canonical single-file type-check

```bash
lllc self check hello.lll
```

Runs the self-hosted LLL checker path (import-aware closure + compile pipeline)
without writing generated target files.

## `lllc check <file.lll>` — legacy stage0 single-file check

This compatibility path is retained for bootstrap/recovery scenarios.

- Not the canonical single-file checker in the current self-host migration.
- May diverge on import resolution behavior versus `lllc self check`.

---

## `lllc check [dir]` — project type-check

```bash
lllc check           # finds lll.toml by walking up from cwd
lllc check ./myapp   # checks project rooted at ./myapp
```

Checks all project modules in topo order with the selected target contract.  
Unknown target names in `[platform].use` are hard errors.

---

## `lllc new <name>` — scaffold project

```bash
lllc new myapp
```

Creates:
```
myapp/
├── lll.toml           [project] name/version/entry
└── src/
    └── Main.lll       module Myapp.Main
```

---

## `lllc run <file.lll>` — compile and run

```bash
lllc run hello.lll
```

1. Same pipeline as `build`.
2. Materializes a temporary multi-file F# project (`.fs` files + `.fsproj`).
3. Executes `dotnet run -c Release -v q --project <tmp>.fsproj`.
4. Propagates the child process exit code.
5. Deletes the temp directory.

Your ll-lang `main()` becomes the F# entry point:

```lll
module Examples.Hello
main() = printfn "Hello, ll-lang!"
```

```bash
lllc run examples/hello.lll
# Hello, ll-lang!
```

For non-F# targets, `lllc run --target <target> file.lll` uses Platform SDK run commands.
Example TypeScript run path:

```bash
lllc run --target ts hello.lll
# internally: npx tsc hello.ts --target es2022 --module esnext && node hello.js
```

---

## `lllc self <cmd> <file> [arg]` — run self-hosted compiler layer

```bash
lllc self check src/Main.lll
lllc self compile src/Main.lll
lllc self symbol src/Main.lll lookupName
```

`lllc self` compiles and runs the `lllcself` ll-lang project and delegates subcommands to its CLI (`check`, `compile`, `render`, `tokens`, `next`, `symbol`).

In the current `1.x` line, `lllc self` is a bridge into the self-hosted
compiler/tool layer, not yet the canonical top-level compiler path. The `v2`
plan is to promote the ll-lang-owned compiler path and demote the stage0 F#
driver to bootstrap-only support.

---

## Generated F# layout

Given `foo.lll`:

```lll
module Demo.Foo

Shape = Circle Float | Rect Float Float | Empty

area(s Shape) =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty -> 0.0
```

`lllc build` writes `foo.fs`:

```fsharp
module Demo.Foo

type Shape =
    | Circle of float
    | Rect of float * float
    | Empty

let area s =
    (match s with
    | Circle r -> (3.14159 * (r * r))
    | Rect(w, h) -> (w * h)
    | Empty -> 0.0)
```

Type tags (`tag UserId`) and units (`unit Meter`) erase at codegen time.
Traits and impls emit flat `let` bindings with mangled names like `Maybe_map`.

---

## Troubleshooting

### `lllc: <exception>`

A bare `lllc: <message>` on stderr means the driver caught an exception — usually a missing file or permission error.

### `E020 ModulePathMismatch`

The `module` header in a file does not match its location under `src/`. For a file at `src/Foo/Bar.lll` in project `myapp`, the header must be `module Myapp.Foo.Bar`.

### `E024 ModuleCycle`

Two or more files import each other, creating a cycle. Restructure so dependencies flow in one direction.

### Parse or type errors

Each error line is self-contained:

```
E002 0:0 UnboundVar foo
E003 0:0 NonExhaustiveMatch Shape missing:Empty
```

### `lllc run` cold start

`lllc run` builds and executes a fresh temporary project each run. For tight edit loops, prefer `lllc build` + `dotnet run --project <your .fsproj>`.

### Nullness warnings on build

The compiler projects enable `<Nullable>enable</Nullable>` under `LangVersion=preview`.
Current baseline is warning-free (`0 Warning(s)` on `dotnet build obsolete/stage0/src/LLLangTool/LLLangTool.fsproj`).
Treat new nullness warnings as regressions to fix rather than suppress.

---

## Invoking archived stage0 via `dotnet run`

If you have not set up the `lllc` alias (see [01-installation.md](01-installation.md)):

```bash
dotnet run --project obsolete/stage0/src/LLLangTool -- build hello.lll
dotnet run --project obsolete/stage0/src/LLLangTool -- build ./myapp
dotnet run --project obsolete/stage0/src/LLLangTool -- new myapp
dotnet run --project obsolete/stage0/src/LLLangTool -- reverse --from ts bin/typescript/myapp.ts
```

---

## `lllc reverse --from <target> <file>` — reverse generated code to ll-lang (experimental)

```bash
lllc reverse --from ts   out.ts
lllc reverse --from py   out.py
lllc reverse --from java out.java
lllc reverse --from cs   out.cs
lllc reverse --from llvm out.ll
```

Accepted source aliases are the same as build targets, including `Platform.*.SDK` aliases.

This command is experimental in 1.0 and not covered by stable compatibility guarantees.

Behavior:
1. Reads generated source from the given file.
2. Recovers minimal ll-lang declarations (`let` and simple function declarations).
3. Writes sibling output as `<input-stem>.reversed.lll` (never overwrites existing `.lll` sources).

Use this as a migration/bootstrap tool for target code that follows emitted idioms:
- constant declarations (`Int`, `Float`, `Bool`, `Str`; plus `Char` where target type carries it)
- typed top-level constants in TS/Python are also recovered (`const x: number = ...`, `x: int = ...`)
- C#/Java numeric field variants for idiomatic hand-written code (`int/long`, `float/double`)
- TypeScript arrow functions (including single-param and block-bodied) and `function ... { return ... }` (including generic signatures)
- Semicolonless TypeScript forms are supported for the same function/const recovery shapes
- Curried lambda chains in TS/C#/Java (`x => y => ...`) and Python nested `def` currying are rehydrated to multi-parameter ll-lang signatures
- Python `def ...: return ...` (single-line or simple block-bodied)
- Java/C# static methods with `return ...` (public/private/protected variants, including generic method signatures)
- Top-level ternary return expressions in TS/C#/Java are lowered to ll-lang `if ... else ...` form
- Block `if/else` + `return` method/function shapes in TS/Python/C#/Java are also lowered to ll-lang `if ... else ...`
- Block `if / else if / else` (and Python `if / elif / else`) + `return` shapes are lowered to nested ll-lang `if ... else if ... else ...`
- Block `if / else if` + fallback `return` (no explicit `else`) is also lowered to nested ll-lang `if ... else if ... else ...`
- TypeScript strict equality operators are normalized during recovery (`===` → `==`, `!==` → `!=`)
- F# `if ... then ... else ...` expressions are lowered to ll-lang `if ... else ...` blocks
- F# nested `let ... in ...` chains are flattened into ll-lang local-binding statement blocks
- F# tuple-style DU constructor calls (e.g. `Ctor(a, b)`) are normalized to curried ll-lang constructor application
- Identifier normalization from `PascalCase` to ll-lang-compatible `lowerCamelCase` during recovery
