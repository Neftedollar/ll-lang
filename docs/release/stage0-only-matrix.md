# Stage0-Only Matrix

Last updated: 2026-04-20
Owner: @Neftedollar

This matrix lists the remaining stage0/F# surfaces after self-host cutover.

| Surface | Why still stage0-only | Owner | Planned removal | Status |
|---|---|---|---|---|
| `.github/workflows/legacy-stage0.yml` | Manual emergency diagnostics for archived stage0 solution/tests | @Neftedollar | Remove after two stable releases with no stage0 emergency use | Transitional |
| `Bootstrap Release Artifacts` (`.github/workflows/bootstrap-release.yml` + `tools/build-bootstrap-artifacts.sh`) | Bootstrap artifact publication still shells to `dotnet publish` of archived stage0 CLI | @Neftedollar | Replace with self-host native artifact builder + retire dotnet path | Transitional |
| `obsolete/stage0/**` | Archived reference/runtime fallback only; not daily path | @Neftedollar | Archive out of default repo path after bootstrap artifact flow is fully self-hosted | Transitional |

## Not stage0-only anymore

- Daily `compile/check/run` command path: self-host default via `tools/lllc-bootstrap.sh`.
- Required CI lane: `Self-host CI` only.
