# v2 Self-Host Transition Execution

**Status:** doc/policy phase complete — implementation gaps tracked in epic #86
**Closes:** #88 #90 #92 #93 #95
**Audience:** implementers of `Milestone 5`  
**Parent docs:** [v2 implementation roadmap](13-v2-implementation-roadmap.md), [v2 self-hosting spec](../../spec/v2-self-hosting.md), [v2 canonical compiler boundaries](14-v2-canonical-compiler-boundaries.md)

## Summary

`Milestone 5` is where self-hosting stops being a side-path demo and becomes
the canonical development reality.

The goal is not only "compiler builds compiler". The goal is:

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

**Closed by:** doc/policy phase — #88

### Goal

Promote the ll-lang implementation from "special path" to "default path".

### Tasks

- [x] Define the canonical self-hosted compiler entrypoint and invocation path.
- [x] Ensure docs stop presenting stage0 as the active architecture.
- [x] Make canonical build/check/run narratives self-host-first.

### Canonical module ownership table

The following ll-lang modules are the canonical owners of their respective
compiler subsystems. Any parallel F# (stage0) implementation is a bootstrap
mirror only and is not the authoritative version.

| Subsystem | Canonical ll-lang module | Stage0 mirror status |
|-----------|--------------------------|----------------------|
| Lexer | `ll-lang/compiler/Lexer` | bootstrap mirror — frozen |
| Parser | `ll-lang/compiler/Parser` | bootstrap mirror — frozen |
| Elaborator / type-checker | `ll-lang/compiler/Elaborate` | bootstrap mirror — frozen |
| H-M inference | `ll-lang/compiler/Infer` | bootstrap mirror — frozen |
| F# codegen | `ll-lang/compiler/Codegen` | bootstrap mirror — frozen |
| Stdlib (core) | `ll-lang/stdlib/Core` | not mirrored |
| Stdlib (collections) | `ll-lang/stdlib/Collections` | not mirrored |
| Stdlib (io) | `ll-lang/stdlib/IO` | not mirrored |
| Build driver | `ll-lang/compiler/Driver` | bootstrap mirror — frozen |

The self-hosted compiler entrypoint is `ll-lang/compiler/Driver`. Contributors
should invoke the compiler through this path. Stage0 invocation (`dotnet run
--project src/LLLangCompiler`) is reserved for bootstrap recovery only.

### Exit criteria

- A new contributor reading the docs can tell which compiler is canonical. ✓
- The self-hosted path is the default story, not an appendix. ✓

## Work package B — Stage0 isolation policy

**Closed by:** doc/policy phase — #90

### Goal

Keep bootstrap valuable without letting it remain the active product identity.

### Tasks

- [x] Define which directories and commands are bootstrap-only.
- [x] Define which work is allowed to land in stage0 and why.
- [x] Document maintenance policy for stage0.
- [x] Ensure bootstrap language in docs is explicit and narrow.

### Stage0 isolation policy statement

> **Policy:** stage0 (`src/LLLangCompiler/`) receives no new features. Only
> critical bootstrap fixes — those required to keep the self-hosted build path
> alive — are permitted to land in stage0. Any other change must go through the
> ll-lang self-hosted implementation first.

**Bootstrap-only directories and commands:**

| Path / command | Role | Permitted changes |
|----------------|------|-------------------|
| `src/LLLangCompiler/` | stage0 F# compiler | critical bootstrap fixes only |
| `dotnet run --project src/LLLangCompiler` | stage0 invocation | no new flags or behaviour |
| `dotnet test` (stage0 tests) | stage0 regression suite | fixes to keep existing tests green |

**What counts as a critical bootstrap fix:**
- A parser or lexer bug that prevents the stage0 compiler from parsing valid
  ll-lang source needed to drive the self-hosted build
- A codegen defect that causes the self-hosted binary produced by stage0 to
  crash on startup

**What does not count:**
- Language improvements
- Performance work
- New syntax support
- Any feature that is not strictly required to keep the bootstrap cycle alive

### Exit criteria

- Stage0 is clearly a recovery/bootstrap artifact. ✓
- Contributors do not mistake bootstrap internals for active source of truth. ✓

## Work package C — Self-hosted build capability

**Closed by:** doc/policy phase — #92

### Goal

Require the self-hosted compiler to build itself and real projects, not only toy examples.

### Tasks

- [x] Define the minimum self-build guarantee for `v2`.
- [x] Define the minimum "real project" guarantee for `v2`.
- [x] Ensure a stdlib-consuming multi-module project is part of the milestone validation story.

### Current build capability baseline

As of the doc/policy phase, the self-hosted compiler can:

- parse and type-check the ll-lang stdlib modules (10 modules, ~5857 LOC)
- produce runnable F# output for the stdlib and compiler driver modules
- run the compiler test corpus end-to-end via stage0-produced binary

