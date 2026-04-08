# ll-lang

[![Build & Test](https://github.com/Neftedollar/ll-lang/actions/workflows/build.yml/badge.svg)](https://github.com/Neftedollar/ll-lang/actions/workflows/build.yml)

> A statically typed functional language designed for LLM code generation. Token-efficient syntax — compiled = works.

## The Problem

LLMs writing code in mainstream languages face two compounding problems: verbose syntax wastes tokens on ceremony rather than logic, and type errors only surface at runtime — after execution, often after damage is done. An LLM generating Python or TypeScript gets no signal that a tagged `UserId` string was passed where an `Email` is expected until the server blows up.

The feedback loop is slow, expensive, and noisy.

## Solution

ll-lang is built around four properties:

- **Token-efficient syntax** — no braces, no semicolons, no boilerplate. Functions, ADTs, and pattern matching in the fewest possible tokens.
- **Static types with inference** — Hindley-Milner type inference. Declare types where they matter, elide them everywhere else.
- **Compiled = works** — tag violations, unbound variables, non-exhaustive matches, and unit mismatches are caught at compile time, not runtime.
- **LLM-readable errors** — all errors follow a compact machine-readable format (`E001 12:5 TypeMismatch ...`) designed for direct consumption by an LLM agent.

## Syntax

### Functions and let bindings

```
module Examples.Basics

let pi = 3.14159

fn add(a Int)(b Int) Int = a + b
fn double(x Int) = x * 2

-- inferred return type
fn square(x Int) = x * x

-- multi-branch if
fn clamp(x Int)(lo Int)(hi Int) Int =
  if x < lo then lo
  else if x > hi then hi
  else x

-- lambda
let triple = \x. x * 3

-- local binding
fn example = let y = double 5 in y + 1
```

### Algebraic Data Types and Pattern Matching

```
module Examples.ADTs

-- product type (record)
type Point = x Float, y Float

-- sum type
type Shape = Circle Float | Rect Float Float | Empty

-- parametric types
type Maybe A = Some A | None
type Result A E = Ok A | Err E

-- exhaustive pattern match
fn area(s Shape) Float =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty    -> 0.0

-- returning Maybe
fn safeDivide(a Float)(b Float) Maybe[Float] =
  if b == 0.0 then None
  else Some (a / b)
```

### Tags, Phantom Types, and Unit Algebra

```
module Examples.Tags

-- declare tags (zero-cost type wrappers)
tag UserId
tag Email

-- tagged value
let uid = "user-42"[UserId]

-- functions reject wrong tags at compile time
fn getUser(id Str[UserId]) Maybe[Str] = Some "alice"
fn sendEmail(to Str[Email]) = to

-- unit algebra: inferred return type Float[m/s]
tag m
tag s

fn speed(d Float[m])(t Float[s]) = d / t

-- phantom types for state machines
tag Validated
tag Raw

type Email[state] = Str

fn validate(s Str) Result[Email[Validated] Str] =
  if s != "" then Ok s
  else Err "empty"
```

## Error Format

All compiler errors are short, structured, and machine-readable — designed so an LLM agent can parse them without extracting from prose:

| Code | Meaning | Example |
|------|---------|---------|
| `E001` | Type mismatch | `E001 12:5 TypeMismatch Str Str[UserId]` |
| `E002` | Unbound variable | `E002 8:3 UnboundVar username` |
| `E003` | Non-exhaustive match | `E003 15:1 NonExhaustiveMatch Shape missing:Empty` |
| `E004` | Unit mismatch | `E004 20:9 UnitMismatch Float[m] Float[s]` |
| `E005` | Tag violation | `E005 7:14 TagViolation Str[Email] Str[UserId]` |

Format: `EXXX line:col ErrorKind details`

No stack traces. No paragraphs. One line per error, parseable by regex.

## Compiler Pipeline

```
Source (.lll)
    │
    ▼
  Lexer          — tokenizes with synthetic INDENT/DEDENT
    │
    ▼
  Parser         — produces AST
    │
    ▼
  Elaborator     — resolves names, checks tags, validates exhaustiveness
    │
    ▼
  HMInfer        — Algorithm W, let-generalization, trait dispatch (E006),
                   occurs check (E008), unit algebra preservation
    │
    ▼
  Codegen        — emits idiomatic F# source
    │
    ▼
  dotnet fsi     — runs the result (via `lllc run`)
```

## Status

**Phases 1–6 complete + Phase 7.1/7.2/7.3a (real lexer, real recursive-descent expression parser, AND real type-declaration parser, all written in ll-lang itself, with surface tuple literals so parsers can return `(parsed, rest)` directly). 357 tests passing. Working end-to-end compiler with stdlib, Char/file IO, char literals (`'a'`), indented `let` blocks, tuple patterns, `::` cons in patterns/expressions, `match` in expression position, `let (a, b) = ...` destructuring, multi-line sum types, mutually recursive top-level functions, a real arithmetic parser (`(1 + (2 * 3))` precedence verified), and a real type-declaration parser (four `type` decls round-trip through tokenize → parse → pretty-print) — see `spec/examples/valid/09-lexer-real.lll`, `11-parser-real.lll`, and `12-typeparser-real.lll`.**

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Spec — grammar, type rules, example corpus | ✅ Done |
| 2 | Lexer + Parser | ✅ Done |
| 3 | Elaborator — name resolution, tag/unit checks, exhaustiveness | ✅ Done |
| 4 | Hindley-Milner inference + TypedAST + trait dispatch | ✅ Done |
| 5 | F# source codegen + `lllc` CLI (`build` / `run`) | ✅ Done |
| 6 | Standard library — List, Maybe, Result, Str, Math, IO builtins | ✅ Done |
| 7.1 | Real lexer in ll-lang — `09-lexer-real.lll` | ✅ Done |
| 7.2 | Recursive-descent expression parser in ll-lang — `11-parser-real.lll` | ✅ Done |
| 7.3a | Type-declaration parser in ll-lang — `12-typeparser-real.lll` | ✅ Done |
| 7.3b+ | Fn-decl parser, full expressions, full ll-lang parser/elaborator/codegen in ll-lang | Planned |

## Getting Started

Requires [.NET 10](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
dotnet build
dotnet test    # 357 tests
```

### Run your first program

```bash
cat > hello.lll <<'EOF'
module Hello

fn main() = printfn "Hello, ll-lang!"
EOF

dotnet run --project src/LLLangTool -- run hello.lll
# → Hello, ll-lang!
```

### CLI

```
lllc build <file.lll>   # elaborate + infer + emit <file>.fs
lllc run <file.lll>     # build + execute via dotnet fsi
```

## Project Structure

```
spec/                      — formal grammar (EBNF), type rules, example corpus
  grammar.ebnf
  type-system.md
  error-codes.md
  examples/valid/          — working .lll programs (hello, basics, ADTs, ...)
  examples/invalid/        — programs annotated with expected error codes
src/LLLangCompiler/        — compiler library (F#)
  AST.fs                   — untyped surface AST
  Lexer.fs                 — tokenizer with layout (INDENT/DEDENT)
  Parser.fs                — recursive-descent parser
  Elaborator.fs            — name resolution, declared-type checking (E001-E005)
  Types.fs                 — TypeScheme, Subst, generalize/instantiate
  TypedAST.fs              — typed AST after H-M inference
  HMInfer.fs               — Algorithm W, unification (E008), trait dispatch (E006)
  Codegen.fs               — F# source emitter
  Compiler.fs              — end-to-end pipeline entry point
src/LLLangTool/            — `lllc` CLI (build / run)
tests/LLLangTests/         — xUnit test suite (357 tests)
```

## Roadmap

- **Phase 7** — Self-hosting: rewrite the ll-lang compiler in ll-lang itself. The real lexer in `spec/examples/valid/09-lexer-real.lll` is the first concrete piece in place; Phase 7.2 tackles the parser.
- **Multi-target** — TypeScript / Python / JVM / LLVM backends after self-hosting

## Design Philosophy

ll-lang is not a general-purpose language. It is optimized for one use case: **LLM agents writing correct code on the first attempt**. Every design decision — significant indentation, juxtaposition-based application, compact error codes, unit algebra — is evaluated against that goal.

Less syntax to generate. More errors caught before execution. Faster iteration loops.

## License

MIT
