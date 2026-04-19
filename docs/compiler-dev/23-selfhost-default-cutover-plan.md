# Self-Hosted Default Cutover Plan

This document defines the rollout path for making self-hosted `lllcself` the canonical command path for default `lllc` developer workflows.

## Scope

- Commands in scope: `compile`, `check`, `run`.
- Launcher in scope: `tools/lllc-bootstrap.sh`.
- Out of scope: backend semantic parity implementation details (tracked separately).

## Why

Today, users can run self-hosted behavior via `lllc self ...`, but default commands still depend on bootstrap binary routing. We need a deterministic, reversible path to make self-hosted behavior the default without breaking teams mid-migration.

## Rollout Controls

`tools/lllc-bootstrap.sh` supports controlled routing through environment variables:

- `LLLC_BOOTSTRAP_SELF_PRESET=off|safe|all`
- `LLLC_BOOTSTRAP_SELF_COMMANDS=compile,check,run` (explicit override)
- `LLLC_BOOTSTRAP_SELF_VERBOSE=1` (debug routing decisions)

Preset behavior:

- `off` (default): no routing changes.
- `safe`: route `compile` + `check` through `self`.
- `all`: route `compile` + `check` + `run` through `self`.

Rollback is immediate by setting:

```bash
LLLC_BOOTSTRAP_SELF_PRESET=off
```

## Parity Contract (current gate)

Current parity gate script:

```bash
./tools/check-stage0-self-parity.sh
```

Compares `lllc check` (stage0 route) vs `lllc self check` on corpus files using:

- exit code parity (`stage0_code == self_code`)
- success-shape sanity:
  - stage0 success output starts with `Checked ...`
  - self success output contains `"ok":true`

First mismatch is printed with both outputs for debugging.

## Phases

1. Phase A (`safe` opt-in):
- Enable in CI smoke lane.
- Verify command-shape and diagnostics stability for `compile/check`.

2. Phase B (`all` opt-in in canary lanes):
- Validate `run` UX/output parity expectations and migration impact.

3. Phase C (default flip):
- Change default preset from `off` to target preset once parity gate is green.
- Keep rollback flag documented and tested.

## Exit Criteria for Default Flip

- Parity gate reports no critical divergence for selected corpus.
- CI includes self-default command routing checks.
- User docs describe default behavior and rollback procedure.
- Release notes include cutover status and known deltas.
