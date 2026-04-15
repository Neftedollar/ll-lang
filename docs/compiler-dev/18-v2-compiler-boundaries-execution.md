# v2 Compiler Boundaries Execution

**Status:** active planning document  
**Audience:** implementers of `Milestone 1`  
**Parent docs:** [v2 implementation roadmap](13-v2-implementation-roadmap.md), [v2 canonical compiler boundaries](14-v2-canonical-compiler-boundaries.md), [v2 pass contracts](15-v2-pass-contracts.md), [v2 self-hosting spec](../../spec/v2-self-hosting.md)

## Summary

`Milestone 1` exists to prevent the self-hosted compiler from becoming another
accidental monolith.

The goal is not only to list phases. The goal is to ensure each compiler
subsystem has:

- one canonical owner
- one input/output contract
- one namespace home
- one test boundary

Without this milestone, later work on stdlib, syntax, and self-hosting will
silently reintroduce stage0 coupling and backend leakage.

## Status model

- `[x]` done in current repo and should be preserved
- `[ ]` not done or not yet canonical for `v2`

## Current-repo baseline

- [x] subsystem ownership map exists in docs
- [x] pass contracts exist in docs
- [x] `Compiler.*` is chosen as the canonical namespace for self-hosted compiler implementation
- [x] `Std.*` is retained for reusable foundation modules
- [x] stage0 and self-hosted slices both exist in repo

Still not done enough for `v2`:

- [x] every phase has a real canonical ll-lang owner — frozen in `14-v2-canonical-compiler-boundaries.md`
- [x] typed-core ownership is no longer implicitly scattered — `Compiler.Types` / `Compiler.Typed` / `Compiler.Infer` are distinct
- [x] lowering is explicit as a phase — `Compiler.Lower` is a required phase in the ownership table
- [x] project/CLI phases are treated as compiler architecture — frozen in ownership table and pass contracts
- [x] pass fixtures and invariants are defined — `15-v2-pass-contracts.md` § Pass fixture shapes
- [ ] ll-lang implementations of the unfilled gaps (`Compiler.Types`, `Compiler.Infer`, `Compiler.Lower`, `Compiler.Project.*`, `Compiler.Cli`) — tracked per sub-issue

## Work package A — Freeze subsystem ownership

### Goal

Turn the ownership matrix from planning aid into a binding engineering rule.

### Tasks

- [x] Freeze the canonical owner module group for each compiler subsystem.
- [x] Mark stage0-only subsystems as explicitly transitional.
- [x] Stop describing any subsystem as “canonical by implication”.
- [x] Ensure docs consistently point to the same owner module groups.

### Exit criteria

- [x] A contributor can identify the canonical owner for every compiler subsystem without reading source history.
- [x] No subsystem is jointly owned by stage0 and self-hosted code as active peers.

**Closed by:** `14-v2-canonical-compiler-boundaries.md` rewrite (this PR). Closes #46.

## Work package B — Make typed-core ownership explicit

### Goal

Separate “types”, “typed IR”, and “inference algorithm” into explicit
responsibility boundaries.

### Tasks

- [x] Freeze `Compiler.Types` as owner of type representations and substitutions.
- [x] Freeze `Compiler.Typed` as owner of typed IR shapes.
- [x] Freeze `Compiler.Infer` as owner of inference and typed-core construction.
- [x] Eliminate documentation that treats these as one blended area.

### Exit criteria

- [x] Type-level utilities, typed IR shapes, and inference logic are conceptually separable.
- [x] Future self-hosted migration does not require re-discovering where typed-core responsibilities live.

**Closed by:** typed-core section in `14-v2-canonical-compiler-boundaries.md`. Closes #47.

## Work package C — Make lowering explicit

### Goal

Prevent backend emitters from re-deriving language semantics.

### Tasks

- [x] Freeze `Compiler.Lower` as a real architectural phase.
- [x] Document which transformations belong in lowering versus backend rendering.
- [x] Define what the lowered IR must already have made explicit before codegen begins.

### Exit criteria

- [x] Backend modules are renderers over lowered IR, not shadow elaborators — documented in ownership table.
- [x] Match compilation and other canonicalizations have one owner — `Compiler.Lower`.

