# ll-lang Language Design Spec

**Date:** 2026-04-07  
**Status:** Approved  
**Author:** roman + Claude (brainstorming session)

---

## Problem Statement

LLMs writing software spend tokens on verbose syntax, deal with ambiguous semantics, and discover bugs only at runtime. ll-lang solves this: a statically typed, maximally compact language where **compiled = works**, designed for LLM generation with a human-readable mode as a secondary output.

---

## Core Goals

1. **Token efficiency** — minimum tokens to express a complete, correct program
2. **Compiler as oracle** — type errors caught at compile time, not runtime; fast LLM feedback loop
3. **Unambiguous** — one way to express one thing; compiler always interprets intent correctly
4. **Multi-target** — compile to .NET, JS/TS, Python, JVM, native binary
5. **Human mode** — separate rendering mode for human review; not the default

---

## File Extension

`.lll` — three letters, unique, no collision with existing formats (`.ll` is taken by LLVM IR).

---

## Section 1: Syntax

### Philosophy

- Significant indentation (like Haskell/Python)
- ASCII-only core — no Unicode operators (tokenization issues across LLM families)
- No optional tokens — every character carries meaning
- No semicolons, no mandatory braces, no trailing commas

### Functions

```lll
-- single expression
fn add(a Int)(b Int) Int = a + b

-- multi-expression (indented body)
fn clamp(x Int)(lo Int)(hi Int) Int =
  if x < lo then lo
  else if x > hi then hi
  else x

-- return type optional when inferable (H-M)
fn double(x Int) = x * 2

-- lambda
let double = \x. x * 2

-- pipe operator (most common composition pattern)
fn process(s Str) Int = s -> trim -> len
```

### Types

```lll
-- product type (record): comma-separated, no braces
type Point = x Float, y Float

-- sum type (ADT)
type Shape = Circle Float | Rect Float Float | Empty

-- parametric types
type Maybe A = Some A | None
type Result A E = Ok A | Err E

-- tag: unified semantic label for any type (replaces both newtype and unit)
-- numeric tags support algebraic unit algebra; all tags prevent implicit coercion
tag UserId   -- applied to Str
tag Email    -- applied to Str
tag m        -- applied to Float, participates in unit algebra
tag s        -- applied to Float
tag kg       -- applied to Float
```

### Pattern Matching (exhaustive — compiler enforces all branches)

```lll
fn area(s Shape) Float =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty -> 0.0
```

### Literals and Collections

```lll
let xs = [1 2 3]           -- list (space-separated, no commas)
let t = 1, "hello"         -- tuple (parens optional, only for grouping/human readability)
let p = Point{x 1.0, y 2.0} -- record construction (type prefix + space-separated field:value)
```

### Two Output Modes

Both modes represent the same AST — different printers, not different compilers.

**Compact (default — for LLM generation and consumption):**
```lll
fn f(x Int)(y Int)=if x>y then x else y
```

**Human (`--human` flag or IDE mode):**
```lll
fn f (x : Int) (y : Int) : Int =
  if x > y then
    x
  else
    y
-- inferred: f :: Int -> Int -> Int
```

Human mode adds: whitespace, inferred type annotations, formatted indentation.

---

## Section 2: Type System

### Foundation: Hindley-Milner inference

Full H-M type inference. Type annotations optional everywhere except:
- Public module exports (recommended for API stability)
- Where inference would be ambiguous

```lll
fn id(x A) A = x           -- polymorphic, inferred
fn const(a A)(b B) A = a   -- inferred
```

### No Nulls, No Exceptions

```lll
-- null replaced by Maybe
fn findUser(id UserId) Maybe[User] = ...

-- exceptions replaced by Result
fn divide(a Float)(b Float) Result[Float Str] =
  if b == 0.0 then Err "div by zero"
  else Ok (a / b)
```

### Traits (HKT — Higher-Kinded Types)

Type classes over type constructors. Enables single generic implementation over any `F`.

```lll
trait Functor F =
  fn map(f A->B)(fa F[A]) F[B]

trait Monad F =
  fn pure(a A) F[A]
  fn bind(fa F[A])(f A->F[B]) F[B]

impl Functor Maybe =
  fn map(f A->B)(fa Maybe[A]) Maybe[B] =
    | Some a -> Some (f a)
    | None -> None

impl Monad Maybe =
  fn pure(a A) Maybe[A] = Some a
  fn bind(fa Maybe[A])(f A->Maybe[B]) Maybe[B] =
    | Some a -> f a
    | None -> None
```

### Constraint-based dispatch

