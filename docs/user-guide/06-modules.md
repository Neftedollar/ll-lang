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

In project mode, only explicitly imported modules contribute cross-module names during front-end checking.  
The implicit prelude (~50 stdlib functions) is still always in scope without any `import`.

## Exports

```lll
export { greet }

greet(name Str) Str = "hello " ++ name
```

Canonical surface: one module-level export list (`export { ... }`) placed after imports.

Current compatibility behavior:
- if `export { ... }` exists, imports only see listed names;
- if export list is absent, imports see all top-level names (compatibility fallback);
- legacy `export decl` still parses for compatibility, but does not by itself restrict visibility.

---

## Project mode (`lll.toml`)

For multi-file programs, create a project manifest `lll.toml` at the project root:

```toml
[project]
name    = "myapp"
version = "1.0.0"       # optional
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

export { greet }

greet(name Str) Str = "Hello, " ++ name ++ "!"
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
- **Cross-module checking is import-scoped, but still file-local in inference.** The compiler now only imports names from declared `import` dependencies (no global sibling leakage), yet each file is still inferred independently and backend builds remain the final authority for some cross-file mismatches.
- **Visibility is export-list first.** Add `export { ... }` to enforce a stable module API; modules without export-list stay all-visible for compatibility.

## Practical advice

- For learning/scripts: use a single `.lll` file with no `lll.toml`. The implicit prelude (`listMap`, `strLen`, `printfn`, `readFile`, …) is always available.
- For `Maybe` / `Result` in single-file mode: declare them locally — `type Maybe A = Some A | None`.
- For multi-file projects: use `lllc new <name>` to get the right directory structure, then add `.lll` files to `src/`.
