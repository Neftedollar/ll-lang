# v2 Implementation Roadmap

**Status:** active planning document  
**Audience:** implementation agents and maintainers  
**Tracking rule:** each checkbox is either done or not done. Partial progress belongs in commit messages or issue notes, not in the checkbox state.

## Summary

This is the execution plan for taking ll-lang from the current `1.x` state to `v2`: a pure ll-lang core where the canonical compiler and toolchain are self-hosted, the language remains token-efficient, and the stdlib/tooling are explicitly optimized for compiler authoring and LLM use.

## Tracking rules

- Mark a task done only when its exit criteria and tests are satisfied.
- If a task is blocked by a language limitation, leave the task unchecked and add a `TODO(v2:<area>)` in code/docs with the missing invariant, next step, and absent test.
- No feature is done until docs and tests are updated.

## Milestone 0 — Spec freeze for v2

- [x] Rewrite `docs/language-spec.md` into a versioned source-of-truth document for `1.x`, `v2`, and deferred features.
- [x] Demote or rewrite `docs/design-spec.md` so it no longer competes with the canonical spec.
- [x] Add `spec/v2-type-system.md`.
- [x] Add `spec/v2-project-system.md`.
- [x] Add `spec/v2-self-hosting.md`.
- [x] Add `spec/v2-llm-tooling.md`.

**Exit criteria**

- `docs/language-spec.md` is the single canonical semantics document.
- Each major `v2` subsystem has one definitive spec document.
- There is no ambiguous “current vs future” contradiction across docs.

**Validation**

- doc contract check passes
- spec index links resolve

## Milestone 1 — Canonical compiler boundaries

Detailed execution breakdown lives in
[v2 compiler boundaries execution](18-v2-compiler-boundaries-execution.md).

- [x] Split the canonical compiler architecture into explicit front-end, typed-core, lowering, backend, and toolchain layers in docs and code layout.
- [x] Eliminate duplicated core type definitions between stage0 and self-hosted modules where a shared ll-lang definition can own the contract.
- [x] Define and document pass invariants and serialized fixture shapes for each major layer.

**Exit criteria**

- A contributor can identify one owner module for each compiler phase.
- The self-hosted compiler no longer depends on hidden backend-specific assumptions in front-end code.
- Architecture overview matches the real code layout.

**Validation**

- compiler-dev docs updated
- pass-level smoke fixtures exist for each major phase

## Milestone 2 — Project and dependency system

Detailed execution breakdown lives in
[v2 project system execution](16-v2-project-system-execution.md).

- [x] Make `lll.toml` the only canonical manifest path.
- [x] Define and implement lock/checksum semantics (`ll.sum` or equivalent canonical lock file).
- [x] Support deterministic path and git dependencies with a single resolver behavior.
- [x] Materialize a canonical `vendor/` layout.
- [x] Ship `lllc mod add`, `lllc mod tidy`, `lllc mod why`, and deterministic `install` semantics.
- [x] Remove legacy parallel dependency flows from the supported path.

**Exit criteria**

- Repeated resolution on the same graph is byte-stable.
- Broken dependency diagnostics are specific and actionable.
- Multi-module projects with transitive deps build through one canonical path.

**Validation**

- path dep tests
- git dep tests
- transitive graph tests
- lockfile determinism tests
- idempotent repeated-run tests

## Milestone 3 — Stdlib foundation for self-hosting

Detailed execution breakdown lives in
[v2 stdlib foundation execution](17-v2-stdlib-foundation-execution.md).

- [x] Stabilize compact canonical APIs for `Std.List`, `Std.Maybe`, `Std.Result`, `Std.Map`, and `Std.Str`.
- [x] Finish `Std.State` as the canonical state-threading foundation.
- [x] Finish `Std.Parsec` as the canonical parser-combinator foundation.
- [x] Keep `Std.Json` and `Std.Toml` as proof-of-use consumers of the parsing stack.
- [x] Finish `Std.Lazy` with explicit delay/force/memoization semantics.
- [x] Separate minimal prelude from self-hosting stdlib from backend helpers.

**Exit criteria**

- A non-trivial compiler module can be written against the stdlib without bootstrap-only hacks.
- There is no requirement to add ad hoc helper functions in compiler code for common parser/state flows.
- `stdlib-reference` describes the actual canonical APIs.

**Validation**

- `Std.State` tests
- `Std.Parsec` tests
- `Std.Json` and `Std.Toml` real parsing tests
- `Std.Lazy` execution and memoization tests
- corpus examples using the new APIs

