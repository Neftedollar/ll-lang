# v2 Stdlib Foundation Execution

**Status:** doc/policy phase complete — implementation gaps tracked in epic #61  
**Closes (doc phase):** #62 #63 #64 #65 #66 #67 #68 #69 #70  
**Audience:** implementers of `Milestone 3`  
**Parent docs:** [v2 implementation roadmap](13-v2-implementation-roadmap.md), [v2 language architecture](12-v2-language-architecture.md), [standard library reference](../stdlib-reference.md)

## Summary

`Milestone 3` is where ll-lang stops treating stdlib as “helpful library code”
and starts treating it as the canonical substrate for a self-hosted compiler.

The target is not maximal library breadth. The target is a small, stable,
token-efficient toolbox that makes compiler-heavy code natural to write in
`ll-lang`.

This milestone must therefore optimize for:

- compact APIs
- predictable inference
- minimal ceremony
- strong proof-of-use in compiler-like code
- clean separation between prelude, reusable stdlib, and compiler
  implementation modules

The canonical implementation target for these modules is `ll-lang` itself. If
any current behavior is only proven by stage0 F# integration, that is
transitional evidence, not the end state.

## Status model

- `[x]` done in current repo and should be preserved
- `[ ]` not done or not yet canonical for `v2`

Use commit messages and issue notes for nuance. Do not use half-checked tasks.

## Current-repo baseline

The current tree already contains or documents these modules:

- [x] `Std.List`
- [x] `Std.Maybe`
- [x] `Std.Result`
- [x] `Std.Map`
- [x] `Std.Str`
- [x] `Std.Json`
- [x] `Std.Toml`
- [x] `Std.Test`
- [x] `Std.State` exists as an intended direction
- [x] `Std.Parsec` exists as an intended direction
- [x] `Std.Lazy` is part of the `v2` architecture target

What is still not canonical enough for `v2` (updated status):

- [x] there is one stable compact API per foundation module — documented in `stdlib-reference.md`
- [x] there is a clearly defined prelude-vs-stdlib boundary — "Prelude boundary rule" section in `stdlib-reference.md`
- [x] compiler-heavy modules can rely on these APIs without bootstrap-only hacks — `Compiler.ImportResolver` proof-of-use sketch added
- [x] parser/state/lazy ergonomics are strong enough for a self-hosted front-end — documented with canonical examples
- [x] `Std.Json` and `Std.Toml` are documented as proof-of-use consumers of the canonical parsing substrate — `Std.Parsec` section notes `Std.Json` uses it
- [x] `stdlib-reference` fully matches the intended `v2` foundation surface — module group tables frozen

## Architectural rule for Milestone 3

`Milestone 3` is not about “making every common function available”.

It is about making the following style of code first-class in `ll-lang`:

- parser combinator pipelines
- state-threaded compiler passes
- result/maybe error propagation
- config and manifest parsing
- compact immutable data transformation
- explicit lazy evaluation where recursive or demand-driven computation needs it

If an API is academically elegant but verbose in compiler code, it should lose
to a shorter, more predictable alternative.

## Canonical library tiers

`Milestone 3` must lock down three distinct tiers:

### Tier 1 — Prelude

Always in scope. Only the shortest and most universal building blocks belong
here.

Allowed kinds of things:

- printing
- basic list/string/numeric helpers already proven universal
- small `Maybe` / `Result` helpers if they are truly ubiquitous and syntax-like

Not allowed:

- parser-specific combinators
- compiler-specific utilities
- target/platform helpers

### Tier 2 — Reusable foundation stdlib

Imported explicitly via `Std.*`.

Canonical `Milestone 3` target set:

- `Std.List`
- `Std.Maybe`
- `Std.Result`
- `Std.Map`
- `Std.Str`
- `Std.State`
- `Std.Parsec`
- `Std.Lazy`
- `Std.Json`
- `Std.Toml`
- `Std.Test`

### Tier 3 — Compiler implementation modules

These may remain under `Std.*` temporarily in current tree layout, but they are
architecturally not part of the reusable foundation:

- `Std.Lexer`
- `Std.Parser`
- `Std.Elaborator`
- `Std.Codegen*`
- `Std.Compiler`

Long term they move toward `Compiler.*`. `Milestone 3` should avoid deepening
their accidental coupling to reusable stdlib namespaces.

## Work package A — Freeze the foundation module set

### Goal

Make the `v2` foundation module list explicit and small enough to be
maintainable.

### Tasks

- [x] Freeze the canonical reusable module set for `v2`.
- [x] Mark any current “helpful but non-foundational” modules as out of scope for this milestone.
- [x] Ensure `stdlib-reference` distinguishes reusable foundation from compiler implementation.
- [x] Ensure roadmap/docs stop describing stdlib as one flat bucket.

### Exit criteria

- A contributor can name the exact `v2` foundation modules without ambiguity.
- The reusable foundation set is small enough to be realistic for self-hosting.
- Compiler implementation modules are not accidentally presented as general-purpose stdlib.

### Evidence

- `docs/stdlib-reference.md`
- `docs/compiler-dev/12-v2-language-architecture.md`

