# v2 Project System Execution

**Status:** active planning document  
**Audience:** implementers of `Milestone 2`  
**Parent docs:** [v2 implementation roadmap](13-v2-implementation-roadmap.md), [v2 project system spec](../../spec/v2-project-system.md)

## Summary

This document decomposes `Milestone 2` into implementation-sized work packages.
It is intentionally operational: each item should be easy to mark yes or no,
and each package has explicit evidence, exit criteria, and validation.

This is not a greenfield design. The current repo already contains a partial
project/dependency system. `Milestone 2` is about turning that behavior into a
fully canonical, fully documented, and fully self-host-friendly contract.

The architectural target is not “better F# dependency tooling”. The target is a
project/dependency system whose canonical implementation is eventually owned by
`ll-lang` itself. Any F# work in this milestone must therefore be judged by one
question: does it clarify, preserve, or unlock the future `ll-lang`-owned
implementation?

## Status model

- `[x]` done in current repo and should be preserved
- `[ ]` not done or not yet canonical for `v2`

Use commit messages and issue notes for nuance. Do not use half-checked tasks.

## Current-repo baseline

The following capabilities already exist in code and tests:

- [x] `lll.toml` parsing with `[project]`, `[deps]`, `[platform]`
- [x] `version` and `entry` fields in manifest parsing
- [x] `GitDep(url, ref)` and `PathDep(path)` source forms
- [x] git dependency default ref of `main`
- [x] `lllc install`
- [x] `lllc mod add`
- [x] `lllc mod tidy`
- [x] `lllc mod why`
- [x] deterministic single-winner resolver
- [x] winner policy `PathDep > GitDep`
- [x] semver-over-non-semver git ranking
- [x] lexical tie-break for non-semver refs
- [x] `ll.sum` generation
- [x] `vendor/` materialization
- [x] stale `vendor/` cleanup
- [x] transitive dependency traversal
- [x] lock-pin influence from `ll.sum`
- [x] project loader integration with `vendor/<dep>/src`

The following are still not canonical enough for `v2`:

- [ ] `lll.toml` is the only supported manifest path
- [ ] manifest schema diagnostics are strict and versioned
- [ ] `ll.sum` contract is explicitly enforced as part of public product surface
- [ ] `vendor/` semantics are documented as the only supported local dependency layout
- [ ] CLI outputs are stabilized for MCP/automation consumption
- [ ] project/dependency behavior is described as one coherent contract across spec, CLI docs, and tests
- [ ] legacy parallel dependency flows are explicitly unsupported

## Work package A — Canonical manifest contract

### Goal

Turn `lll.toml` from “what current parser accepts” into a documented and
enforced source-of-truth contract.

### Tasks

- [ ] Make `lll.toml` the only canonical manifest path in docs, CLI help, and implementation.
- [ ] Demote `ll.toml` fallback to compatibility-only behavior, then remove it from the supported path.
- [ ] Require `project.name` and `project.version` as canonical fields.
- [ ] Keep `project.entry` as the canonical executable entry selector.
- [ ] Define policy for unknown keys/tables: tolerated only during migration, then diagnosable.
- [ ] Ensure scaffolded projects always emit the canonical manifest shape.

### Exit criteria

- A new contributor sees exactly one manifest name in docs and CLI examples.
- The manifest schema is stable enough for MCP and tool-assisted editing.
- Compatibility fallback, if temporarily retained, is explicitly marked non-canonical.
- The manifest contract is precise enough to be reimplemented in `ll-lang`
  without reading F# parser internals.

### Evidence

- `Manifest.fs`
- `ProjectLoader.findManifest`
- scaffold tests in `ModuleSystemTests.fs`

## Work package B — Resolver semantics as product contract

### Goal

Promote the current deterministic resolver behavior from implementation detail
to intentional product contract.

### Tasks

- [ ] Freeze the single-winner-per-dependency-name model as the `v2` baseline.
- [ ] Freeze winner ranking:
  - path beats git
  - semver git ref beats non-semver git ref
  - semver compares numerically
  - non-semver compares lexically by ref then URL
- [ ] Freeze root-restart convergence semantics when a stronger contender appears.
- [ ] Document what `ll.sum` may pin and what it may not override.
- [ ] Add a versioned upgrade note for any future resolver beyond this baseline.

