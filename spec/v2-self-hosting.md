# ll-lang v2 Self-Hosting Contract

**Status:** planned  
**Scope:** defines what “self-hosted” means operationally for `v2`.

## Summary

`v2` is the point where the canonical ll-lang compiler stops being the F#
implementation and becomes the ll-lang implementation. The F# compiler is
retained only as a frozen stage0 bootstrap and recovery artifact.

## Terms

- **Stage0** — frozen bootstrap compiler implemented in F#
- **Stage1** — compiler built from the ll-lang source using stage0
- **Stage2** — compiler built from the same ll-lang source using stage1
- **Canonical compiler** — the ll-lang implementation under active development

## Required v2 state

`v2` requires all of the following:

1. The canonical compiler source tree is ll-lang.
2. The canonical stdlib source tree is ll-lang.
3. The canonical project resolver and main CLI logic are ll-lang.
4. Stage0 exists only to bootstrap or recover the canonical compiler.
5. New compiler features land in the canonical ll-lang compiler first.

## Compiler responsibilities that must be self-hosted

The canonical ll-lang implementation must own:

- lexing
- parsing
- surface validation/desugaring
- elaboration / resolution
- type inference / typed-core validation
- backend-neutral lowering
- backend emission
- project loading and dependency resolution
- CLI command orchestration

If a subsystem remains implemented only in F#, `v2` is not complete.

## Bootstrap policy

The stage0 F# compiler is allowed to remain in the repo under an isolated
bootstrap path, but:

- it is not the source of truth
- feature work must not land only there
- docs must describe it as bootstrap-only
- CI must verify the self-hosted path, not just the bootstrap path

## Fixpoint policy

Fixpoint remains an important validation tool, but `v2` is not defined only by
`compiler1 == compiler2` on emitted source.

`v2` requires the stronger condition:

- the self-hosted compiler is the canonical development path
- it can build itself
- it can build a real multi-module dependency-bearing project
- it can build stdlib-consuming examples through the canonical project flow

## Documentation obligations

Any self-hosting milestone must update:

- architecture overview
- self-hosting roadmap
- build/recovery instructions
- release gate documentation

If a human cannot tell which compiler is canonical by reading the docs, the
self-hosting transition is incomplete.

## Deferred beyond v2

Not required for `v2`:

- removing stage0 from the repository entirely
- making bootstrap independent of .NET
- multi-platform self-host bootstrap independence
- optimizer-heavy self-hosted pipelines beyond correctness and maintainability

## Validation targets

The self-hosting contract is not complete without:

- stage1 build test
- stage2/fixpoint test
- self-hosted compiler smoke test on a real project
- stdlib-consuming project build
- documented bootstrap recovery path