**Closed by:** lowering section in `14-v2-canonical-compiler-boundaries.md` + pass contract #7 in `15-v2-pass-contracts.md`. Closes #48.

## Work package D — Project and CLI as first-class compiler phases

### Goal

Treat manifest loading, project loading, and CLI orchestration as part of the
compiler architecture rather than shell glue.

### Tasks

- [x] Freeze `Compiler.Project.Manifest`, `Compiler.Project.Loader`, and `Compiler.Cli` as phase owners.
- [x] Define their boundaries relative to language phases.
- [x] Ensure docs stop treating CLI behavior as outside compiler architecture.

### Exit criteria

- [x] Compiler invocation path is architecturally explicit from manifest to artifact.
- [x] Self-hosting discussions no longer stop at backend codegen.

**Closed by:** project/CLI section in `14-v2-canonical-compiler-boundaries.md` + pass contracts #9–#11. Closes #49.

## Work package E — Pass fixtures and invariant enforcement

### Goal

Give each major phase a testable contract, not just a prose description.

### Tasks

- [x] Define which fixture shape or corpus artifact validates each major pass.
- [x] Define what invariants are checked at each pass boundary.
- [x] Ensure docs can point implementers to one validation story per pass.

### Exit criteria

- [x] Each major phase has a plausible smoke fixture or boundary test shape.
- [x] “Pass contract” is enforceable, not just descriptive.

**Closed by:** “Pass fixture shapes” section in `15-v2-pass-contracts.md`. Closes #50.

## Work package F — Duplicate definition cleanup strategy

### Goal

Avoid letting stage0 and self-hosted trees drift through duplicated core
definitions.

### Tasks

- [x] Identify which shared definitions should become ll-lang-owned first.
- [x] Document which duplicates are acceptable only as temporary mirrors.
- [x] Attach explicit migration notes to any still-duplicated core shapes.

### Exit criteria

- [x] Duplication is intentional and bounded, not accidental.
- [x] The roadmap for moving ownership into ll-lang is visible.

**Closed by:** “Duplication audit and migration notes” section in `14-v2-canonical-compiler-boundaries.md`. Closes #51.

## Recommended implementation order

1. ~~Work package A — subsystem ownership~~ ✓ closed #46
2. ~~Work package B — typed-core ownership~~ ✓ closed #47
3. ~~Work package C — lowering~~ ✓ closed #48
4. ~~Work package D — project/CLI phases~~ ✓ closed #49
5. ~~Work package E — fixtures and invariants~~ ✓ closed #50
6. ~~Work package F — duplicate definition cleanup strategy~~ ✓ closed #51

## Definition of done for Milestone 1

`Milestone 1` doc/policy phase is complete. Remaining work:

- [ ] `Compiler.Types`, `Compiler.Typed`, `Compiler.Infer` implemented in ll-lang
- [ ] `Compiler.Lower` implemented in ll-lang
- [ ] `Compiler.Project.Manifest`, `Compiler.Project.Loader`, `Compiler.Cli` implemented in ll-lang
- [x] `Compiler.Backend.CSharp` (ll-lang) — `stdlib/src/CodegenCSharp.lll` added (548 LOC, 20 tests)
- [ ] direct tests for each ll-lang-owned subsystem
- [ ] end-to-end self-hosted pipeline test independent of stage0 behavior

## Questions to clarify after Milestone 1

### Namespace questions

- Which existing `Std.*` compiler modules should move first into `Compiler.*` versus remain temporary compatibility facades?
- Do we want one flat `Compiler.*` tree first, or nested phase-oriented namespaces immediately?

### Typed-core questions

- What is the smallest useful lowered IR for `v2` before optimizer work exists?
- Which metadata must survive from elaboration/inference into lowering and backend phases?

### Migration questions

- Which subsystem should be the first mandatory ll-lang-owned phase in the canonical development loop?
- Where is duplication still buying bootstrap safety, and where is it already just drift risk?

## Non-goals for Milestone 1

- implementing all self-hosted compiler phases immediately
- final backend parity across all targets
- optimizer architecture
- project-system semantics beyond architectural ownership