### Exit criteria

- Same graph always resolves to the same winners.
- Resolver behavior is explainable without reading `Program.fs`.
- A future resolver change would require an explicit spec change, not a silent refactor.
- The winner model is documented tightly enough to be ported into the
  self-hosted compiler/tooling path.

### Evidence

- resolver code around `compareGitCandidates`, `comparePreferredWinner`, `resolveWithPreferred`
- deterministic winner tests in `ModuleSystemTests.fs`

## Work package C — Lock file contract

### Goal

Turn `ll.sum` into a real public contract rather than an incidental artifact.

### Tasks

- [ ] Freeze line format and ordering semantics.
- [ ] Define whether comments and blank lines remain tolerated.
- [ ] Specify exactly which fields participate in pinning and which only in drift detection.
- [ ] Document recovery behavior when `ll.sum` is absent, stale, or partially malformed.
- [ ] Decide whether checksum drift is fatal during `check/build` or only corrected by `install/tidy`.

### Exit criteria

- A developer can hand-inspect `ll.sum` and know what it means.
- Tooling can parse `ll.sum` deterministically.
- Tests cover both generation and consumption semantics.
- The file format is simple enough to be parsed and emitted by `ll-lang`
  without special bootstrap exceptions.

### Evidence

- `writeLlSum`
- `readLlSumSources`
- `ll.sum` tests in `ModuleSystemTests.fs`

## Work package D — Vendor layout and source ownership

### Goal

Make `vendor/` the only supported materialization model and lock down its
interaction with nested path deps and project loading.

### Tasks

- [ ] Freeze `vendor/<dep>/` as the only canonical local layout.
- [ ] Define git materialization semantics precisely enough for reproducibility.
- [ ] Define path-dependency copy semantics precisely enough for nested local graphs.
- [ ] Freeze stale directory cleanup as canonical install/tidy behavior.
- [ ] Define whether tool-private caches may exist and confirm they are non-semantic.

### Exit criteria

- Builds do not depend on hidden dependency locations outside `vendor/`.
- Nested path dependency behavior is explained and tested.
- Project loader and installer agree on vendored tree ownership.
- The vendored tree contract is backend-neutral and self-host-friendly.

### Evidence

- `materializeDepOrFail`
- `ProjectLoader.loadDepFiles`
- stale vendor cleanup tests

## Work package E — CLI lifecycle contract

### Goal

Make the dependency CLI a coherent lifecycle instead of a bag of commands.

### Tasks

- [ ] Define `install` as the canonical graph realization command.
- [ ] Define `mod add` as manifest mutation plus install.
- [ ] Define `mod tidy` as stale cleanup plus lock rewrite.
- [ ] Define `mod why` as graph explanation, not a best-effort debug helper.
- [ ] Stabilize command side effects and human-readable output shape.
- [ ] Add or reserve machine-readable output modes if MCP automation needs them.

### Exit criteria

- Every dependency lifecycle operation maps to exactly one command.
- Side effects are deterministic and documented.
- `mod why` becomes a reliable graph-observability surface for humans and LLMs.
- Command semantics are factored so they can migrate from stage0 CLI code into
  `ll-lang`-owned tooling.

### Evidence

- `Program.fs` command handlers
- command help text
- `ModuleSystemTests.fs`

## Work package F — Project loader and module graph integration

### Goal

Ensure dependency resolution, module loading, and build ordering form one
backend-independent project model.

### Tasks

- [ ] Freeze source discovery rules for root and vendored deps.
- [ ] Freeze module-path derivation from project name plus file path.
- [ ] Freeze module-path mismatch as a stable diagnostic.
- [ ] Freeze topological ordering as dependency-first across root plus vendored modules.
- [ ] Clarify which unresolved imports are tolerated during ad hoc `run` and which are hard errors during project `check/build`.

### Exit criteria

- Project load behavior is documented independently of any backend.
- Contributors can reason about graph construction without reading parser code.
- Self-hosted compiler projects and ordinary multi-module apps use the same loader contract.
- The loader contract is precise enough to become part of the self-hosted
  compiler without semantic drift.

### Evidence

- `ProjectLoader.fs`
- `resolveRunImports` behavior in `Program.fs`

