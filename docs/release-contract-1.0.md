# ll-lang Release Contract 1.0

This document defines what is guaranteed in `1.0` and what is explicitly out of
scope for compatibility guarantees.

## Stable in 1.0

The following surface is covered by `1.0` compatibility guarantees:

- Core compiler pipeline: lexer, parser, elaborator, HM inference, diagnostics,
  and forward code generation.
- CLI workflows: `build`, `run`, `new`, `install`, `mcp`, `self`, and
  project-mode `check [dir]`.
- Stable targets: `fs` (default), `ts`, `py`, `java`, `cs`.
- Project mode (`lll.toml`, multi-file topo build, module checks).
- Published error-code contract (`E001`, `E002`, `E003`, `E004`, `E005`,
  `E007`, `E008`, `E020`, `E024`, `E025`, `E026`).

## Experimental (Not Covered by 1.0 Guarantees)

These features remain available but are explicitly non-blocking for `1.0`:

- `lllc reverse --from ...` and reverse transpiler behavior.
- `--target llvm` backend (subset-oriented surface).
- `lllc check <file.lll>` stage0 single-file path (legacy compatibility mode).

Behavior or output shape in these areas may change between minor releases while
the team designs a long-term architecture.

## 1.0 Release Gates

`1.0` release readiness requires:

- `release-core` CI lane green.
- User-facing docs aligned with stable vs experimental labels.
- `tools/check-doc-contract.sh` green.

Experimental CI lanes are informative and do not block `1.0`.

## Compatibility Policy

- Stable (`1.0`) surface follows semantic versioning expectations.
- Experimental surface may evolve without the same compatibility guarantees.