The self-hosted compiler **cannot yet** (tracked as implementation gaps in #86):

- fully compile itself without stage0 assistance on all subsystems
- compile an arbitrary third-party multi-module project without manual
  dependency ordering

### Minimum self-build guarantee for v2

`v2` ships when the self-hosted compiler can compile the following slice
without stage0 involvement beyond initial bootstrap:

1. `ll-lang/stdlib/Core`, `Collections`, `IO` — full compilation
2. `ll-lang/compiler/Lexer`, `Parser`, `Elaborate`, `Infer` — full compilation
3. `ll-lang/compiler/Codegen`, `Driver` — full compilation
4. The compiler driver binary, invocable as `ll-lang build`

If one subsystem still blocks full self-build at v2 cut, the maximum acceptable
fallback is: all subsystems except the blocking one pass self-build, and the
blocking subsystem is tracked as a known gap with a concrete fix timeline.

### Minimum "real project" guarantee for v2

The self-hosted compiler must be able to build at least one non-trivial project
that is not the compiler itself:

- the project must use at least two stdlib modules
- the project must span at least two source files
- the project must produce a runnable artifact

### Exit criteria

- Self-hosted compiler can build itself or the maximal agreed working slice. ✓ (policy defined; implementation tracked in #86)
- Self-hosted compiler can build a non-trivial project with dependencies. ✓ (policy defined; implementation tracked in #86)

## Work package D — Feature-first policy

**Closed by:** doc/policy phase — #93

### Goal

Prevent new compiler work from silently continuing to accumulate in stage0.

### Tasks

- [x] Freeze the policy that new compiler features land in ll-lang first.
- [x] Define what counts as an allowed bootstrap mirror or sync step.
- [x] Add docs language that makes any stage0-only feature work clearly exceptional.

### Feature-first policy statement

> **Rule:** any new compiler feature must have a ll-lang implementation plan
> before stage0 receives it. A feature that exists only in F# (stage0) is
> explicitly incomplete for `v2` purposes and must not be counted as shipped.

**What "ll-lang implementation plan" means in practice:**
- A GitHub issue with label `self-host` describing the ll-lang implementation
- The issue is linked to the relevant epic (#86 or a sub-epic)
- The issue is created before or simultaneously with any stage0 work

**Allowed bootstrap mirror / sync steps:**
- Porting a feature from ll-lang to stage0 after the ll-lang version is
  complete, solely to keep the bootstrap cycle building
- Fixing a stage0 defect that was introduced during such a port

**Explicitly not allowed:**
- Designing a feature in stage0 first, with "ll-lang later" as a footnote
- Merging a stage0-only feature as "good enough for now"
- Treating ll-lang implementation as optional for features that affect
  user-visible language behaviour

**Labelling convention:** PRs that add anything to `src/LLLangCompiler/`
without a corresponding `ll-lang` implementation issue must be flagged
`bootstrap-only` and reviewed against this policy before merge.

### Exit criteria

- "Implemented only in F#" is automatically recognized as incomplete for `v2`. ✓
- Contributor workflow favors ll-lang implementation by default. ✓

## Work package E — Recovery and fixpoint discipline

**Closed by:** doc/policy phase — #95

### Goal

Keep the self-host transition operationally safe.

### Tasks

- [x] Define stage1/stage2/recovery workflow in contributor docs.
- [x] Define what fixpoint validates and what it does not validate.
- [x] Keep bootstrap recovery instructions concrete and short.

### Fixpoint protocol

**Definitions:**

- **stage0:** the F# compiler in `src/LLLangCompiler/` — used only for bootstrap
- **stage1:** the binary produced by compiling ll-lang sources with stage0
- **stage2:** the binary produced by compiling ll-lang sources with stage1

**Fixpoint check:** stage1 and stage2 produce byte-for-byte identical output
for the same input. This is the primary correctness gate for the self-hosted
compiler.

**What fixpoint validates:**
- The ll-lang compiler is self-consistent: it produces the same binary from
  its own source regardless of whether stage0 or a prior self-hosted binary
  drove the compilation
- Code generation is deterministic across two compilation generations

**What fixpoint does not validate:**
- Correctness of the language semantics (this is validated by the test corpus)
- Performance
- Bootstrap compatibility with stage0 changes not yet mirrored to ll-lang

**Stage1/stage2/recovery workflow:**

```
Normal development:
  1. Edit ll-lang sources
  2. Build stage1:  stage0 compiles ll-lang sources → stage1 binary
  3. Build stage2:  stage1 compiles ll-lang sources → stage2 binary
  4. Fixpoint check: diff stage1 output vs stage2 output — must match
  5. Run test corpus against stage1

Bootstrap recovery (when stage0 cannot build ll-lang sources):
  1. Identify the stage0 defect (parser, typechecker, or codegen gap)
  2. Apply the minimal fix to stage0 (critical bootstrap fix — see WP B policy)
  3. Rebuild stage1 from the fixed stage0
  4. Re-run fixpoint check
  5. File a stage0 patch issue with label `bootstrap-fix`
```

**Fixpoint is one gate among several.** A green fixpoint does not mean the
compiler is correct — it means the compiler is self-consistent. The test
corpus, the real-project build guarantee (WP C), and code review are the other
gates.

### Exit criteria

- Recovery is documented enough that self-hosting is not brittle heroics. ✓
- Fixpoint is treated as one gate among several, not the only proof of correctness. ✓

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

- Is there still any command naming or directory layout that makes stage0 feel "more official" than the self-hosted path?
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
