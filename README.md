# ll-lang

[![Self-host CI](https://github.com/Neftedollar/ll-lang/actions/workflows/build.yml/badge.svg)](https://github.com/Neftedollar/ll-lang/actions/workflows/build.yml)

> **A statically-typed functional language designed for LLM code generation.** Token-efficient syntax, compiled = works, errors formatted for LLMs to read directly.

```
module Hello

Hello = printfn "Hello, ll-lang!"
```

```
$ lllc run hello.lll
Hello, ll-lang!
```

Jump to [Problem](#problem), [Solution](#solution), [Syntax](#syntax), [Getting Started](#getting-started).

## Status

Working end-to-end compiler with self-host CI (bootstrap `lllc` artifact + corpus checks + CLI e2e + LLVM smoke). Archived stage0 .NET code remains only under `obsolete/stage0` for emergency bootstrap diagnostics. All 10 compiler phases green: lexer → parser → elaborator → Hindley-Milner inference → F# codegen → `lllc` CLI → stdlib → module system → MCP server → TypeScript + Python + Java + C# + LLVM codegen.
Current release line: **1.0.0**.

**Release contract (1.0):**
- **Stable:** core compiler + `lllc build/run/new/install/mcp/self` + `lllc check [dir]` + targets `fs/ts/py/java/cs`
- **Experimental:** `lllc reverse` and `--target llvm` (subset backend, non-blocking for 1.0)
- Full contract: [`docs/release-contract-1.0.md`](docs/release-contract-1.0.md)

**Bootstrap: COMPLETE.** `compiler₁.fs == compiler₂.fs` — ll-lang compiles itself (2900+ line bootstrap compiler, fixpoint achieved).

**Self-hosted stdlib** — 10 modules (5857 LOC of ll-lang), covering parsing, type inference, codegen, and data structures:

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

**Token efficiency** — ll-lang is 8–17% more compact than F# on real code, and 1.3–5.9× more compact than TypeScript / Python / Java on type definitions.

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Spec (grammar + corpus) | ✅ |
| 2 | Lexer + Parser | ✅ |
| 3 | Elaborator (exhaustiveness, tag/unit checks) | ✅ |
| 4 | Hindley-Milner + TypedAST + trait dispatch | ✅ |
| 5 | F# codegen + `lllc` CLI | ✅ |
| 6 | Stdlib (~50 builtins) | ✅ |
| **7** | **Bootstrap fixpoint** — ll-lang compiles itself (`compiler₁.fs == compiler₂.fs`) | ✅ |
| **8** | **Module system** — `lll.toml`, multi-file builds, `lllc new`, topo-sort, E020/E024 | ✅ |
| **9** | **MCP server** — `lllc mcp` stdio server (self-hosted `lllcself`) with 28 tools for Claude Code / Cursor / Zed | ✅ |
| **10** | **Multi-platform codegen** — `lllc build --target ts\|py\|java\|cs\|llvm`; TypeScript DU + Python @dataclass + Java sealed interfaces + C# records + LLVM IR (`llvm` is experimental subset in 1.0) | ✅ |

## Getting Started

Bootstrap path (default) does not require .NET.

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
LLLC_BOOTSTRAP_REINSTALL=1 ./tools/check-selfhost-ci.sh
```

Archived stage0 remains in `obsolete/stage0` and is not part of default CI/path.

### Bootstrap installer (pinned release artifact)

For clean-machine bootstrap flows, install a pinned prebuilt `lllc` artifact:

```bash
./tools/bootstrap-self.sh install
BOOTSTRAP_BIN="$(./tools/bootstrap-self.sh path)"
"$BOOTSTRAP_BIN" check "$PWD/lllcself/src/Main.lll"
```

`tools/bootstrap-self.sh` verifies `sha256` against
[`bootstrap/lllc-bootstrap.lock.json`](bootstrap/lllc-bootstrap.lock.json)
before extraction. Use `--reinstall` to force refresh.

To build a new bootstrap release bundle and regenerate the lock:

```bash
./tools/build-bootstrap-artifacts.sh --version vX.Y.Z
```

Strict no-fallback launcher:

```bash
./tools/lllc-bootstrap.sh check "$PWD/lllcself/src/Main.lll"
```

`tools/lllc-bootstrap.sh` always executes the pinned bootstrap binary and does
not fall back to stage0 / `dotnet run` bridge paths.

### Run your first program

```bash
cat > hello.lll <<'EOF'
module Hello

Hello = printfn "Hello, ll-lang!"
EOF

./tools/lllc-bootstrap.sh run hello.lll
# → Hello, ll-lang!
```

### CLI

```
./tools/lllc-bootstrap.sh build   [--target fs|ts|py|java|cs|llvm] <file|dir>
./tools/lllc-bootstrap.sh compile [--target fs|ts|py|java|cs|llvm] <file.lll>
./tools/lllc-bootstrap.sh check   [--target fs|ts|py|java|cs|llvm] <file|dir>
./tools/lllc-bootstrap.sh run     [--target fs|ts|py|java|cs|llvm] <file.lll>
./tools/lllc-bootstrap.sh new <name>
./tools/lllc-bootstrap.sh install
./tools/lllc-bootstrap.sh mod add <name>=<source>
./tools/lllc-bootstrap.sh mod tidy
./tools/lllc-bootstrap.sh mod why <dep>
./tools/lllc-bootstrap.sh mcp
```

### Create a multi-file project

```bash
./tools/lllc-bootstrap.sh new myapp
cd myapp
# edit src/Main.lll, add more .lll files to src/
../tools/lllc-bootstrap.sh check .
../tools/lllc-bootstrap.sh build --target fs .
```

### Multi-target from lll.toml

```toml
# lll.toml
[project]
name = "myapp"

[platform]
use = ["fsharp", "typescript"]
```

```bash
lllc build    # compiles once, emits to both targets:
              #   bin/fsharp/myapp.fs
              #   bin/typescript/myapp.ts
```

## For LLM Agents: MCP Integration

ll-lang ships a built-in MCP server. Wire it to Claude Code, Cursor, or Zed — your LLM client gains structured tools to compile, check, and run ll-lang code without parsing shell output:

```json
// claude_desktop_config.json / .cursor/mcp.json
{
  "mcpServers": {
    "lllc": {
      "command": "lllc",
      "args": ["mcp"]
    }
  }
}
```

Available MCP tools (28):
- Core compile/check: `compile_source`, `check_source`, `compile_file`, `check_file`
- Diagnostics & repair: `diagnose_source`, `diagnose_file`, `explain_error`, `fix_suggest`, `apply_fix_preview`
- Formatting & AST: `format_source`, `format_file`, `parse_source`, `typed_ast`
- Project graph/build: `project_graph`, `check_project`, `build_project`
- Symbol navigation: `symbols`, `definition`, `references`
- Dependency helpers: `mod_add`, `mod_tidy`, `mod_why`
- Test helpers: `test_list`, `test_run` (structured self-host suite over `tools/check-selfhost-ci.sh`)
- Catalog/meta: `stdlib_search`, `list_errors`, `lookup_error`, `list_targets`

The agent can ask "does this compile?" and get a structured JSON response with error codes, line numbers, and fix hints — no scraping required.

## Problem

LLMs writing code in mainstream languages face two compounding problems: verbose syntax wastes tokens on ceremony rather than logic, and type errors only surface at runtime — after execution, often after damage is done. An LLM generating Python or TypeScript gets no signal that a tagged `UserId` string was passed where an `Email` is expected until the server blows up.

The feedback loop is slow, expensive, and noisy.

## Solution

ll-lang is built around four properties:

- **Token-efficient syntax** — no braces, no semicolons, no boilerplate. No `fn`/`type`/`in`/`then`/`with` keywords — declarations use an uppercase/lowercase convention.
- **Static types with inference** — Hindley-Milner type inference. Declare types where they matter, elide them everywhere else.
- **Compiled = works** — tag violations, unbound variables, non-exhaustive matches, and unit mismatches are caught at compile time, not runtime.
- **LLM-readable errors** — all errors follow a compact machine-readable format (`E001 12:5 TypeMismatch ...`) designed for direct consumption by an LLM agent.

## Syntax

### Functions and let bindings

No `fn` keyword — uppercase names declare types, lowercase names declare values. The body follows `=`.

```
module Examples.Basics

pi = 3.14159

add(a Int)(b Int) Int = a + b
double(x Int) = x * 2

-- inferred return type
square(x Int) = x * x

-- multi-branch if
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

Uppercase names introduce type declarations. `tag` declares a zero-cost wrapper.

```
module Examples.ADTs

-- sum type
Shape = Circle Float | Rect Float Float | Empty

-- parametric types
Maybe A = Some A | None
Result A E = Ok A | Err E

-- exhaustive pattern match
area(s Shape) Float =
  match s with
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty    -> 0.0

-- returning Maybe
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

-- declare tags (zero-cost type wrappers)
tag UserId
tag Email

-- tagged value
uid = "user-42"[UserId]

-- functions reject wrong tags at compile time
getUser(id Str[UserId]) Maybe[Str] = Some "alice"
sendEmail(to Str[Email]) = to

-- unit algebra: inferred return type Float[m/s]
tag m
tag s

speed(d Float[m])(t Float[s]) = d / t
```

### Modules and Imports

```
module Examples.App

import Map
import Toml

config = Toml.parse (readFile "config.toml")
```

### Keywords

ll-lang has 15 keywords: `match`, `if`, `else`, `import`, `export`, `module`, `trait`, `impl`, `external`, `opaque`, `tag`, `unit`, `true`, `false`, `let`. Everything else — most function/type declaration forms — is expressed through the uppercase/lowercase convention.

## Error Format

All compiler errors are short, structured, and machine-readable — designed so an LLM agent can parse them without extracting from prose:

| Code | Meaning | Example |
|------|---------|---------|
| `E001` | Type mismatch | `E001 12:5 TypeMismatch Str Str[UserId]` |
| `E002` | Unbound variable | `E002 8:3 UnboundVar username` |
| `E003` | Non-exhaustive match | `E003 15:1 NonExhaustiveMatch Shape missing:Empty` |
| `E004` | Unit mismatch | `E004 20:9 UnitMismatch Float[m] Float[s]` |
| `E005` | Tag violation | `E005 7:14 TagViolation Str[Email] Str[UserId]` |

Format: `EXXX line:col ErrorKind details`. No stack traces, no paragraphs, one line per error, parseable by regex.

## Multi-Platform Output

Write once in ll-lang, compile to any target:

```bash
lllc build --target fs   adts.lll   # → F# discriminated unions
lllc build --target ts   adts.lll   # → TypeScript sealed interfaces
lllc build --target py   adts.lll   # → Python @dataclass + Union
lllc build --target java adts.lll   # → Java 21 sealed interfaces
lllc build --target cs   adts.lll   # → C# records + interfaces
lllc build --target llvm adts.lll   # → LLVM IR (experimental subset)
```

Same source, same semantics on stable targets (`fs/ts/py/java/cs`), with an additional experimental LLVM backend.

## Compiler Pipeline

```
Source (.lll)
    ▼  Lexer       — tokenizes with synthetic INDENT/DEDENT
    ▼  Parser      — produces AST
    ▼  Elaborator  — name resolution, tag checks, exhaustiveness
    ▼  HMInfer     — Algorithm W, let-generalization, trait dispatch (E006),
                     occurs check (E008), unit algebra preservation
    ▼  Codegen     — emits idiomatic F# / TS / Python / Java / C# / LLVM
    ▼  dotnet run --project <tmp fsproj>  — runs the result (via `lllc run`)
```

## Project Structure

```
spec/                      — formal grammar (EBNF), type rules, example corpus
  grammar.ebnf
  type-system.md
  error-codes.md
  examples/valid/          — working .lll programs (hello, basics, ADTs, ...)
  examples/invalid/        — programs annotated with expected error codes
lllcself/src/              — self-hosted ll-lang implementation of CLI subcommands
  Mcp.lll                  — MCP server (28 tools for LLM clients)
stdlib/                    — self-hosted stdlib (10 modules, 5857 LOC ll-lang)
obsolete/stage0/           — archived stage0 (.NET) compiler/tool/tests (manual use only)
docs/user-guide/           — user documentation
docs/compiler-dev/         — compiler developer documentation
```

## Roadmap

All 10 phases complete. Upcoming work:

- **Language quality** — structured `LLError` fields, lexer error recovery, parser module split
- **Stdlib expansion** — more string/list/IO builtins, async IO primitives
- **Package registry** — `lllc install` with a central package index
- **LLVM parity + WASM target** — close remaining LLVM feature gaps, then native executables
- **Language server** — LSP hover, go-to-definition, inline errors

## Design Philosophy

ll-lang is not a general-purpose language. It is optimized for one use case: **LLM agents writing correct code on the first attempt**. Every design decision — significant indentation, juxtaposition-based application, compact error codes, unit algebra, concise keyword vocabulary — is evaluated against that goal.

Less syntax to generate. More errors caught before execution. Faster iteration loops.

## License

MIT
