# Modules and Projects

ll-lang supports both single-file programs and multi-file projects. Every `.lll` file starts with a module header.

## Module header

```lll
module Examples.Basics
```

The header must be the first non-comment line. The path is one or more uppercase-starting segments joined by dots.

In project mode the path must match the file location: a file at `src/Foo/Bar.lll` in project `myapp` must declare `module Myapp.Foo.Bar`.

## Imports

```lll
module Examples.Modules
import Std.List
import Std.Maybe

fn firstDoubled(xs List[Int]) Maybe[Int] =
  xs -> head -> map (\x. x * 2)
```

Each `import` declares a dependency on another module. Imports appear immediately after the module header, before any declarations.

The **implicit prelude** (~50 stdlib functions) is always in scope without any `import`. Writing `import Std.List` currently parses correctly but is a no-op — all prelude names are already visible.

## Exports

```lll
export fn greet(name Str) Str = "hello " ++ name
```

Prefixing a declaration with `export` marks it as public to other modules. Without `export`, a declaration is private to the file.

---

## Project mode (`lll.toml`)

For multi-file programs, create a project manifest `lll.toml` at the project root:

```toml
[project]
name    = "myapp"
version = "0.1.0"       # optional
entry   = "src/Main.lll"  # optional, default src/Main.lll

[deps]
# Future: external ll-lang packages (Go-style module paths)
# "github.com/alice/json" = "v1.2.0"

[platform]
# Opt-in to platform-specific modules (Phase 8 PR4)
# use = ["Platform.IO", "Platform.Math"]
```

### Directory layout

```
myapp/
├── lll.toml          ← project manifest
├── src/
│   ├── Main.lll     ← module Myapp.Main
│   └── Lib.lll      ← module Myapp.Lib
└── bin/             ← generated: myapp.fs + myapp.fsproj
```

### Module path convention

The compiler derives the expected module path from the file location:

| File | Expected header |
|------|----------------|
| `src/Main.lll` | `module Myapp.Main` |
| `src/Foo/Bar.lll` | `module Myapp.Foo.Bar` |

If the declared path doesn't match the expected path you get `E020 ModulePathMismatch`.

### Import ordering

Files are compiled in topological order (dependencies first). Import cycles produce `E024 ModuleCycle`.

### Build commands

```bash
# Scaffold a new project
lllc new myapp

# Build the project (reads lll.toml in current directory or any parent)
cd myapp && lllc build

# Build a project in a specific directory
lllc build ./myapp

# Single-file (no lll.toml needed — unchanged from before)
lllc build hello.lll
```

Output: `bin/myapp.fs` + `bin/myapp.fsproj` (ready for `dotnet build`).

### Two-file example

`src/Greet.lll`:
```lll
module Hello.Greet

export fn greet(name Str) Str = "Hello, " ++ name ++ "!"
```

`src/Main.lll`:
```lll
module Hello.Main
import Hello.Greet

fn main() Str = greet "World"
```

`lll.toml`:
```toml
[project]
name = "hello"
```

```bash
lllc build   # → bin/hello.fs (both modules concatenated)
```

---

## Error codes

| Code | Name | Meaning |
|------|------|---------|
| E020 | ModulePathMismatch | `module` header does not match the file's location in `src/` |
| E024 | ModuleCycle | Import graph contains a cycle |
| E025 | NoProjectForImport | Non-`Std.*` import used in single-file mode (no `lll.toml`) |

---

## Known limitations

- **No dep resolution yet.** The `[deps]` section is parsed and schema-frozen but packages are not fetched. Writing a dep that isn't vendored locally produces `E022 UnresolvedDep` (only when actually imported).
- **`[platform]` target selection is live, `Platform.*` module APIs are still partial.**  
  The manifest's `[platform] use = [...]` and `Platform.*.SDK` aliases are wired into build/CLI flow, but many `import Platform.*` module surfaces are still being implemented incrementally.
- **Cross-module type checking is partial.** Each file is elaborated and type-checked independently; F# handles cross-module type resolution in the concatenated output. This means `E002 UnboundVar` may not fire for missing imported names at compile time — but `dotnet build` will catch them.
- **No `export` visibility enforcement yet.** The `export` flag is tracked in the AST but not enforced — every declaration is accessible.

## Practical advice

- For learning/scripts: use a single `.lll` file with no `lll.toml`. The implicit prelude (`listMap`, `strLen`, `printfn`, `readFile`, …) is always available.
- For `Maybe` / `Result` in single-file mode: declare them locally — `type Maybe A = Some A | None`.
- For multi-file projects: use `lllc new <name>` to get the right directory structure, then add `.lll` files to `src/`.