```lll
-- works for any F with Functor impl
fn transform[F: Functor](xs F[A])(f A->B) F[B] = map f xs

transform (Some 5) (\x. x * 2)   -- -> Some 10
transform [1 2 3] (\x. x * 2)    -- -> [2 4 6]
```

### Semantic Tags (unified system)

`tag` is a single mechanism that replaces both newtypes and units of measure.
A tag applied to any value creates a distinct type — not implicitly convertible.
Tags on numeric types additionally participate in unit algebra (compile-time dimensional analysis).

```lll
tag UserId
tag Email
tag m
tag s
tag kg

-- tag applied at value site with postfix [Tag]
let uid = "user-42"[UserId]      -- type: Str[UserId]
let dist = 5.0[m]                -- type: Float[m]
let age = 25[Years]              -- type: Int[Years]

-- functions declare tagged parameters
fn getUser(id Str[UserId]) Maybe[User] = ...
fn sendEmail(to Str[Email]) IO[Unit] = ...

-- getUser "raw-string"          <- E005: Str != Str[UserId]
-- getUser "user-42"[UserId]     <- ok

-- numeric tags: unit algebra inferred by compiler
fn speed(d Float[m])(t Float[s]) = d / t
-- return type inferred as Float[m/s]

fn kineticEnergy(m Float[kg])(v Float[m/s]) = 0.5 * m * v * v
-- return type inferred as Float[kg*m^2/s^2]

-- speed(5.0[kg])(2.0[s])        <- E004 UnitMismatch: expected Float[m], got Float[kg]
-- non-numeric tags don't compose algebraically — only identity check
```

### Phantom Types

Tags as type-level state markers. Zero runtime cost. Encodes valid state transitions
in the type system — compiler prevents using unvalidated data where validated is required.

```lll
tag Validated
tag Raw

-- Email[Raw] and Email[Validated] are distinct types
type Email[state] = Str

fn validate(s Str) Result[Email[Validated] Str] = ...
fn send(e Email[Validated]) IO[Unit] = ...

-- send "raw@mail.com"              <- E001: Str != Email[Validated]
-- send "raw@mail.com"[Raw]         <- E001: Email[Raw] != Email[Validated]
-- validate "x@y.com" -> Ok e; send e   <- ok
```

### What Is Intentionally Absent

- No `null` / `undefined`
- No implicit type coercions
- No exceptions (use `Result`)
- No function overloading by name (use traits)
- No `for`/`while` loops — recursion + `map`/`fold` only (one way to iterate)

---

## Section 3: Compiler Architecture

### Pipeline

```
Source (.lll)
    │
    ▼
[Lexer] → Token stream
    │
    ▼
[Parser] → AST
    │
    ▼
[Elaborator] → Typed AST
    │  H-M inference, HKT resolution,
    │  trait dispatch, unit checking
    ▼
[Desugarer] → Core IR
    │  typed lambda calculus
    │  all patterns/sugar expanded
    ▼
[Optimizer] → Core IR  (optional in MVP)
    │
    ▼
[Backend] → target output
```

### Core IR

Minimal typed lambda calculus. All syntactic sugar desugars to this set.

```
expr =
  | Var name
  | Lit value
  | App expr expr
  | Lam name type expr
  | Let name expr expr
  | Match expr [branch]
  | Con name [expr]
  | Record [(name, expr)]
```

### Backend Interface

Abstract — supports both source transpilation and direct bytecode emission.

```lll
trait Backend =
  fn emit(ir CoreIR)(opts Options) Output

impl Backend FSharpSource     -- .lll → .fs → (F# compiler) → IL
impl Backend DotNetIL         -- .lll → IL directly (Mono.Cecil)
impl Backend TypeScript       -- .lll → .ts
impl Backend PythonSource     -- .lll → .py
impl Backend LlvmIR           -- .lll → LLVM IR → native binary
impl Backend KotlinSource     -- .lll → .kt → JVM bytecode
```

---

## Section 4: Module System

```lll
module MyLib.Utils

import Std.List
import Std.Maybe
import Platform.IO       -- abstract platform interface

export fn readLines(path Str) IO[List[Str]] = ...
```

### Platform namespaces

`Platform.*` modules are abstract interfaces. Resolved per target at compile time.

```lll
import Platform.IO          -- available on all targets
import Platform.DotNet.ASP  -- .NET only; compile error on other targets
import Platform.ML          -- Python target: maps to numpy/torch bindings
```

If a target doesn't implement a platform module → `E007 PlatformMismatch` at compile time.

---

## Section 5: Error Model

### Two error formats

**Compact (default — for LLM consumption):**
```
E001 12:5 TypeMismatch UserId Int hint:wrap:UserId
E003 8:1 NonExhaustiveMatch Shape missing:Empty
```