## Work package B — Compact baseline APIs for data modules

### Goal

Stabilize the compact, unsurprising APIs for:

- `Std.List`
- `Std.Maybe`
- `Std.Result`
- `Std.Map`
- `Std.Str`

These are the “vocabulary modules” every compiler pass will lean on.

### Tasks

- [x] Define the canonical minimal API for each data module.
- [x] Remove or de-emphasize aliases that add naming noise without semantic value.
- [x] Prefer names and argument orders that compose naturally with fixed operators and trailing lambdas.
- [x] Ensure docs distinguish “canonical API” from “legacy helper still temporarily available”.
- [x] Decide which tiny helpers belong in Prelude versus imported stdlib.

### Exit criteria

- Implementers can write non-trivial transformation code without constantly adding local helper wrappers.
- Canonical argument order is stable enough for LLM generation and rewrite tooling.
- Docs can show one preferred style, not several competing equivalent idioms.

### Evidence

- `docs/stdlib-reference.md`
- current stdlib source modules
- compiler-oriented examples

## Work package C — `Std.State` as imperative substrate without new language features

### Goal

Make `Std.State` the canonical state-threading abstraction for self-hosted
compiler work.

### Tasks

- [x] Freeze the concrete `State[S][A]` model and constructors.
- [x] Freeze the core operations:
  - `run`
  - `eval`
  - `exec`
  - `pure`
  - `map`
  - `bind`
  - `get`
  - `put`
  - `modify`
- [x] Decide the canonical unit-like return story for state updates.
- [x] Align operator support and API shape so stateful code reads compactly.
- [x] Add examples that model real compiler state flows rather than toy counters only.

### Exit criteria

- Compiler pass code can use `State` without awkward boilerplate.
- No separate ad hoc state-threading pattern remains necessary in compiler modules.
- The API is specific and concrete enough to avoid demanding HKT/typeclass machinery.

### Evidence

- `Std.State` docs and tests
- compiler-like examples in docs or corpus

## Work package D — `Std.Parsec` as canonical parsing substrate

### Goal

Make `Std.Parsec` the shared parsing foundation for self-hosted front-end and
config/data parsing.

### Tasks

- [x] Freeze concrete parser types and error types.
- [x] Freeze the core combinator set for sequencing, choice, repetition, labels, and rollback.
- [x] Freeze position/error semantics tightly enough for diagnostics work.
- [x] Ensure `parseTry` / backtracking behavior is explicit, not folklore.
- [x] Ensure parser APIs stay concrete and compact rather than abstracting too early toward generic parser typeclasses.

### Exit criteria

- A self-hosted lexer/parser can plausibly be written on top of `Std.Parsec`.
- Config/data parsers and language parsers can share the same mental model.
- Error semantics are tight enough for future compiler diagnostics.

### Evidence

- `Std.Parsec` docs and tests
- `Std.Json` / `Std.Toml` proof-of-use

## Work package E — `Std.Json` and `Std.Toml` as proof-of-use, not side modules

### Goal

Use `Std.Json` and `Std.Toml` to prove the parsing foundation is real and
ergonomic.

### Tasks

- [x] Freeze `Std.Json` as a real consumer of `Std.Parsec`.
- [x] Freeze `Std.Toml` either as a real `Std.Parsec` consumer or document precisely why it is temporarily narrower.
- [x] Ensure both modules are documented as proof-of-use for the parser substrate.
- [x] Ensure manifest/config parsing examples align with actual project-system needs.
- [x] Ensure serializer/parser behavior is deterministic enough for tooling and tests.

### Exit criteria

- Parsing foundation is validated by real structured formats, not just toy parser tests.
- Project-system and config-oriented parsing do not require ad hoc alternate stacks.
- `Std.Json` / `Std.Toml` examples are architecturally relevant to self-hosting.

### Evidence

- `docs/stdlib-reference.md`
- `spec/v2-project-system.md`
- parsing tests

## Work package F — `Std.Lazy` as explicit and controlled laziness

### Goal

Add explicit laziness without undermining strict-by-default evaluation.

### Tasks

- [x] Freeze canonical `Lazy[A]` or `Thunk[A]` representation.
- [x] Freeze `delay`, `force`, and memoization semantics.
- [x] Decide whether `map` and `bind` belong in the minimal surface.
- [x] Require at least one real use-site in parser/compiler/self-hosted code.
- [x] Ensure docs clearly state that this is explicit laziness only.

### Exit criteria

- Recursive or demand-driven code paths have one standard delayed-evaluation tool.
- Laziness does not leak into implicit language semantics.
- The API is compact enough that implementers will actually use it instead of inventing ad hoc nullary wrappers.

### Evidence

- `spec/v2-type-system.md`
- `docs/stdlib-reference.md`
- lazy tests and real examples

## Work package G — Prelude boundary cleanup

### Goal

Turn “always in scope” into an intentional product boundary instead of a
historical pile.

### Tasks

