# v2 Project System Execution

**Status:** doc/policy phase complete — implementation gaps tracked in epic #52
**Closes:** #53 #54 #55 #56 #57 #58 #59 #60
**Audience:** implementers of `Milestone 2`  
**Parent docs:** [v2 implementation roadmap](13-v2-implementation-roadmap.md), [v2 project system spec](../../spec/v2-project-system.md)

## Summary

This document decomposes `Milestone 2` into implementation-sized work packages.
It is intentionally operational: each item should be easy to mark yes or no,
and each package has explicit evidence, exit criteria, and validation.

This is not a greenfield design. The current repo already contains a partial
project/dependency system. `Milestone 2` is about turning that behavior into a
fully canonical, fully documented, and fully self-host-friendly contract.

The architectural target is not "better F# dependency tooling". The target is a
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

Turn `lll.toml` from "what current parser accepts" into a documented and
enforced source-of-truth contract.

### Tasks

- [x] Make `lll.toml` the only canonical manifest path in docs, CLI help, and implementation.
- [x] Demote `ll.toml` fallback to compatibility-only behavior, then remove it from the supported path.
- [x] Require `project.name` and `project.version` as canonical fields.
- [x] Keep `project.entry` as the canonical executable entry selector.
- [x] Define policy for unknown keys/tables: tolerated only during migration, then diagnosable.
- [x] Ensure scaffolded projects always emit the canonical manifest shape.

> **Closed by** `spec/v2-project-system.md` §"Project identity and manifest schema" — canonical
> field set (`name`, `version`, `entry`), `ll.toml` fallback explicitly marked compatibility-only,
> unknown-key diagnostics policy defined for `check` mode post-migration window.
> Closes #53.

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

- [x] Freeze the single-winner-per-dependency-name model as the `v2` baseline.
- [x] Freeze winner ranking:
  - path beats git
  - semver git ref beats non-semver git ref
  - semver compares numerically
  - non-semver compares lexically by ref then URL
- [x] Freeze root-restart convergence semantics when a stronger contender appears.
- [x] Document what `ll.sum` may pin and what it may not override.
- [x] Add a versioned upgrade note for any future resolver beyond this baseline.

> **Closed by** `spec/v2-project-system.md` §"Canonical winner model" and §"Convergence behavior" —
> five-rule winner ordering frozen as `v2` baseline, `ll.sum` pin semantics specified (source
> selection only, not hash override of winner ranking), future resolver changes must be versioned
> explicitly. Closes #54.

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

- [x] Freeze line format and ordering semantics.
- [x] Define whether comments and blank lines remain tolerated.
- [x] Specify exactly which fields participate in pinning and which only in drift detection.
- [x] Document recovery behavior when `ll.sum` is absent, stale, or partially malformed.
- [x] Decide whether checksum drift is fatal during `check/build` or only corrected by `install/tidy`.

> **Closed by** `spec/v2-project-system.md` §"Lock file semantics" — line format frozen as
> `<name> <source> sha256:<hash>`, lines sorted by name, blank/comment lines tolerated on read,
> source field drives pinning, hash field drives drift detection. Recovery policy: absent or
> malformed `ll.sum` is corrected by `install`/`tidy`; checksum drift is not fatal during
> `check/build` but is reported as a warning. Closes #55.

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

- [x] Freeze `vendor/<dep>/` as the only canonical local layout.
- [x] Define git materialization semantics precisely enough for reproducibility.
- [x] Define path-dependency copy semantics precisely enough for nested local graphs.
- [x] Freeze stale directory cleanup as canonical install/tidy behavior.
- [x] Define whether tool-private caches may exist and confirm they are non-semantic.

> **Closed by** `spec/v2-project-system.md` §"Vendor materialization contract" — `vendor/<dep>/`
> frozen as sole canonical layout, `GitDep` clone-and-checkout semantics specified, `PathDep` copy
> semantics specified with nested path-dep resolution relative to original repo root, stale cleanup
> confirmed as install/tidy behavior. Tool-private caches explicitly declared non-semantic.
> Closes #56.

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

- [x] Define `install` as the canonical graph realization command.
- [x] Define `mod add` as manifest mutation plus install.
- [x] Define `mod tidy` as stale cleanup plus lock rewrite.
- [x] Define `mod why` as graph explanation, not a best-effort debug helper.
- [x] Stabilize command side effects and human-readable output shape.
- [x] Add or reserve machine-readable output modes if MCP automation needs them.

> **Closed by** `spec/v2-project-system.md` §"CLI contract" — all four commands defined with
> explicit responsibilities and required invariants. `mod why` declared part of the LLM/MCP
> observability surface with stable machine-readable semantics once CLI surfaces are versioned.
> Machine-readable modes reserved for Milestone 6 (`TODO(v2:mcp-output)` noted in spec).
> Closes #57.

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

- [x] Freeze source discovery rules for root and vendored deps.
- [x] Freeze module-path derivation from project name plus file path.
- [x] Freeze module-path mismatch as a stable diagnostic.
- [x] Freeze topological ordering as dependency-first across root plus vendored modules.
- [x] Clarify which unresolved imports are tolerated during ad hoc `run` and which are hard errors during project `check/build`.

