# ll-lang v2 Type System

**Status:** planned  
**Scope:** canonical target for the `v2` line, not the current `1.x` shipped contract.

## Summary

`v2` keeps a compact Hindley-Milner core and explicitly avoids making full
HKT/typeclass inference a release requirement. The type system is designed to
make compiler authoring and self-hosting practical with predictable inference,
small syntax, and stable diagnostics.

## Core judgments

The `v2` checker remains centered around:

- surface declaration checking
- HM-style inference for expressions and local bindings
- principal-typing behavior where feasible
- explicit annotation boundaries where inference would otherwise become brittle

The canonical type forms are:

- primitives: `Int`, `Float`, `Str`, `Bool`, `Char`, `Unit`
- function types: `A -> B`
- tuples
- lists
- named ADTs / records
- parametric types
- tagged types
- unit-annotated numeric types
- explicit `Lazy[A]`

## Design decisions

### 1. HM remains the baseline

`v2` keeps the current `HM + rank-1` mental model:

- top-level and local bindings infer where possible
- explicit annotations remain allowed anywhere useful
- public APIs should still prefer explicit parameter types
- return type annotations remain optional but recommended on stable boundaries

### 2. No full HKT/typeclass requirement in v2

The language does not need a general higher-kinded solver to ship `v2`.

Instead:

- `State`, `Parsec`, `Result`, and `Lazy` are treated as canonical library
  abstractions with specialized APIs
- compact operators may be built in for common chaining forms
- generalized trait evidence / dictionary elaboration is deferred

This keeps inference predictable and reduces compiler complexity during the
self-host transition.

### 3. Bidirectional typing is optional and scoped

If `v2` introduces bidirectional checking, it must be limited to cases where it
materially improves one of:

- syntax compactness
- operator typing clarity
- constructor/function passing
- diagnostics for compiler-heavy code

It must not become an excuse to widen the language surface without a concrete
self-hosting payoff.

### 4. Tags and units stay first-class

`v2` preserves the current distinction:

- semantic tags create distinct types without runtime cost
- units participate in numeric unit algebra
- phantom-state encodings remain a preferred pattern for stateful APIs without
  runtime mutation

These are part of the language identity and directly support “compiled = works”.

### 5. Explicit laziness only

The language stays strict by default.

`Lazy[A]` is an explicit type with explicit operations:

- `delay`
- `force`
- memoization semantics

No hidden lazy evaluation or global evaluation-mode switch is introduced.

## Required v2 typing clarifications

The main language spec must lock down:

1. zero-arg function vs value equivalence rules
2. constructor-as-function passing rules
3. typing of fixed operators such as `|>`, `>>=`, `>>`, `<|>`
4. where annotations are required for ambiguous lambdas or overloaded-looking
   shapes
5. how tagged and unit-annotated values print and compare in diagnostics

## Diagnostics policy

Every non-trivial type-system feature added in `v2` must preserve the compact
diagnostic contract:

- expected vs actual
- origin stage
- real source position
- shortest actionable fix

If a design would require much noisier diagnostics to remain understandable, it
should be deferred.

## Deferred beyond v2

These are explicitly out of scope for `v2` unless separately promoted:

- full HKT inference
- generalized typeclass solving
- effect rows / algebraic effects
- GADT-style pattern refinement
- dependent typing or value-level types

## Validation targets

`v2` type-system work is not complete without:

- HM regression coverage
- operator typing tests
- constructor/function passing tests
- tagged/unit mismatch diagnostics
- self-hosted compiler modules compiling under the intended rules
