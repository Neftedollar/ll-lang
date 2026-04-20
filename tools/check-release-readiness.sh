#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

fail() {
  echo "check-release-readiness: $*" >&2
  exit 1
}

BUILD_WF="$ROOT_DIR/.github/workflows/build.yml"
LEGACY_WF="$ROOT_DIR/.github/workflows/legacy-stage0.yml"
POLICY_DOC="$ROOT_DIR/docs/release/ci-exceptions-policy.md"
MATRIX_DOC="$ROOT_DIR/docs/release/stage0-only-matrix.md"
EVIDENCE_DOC="$ROOT_DIR/docs/release/readiness-evidence.md"

[[ -f "$BUILD_WF" ]] || fail "missing workflow: $BUILD_WF"
[[ -f "$LEGACY_WF" ]] || fail "missing workflow: $LEGACY_WF"
[[ -f "$POLICY_DOC" ]] || fail "missing policy doc: $POLICY_DOC"
[[ -f "$MATRIX_DOC" ]] || fail "missing stage0-only matrix doc: $MATRIX_DOC"
[[ -f "$EVIDENCE_DOC" ]] || fail "missing readiness evidence doc: $EVIDENCE_DOC"

if grep -q 'continue-on-error:\s*true' "$BUILD_WF"; then
  fail "Self-host CI must not use continue-on-error"
fi

grep -q 'workflow_dispatch:' "$LEGACY_WF" || fail "legacy-stage0 workflow must remain manual (workflow_dispatch)"
grep -q 'continue-on-error:\s*true' "$LEGACY_WF" || fail "legacy-stage0 workflow must stay non-blocking"

grep -q 'Allowed exceptions in the required lane: \*\*none\*\*' "$POLICY_DOC" || fail "policy doc must state no allowed exceptions in required lane"
grep -q '\.github/workflows/build\.yml' "$POLICY_DOC" || fail "policy doc must reference required lane workflow"
grep -q '\.github/workflows/legacy-stage0\.yml' "$POLICY_DOC" || fail "policy doc must reference legacy stage0 workflow"

grep -q 'Self-host CI' "$EVIDENCE_DOC" || fail "readiness evidence must describe required lane"
grep -q 'tools/check-selfhost-multimodule\.sh' "$EVIDENCE_DOC" || fail "readiness evidence must reference multi-module check"
grep -q 'Stage0-Only Matrix' "$EVIDENCE_DOC" || fail "readiness evidence must link stage0 matrix"

for step in \
  "Bootstrap Self-host Check Suite" \
  "Self-host CLI E2E" \
  "Self-host Multi-module Readiness" \
  "Stage0 vs Self Parity Gate" \
  "LLVM Smoke Chain" \
  "Doc Contract Check" \
  "Nullability Suppression Guard" \
  "Release Readiness Contract"; do
  grep -q "$step" "$BUILD_WF" || fail "missing required CI step: $step"
done

grep -q 'Bootstrap Release Artifacts' "$MATRIX_DOC" || fail "stage0-only matrix must include bootstrap release workflow"
grep -q 'legacy-stage0\.yml' "$MATRIX_DOC" || fail "stage0-only matrix must include legacy stage0 workflow"

echo "check-release-readiness: OK"
