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
  Parser         — produces typed AST
    │
    ▼
  Elaborator     — resolves names, checks tags, validates exhaustiveness
    │
    ▼
  [Phase 4] H-M Inference  — full type inference (in progress)
    │
    ▼
  [Phase 5] Codegen        — .NET IL / F# target
```

## Status

**Phases 1–3 complete. 88 tests passing.**

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Spec — grammar, type rules, example corpus | Done |
| 2 | Lexer + Parser | Done |
| 3 | Elaborator — name resolution, tag/unit checks, exhaustiveness | Done |
| 4 | H-M type inference | In progress |
| 5 | Code generation (.NET IL) | Planned |
| 6 | Standard library | Planned |
| 7 | Self-hosting | Planned |

## Getting Started

Requires [.NET 10](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
dotnet build
dotnet test
```

All 88 tests should pass.

## Project Structure

```
spec/                    — formal grammar (EBNF), type rules, example corpus
  grammar.ebnf
  examples/valid/        — valid .lll programs
  examples/invalid/      — programs with expected error codes
src/LLLangCompiler/      — compiler source (F#)
  AST.fs                 — AST types
  Lexer.fs               — tokenizer
  Parser.fs              — parser
  Elaborator.fs          — name resolution + type checking
tests/LLLangTests/       — test suite (88 tests)
```

## Roadmap

- **Phase 4** — Hindley-Milner inference: full parametric polymorphism, let-generalization
- **Phase 5** — Code generation: compile to .NET IL or transpile to F#
- **Phase 6** — Standard library: List, Option, Result, IO primitives
- **Phase 7** — Self-hosting: ll-lang compiler written in ll-lang

## Design Philosophy

ll-lang is not a general-purpose language. It is optimized for one use case: **LLM agents writing correct code on the first attempt**. Every design decision — significant indentation, juxtaposition-based application, compact error codes, unit algebra — is evaluated against that goal.

Less syntax to generate. More errors caught before execution. Faster iteration loops.

## License

MIT