## Work package G — Compatibility cleanup

### Goal

Remove or quarantine all behavior that would reintroduce multiple supported
dependency paths.

### Tasks

- [ ] Remove `ll.toml` from supported-path docs.
- [ ] Remove any old dependency path that competes with `vendor/`.
- [ ] Audit CLI docs and README for obsolete manifest/dependency language.
- [ ] Add explicit `TODO(v2:resolver)` or `TODO(v2:bootstrap)` markers where temporary compatibility remains.

### Exit criteria

- The supported path is unambiguous in docs and code.
- Compatibility shims, if any remain, are visibly transitional.
- Remaining transitional F#-only behaviors are tracked as explicit
  `TODO(v2:resolver)` / `TODO(v2:bootstrap)` items rather than accidental product surface.

### Evidence

- README
- user-guide CLI docs
- project loader manifest discovery

## Work package H — Validation and release gates

### Goal

Turn existing tests into an explicit `v2` quality gate for project-system work.

### Tasks

- [ ] Group current resolver/install tests under an explicit `Milestone 2` validation matrix in docs.
- [ ] Add missing diagnostics coverage where contract is stronger than current tests.
- [ ] Add self-hosted compiler project build coverage through canonical dependency flow.
- [ ] Add idempotence gate for repeated `install` and `tidy`.
- [ ] Ensure docs mention which test file is the authority for resolver scenarios.

### Exit criteria

- `Milestone 2` can be declared done from one visible validation list.
- There is no silent gap between docs and tests for canonical dependency behavior.

### Evidence

- `tests/LLLangTests/ModuleSystemTests.fs`
- self-hosting build tests

## Recommended implementation order

Implementers should take `Milestone 2` in this order:

1. Work package A — canonical manifest contract
2. Work package B — resolver semantics contract
3. Work package C — lock file contract
4. Work package D — vendor layout contract
5. Work package E — CLI lifecycle contract
6. Work package F — loader/module graph integration
7. Work package G — compatibility cleanup
8. Work package H — validation and release gates

This order matters:

- manifest and resolver rules define the semantics
- lock and vendor define the persistent realized state
- CLI defines operational lifecycle
- loader integration ensures the compiler actually consumes the same model
- cleanup and validation happen last once the canonical path is explicit

## Definition of done for Milestone 2

`Milestone 2` is done only when all of the following are true:

- `lll.toml` is the only supported manifest path
- one resolver behavior is documented and enforced
- `ll.sum` semantics are stable and versioned
- `vendor/` is the only supported local dependency layout
- `install`, `mod add`, `mod tidy`, and `mod why` are documented as one lifecycle
- loader/build behavior agrees with the project-system spec
- compatibility-only paths are visibly transitional or removed
- validation matrix is green and referenced from the roadmap
- the semantics are specified tightly enough that the canonical implementation
  can move into `ll-lang` without reverse-engineering stage0 code

## Questions to clarify after Milestone 2

These are not blockers for planning the milestone, but they should be reviewed
explicitly once the milestone is substantially implemented.

### Manifest questions

- Do we remove `ll.toml` fallback immediately, or keep it as a short migration shim with an explicit deprecation window?
- Should unknown manifest keys become hard errors in `check/build`, or remain warnings for one transition cycle?

### Resolver questions

- Is the current single-winner resolver sufficient for `v2`, or do real self-hosted use-cases already justify a post-`v2` resolver upgrade plan?
- Should `ll.sum` pinning be limited to source selection only, or also become a stricter reproducibility gate during normal project commands?

### CLI questions

- Do we need machine-readable output modes in `mod why` and `install` immediately for MCP, or can that be a Milestone 6 concern?
- Which command outputs must be treated as stable public surface versus human-only diagnostics?

### Self-hosting questions

- Which parts of manifest parsing, graph loading, and resolver logic should be the first mandatory `ll-lang`-owned implementation slice?
- Are any current stage0 project-system behaviors too coupled to filesystem/process details to port directly without an intermediate abstraction layer?

## Non-goals for Milestone 2

These belong after `v2` unless separately promoted:

- registry ecosystem
- semver ranges
- workspaces
- partial graph installation
- multi-version dependency retention
- distributed cache as part of semantics
