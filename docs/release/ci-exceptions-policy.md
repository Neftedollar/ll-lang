# CI Exceptions Policy (Release Readiness)

Last updated: 2026-04-20
Owner: @Neftedollar

## Policy

- Required release lane is `Self-host CI` (`.github/workflows/build.yml`).
- Allowed exceptions in the required lane: **none**.
- `continue-on-error` is not allowed in `Self-host CI`.
- Archived stage0 workflow is non-blocking by policy and may use `continue-on-error`.

## Allowed exceptions registry

| Scope | Allowed | Rationale | Review cadence |
|---|---|---|---|
| `.github/workflows/build.yml` (`Self-host CI`) | No | Release-critical lane for LLL-first operation | Every release cut |
| `.github/workflows/legacy-stage0.yml` (`Archived Stage0 (.NET, manual)`) | Yes (non-blocking/manual) | Emergency bootstrap diagnostics only | Every release cut |

## Enforcement

`tools/check-release-readiness.sh` verifies that:

1. `Self-host CI` has no `continue-on-error`.
2. Required release steps are present.
3. Archived stage0 workflow remains manual and explicitly non-blocking.
