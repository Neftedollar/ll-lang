# The `lllc` CLI

`lllc` (the `LLLangTool` project) drives the compiler for both single-file and multi-file project builds.

```
Usage:
  lllc build <file.lll>     compile single file
  lllc build [dir]          compile project (reads ll.toml)
  lllc run   <file.lll>     compile and run single file
  lllc new   <name>         scaffold new project
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
lllc build           # finds ll.toml by walking up from cwd
lllc build ./myapp   # builds project rooted at ./myapp
```

Requires a `ll.toml` manifest at the project root (see [06-modules.md](06-modules.md)).

1. Reads `ll.toml` from the project root.
2. Globs all `*.lll` files under `src/` recursively.
3. Validates each file's `module` header matches its path (E020).
4. Topologically sorts by imports (E024 on cycle).
5. Compiles each file in dependency order.
6. Concatenates all modules into `bin/<name>.fs`.
7. Generates `bin/<name>.fsproj` for `dotnet build`.

```bash
# After lllc build:
dotnet build bin/myapp.fsproj     # compile to .dll / .exe
dotnet run   --project bin/myapp.fsproj
```

---

## `lllc new <name>` — scaffold project

```bash
lllc new myapp
```

Creates:
```
myapp/
├── ll.toml           [project] name = "myapp"
└── src/
    └── Main.lll      module Myapp.Main
```

---

## `lllc run <file.lll>` — compile and run

```bash
lllc run hello.lll
```

1. Same pipeline as `build`.
2. Writes the emitted F# to a temporary `.fsx` file.
3. Strips the `module` header and `[<EntryPoint>]` attribute (F# interactive does not honor them).
4. Appends `main [||] |> int64 |> exit` so `fn main()` actually executes.
5. Shells out to `dotnet fsi <tmp>.fsx`.
6. Waits for `fsi` to exit and propagates its exit code.
7. Deletes the temp file.

Your ll-lang `fn main()` becomes the F# entry point:

```lll
module Examples.Hello
fn main() = printfn "Hello, ll-lang!"
```

```bash
lllc run examples/hello.lll
# Hello, ll-lang!
```

---

## Generated F# layout

Given `foo.lll`:

```lll
module Demo.Foo

type Shape = Circle Float | Rect Float Float | Empty

fn area(s Shape) Float =
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

### `dotnet fsi` is slow

`lllc run` starts a fresh F# interactive session each time. Cold-start is typically 2–5 s. For faster iteration, use `lllc build` + `dotnet run`.

### Nullness warnings on build

The compiler projects enable `<Nullable>enable</Nullable>` under `LangVersion=preview`. A few `FS3261` warnings from `File.ReadAllText` and `Process.Start` are harmless pre-existing warnings.

---

## Invoking via `dotnet run`

If you have not set up the `lllc` alias (see [01-installation.md](01-installation.md)):

```bash
dotnet run --project src/LLLangTool -- build hello.lll
dotnet run --project src/LLLangTool -- build ./myapp
dotnet run --project src/LLLangTool -- new myapp
```
