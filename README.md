# ll-lang

[![Build & Test](https://github.com/Neftedollar/ll-lang/actions/workflows/build.yml/badge.svg)](https://github.com/Neftedollar/ll-lang/actions/workflows/build.yml)

> **A statically-typed functional language designed for LLM code generation.** Token-efficient syntax, compiled = works, and errors formatted for LLMs to read directly.

```
module Hello

fn main() = printfn "Hello, ll-lang!"
```

```
$ lllc run hello.lll
Hello, ll-lang!
```

Jump to [Problem](#problem), [Solution](#solution), [Syntax](#syntax), [Getting Started](#getting-started).

## Status

Working end-to-end compiler with a **395-test** suite, written in F# / .NET 10. All 7 compiler phases green: lexer → parser → elaborator → Hindley-Milner inference → F# codegen → `lllc` CLI → stdlib (~50 builtins).

**Bootstrap progress (Phase 7 — ll-lang hosting itself):**

| Artifact | Shape | Status | Source |
|---|---|---|---|
| Lexer | multi-char idents, keywords, ops | ✅ | [`09-lexer-real.lll`](spec/examples/valid/09-lexer-real.lll) |
| Arithmetic parser | `+ - * /`, precedence, parens | ✅ | [`11-parser-real.lll`](spec/examples/valid/11-parser-real.lll) |
| Type-decl parser | sum types, type params | ✅ | [`12-typeparser-real.lll`](spec/examples/valid/12-typeparser-real.lll) |
| Fn-decl parser | curried params, return types | ✅ | [`13-fnparser-real.lll`](spec/examples/valid/13-fnparser-real.lll) |
| Expression parser | let / if / match / lambda / app | ✅ | [`14-exprparser-real.lll`](spec/examples/valid/14-exprparser-real.lll) |
| **Full module parser** | **all of the above, in one program** | ✅ | [`15-moduleparser-real.lll`](spec/examples/valid/15-moduleparser-real.lll) |
| Elaborator — name res + exhaustiveness | collect + check passes, E002 unbound-var, E003 non-exhaustive match | ✅ | [`16-elaborator-real.lll`](spec/examples/valid/16-elaborator-real.lll) |
| **Parser + Elaborator pipeline** | **lex → parse → elaborate in one ll-lang program** | ✅ | [`17-pipeline-real.lll`](spec/examples/valid/17-pipeline-real.lll) |
| HM inference (unify) | `TypeExpr` AST + `Subst` + `unify` mirroring `HMInfer.unify` | ✅ | [`18-hminfer-real.lll`](spec/examples/valid/18-hminfer-real.lll) |

The module parser (979 lines of ll-lang) consumes `module M \n import ... \n tag ... \n type ... \n let ... \n fn ... = ...` and pretty-prints a `List[Decl]` AST — **real proof that ll-lang can express its own front-end**. The elaborator slice (512 lines) walks a hardcoded `List[Decl]` AST with a two-pass `collectDecls` → `checkDecls` pipeline plus an exhaustiveness pass, and emits `E002 UnboundVar <name>` for every free variable and `E003 NonExhaustiveMatch <type> missing <ctor>` for every clause-sugar match that doesn't cover its sum type — mirroring the F# host elaborator's name-resolution + constructor-coverage semantics. The pipeline slice (~1500 lines) stitches the two halves into one program: a source string goes in, `tokenize` + `parseModule` + `elaborate` runs back-to-back, and the resulting error list is printed. **Showcase milestone** — first time two compiler layers authored in ll-lang share a single AST and run back-to-back on a real source string.

Still to come: elaborator, HM-inference, and codegen rewrites in ll-lang, then bootstrap fixpoint (compiler₀ compiles compiler.lll → compiler₁; compiler₁ compiles compiler.lll → compiler₂ == compiler₁).

| Phase | Description | Status |
|---|---|---|
| 1 | Spec (grammar + corpus) | ✅ |
| 2 | Lexer + Parser | ✅ |
| 3 | Elaborator (exhaustiveness, tag/unit checks) | ✅ |
| 4 | Hindley-Milner + TypedAST + trait dispatch | ✅ |
| 5 | F# codegen + `lllc` CLI | ✅ |
| 6 | Stdlib (~50 builtins) | ✅ |
| **7.1 – 7.5** | **ll-lang front-end in ll-lang** (lexer + 4 parser slices + full module parser, 979 lines) | ✅ |
| **7.6a + 7.6b** | **Elaborator slices A + B in ll-lang** (name resolution + E002 unbound-var; constructor-coverage exhaustiveness + E003 non-exhaustive match, 512 lines) | ✅ |
| **7.6 integration** | **Parser + elaborator pipeline in one ll-lang program** (`17-pipeline-real.lll`, ~1500 lines) | ✅ |
| **7.7a** | **HM inference slice A in ll-lang** (`TypeExpr`, `Subst`, `unify`, `18-hminfer-real.lll`, 287 lines) | ✅ |
| 7.6+ / 7.7+ | E001 type checking, `inferExpr`, multi-line bodies, `trait`/`impl`, codegen-in-ll-lang | 🚧 |
| 7.8 | Bootstrap fixpoint | ⏳ |

## Getting Started

Requires [.NET 10](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
dotnet build
dotnet test    # 395 tests
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

### See ll-lang parse itself

```bash
dotnet run --project src/LLLangTool -- run spec/examples/valid/15-moduleparser-real.lll
```

This runs a 979-line ll-lang program that tokenizes, parses, and pretty-prints a whole ll-lang module — written entirely in ll-lang itself.

### CLI

```
lllc build <file.lll>   # elaborate + infer + emit <file>.fs
lllc run <file.lll>     # build + execute via dotnet fsi
```

## Problem

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

Format: `EXXX line:col ErrorKind details`. No stack traces, no paragraphs, one line per error, parseable by regex.

## Compiler Pipeline

```
Source (.lll)
    ▼  Lexer       — tokenizes with synthetic INDENT/DEDENT
    ▼  Parser      — produces AST
    ▼  Elaborator  — name resolution, tag checks, exhaustiveness
    ▼  HMInfer     — Algorithm W, let-generalization, trait dispatch (E006),
                     occurs check (E008), unit algebra preservation
    ▼  Codegen     — emits idiomatic F# source
    ▼  dotnet fsi  — runs the result (via `lllc run`)
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
tests/LLLangTests/         — xUnit test suite (395 tests)
```

## Roadmap

- **Phase 7.6** — elaborator in ll-lang (name resolution shipped in 7.6a; constructor-coverage exhaustiveness shipped in 7.6b; E001 type checking, E004 / E005 tag checks remain), plus heavier front-end slices (multi-line fn bodies, `trait` / `impl` module-level decls).
- **Phase 7.7** — H-M inference rewritten in ll-lang.
- **Phase 7.8** — codegen in ll-lang, then bootstrap fixpoint (compiler₁ == compiler₂).
- **Multi-target backends** — TypeScript / Python / JVM / LLVM after self-hosting lands.

## Design Philosophy

ll-lang is not a general-purpose language. It is optimized for one use case: **LLM agents writing correct code on the first attempt**. Every design decision — significant indentation, juxtaposition-based application, compact error codes, unit algebra — is evaluated against that goal.

Less syntax to generate. More errors caught before execution. Faster iteration loops.

## License

MIT