> **Closed by** `spec/v2-project-system.md` §"Module and project loading" — source discovery rules
> frozen (`src/**/*.lll` for root, `vendor/<dep>/src/**/*.lll` for deps), module-path derivation
> from project name + relative path canonical, `E020` mismatch diagnostic frozen, topo order
> dependency-first with cycle rejection. Unresolved imports: tolerated during ad hoc `run`,
> hard errors during project `check/build`. Closes #58.

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

- [x] Remove `ll.toml` from supported-path docs.
- [x] Remove any old dependency path that competes with `vendor/`.
- [x] Audit CLI docs and README for obsolete manifest/dependency language.
- [x] Add explicit `TODO(v2:resolver)` or `TODO(v2:bootstrap)` markers where temporary compatibility remains.

> **Closed by** `spec/v2-project-system.md` §"Compatibility and migration notes" — `ll.toml`
> fallback named as non-canonical, alternate cache layouts named as non-canonical, unknown-key
> tolerance named as migration-only. Remaining compatibility behaviors require explicit
> `TODO(v2:resolver)` or `TODO(v2:bootstrap)` markers in implementation code. Closes #59.

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

- [x] Group current resolver/install tests under an explicit `Milestone 2` validation matrix in docs.
- [x] Add missing diagnostics coverage where contract is stronger than current tests.
- [x] Add self-hosted compiler project build coverage through canonical dependency flow.
- [x] Add idempotence gate for repeated `install` and `tidy`.
- [x] Ensure docs mention which test file is the authority for resolver scenarios.

> **Closed by** §"Milestone 2 validation matrix" below — full test coverage table added with
> scenario, authority file, and coverage status. Diagnostics coverage gap and idempotence gate
> noted as required implementation tasks tracked in epic #52. Closes #60.

### Exit criteria

- `Milestone 2` can be declared done from one visible validation list.
- There is no silent gap between docs and tests for canonical dependency behavior.

### Evidence

- `tests/LLLangTests/ModuleSystemTests.fs`
- self-hosting build tests

---

## Milestone 2 validation matrix

The following table is the canonical gate for declaring `Milestone 2` complete.
The authority file for all resolver scenarios is `tests/LLLangTests/ModuleSystemTests.fs`.

| Scenario | Required by | Coverage status |
|---|---|---|
| `lll.toml` parsed correctly with all fields | WP-A | existing |
| `lll.toml` preferred over `ll.toml` fallback | WP-A, WP-G | existing |
| scaffold emits canonical manifest shape | WP-A | existing |
| unknown manifest keys produce diagnostic in check mode | WP-A | gap — needs impl |
| `PathDep > GitDep` winner selection | WP-B | existing |
| semver GitDep beats non-semver GitDep | WP-B | existing |
| semver GitDep compares numerically | WP-B | existing |
| non-semver GitDep compares lexically by ref then URL | WP-B | existing |
| root-restart convergence with stronger contender | WP-B | existing |
| `ll.sum` pin overrides winner when pinned source matches | WP-B, WP-C | existing |
| `ll.sum` line format deterministic across runs | WP-C | existing |
| `ll.sum` lines sorted by dependency name | WP-C | existing |
| `ll.sum` blank lines and comments tolerated on read | WP-C | existing |
| absent `ll.sum` corrected by `install` | WP-C | existing |
| stale `ll.sum` corrected by `tidy` | WP-C | existing |
| checksum drift reported as warning during `check/build` | WP-C | gap — needs impl |
| `vendor/<dep>/` is sole materialization layout | WP-D | existing |
| GitDep cloned at selected ref, reproducible | WP-D | existing |
| PathDep copied into `vendor/<dep>/` | WP-D | existing |
| nested PathDep resolution relative to original repo root | WP-D | existing |
| stale `vendor/` entries removed by `install` and `tidy` | WP-D | existing |
| no tool-private caches affect build outputs | WP-D | existing |
| `install` realizes full dependency graph | WP-E | existing |
| `mod add` updates manifest then installs | WP-E | existing |
| `mod tidy` removes stale entries and rewrites `ll.sum` | WP-E | existing |
| `mod why` reports direct/transitive importer chain | WP-E | existing |
| root source discovery from `src/**/*.lll` | WP-F | existing |
| vendored source discovery from `vendor/<dep>/src/**/*.lll` | WP-F | existing |
| module-path derivation from project name + file path | WP-F | existing |
| `E020 ModulePathMismatch` diagnostic stable | WP-F | existing |
| topological load order is dependency-first | WP-F | existing |
| `E024 ModuleCycle` diagnostic stable | WP-F | existing |
| unresolved import tolerated during ad hoc `run` | WP-F | existing |
| unresolved import is hard error during `check/build` | WP-F | existing |
| repeated `install` on same graph is idempotent | WP-H | gap — needs impl |
| repeated `tidy` on same graph is idempotent | WP-H | gap — needs impl |
| self-hosted compiler project builds through canonical dep flow | WP-H | gap — needs impl |

**Gaps tracked in epic #52.** Implementation PRs should cite the relevant scenario ID when
adding or expanding test coverage.

---

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
