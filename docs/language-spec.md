# ll-lang Language Specification

**Status:** canonical language spec  
**Current stable line:** `1.x`  
**Planned next line:** `v2`  
**Extension:** `.lll`  
**Encoding:** UTF-8 source, ASCII-only operator surface

## 1. Authority and versioning

This document is the single source of truth for ll-lang language behavior.

It is versioned in three buckets:

- **Stable `1.x`** — behavior covered by the shipped release contract.
- **Planned `v2`** — intended target architecture and language direction.
- **Deferred** — explicitly outside the `v2` commitment.

If another document disagrees with this one:

- use this document for current language behavior
- treat older design documents as motivation or backlog unless they are updated

Supporting specs for `v2` live in:

- [v2-type-system](/Users/roman/Documents/dev/tens/code/ll-lang/spec/v2-type-system.md)
- [v2-project-system](/Users/roman/Documents/dev/tens/code/ll-lang/spec/v2-project-system.md)
- [v2-self-hosting](/Users/roman/Documents/dev/tens/code/ll-lang/spec/v2-self-hosting.md)
- [v2-llm-tooling](/Users/roman/Documents/dev/tens/code/ll-lang/spec/v2-llm-tooling.md)

The release contract for `1.0` is defined separately in
[release-contract-1.0.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/release-contract-1.0.md).

## 2. Stable `1.x`

### 2.1 Product definition

`1.x` is a statically-typed functional language compiled to:

- F# (`fs`, default)
- TypeScript (`ts`)
- Python (`py`)
- Java (`java`)
- C# (`cs`)

LLVM (`llvm`) exists but remains experimental in `1.x`.

The stable `1.x` surface includes:

- lexer, parser, elaborator, HM inference, diagnostics, and forward codegen
- CLI workflows: `build`, `check`, `run`, `new`, `install`, `mcp`
- project mode via `lll.toml`
- module system with deterministic topo loading
- structured error-code contract

### 2.2 Lexical structure

**Indentation**

- Significant indentation.
- Files must use spaces, not tabs.
- Indentation must be consistent per file.
- The lexer emits synthetic `INDENT` and `DEDENT` tokens.

**Comments**

- `--` starts a line comment and runs to end of line.

**Keywords**

The current lexer reserves:

`match  if  else  import  export  module  trait  impl  external  opaque  tag  unit  true  false  let`

Notes:

- `let` is valid in local and top-level bindings.
- Bare declaration forms remain idiomatic even where older docs used `fn` or `type`.

**Identifiers**

- values/functions: `[a-z_][a-zA-Z0-9_]*`
- types/constructors/modules: `[A-Z][a-zA-Z0-9_]*`
- some `external` names may use mixed-case host symbols, such as `JSON_parse`

**Literals**

- `42` — `Int`
- `3.14` — `Float`
- `"hello"` — `Str`
- `true`, `false` — `Bool`
- `'a'` — `Char`

### 2.3 Types

**Primitive types**

`Int  Float  Str  Bool  Char  Unit`

**Composite types**

```lll
[1 2 3]            -- List[Int]
[1; 2; 3]          -- List[Int]
1, "hello"         -- (Int, Str)
Int -> Int -> Bool -- right-associative function type
List[A]            -- parametric type application
RBMap[K][V]        -- nested type application
```

List rules:

- `[a b c]` is the compact list form
- `[a; b; c]` is the explicit-separator form
- commas inside brackets do not form lists

**User-defined types**

```lll
Maybe A = Some A | None
Result A E = Ok A | Err E

Shape =
  | Circle Float
  | Rect Float Float
  | Empty

Point = x Float, y Float
```

### 2.4 Tags and units

Stable `1.x` supports:

- semantic tags via `tag`
- numeric unit algebra via `unit`

```lll
tag UserId
unit m
unit s

uid = "user-42"[UserId]
speed(d Float[m])(t Float[s]) = d / t
```

Contract:

- tagged values are distinct types
- tag mismatch is a compile-time error (`E005`)
- incompatible units are a compile-time error (`E004`)
- numeric `*` and `/` compose units algebraically

### 2.5 Declarations

Canonical `1.x` declaration forms:

```lll
module My.App

import Std.Map
import "github.com/user/json/Parser"   -- remote import (M2)

export { main, helper }

Maybe A = Some A | None

add(a Int)(b Int) = a + b

main() =
  _ = printfn "hello"
  0

external console_log(msg Str) Unit
opaque Any
```

Stable declaration categories:

- module header
- imports (local: `import Std.Map`; remote: `import "github.com/user/repo/Module"`)
- explicit export list
- top-level value bindings
- top-level function bindings
- ADTs and record-like product types
- `external`
- `opaque`
- `tag`
- `unit`

Legacy compatibility:

- per-declaration `export decl` is still accepted
- no-arg call sites may use either `main` or `main()`

### 2.6 Expressions

Stable `1.x` expression forms:

- literals
- variables
- constructors
- application by juxtaposition
- `f()` zero-arg call suffix
- arithmetic and comparison operators
- `!expr`
- `if`
- `match`
- lambdas
- local layout bindings
- tuples
- lists
- cons `::`
- tagged values `value[Tag]`
- fixed symbolic operators `|>`, `>>=`, `>>`, `<|>`

Examples:

```lll
double(x Int) = x * 2

process(xs List[Int]) =
  ys = xs |> listMap (\x. x * 2)
  listLen ys

area(s Shape) =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty -> 0.0

main() =
  _ = printfn "step 1"
  _ = printfn "step 2"
  0
```

