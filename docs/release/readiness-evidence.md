# Release Readiness Evidence

Last updated: 2026-04-20
Owner: @Neftedollar

## Required CI suite (must be green)

`Self-host CI` (`.github/workflows/build.yml`) is the release-critical lane and includes:

1. `tools/check-selfhost-ci.sh`
2. `tools/check-selfhost-e2e.sh`
3. `tools/check-selfhost-multimodule.sh`
4. `tools/check-stage0-self-parity.sh`
5. `tools/check-llvm-smoke.sh`
6. `tools/check-doc-contract.sh`
7. `tools/check-no-fs3261-suppression.sh`
8. `tools/check-release-readiness.sh`

Policy for allowed exceptions is documented in [CI Exceptions Policy](./ci-exceptions-policy.md).

## Multi-module self-host evidence

`tools/check-selfhost-multimodule.sh` constructs and validates a dependency-bearing project graph:

- `Graphdemo.Main`
- `Graphdemo.Flow`
- `Graphdemo.Math`
- `Graphdemo.Const`

The script requires:

- `lllc self check <project-dir>` returns `"ok":true`
- `lllc self build --target py <project-dir>` emits all project modules

## Stage0-only evidence

See [Stage0-Only Matrix](./stage0-only-matrix.md) for the explicit residual stage0 scope and planned removal path.