**Human (`--human` flag):**
```
Error[E001] Type mismatch at line 12, col 5
  Expected: UserId
  Got:      Int
  Hint: wrap with UserId(...)
```

### Error taxonomy (fixed codes — LLM knows them)

| Code | Name | Description |
|------|------|-------------|
| E001 | TypeMismatch | Wrong type at usage site |
| E002 | UnboundVar | Variable not in scope |
| E003 | NonExhaustiveMatch | Pattern match missing branches |
| E004 | UnitMismatch | Wrong unit of measure |
| E005 | NewtypeViolation | Raw type passed where newtype expected |
| E006 | MissingImpl | No trait impl found for type |
| E007 | PlatformMismatch | Platform module unavailable on target |
| E008 | InfiniteType | Recursive type without Fix wrapper |

### LLM feedback loop

```
generate .lll code
    │
    ▼
llc compile --target dotnet --errors compact
    │
    ├── ok → ship
    └── errors → parse compact errors → targeted fix → retry
```

Compiler as fast oracle. No tests needed to catch type/unit/pattern errors.

---

## Section 6: Standard Library

Minimal core. Everything platform-specific goes in `Platform.*`.

```
Std.List     -- map fold filter zip head tail
Std.Maybe    -- map bind fromMaybe
Std.Result   -- map bind mapErr
Std.Str      -- len trim split join
Std.Math     -- +  -  *  /  mod  abs  sqrt
Std.IO       -- abstract IO (implemented per platform)
```

Inclusion criterion: if available on every target platform, it goes in Std.

---

## Section 7: Roadmap

| Phase | Scope | Output |
|-------|-------|--------|
| **1 — Spec** | Grammar (EBNF), type system formal spec, error codes, example programs | Formal spec doc |
| **2 — .NET MVP** | Lexer → Parser → H-M inference → F# source codegen, CLI `llc` | Working compiler, .NET target |
| **2b — .NET IL** | Direct IL emission via Mono.Cecil (no F# compiler dependency) | Standalone .NET binary output |
| **3 — JS/TS** | TypeScript codegen, Platform.Browser + Platform.Node | Web/Node target |
| **4 — Python** | Python codegen, Platform.ML (numpy/torch) | AI/ML target |
| **5 — JVM** | Kotlin source codegen → JVM bytecode | Enterprise target |
| **6 — Binary** | LLVM IR backend → native binary | Native target |
| **7 — Bootstrap** | Rewrite compiler in ll-lang itself (self-hosting) | `llc` compiles itself |

---

## Backlog

- **llm-bytecode-runtime**: ll-lang as an LLM scratchpad/intermediate execution format — LLM generates structured plan, interpreter executes. Separate project once core language is stable.
- **mcp-compiler-integration**: Embed an MCP server directly in `llc` so an LLM agent can call compiler tools natively — `llc/compile`, `llc/check-types`, `llc/get-errors`, `llc/format`. Gives LLMs structured, programmatic access to the compiler without shell invocation. Phase 3+ after CLI is stable.
- **refinement-types**: Predicate-based types like `{x Int | x > 0}` and `{xs List[A] | len xs > 0}`. Requires SMT solver (Z3). Catches value-level invariants at compile time. Phase 3+ extension.
- **self-hosting (bootstrap)**: Write the ll-lang compiler in ll-lang itself. Phase 7 milestone. Proof of language maturity — if the compiler can compile itself, the language is expressive enough for real systems. Classic bootstrap sequence: compiler₀ (F#) → compile compiler.lll → compiler₁ (ll-lang binary) → compile compiler.lll again → compiler₂ must equal compiler₁ (fixpoint).

---

## Key Decisions Summary

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Syntax style | Short-keyword functional (B) + symbol shortcuts (A) | LLM-friendly without fine-tuning |
| Type inference | Hindley-Milner + optional annotations | Max brevity, max safety |
| HKT | Yes (Functor, Monad, etc.) | High abstraction = fewer tokens |
| Null handling | No null, use Maybe | Eliminates class of LLM runtime bugs |
| Error handling | No exceptions, use Result | Compiler-visible error handling |
| Loops | No for/while, use map/fold | One way to iterate |
| Semantic tags | `tag UserId`, `"x"[UserId]`, `5.0[m]` | Unified newtype + units; postfix syntax minimal |
| Unit algebra | numeric tags only, return type inferred | Dimension safety without extra annotations |
| Phantom types | `type Email[state]`, `tag Validated` | State-machine safety via type params, zero cost |
| Output modes | Compact (default) + Human (flag) | Both LLM and human needs met |
| Backend strategy | Abstract Backend trait, source first then IL | Incremental, testable |
| File extension | `.lll` | Unique, no conflicts |