Operator contract (versioned):

- **Operator Contract Version:** `1.0.0`
- **Status:** stable for `1.x` self-host parser surface
- **Change policy:** any new symbolic operator or precedence change requires a
  spec version bump.

| Operator | Role | Associativity | Precedence (low -> high) |
|---|---|---|---|
| `<|>` | choice | left | 1 |
| `>>=` | bind | left | 2 |
| `>>` | sequence (discard left) | left | 2 |
| `|>` / `->` | pipe (`->` is legacy alias in expression context) | left | 3 |
| `==` `!=` `<` `>` `<=` `>=` | comparisons | left | 4 |
| `::` | list cons | right | 5 |
| `+` `-` | additive arithmetic | left | 6 |
| `*` `/` | multiplicative arithmetic | left | 7 |
| application by juxtaposition | function application | left | 8 |

These operators are fixed language forms, not a user-defined operator framework.
The canonical default fixity set lives in `Std.Operators` and is loaded by
default prelude wiring in self-host checks/builds. Parser precedence in `1.x`
must stay aligned with that canonical table.

### 2.7 Patterns

Stable `1.x` pattern forms:

- wildcard `_`
- variable
- literal
- constructor pattern
- nested constructor pattern
- cons pattern `h :: t`
- empty list `[]`
- tuple pattern

All matches must be exhaustive. Missing coverage is `E003`.

### 2.8 Type checking and inference

Stable `1.x` uses Hindley-Milner-style inference.

Practical contract:

- function parameters are annotated in canonical examples and APIs
- return types are usually inferable and may be omitted
- local bindings are inferred
- diagnostics carry compact machine-readable error codes

The stable `1.x` error-code set covered by the release contract is:

- `E001` `TypeMismatch`
- `E002` `UnboundVar`
- `E003` `NonExhaustiveMatch`
- `E004` `UnitMismatch`
- `E005` `TagViolation`
- `E007` `PlatformMismatch`
- `E008` `InfiniteType`
- `E020` `ModulePathMismatch`
- `E024` `ModuleCycle`
- `E025` `NoProjectForImport`
- `E026` `UnknownExternalMapping`
- `E027` `InvalidFixityAssoc`
- `E028` `InvalidFixityPrecedence`
- `E029` `DuplicateFixity`
- `E030` `ReservedOperatorFixity`

### 2.9 Module and project system

Stable `1.x` project shape:

- `lll.toml` manifest at project root
- `src/` source tree
- module-path to file-path validation
- project load/build via topo-sorted module graph

Canonical manifest example:

```toml
[project]
name = "myapp"
version = "1.0.0"
entry = "src/Main.lll"
```

Stable CLI/project workflows:

- `lllc new`
- `lllc check [dir]`
- `lllc self check <file.lll>`
- `lllc build`
- `lllc run`
- `lllc install`
- `lllc mcp`

Dependency-management expansion beyond this baseline is planned for `v2`.

### 2.10 Backends

Stable `1.x` backends:

- `fs`
- `ts`
- `py`
- `java`
- `cs`

Experimental in `1.x`:

- `llvm`
- `reverse`

### 2.11 Stdlib and tooling

Stable `1.x` includes:

- prelude functions always in scope
- self-hosted stdlib modules under `stdlib/src`
- embedded MCP server

Canonical reference docs:

- [stdlib-reference.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/stdlib-reference.md)
- [09-mcp.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/user-guide/09-mcp.md)
- [09-llm-prompting.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/user-guide/09-llm-prompting.md)

## 3. Planned `v2`

`v2` is the first release where the canonical compiler/toolchain path is fully
owned by ll-lang.

High-level commitments:

- pure ll-lang core for canonical compiler, stdlib, project resolver, and CLI logic
- F# retained only as isolated stage0 bootstrap
- small strict language core preserved
- explicit `Lazy`
- stronger project/dependency model
- stdlib designed specifically for compiler authoring and LLM use
- LLM leverage concentrated in MCP, docs, diagnostics, and benchmarks

`v2` does **not** require:

- full HKT/typeclass inference as a release blocker
- S-expression primary syntax
- language-level prompt directives

`v2` details are defined by the companion specs:

- [v2-type-system](/Users/roman/Documents/dev/tens/code/ll-lang/spec/v2-type-system.md)
- [v2-project-system](/Users/roman/Documents/dev/tens/code/ll-lang/spec/v2-project-system.md)
- [v2-self-hosting](/Users/roman/Documents/dev/tens/code/ll-lang/spec/v2-self-hosting.md)
- [v2-llm-tooling](/Users/roman/Documents/dev/tens/code/ll-lang/spec/v2-llm-tooling.md)

## 4. Deferred beyond `v2`

Explicitly deferred research tracks:

- full HKT/typeclass engine with constrained inference
- effect rows / capability system
- S-expression IR or macro notation as a first-class language layer
- reverse parsing as a release-critical path
- optimizer-heavy IR work beyond self-hosting and backend sanity

These may be revisited in a later roadmap, but they are not part of the `v2`
baseline.

## 5. Documentation map

Use these documents together:

- current stable release guarantees:
  [release-contract-1.0.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/release-contract-1.0.md)
- compiler contributor entrypoint:
  [compiler-dev/README.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/compiler-dev/README.md)
- `v2` architecture north star:
  [12-v2-language-architecture.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/compiler-dev/12-v2-language-architecture.md)
- tracked `v2` execution plan:
  [13-v2-implementation-roadmap.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/compiler-dev/13-v2-implementation-roadmap.md)