## Milestone 4 — Syntax ergonomics for compiler-heavy code

Detailed execution breakdown lives in
[v2 syntax ergonomics execution](19-v2-syntax-ergonomics-execution.md).

- [x] Stabilize the fixed operator layer: `|>`, `>>=`, `>>`, `<|>`.
- [x] Define precedence and associativity once and enforce it in both parsers and all codegens.
- [x] Stabilize constructor/function passing where canonical.
- [x] Stabilize trailing lambda and zero-arg call/value rules.
- [x] Remove or de-emphasize noisy syntactic forms that do not buy semantic power.

**Exit criteria**

- Compiler-style code in ll-lang becomes shorter without becoming parser-ambiguous.
- Both parsers accept the same surface for these constructs.
- Backend output preserves operator semantics without emitting raw symbolic identifiers.

**Validation**

- parser parity tests
- precedence tests
- constructor passing tests
- trailing lambda tests
- zero-arg call/value tests

## Milestone 5 — Canonical self-host transition

Detailed execution breakdown lives in
[v2 self-host transition execution](20-v2-self-host-transition-execution.md).

- [x] Promote the self-hosted compiler from side-path to canonical implementation path.
- [ ] Ensure the self-hosted compiler can compile itself and a real dependency-bearing project.
- [x] Move F# stage0 into an isolated bootstrap role with explicit maintenance policy.
- [x] Ensure new compiler features land in ll-lang first.
- [ ] Remove active-development dependence on F# compiler internals.

**Exit criteria**

- The canonical compiler source tree is ll-lang.
- A fresh developer can build stage1/stage2 through documented steps.
- The F# source is clearly bootstrap-only and not treated as the active source of truth.

**Validation**

- fixpoint/self-host tests
- multi-module self-hosted build tests
- stdlib-consuming project build tests
- bootstrap recovery smoke test

## Milestone 6 — LLM operating system around the language

Detailed execution breakdown lives in
[v2 llm operating system execution](21-v2-llm-operating-system-execution.md).

- [x] Expand MCP tools around authoring, diagnostics, stdlib discovery, project graph inspection, and benchmark reporting.
- [x] Version prompt packs and compact repair recipes alongside the compiler.
- [x] Publish ll-lang authoring conventions for LLM agents as canonical docs, not incidental notes.
- [x] Add machine-checked examples for common LLM workflows.

**Exit criteria**

- An LLM client can discover grammar, stdlib, project structure, and error contracts without scraping prose.
- Prompt packs and MCP tools reflect the actual shipped language surface.
- `docs/user-guide/09-mcp.md` and `docs/user-guide/09-llm-prompting.md` align with the live implementation.

**Validation**

- MCP contract tests
- docs examples compile
- prompt-pack regression samples round-trip through compile/check flows

## Milestone 7 — Benchmarks and release gates

Detailed execution breakdown lives in
[v2 benchmarks and release gates execution](22-v2-benchmarks-release-gates-execution.md).

- [x] Add a token-efficiency benchmark suite versus F#, TypeScript, Python, Java, and C#.
- [x] Add compile-latency and self-hosted-vs-stage0 benchmark baselines.
- [x] Add semantic equivalence corpus checks across stable backends.
- [x] Add explicit `v2` release gates to CI and docs.

**Exit criteria**

- Token-efficiency claims are backed by versioned benchmark artifacts.
- Performance regressions are observable and diffable.
- `v2` has a concrete release checklist instead of an aspirational milestone.

**Validation**

- benchmark runner in CI or reproducible local script
- golden benchmark artifacts checked in or published deterministically
- release gate doc references current CI jobs

## Deferred after v2

- [ ] Full HKT/typeclass engine with constrained inference
- [ ] Effect rows / capability system
- [ ] S-expression IR or macro notation
- [ ] Reverse parsing as a release-critical architecture path
- [ ] Optimizer-heavy IR work beyond self-hosting and backend sanity

These remain intentionally unchecked until they are pulled into a later roadmap with separate specs and release criteria.

## Agent implementation order

When an implementation agent picks up `v2` work, the default order is:

1. Milestone 0
2. Milestone 3
3. Milestone 4
4. Milestone 2
5. Milestone 5
6. Milestone 6
7. Milestone 7

Rationale: spec first, then the stdlib and syntax foundations the self-hosted compiler needs, then project/toolchain hardening, then promotion of the self-hosted path, then LLM tooling and benchmarking.
