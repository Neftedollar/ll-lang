# ll-lang v2 Project System

**Status:** planned  
**Scope:** canonical project/dependency model for the `v2` line.

## Summary

`v2` defines one supported way to build ll-lang projects. The goals are
determinism, minimal ceremony, and behavior that is easy for both humans and
LLM agents to discover and reproduce.

## Canonical project artifacts

`v2` standardizes the following project files and directories:

- `lll.toml` — required manifest
- `ll.sum` — lock/checksum state
- `vendor/` — canonical local materialization of external dependencies
- `src/` — source tree

There must not be multiple permanent dependency mechanisms that compete with
each other in the supported path.

## Manifest responsibilities

`lll.toml` owns:

- package identity
- version
- entry module or entry file
- dependency declarations
- target/platform preferences when relevant

The main language spec should define the semantic meaning of each field, while
the CLI docs define user-facing commands.

## Dependency model

`v2` project resolution must support:

- local path dependencies
- git dependencies
- transitive dependency graphs
- deterministic resolution

If the resolver is not a full MVS-style system in `v2`, it must still expose
one canonical and repeatable behavior. “Temporary” alternate resolution modes
must not become part of the supported contract.

## Locking and vendoring

`ll.sum` is the authoritative lock/checksum file for resolved dependencies.

`vendor/` is the authoritative local copy/layout used by builds. This implies:

- repeated installs on the same graph are byte-stable
- stale lock/vendor mismatches are diagnosable
- agents do not need to guess where external modules came from

## Build and load order

The project system must define:

- module discovery rules
- import-to-file resolution rules
- dependency graph construction
- topological load/build order
- cycle diagnostics

These rules must be documented independently of any one backend.

## CLI surface required by v2

The canonical CLI project flow includes:

- `lllc install`
- `lllc mod add`
- `lllc mod tidy`
- `lllc mod why`
- project `build`
- project `check`
- project `run`

Each command should have deterministic side effects and a documented contract.

## Library vs executable contract

`v2` must clearly distinguish:

- library projects
- executable projects

The project system must define:

- how the entrypoint is chosen
- when backend entrypoint code is emitted
- what “library build” means for generated outputs

Library compilation must not depend on synthetic `main` generation.

## Diagnostics policy

Project-system diagnostics should report at least:

- missing manifest fields
- invalid dependency declarations
- unresolved modules
- graph cycles
- lock/vendor drift
- unsupported external mappings per target

All must be stable enough for use in MCP and automated repair loops.

## Deferred beyond v2

These are not required for the `v2` baseline:

- multi-registry dependency federation
- semver range solving beyond the chosen canonical resolver
- remote package registry ecosystem
- workspace-level incremental compilation

## Validation targets

The `v2` project system is not complete without:

- path dep tests
- git dep tests
- transitive graph tests
- lock determinism tests
- `vendor/` materialization tests
- repeated-run idempotence tests
- self-hosted compiler builds through the canonical project path
