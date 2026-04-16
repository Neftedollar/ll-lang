# ll-lang

[![Build & Test](https://github.com/Neftedollar/ll-lang/actions/workflows/build.yml/badge.svg)](https://github.com/Neftedollar/ll-lang/actions/workflows/build.yml)

> **A statically-typed functional language designed for LLM code generation.**
> Token-efficient syntax. Compiled means works. Errors your agent can parse in one line.

```
module Hello

Hello = printfn "Hello, ll-lang!"
```

```bash
$ lllc run hello.lll
Hello, ll-lang!
```

---

## Why ll-lang?

LLMs writing TypeScript or Python burn tokens on ceremony — `function`, `const`, `: string`, braces, semicolons — then ship code that only fails at runtime. By then it's too late.

ll-lang fixes both problems at once.

**Less to generate:**

| Task | TypeScript | ll-lang |
|------|-----------|---------|
| Declare a tagged type | `type UserId = string & { __brand: 'UserId' }` | `tag UserId` |
| Define a sum type | `type Shape = \| { kind: 'Circle'; r: number } \| { kind: 'Rect'; w: number; h: number }` | `Shape = Circle Float \| Rect Float Float` |
| Write a generic function | `function map<A, B>(f: (a: A) => B, xs: A[]): B[]` | `map(f A -> B)(xs List[A]) List[B]` |

ll-lang is **8–17% more compact than F#** and **1.3–5.9× more compact than TypeScript / Python / Java** on real type-heavy code.

**Earlier failures:**

```
E001 12:5  TypeMismatch Str Str[UserId]
E005 7:14  TagViolation Str[Email] Str[UserId]
E003 15:1  NonExhaustiveMatch Shape missing:Empty
```

One line per error. No stack traces. Regex-parseable. Your agent reads them directly and fixes on the next attempt — no extraction, no prose, no guessing.

**Compiles to everything you already use:**

```bash
lllc build --target ts   shapes.lll   # TypeScript discriminated unions
lllc build --target py   shapes.lll   # Python @dataclass + Union
lllc build --target java shapes.lll   # Java 21 sealed interfaces
lllc build --target cs   shapes.lll   # C# records + interfaces
lllc build --target fs   shapes.lll   # F# (default)
lllc build --target llvm shapes.lll   # LLVM IR (experimental)
```

---

## Quick Start

**Requires [.NET 10](https://dotnet.microsoft.com/download).**

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
dotnet build
```

### 30 seconds to first program

```bash
cat > hello.lll <<'EOF'
module Hello

Hello = printfn "Hello, ll-lang!"
EOF

dotnet run --project src/LLLangTool -- run hello.lll
# → Hello, ll-lang!
```

### Scaffold a project

> **Note:** After `dotnet build`, the `lllc` binary is not on PATH. Use
> `dotnet run --project src/LLLangTool --` as a prefix, or add the build output
> directory to PATH: `export PATH="$PATH:$(pwd)/src/LLLangTool/bin/Debug/net10.0"`.

```bash
lllc new myapp          # creates myapp/lll.toml + myapp/src/Main.lll
cd myapp
lllc build              # → bin/fsharp/myapp.fsproj
dotnet run --project bin/fsharp/myapp.fsproj
```

---

## Language Tour

### Functions — no `fn` keyword

Uppercase names = types. Lowercase names = values. The body follows `=`.

```
module Examples.Basics

pi = 3.14159

add(a Int)(b Int) Int = a + b
double(x Int) = x * 2           -- return type inferred

-- multi-branch
clamp(x Int)(lo Int)(hi Int) Int =
  if x < lo
    lo
  else if x > hi
    hi
  else x

-- lambda
triple = \x. x * 3

-- local binding
example =
  y = double 5
  y + 1
```

### Algebraic Data Types and Pattern Matching

```
module Examples.ADTs

Shape = Circle Float | Rect Float Float | Empty

Maybe A = Some A | None
Result A E = Ok A | Err E

area(s Shape) Float =
  match s with
  | Circle r   -> 3.14159 * r * r
  | Rect w h   -> w * h
  | Empty      -> 0.0

safeDivide(a Float)(b Float) Maybe[Float] =
  if b == 0.0 then None
  else Some (a / b)
```

### Traits

```
module Examples.Traits

trait Show A =
  show(a A) Str

impl Show Int =
  show(n Int) Str = intToStr n

impl Show Bool =
  show(b Bool) Str = if b then "true" else "false"

printVal(x A) [Show A] = printfn (show x)
```

### Tags, Phantom Types, and Unit Algebra

```
module Examples.Tags

tag UserId
tag Email

uid = "user-42"[UserId]

getUser(id Str[UserId]) Maybe[Str] = Some "alice"
sendEmail(to Str[Email]) = to

-- unit algebra: speed has type Float[m/s], inferred
tag m
tag s

speed(d Float[m])(t Float[s]) = d / t
```

Passing `uid` to `sendEmail` is a **compile-time error** (`E005 TagViolation`), not a runtime one.

### Modules and Imports

```
module Examples.App

import Map
import Toml

config = Toml.parse (readFile "config.toml")
entries = Map.fromList [("key", "value")]
```

### Only 15 keywords

`match`, `if`, `else`, `import`, `export`, `module`, `trait`, `impl`, `external`, `opaque`, `tag`, `unit`, `true`, `false`, `let`.

Everything else — functions, types, traits, let bindings — is expressed through the uppercase/lowercase convention.

---

## Error Codes

All compiler errors are compact, structured, and machine-readable:

| Code | Meaning | Example output |
|------|---------|---------------|
| `E001` | Type mismatch | `E001 12:5 TypeMismatch Str Str[UserId]` |
| `E002` | Unbound variable | `E002 8:3 UnboundVar username` |
| `E003` | Non-exhaustive match | `E003 15:1 NonExhaustiveMatch Shape missing:Empty` |
| `E004` | Unit mismatch | `E004 20:9 UnitMismatch Float[m] Float[s]` |
| `E005` | Tag violation | `E005 7:14 TagViolation Str[Email] Str[UserId]` |

Format: `EXXX line:col ErrorKind details`. Full list: [`spec/error-codes.md`](spec/error-codes.md).

---

## MCP Integration

ll-lang ships a built-in [MCP](https://modelcontextprotocol.io/) server. Wire it to Claude Code, Cursor, or Zed — your LLM client gains structured tools to compile, type-check, and run ll-lang code without parsing shell output.

```json
// claude_desktop_config.json / .cursor/mcp.json
{
  "mcpServers": {
    "lllc": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/ll-lang/src/LLLangTool", "--", "mcp"]
    }
  }
}
```

**10 MCP tools:** `compile_file`, `compile_source`, `check_file`, `check_source`, `run_file`, `list_errors`, `lookup_error`, `stdlib_search`, `grammar_lookup`, `project_info`.

The agent asks "does this compile?" and gets structured JSON with error codes, line numbers, and fix hints — no scraping required.

---

## CLI Reference

```
lllc build <file.lll>               compile → <file>.fs  (F# default)
lllc build --target ts <file.lll>   compile → <file>.ts
lllc build --target py <file.lll>   compile → <file>.py
lllc build --target java <file.lll> compile → <file>.java (Java 21)
lllc build --target cs <file.lll>   compile → <file>.cs
lllc build --target llvm <file.lll> compile → <file>.ll  (experimental)
lllc build [dir]                    compile project (reads lll.toml)
lllc check <file.lll>               type-check, no codegen
lllc run   <file.lll>               compile and run via temporary F# project
lllc new   <name>                   scaffold new project
lllc install                        resolve deps into vendor/ + write ll.sum
lllc mod tidy                       sync dependencies
lllc mod add dep=https://repo#ref   add dependency
lllc mod why dep                    explain dependency chain
lllc mcp                            run MCP server (stdio)
```

---

## Status

All 10 compiler phases are complete and green in CI. Current release: **1.0.0**.

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Spec (grammar + corpus) | done |
| 2 | Lexer + Parser | done |
| 3 | Elaborator (exhaustiveness, tag/unit checks) | done |
| 4 | Hindley-Milner inference + TypedAST + trait dispatch | done |
| 5 | F# codegen + `lllc` CLI | done |
| 6 | Stdlib (~50 builtins) | done |
| 7 | Bootstrap fixpoint — `compiler₁.fs == compiler₂.fs` | done |
| 8 | Module system — `lll.toml`, multi-file, `lllc new`, topo-sort | done |
| 9 | MCP server — `lllc mcp` stdio server (10 tools) | done |
| 10 | Multi-platform codegen — TypeScript, Python, Java 21, C#, LLVM | done |

**Release contract (1.0):**
- **Stable:** core compiler + `lllc build/check/run/new/install/mcp` + targets `fs/ts/py/java/cs`
- **Experimental:** `lllc reverse`, `--target llvm` (subset backend)
- Full contract: [`docs/release-contract-1.0.md`](docs/release-contract-1.0.md)

**Self-hosted stdlib** — 10 modules (5857 LOC of ll-lang):

| Module | LOC | Description |
|--------|-----|-------------|
| `Map.lll` | 223 | Okasaki red-black tree, O(log n) |
| `Toml.lll` | 292 | TOML config parser |
| `Lexer.lll` | 473 | Tokenizer |
| `Parser.lll` | 802 | Recursive descent parser |
| `Elaborator.lll` | 344 | Type checker / name resolver |
| `Codegen.lll` | 569 | F# emitter |
| `CodegenTS.lll` | 492 | TypeScript emitter |
| `CodegenPy.lll` | 501 | Python emitter |
| `CodegenJava.lll` | 633 | Java 21 emitter |
| `Compiler.lll` | 1516 | Full pipeline (source → F#) |

**Bootstrap: COMPLETE.** `compiler₁.fs == compiler₂.fs` — ll-lang compiles itself (2900+ line bootstrap compiler, fixpoint achieved).

---

## Project Structure

```
spec/                      formal grammar (EBNF), type rules, example corpus
  grammar.ebnf
  type-system.md
  error-codes.md
  examples/valid/          working .lll programs (hello, basics, ADTs, ...)
  examples/invalid/        programs annotated with expected error codes
src/LLLangCompiler/        compiler library (F#)
  AST.fs                   untyped surface AST
  Lexer.fs                 tokenizer with layout (INDENT/DEDENT)
  Parser.fs                recursive-descent parser
  Elaborator.fs            name resolution, declared-type checking (E001-E005)
  Types.fs                 TypeScheme, Subst, generalize/instantiate
  TypedAST.fs              typed AST after H-M inference
  HMInfer.fs               Algorithm W, unification (E008), trait dispatch
  Codegen.fs               F# source emitter
  CodegenTS.fs             TypeScript source emitter
  CodegenPy.fs             Python source emitter
  CodegenJava.fs           Java 21 source emitter
  Compiler.fs              end-to-end pipeline + Target dispatch
src/LLLangTool/            lllc CLI
  Mcp.fs                   MCP server (10 tools)
  Program.fs               entry point
stdlib/                    self-hosted stdlib (10 modules, 5857 LOC ll-lang)
tests/LLLangTests/         xUnit test suite (see CI for current count)
docs/user-guide/           user documentation
docs/compiler-dev/         compiler developer documentation
```

---

## Roadmap

- **Language quality** — structured `LLError` fields, lexer error recovery, parser module split
- **Stdlib expansion** — more string/list/IO builtins, async IO primitives
- **Package registry** — `lllc install` with a central package index
- **LLVM parity + WASM** — close remaining LLVM feature gaps, then native executables
- **Language server** — LSP hover, go-to-definition, inline errors

---

## Design Philosophy

ll-lang is not a general-purpose language. It is optimized for one use case: **LLM agents writing correct code on the first attempt**. Every design decision — significant indentation, juxtaposition-based application, compact error codes, unit algebra, concise keyword vocabulary — is evaluated against that goal.

Less syntax to generate. More errors caught before execution. Faster iteration loops.

---

## Documentation

- [User Guide](docs/user-guide/) — language reference for ll-lang programmers
- [Getting Started](docs/getting-started.md) — step-by-step introduction
- [Language Spec](docs/language-spec.md) — formal language description
- [LLM Best Practices](docs/llm-best-practices.md) — how to use ll-lang with AI agents
- [Stdlib Reference](docs/stdlib-reference.md) — built-in functions and modules
- [Error Codes](spec/error-codes.md) — full list of compiler error codes
- [Compiler Dev Docs](docs/compiler-dev/) — contributing to the compiler itself

---

## Contributing

We welcome contributions. See [CONTRIBUTING.md](CONTRIBUTING.md) for setup instructions, architecture overview, and PR guidelines.

Quick start:

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
dotnet build
dotnet test
```

Found a bug? [Open an issue](https://github.com/Neftedollar/ll-lang/issues/new/choose).

---

## License

MIT
