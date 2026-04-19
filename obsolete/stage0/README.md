# Archived Stage0 (.NET)

This directory contains the archived F# stage0 compiler/tool/test suite.

- Path: `obsolete/stage0/`
- Daily path: **NOT USED**
- Default CI: **self-host only** (`tools/check-selfhost-ci.sh`, `tools/check-selfhost-e2e.sh`, `tools/check-llvm-smoke.sh`)
- Manual diagnostics only: `.github/workflows/legacy-stage0.yml`

Scope policy:
- No new feature work in stage0.
- Only emergency bootstrap diagnostics/fixes when required.
- Canonical implementation lives in `.lll` (`lllcself/`, `stdlib/`).