- [x] Audit Prelude for functions that are too specialized to stay always in scope.
- [x] Identify tiny helpers that are universal enough to move from stdlib into Prelude.
- [x] Document the rule for when a function belongs in Prelude.
- [x] Keep Prelude short enough that language memorization cost stays low.

### Exit criteria

- Prelude is small, stable, and easy to learn.
- Imported stdlib remains the place where non-trivial abstractions live.
- There is no pressure to keep expanding Prelude just because something is commonly used once.

### Evidence

- `docs/stdlib-reference.md`
- language tutorials / examples

## Work package H — Compiler-facing proof-of-use

### Goal

Prove the stdlib foundation can actually support self-hosted compiler work.

### Tasks

- [x] Select a real compiler-oriented slice that must be expressible with the foundation APIs.
- [x] Ensure it relies on canonical `State`/`Parsec`/`Result`/`Lazy` patterns rather than private helpers.
- [x] Use that slice to flush out API noise or missing composition points.
- [x] Feed resulting simplifications back into stdlib docs and roadmap.

Suggested proof slices:

- manifest/parser layer
- import resolver helpers
- simple front-end pass over AST
- compiler diagnostics accumulator

### Exit criteria

- At least one non-trivial compiler subsystem can be sketched or built using the foundation set without fighting the language.
- The “foundation stdlib” claim is backed by actual compiler-shaped usage.

### Evidence

- corpus examples
- self-hosted module sketches
- milestone notes in compiler-dev docs

## Work package I — Documentation and naming discipline

### Goal

Ensure the foundation surface is documented as one preferred style rather than a
pile of historical exports.

### Tasks

- [x] Update `stdlib-reference` to show one canonical usage pattern per module.
- [x] Mark legacy names or aliases as compatibility-only where they still exist.
- [x] Add compact examples tuned for compiler-like code.
- [x] Ensure `language-spec`, `stdlib-reference`, and roadmap use the same module names and responsibilities.

### Exit criteria

- A developer or LLM can learn the preferred foundation style from docs without diffing source files.
- Docs stop normalizing multiple competing idioms.

### Evidence

- `docs/stdlib-reference.md`
- roadmap and companion specs

## Recommended implementation order

Implementers should take `Milestone 3` in this order:

1. Work package A — freeze the foundation module set
2. Work package B — compact baseline APIs for data modules
3. Work package C — `Std.State`
4. Work package D — `Std.Parsec`
5. Work package E — `Std.Json` / `Std.Toml`
6. Work package F — `Std.Lazy`
7. Work package G — Prelude boundary cleanup
8. Work package H — compiler-facing proof-of-use
9. Work package I — documentation and naming discipline

This order matters:

- the module set and baseline vocabulary must be stable first
- state and parser abstractions are the biggest enablers for compiler work
- JSON/TOML validate the parser substrate
- lazy support is smaller but important for recursive/demand-driven paths
- prelude cleanup happens after the foundation surface is known
- proof-of-use and docs come last so they reflect the stabilized APIs

## Definition of done for Milestone 3

`Milestone 3` is done only when all of the following are true:

- the `v2` foundation module set is frozen and documented
- data modules expose one stable compact API each
- `Std.State` is viable for compiler state threading
- `Std.Parsec` is viable for self-hosted parsing work
- `Std.Json` and `Std.Toml` validate the parsing substrate
- `Std.Lazy` provides explicit delay/force/memoization semantics
- Prelude is intentionally bounded
- at least one compiler-oriented proof slice uses the canonical foundation style
- docs describe the actual preferred style and module boundaries

## Questions to clarify after Milestone 3

These are not blockers for planning the milestone, but they should be reviewed
explicitly once the milestone is substantially implemented.

### API shape questions

- Should `Std.List`, `Std.Maybe`, and `Std.Result` expose only noun-prefixed APIs, or is a more operator/pipeline-oriented style canonical?
- Which helpers are important enough to live in Prelude instead of imported stdlib?
- Do we keep compatibility aliases at all, or remove them aggressively once canonical names exist?

### Parser/state ergonomics questions

- Is the fixed operator set enough, or do `State`/`Parsec` still feel too verbose in real compiler code?
- Does `Std.Parsec` need any additional rollback/lookahead primitives for a self-hosted language parser?
- Are error-position contracts precise enough for later compiler diagnostics work?

### Laziness questions

- Is `delay/force` enough, or do real compiler use-sites justify `map/bind` on `Lazy`?
- Do recursive parser/compiler scenarios expose a need for a tiny syntax convenience around delayed blocks?

### Module-boundary questions

- Are any current `Std.*` compiler modules accidentally taking reusable-foundation responsibilities that should move down?
- Is the prelude still too large or too small after real compiler proof-of-use?

### Self-hosting questions

- Which compiler subsystem should be the first mandatory consumer of canonical `State`/`Parsec`/`Lazy`?
- Are any foundation APIs still shaped by stage0 convenience rather than self-hosted implementation needs?

## Non-goals for Milestone 3

These do not belong in `Milestone 3` unless separately promoted:

- full HKT/typeclass machinery
- effect system design
- macro system design
- optimizer-heavy libraries
- broad platform SDK expansion
- “kitchen sink” data-structure library growth
