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

- [ ] every phase has a real canonical ll-lang owner
- [ ] typed-core ownership is no longer implicitly scattered across stage0 types and typed AST files
- [ ] lowering is explicit as a phase rather than hidden in backend emitters
- [ ] project/CLI phases are treated as compiler architecture, not tooling afterthoughts
- [ ] pass fixtures and invariants are used as active engineering boundaries

## Work package A — Freeze subsystem ownership

### Goal

Turn the ownership matrix from planning aid into a binding engineering rule.

### Tasks

- [ ] Freeze the canonical owner module group for each compiler subsystem.
- [ ] Mark stage0-only subsystems as explicitly transitional.
- [ ] Stop describing any subsystem as “canonical by implication”.
- [ ] Ensure docs consistently point to the same owner module groups.

### Exit criteria

- A contributor can identify the canonical owner for every compiler subsystem without reading source history.
- No subsystem is jointly owned by stage0 and self-hosted code as active peers.

## Work package B — Make typed-core ownership explicit

### Goal

Separate “types”, “typed IR”, and “inference algorithm” into explicit
responsibility boundaries.

### Tasks

- [ ] Freeze `Compiler.Types` as owner of type representations and substitutions.
- [ ] Freeze `Compiler.Typed` as owner of typed IR shapes.
- [ ] Freeze `Compiler.Infer` as owner of inference and typed-core construction.
- [ ] Eliminate documentation that treats these as one blended area.

### Exit criteria

- Type-level utilities, typed IR shapes, and inference logic are conceptually separable.
- Future self-hosted migration does not require re-discovering where typed-core responsibilities live.

## Work package C — Make lowering explicit

### Goal

Prevent backend emitters from re-deriving language semantics.

### Tasks

- [ ] Freeze `Compiler.Lower` as a real architectural phase.
- [ ] Document which transformations belong in lowering versus backend rendering.
- [ ] Define what the lowered IR must already have made explicit before codegen begins.

### Exit criteria

- Backend modules are renderers over lowered IR, not shadow elaborators.
- Match compilation and other canonicalizations have one owner.

## Work package D — Project and CLI as first-class compiler phases

### Goal

Treat manifest loading, project loading, and CLI orchestration as part of the
compiler architecture rather than shell glue.

### Tasks

- [ ] Freeze `Compiler.Project.Manifest`, `Compiler.Project.Loader`, and `Compiler.Cli` as phase owners.
- [ ] Define their boundaries relative to language phases.
- [ ] Ensure docs stop treating CLI behavior as outside compiler architecture.

### Exit criteria

- Compiler invocation path is architecturally explicit from manifest to artifact.
- Self-hosting discussions no longer stop at backend codegen.

## Work package E — Pass fixtures and invariant enforcement

### Goal

Give each major phase a testable contract, not just a prose description.

### Tasks

- [ ] Define which fixture shape or corpus artifact validates each major pass.
- [ ] Define what invariants are checked at each pass boundary.
- [ ] Ensure docs can point implementers to one validation story per pass.

### Exit criteria

- Each major phase has a plausible smoke fixture or boundary test shape.
- “Pass contract” is enforceable, not just descriptive.

## Work package F — Duplicate definition cleanup strategy

### Goal

Avoid letting stage0 and self-hosted trees drift through duplicated core
definitions.

### Tasks

- [ ] Identify which shared definitions should become ll-lang-owned first.
- [ ] Document which duplicates are acceptable only as temporary mirrors.
- [ ] Attach explicit migration notes to any still-duplicated core shapes.

### Exit criteria

- Duplication is intentional and bounded, not accidental.
- The roadmap for moving ownership into ll-lang is visible.

## Recommended implementation order

1. Work package A — subsystem ownership
2. Work package B — typed-core ownership
3. Work package C — lowering
4. Work package D — project/CLI phases
5. Work package E — fixtures and invariants
6. Work package F — duplicate definition cleanup strategy

## Definition of done for Milestone 1

`Milestone 1` is done only when all of the following are true:

- every major compiler subsystem has one canonical ll-lang owner
- `Compiler.*` versus `Std.*` boundary is stable and documented
- typed-core, lowering, project, and CLI phases are architecturally explicit
- pass contracts are tied to actual validation artifacts
- remaining stage0 duplication is explicitly transitional

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
