# The `llc` CLI

`llc` (the `LLLangTool` project) is a thin driver around the compiler library.
It exposes two commands: `build` and `run`.

```
Usage: llc <build|run> <file.lll>
```

Anything else prints the usage and exits with code 1.

## `llc build`

```bash
llc build hello.lll
```

Behavior:

1. Reads the `.lll` file from disk.
2. Runs the full pipeline: lex → parse → elaborate → infer → codegen.
3. On success, writes the emitted F# source next to the input with the
   extension changed to `.fs`.
4. Prints `Built <filename>.fs` and exits 0.
5. On error, prints each error's compact message to stderr and exits 1.

Example output for `01-basics.lll`:

```fsharp
module Examples.Basics

let pi = 3.14159

let greeting = "hello"

let add a b =
    (a + b)

let double x =
    (x * 2L)

let square x =
    (x * x)
...
```

Note: integer literals emit as `int64` (`2L` not `2`).

## `llc run`

```bash
llc run hello.lll
```

Behavior:

1. Same pipeline as `build`.
2. Writes the emitted F# to a temporary `.fsx` file.
3. Strips the `module` header and `[<EntryPoint>]` attribute (F#
   interactive does not honor them).
4. Appends `main [||] |> exit` so `fn main()` actually executes.
5. Shells out to `dotnet fsi <tmp>.fsx`.
6. Waits for `fsi` to exit and propagates its exit code.
7. Deletes the temp file.

Your ll-lang `fn main()` becomes the F# entry point:

```lll
module Examples.Hello
fn main() = printfn "Hello, ll-lang!"
```

```bash
llc run examples/hello.lll
# Hello, ll-lang!
```

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

`llc build` writes `foo.fs`:

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

Type tags (`tag UserId`, `tag m`) emit nothing — they erase at codegen time.
`unit Meter` similarly emits nothing. Traits and impls emit flat `let`
bindings with mangled names like `Maybe_map`.

## Troubleshooting

### `llc: <exception>`

A bare `llc: <message>` on stderr means the driver caught an exception —
usually a missing file or a permission error. Check the file path.

### Parse or infer errors

Each error line is self-contained:

```
E005 TaggedUntaggedMismatch Str vs Str[UserId]
E003 0:0 NonExhaustiveMatch Shape missing:Empty
```

Column and line numbers are currently `0:0` in many cases — position
tracking is partial. The error name and types are always reliable.

### `dotnet fsi` is slow

`llc run` starts a fresh F# interactive session each time. Cold-start is
typically 2 to 5 seconds on a modern machine. For fast iteration, use
`llc build` to get the `.fs` file and compile it as part of a regular
`dotnet build` project.

### Nullness warnings on build

The `LLLangTool` project enables `<Nullable>enable</Nullable>` under
`LangVersion=preview`. `dotnet run --project src/LLLangTool` prints a few
`FS3261` warnings from `File.ReadAllText` and `Process.Start` returning
nullable types. They are harmless and do not affect execution.

## Invoking via `dotnet run`

If you have not set up the `llc` alias (see
[01-installation](01-installation.md)), every command becomes:

```bash
dotnet run --project src/LLLangTool -- build hello.lll
dotnet run --project src/LLLangTool -- run hello.lll
```

The `--` separator passes the following arguments to `llc` rather than
consuming them as `dotnet run` flags.
