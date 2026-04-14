# v2 Language Architecture

**Status:** planned  
**Audience:** compiler contributors and implementation agents  
**Purpose:** define the target shape of ll-lang after `1.x`, so the roadmap and spec work against one stable design.

## Summary

`v2` is the release where ll-lang stops being "a language with a self-hosted slice" and becomes "a language whose canonical compiler/toolchain is itself written in ll-lang".

The primary design rule is unchanged: **minimize tokens without making semantics ambiguous**. `v2` does not chase maximal type-theory power. It chases the smallest language that can comfortably implement and extend its own compiler.

## Product definition

`v2` is complete when all of the following are true:

1. The canonical compiler source, stdlib source, project resolver, and CLI logic live in ll-lang.
2. F# remains only as a frozen bootstrap artifact under a clearly isolated `bootstrap/` path.
3. New language/compiler features land in the self-hosted implementation first.
4. The language core stays small, strict, and deterministic; LLM-specific leverage lives mainly in stdlib, MCP, docs, and tooling.

## Architectural decisions

### 1. Core language stays small

The `v2` core includes:

- modules, imports, exports, manifest-driven projects
- ADTs, records, tuples, lists, tags, units, phantom-state encodings
- Hindley-Milner inference with explicit annotation boundaries where needed
- pattern matching with exhaustiveness
- strict evaluation by default
- explicit laziness via stdlib-backed `Lazy`
- a fixed compact operator layer for pipeline/bind/choice/sequence

The `v2` core explicitly excludes:

- user-defined operators
- macros in the core language
- exceptions, implicit effects, implicit coercions
- general HKT/typeclass machinery as a release blocker
- dual primary syntaxes

### 2. Type system strategy: HM first, specialized abstractions second

`v2` keeps `HM + rank-1` as the baseline. The implementation may add small bidirectional islands only when they reduce ambiguity or dramatically improve diagnostics.

The language is **not** required to support full HKT/typeclass inference in `v2`.

Instead, the self-hosting ergonomics target is:

- compact `State`
- compact `Result`
- compact `Parsec`
- compact `Lazy`
- predictable constructor/function passing
- predictable trailing-lambda and zero-arg call semantics

Monadic style is therefore a library and operator-layer concern, not proof that ll-lang must implement a general higher-kinded abstraction engine in `v2`.

### 3. Canonical self-hosting boundary

The implementation is split into three layers:

1. **Stage0 bootstrap**
   Frozen F# compiler used only to build or recover the self-hosted compiler.
2. **Canonical self-hosted compiler**
   The implementation under active development. This is the source of truth.
3. **Runtime/platform shims**
   Minimal per-backend helpers needed to run emitted programs and toolchain code.

The stage0 compiler is not removed from the repo in `v2`, but it is demoted from active implementation to bootstrap support.

### 4. Stdlib is part of the architecture, not an accessory

`v2` is designed around three library tiers:

1. **Prelude**
   Always in scope. Only the shortest, most universal building blocks belong here.
2. **Self-hosting foundation stdlib**
   `Std.List`, `Std.Maybe`, `Std.Result`, `Std.Map`, `Std.Str`, `Std.State`, `Std.Parsec`, `Std.Lazy`, `Std.Json`, `Std.Toml`, `Std.Test`.
3. **Platform/backend helpers**
   Target-specific helpers and FFI shims that should not pollute the core language model.

The standard library API must be designed for compiler-heavy code, not for textbook completeness.

### 5. LLM-first value lives outside the grammar where possible

The language should remain compact and unsurprising. LLM leverage should come from:

- compiler error shape
- MCP tools
- prompt packs and best-practice docs
- predictable stdlib naming
- token-efficiency benchmarks
- codegen conventions that are easy to reverse-engineer and diff

`v2` should not add prompt directives or agent instructions as core syntax unless a later, explicit design proves that the value is worth the surface-area cost.

### 6. S-expressions are optional IR notation, not a second main syntax

If an S-expression layer is explored, it must be constrained to one of these roles:

- macro IR
- structural fixture format
- AST serialization/debug view
- test input for transformation passes

It must not become a second primary authoring syntax in `v2`.

## Compiler architecture target

The canonical compiler pipeline for `v2` is:

1. Source text
2. Lexer
3. Parser
4. Surface AST validation/desugaring
5. Elaborator / name resolution / declared-type checks
6. Typed core inference and validation
7. Backend-neutral lowering
8. Backend emission
9. Project resolver / build driver / CLI orchestration

Required invariants:

- every major pass has an explicit input/output contract
- at least one typed IR remains stable across backend work
- backend emitters do not re-derive front-end semantics ad hoc
- each pass can be tested independently on corpus fixtures

## What must be written into the next spec

The next spec pass must define:

1. canonical declaration and export surface
2. operator table and precedence
3. zero-arg function/value/call rules
4. explicit laziness semantics
5. project and dependency model
6. self-hosting contract: stage0 vs canonical compiler
7. what is intentionally deferred beyond `v2`

`docs/language-spec.md` must become the single canonical behavior document. The older `docs/design-spec.md` should be treated as motivation / vision unless rewritten to match the actual roadmap.

The expected companion spec set is:

- `spec/v2-type-system.md`
- `spec/v2-project-system.md`
- `spec/v2-self-hosting.md`
- `spec/v2-llm-tooling.md`

## What is intentionally deferred past v2

These are valid research directions, but not required for `v2`:

- full HKT/typeclass solver with constrained inference
- effect rows / algebraic effects
- macro system in the core language
- S-expression primary syntax
- reverse parsing as part of the release-critical architecture
- optimizer-heavy IR work beyond what self-hosting and backend sanity require

## Documentation policy for v2 work

Any compiler-facing feature PR in the `v2` track must update all relevant docs in the same change:

- `docs/language-spec.md` when syntax or semantics change
- `docs/stdlib-reference.md` when canonical APIs change
- `docs/compiler-dev/*` when pipeline boundaries or contributor workflows change
- benchmark docs when token or runtime claims change

No `v2` implementation work is considered complete if the design docs still describe the old behavior.
