# v2 Self-Host Transition Execution

**Status:** active planning document  
**Audience:** implementers of `Milestone 5`  
**Parent docs:** [v2 implementation roadmap](13-v2-implementation-roadmap.md), [v2 self-hosting spec](../../spec/v2-self-hosting.md), [v2 canonical compiler boundaries](14-v2-canonical-compiler-boundaries.md)

## Summary

`Milestone 5` is where self-hosting stops being a side-path demo and becomes
the canonical development reality.

The goal is not only “compiler builds compiler”. The goal is:

- canonical source of truth is ll-lang
- stage0 is bootstrap only
- feature development flows through self-hosted path first
- recovery remains documented and realistic

## Status model

- `[x]` done in current repo and should be preserved
- `[ ]` not done or not yet canonical for `v2`

## Current-repo baseline

- [x] a self-hosted compiler slice exists
- [x] stage0 F# compiler exists and is operational
- [x] self-hosting is already part of product direction and docs

Still not done enough for `v2`:

- [ ] the ll-lang compiler is not yet the unambiguous canonical implementation path
- [ ] stage0 is still too central to everyday architecture and contributor understanding
- [ ] feature-first policy for ll-lang-owned implementation is not fully enforced
- [ ] self-host build path is not yet the default contributor workflow

## Work package A — Canonical path promotion

### Goal

Promote the ll-lang implementation from “special path” to “default path”.

### Tasks

- [ ] Define the canonical self-hosted compiler entrypoint and invocation path.
- [ ] Ensure docs stop presenting stage0 as the active architecture.
- [ ] Make canonical build/check/run narratives self-host-first.

### Exit criteria

- A new contributor reading the docs can tell which compiler is canonical.
- The self-hosted path is the default story, not an appendix.

## Work package B — Stage0 isolation policy

### Goal

Keep bootstrap valuable without letting it remain the active product identity.

### Tasks

- [ ] Define which directories and commands are bootstrap-only.
- [ ] Define which work is allowed to land in stage0 and why.
- [ ] Document maintenance policy for stage0.
- [ ] Ensure bootstrap language in docs is explicit and narrow.

### Exit criteria

- Stage0 is clearly a recovery/bootstrap artifact.
- Contributors do not mistake bootstrap internals for active source of truth.

## Work package C — Self-hosted build capability

### Goal

Require the self-hosted compiler to build itself and real projects, not only toy examples.

### Tasks

- [ ] Define the minimum self-build guarantee for `v2`.
- [ ] Define the minimum “real project” guarantee for `v2`.
- [ ] Ensure a stdlib-consuming multi-module project is part of the milestone validation story.

### Exit criteria

- Self-hosted compiler can build itself or the maximal agreed working slice.
- Self-hosted compiler can build a non-trivial project with dependencies.

## Work package D — Feature-first policy

### Goal

Prevent new compiler work from silently continuing to accumulate in stage0.

### Tasks

- [ ] Freeze the policy that new compiler features land in ll-lang first.
- [ ] Define what counts as an allowed bootstrap mirror or sync step.
- [ ] Add docs language that makes any stage0-only feature work clearly exceptional.

### Exit criteria

- “Implemented only in F#” is automatically recognized as incomplete for `v2`.
- Contributor workflow favors ll-lang implementation by default.

## Work package E — Recovery and fixpoint discipline

### Goal

Keep the self-host transition operationally safe.

### Tasks

- [ ] Define stage1/stage2/recovery workflow in contributor docs.
- [ ] Define what fixpoint validates and what it does not validate.
- [ ] Keep bootstrap recovery instructions concrete and short.

### Exit criteria

- Recovery is documented enough that self-hosting is not brittle heroics.
- Fixpoint is treated as one gate among several, not the only proof of correctness.

## Recommended implementation order

1. Work package A — canonical path promotion
2. Work package B — stage0 isolation
3. Work package C — self-hosted build capability
4. Work package D — feature-first policy
5. Work package E — recovery and fixpoint discipline

## Definition of done for Milestone 5

`Milestone 5` is done only when all of the following are true:

- ll-lang implementation is the documented canonical compiler
- stage0 is isolated and explicitly bootstrap-only
- self-hosted compiler can build itself and a real dependency-bearing project
- contributor workflow and feature policy are self-host-first
- recovery path remains documented and usable

## Questions to clarify after Milestone 5

### Canonical-path questions

- Is there still any command naming or directory layout that makes stage0 feel “more official” than the self-hosted path?
- Which self-hosted entrypoint should remain user-facing long term?

### Bootstrap questions

- What is the minimum bootstrap maintenance we are willing to carry after `v2`?
- Which stage0 subsystems are still mirrors versus intentionally frozen?

### Validation questions

- What is the smallest acceptable self-hosted build slice if one subsystem still blocks full self-build?
- Which smoke projects best represent real-world confidence for self-hosted builds?

## Non-goals for Milestone 5

- deleting stage0 from the repo
- cross-platform bootstrap independence
- advanced optimizer or packaging work unrelated to self-host transition
