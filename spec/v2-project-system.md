# ll-lang v2 Project System

**Status:** frozen — doc/policy phase complete (epic #52, closes #53–#60)  
**Scope:** canonical project/dependency model for the `v2` line.

## Summary

`v2` defines exactly one supported way to build, resolve, and explain ll-lang
projects. The goals are:

- deterministic project resolution
- minimal user ceremony
- behavior that is easy for humans and LLM agents to discover and reproduce
- a single manifest, lock, and vendor model
- backend-independent module/project semantics

This spec is intentionally stricter than the current `1.x` implementation. If
the current code supports compatibility fallbacks, those are migration aids and
not part of the `v2` contract unless explicitly listed here.

For `v2`, the canonical long-term owner of this project system is the
self-hosted `ll-lang` implementation. F# implementations may continue to exist
as stage0/bootstrap or migration bridges, but they are not the intended final
implementation authority for manifest, resolver, lock, or vendor semantics.

## Canonical project artifacts

`v2` standardizes the following project files and directories:

- `lll.toml` — required manifest
- `ll.sum` — authoritative lock/checksum state
- `vendor/` — canonical local materialization of resolved dependencies
- `src/` — source tree

No other permanent dependency mechanism is part of the supported path.
In particular:

- `ll.toml` fallback is compatibility-only and not canonical in `v2`
- ad hoc hidden dependency directories or tool-private cache layouts are not
  part of the project contract
- backend-specific project manifests are derived artifacts, not source-of-truth

## Project identity and manifest schema

`lll.toml` owns:

- package identity
- package version
- executable entry selection
- dependency declarations
- target/platform preferences when relevant

### Required top-level tables

- `[project]`
- `[deps]` is optional
- `[platform]` is optional

Unknown tables and unknown keys may be tolerated by the parser during `1.x`
transition, but `v2` tools should diagnose them in `check` mode once the
migration window closes.

### `[project]` fields

- `name : string`
  - required
  - logical project/module root name
- `version : string`
  - required in `v2`
  - current parser default of `0.0.0` is a migration behavior, not the
    intended long-term contract
- `entry : string`
  - required for executable projects
  - canonical path relative to project root, typically `src/Main.lll`

### `[deps]` values

Each dependency key is the logical dependency name. Supported source forms:

- `dep = "https://host/repo.git#v1.2.3"`
- `dep = "https://host/repo.git"`
  - defaults to `main`
- `dep = { path = "../local-repo" }`

`v2` keeps the source model intentionally small:

- `GitDep(url, ref)`
- `PathDep(path)`

No registry package form is required for `v2`.

### `[platform]`

Current implementation supports:

- `[platform]`
- `use = ["fsharp", "csharp", ...]`

In `v2`, this section remains advisory target selection owned by the project
manifest. Backend-specific extensions must not redefine dependency semantics.

## Canonical dependency model

The `v2` resolver must support:

- local path dependencies
- git dependencies
- transitive dependency graphs
- deterministic convergence on exactly one winner per logical dependency name

`v2` does not require a full registry-oriented MVS solver. It does require one
canonical and repeatable winner policy.

### Canonical winner model

Resolution is performed over a graph of contenders keyed by dependency name.
For each logical dependency name, the resolver chooses a single winner.

Current implementation already follows this ordering, and `v2` adopts it as the
canonical baseline unless superseded by a later `TODO(v2:resolver)` upgrade:

1. `PathDep` outranks `GitDep`
2. For `GitDep`, semver refs outrank non-semver refs
3. Semver refs are compared numerically
4. Non-semver refs are compared lexically by ref, then URL
5. A matching `ll.sum` pin overrides normal winner ranking when the pinned
   source matches one of the available contenders

This implies:

- the resolver is single-winner, not per-edge multi-version
- winner selection is deterministic
- transitive conflicts converge through restart, not by keeping parallel copies

### Convergence behavior

The current installer restarts resolution from the root whenever a stronger
winner appears for an already-seen dependency name. `v2` keeps the semantic
invariant, even if the internal algorithm changes:

- dependency graphs must converge from the root view
- lower-priority transitive tails must not survive after a stronger winner
  replaces them
- repeated installs on the same graph must produce the same `vendor/` tree and
  `ll.sum`

If the algorithm changes after `v2`, the externally visible behavior must
remain deterministic or be versioned explicitly.

## Lock file semantics

`ll.sum` is the authoritative lock/checksum file for resolved dependencies.

Each line represents one resolved dependency and must contain:

- logical dependency name
- canonical source text
- content hash of the vendored materialization

Current implementation writes lines in this shape:

`<name> <source> sha256:<hash>`

Where `<source>` is rendered as either:

- `git:<url>#<ref>`
- `path:<path>`

### `ll.sum` invariants

- lines are sorted by dependency name
- blank lines and comment lines may be ignored on read
- the source portion participates in winner pinning
- the hash portion participates in drift detection and reproducibility checks

### Recovery and drift policy

- if `ll.sum` is absent, `install` and `tidy` create it from scratch
- if `ll.sum` is partially malformed, `install` and `tidy` overwrite it
- checksum drift (hash in `ll.sum` does not match `vendor/` content) is reported
  as a warning during `check/build`; it does not cause a hard build failure
- correcting drift always goes through `install` or `tidy`, never silently

### Lock responsibilities

`ll.sum` is responsible for:

- remembering which winner was selected for each dependency name
- ensuring repeated installs converge to the same winner set
- making vendored state auditable by humans and tools

`ll.sum` is not a substitute for dependency declarations in `lll.toml`; it is a
realized-resolution artifact, not the source graph declaration.

## Vendor materialization contract

`vendor/` is the authoritative local copy/layout used by builds and project
loading.

For each resolved dependency `dep`, the canonical location is:

- `vendor/dep/`

Expected contents are the dependency repository or copied path tree, including
its own `lll.toml` and `src/`.

### Vendor invariants

- only resolved winners exist in `vendor/`
- stale directories are removed during `install` / `mod tidy`
- vendored layout is stable across repeated runs on the same graph
- project loading reads dependency modules from `vendor/<name>/src/`

### Materialization semantics by source kind

- `GitDep`
  - cloned into `vendor/<name>/`
  - checked out at the selected ref
- `PathDep`
  - copied into `vendor/<name>/`
  - relative nested path dependencies continue resolving relative to the
    original owning repository root, not the vendored copy

That last rule is important: it preserves correct nested path-dependency
resolution for transitive local projects.

## Module and project loading

The project system defines:

- manifest discovery rules
- source discovery rules
- dependency source loading rules
- import graph construction
- topological load/build order
- cycle diagnostics

### Manifest discovery

In `v2`, the canonical manifest name is `lll.toml`.

Current loader still supports `ll.toml` as fallback. That fallback is
compatibility behavior only and should be removed from the supported path before
declaring the `v2` project system complete.

### Source discovery

Canonical source discovery is:

- root project sources from `src/**/*.lll`
- dependency sources from `vendor/<dep>/src/**/*.lll`

### Module path contract

The module path of a source file is determined by:

- project name as the root namespace segment
- file path relative to `src/`

If an explicit module path in source disagrees with the file-derived path, the
project loader must report a stable module-path mismatch diagnostic.

### Graph construction

Project loading constructs:

- the root module graph
- the dependency module graph
- a combined project-visible graph

Only imports that resolve to known project or vendored modules participate in
topological ordering.

### Topological order

Build/load order is dependency-first. Cycles must be rejected with a stable
diagnostic independent of backend.

## CLI contract

The canonical project flow includes:

- `lllc install`
- `lllc mod add`
- `lllc mod tidy`
- `lllc mod why`
- project `build`
- project `check`
- project `run`

### `lllc install`

Responsibilities:

- read `lll.toml`
- resolve the full dependency graph
- choose one winner per logical dependency name
- materialize winners into `vendor/`
- remove stale vendored directories
- rewrite `ll.sum`

Required invariants:

- byte-stable repeated runs on the same graph
- deterministic winner selection
- non-zero exit on manifest or dependency failure

### `lllc mod add <name>=<source>`

Responsibilities:

- parse a single dependency source
- update `[deps]` in `lll.toml`
- preserve canonical manifest rendering
- run install semantics after update

Accepted source forms:

- `https://...#ref`
- `https://...`
- `path:../dir`

### `lllc mod tidy`

Responsibilities:

- reconcile `vendor/` with declared and transitively resolved dependencies
- remove stale vendored entries
- rewrite `ll.sum`

### `lllc mod why <dep>`

Responsibilities:

- explain why a dependency exists in the resolved graph
- report direct or transitive chain from root project
- report local direct importers when available

This command is part of the LLM- and MCP-facing observability story and must
retain stable machine-readable semantics once the CLI surfaces are versioned.

`TODO(v2:mcp-output)` — machine-readable structured output mode for `mod why`
and `install` is reserved for Milestone 6. Human-readable output only in `v2`.

## Library vs executable contract

`v2` must clearly distinguish:

- library projects
- executable projects

The project system defines:

- how an entrypoint is chosen
- when backend entrypoint code is emitted
- what “library build” means for generated outputs

`entry` in `lll.toml` names the executable entry module/file when the project
is built or run as an executable.

Library compilation must not depend on synthetic `main` generation. If a
project is compiled as a library, the absence of an executable entrypoint must
not force backend-specific entry shims.

## Diagnostics policy

Project-system diagnostics must cover at least:

- missing or unreadable manifest
- invalid manifest structure
- invalid dependency declaration syntax
- missing path dependency roots
- failed git clone or checkout
- unresolved modules
- module path mismatch
- module cycles
- lock/vendor drift
- unsupported platform/backend mapping required by the selected target

Diagnostics must be stable enough for:

- MCP consumption
- automated repair loops
- deterministic tests

## Compatibility and migration notes

These behaviors may exist in current `1.x` code but are not part of the final
`v2` supported path:

- `ll.toml` fallback manifest discovery
- tolerance of unknown manifest keys without diagnostics
- any alternate dependency source cache outside `vendor/`

If they remain temporarily for bootstrap reasons, they must be documented as
compatibility-only and tracked under explicit `TODO(v2:resolver)` or
`TODO(v2:bootstrap)` notes.

## Deferred beyond v2

These are not required for the `v2` baseline:

- multi-registry dependency federation
- semver range solving beyond the chosen canonical resolver
- remote package registry ecosystem
- workspace-level incremental compilation
- lockfile-driven partial installs

## Validation targets

The `v2` project system is not complete without:

- path dep tests
- git dep tests
- transitive graph tests
- lock determinism tests
- `vendor/` materialization tests
- stale vendor cleanup tests
- repeated-run idempotence tests
- `mod add` / `mod tidy` / `mod why` tests
- self-hosted compiler builds through the canonical project path
