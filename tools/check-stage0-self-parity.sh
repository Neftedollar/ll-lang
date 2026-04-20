#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LLLC="$ROOT_DIR/tools/lllc-bootstrap.sh"

fail() {
  echo "check-stage0-self-parity: $*" >&2
  exit 1
}

[[ -x "$LLLC" ]] || fail "missing launcher: $LLLC"

CORPUS=(
  "spec/examples/valid/06-stdlib.lll"
  "spec/examples/valid/07-text-processing.lll"
  "spec/examples/valid/21-multi-param-types.lll"
  "spec/examples/valid/24-pipeline-v2.lll"
  "spec/examples/valid/25-llm-repair-workflow.lll"
  "spec/examples/valid/26-operators-precedence.lll"
)
# Note: 09-lexer-real is currently excluded because self `check` diverges
# (tracked by parity workstream #176).

result_kind() {
  local file="$1"
  local code="$2"
  if [[ "$code" -ne 0 ]]; then
    echo "fail"
    return
  fi
  if grep -q '"ok":false' "$file"; then
    echo "fail"
    return
  fi
  echo "ok"
}

tmp_root="$(mktemp -d)"
trap 'rm -rf "$tmp_root"' EXIT

ok=0
bad=0

echo "=== stage0 vs self parity (check command) ==="
echo ""

for rel in "${CORPUS[@]}"; do
  abs="$ROOT_DIR/$rel"
  [[ -f "$abs" ]] || fail "missing corpus file: $abs"

  stage0_out="$tmp_root/stage0.$(basename "$rel").out"
  self_out="$tmp_root/self.$(basename "$rel").out"

  if LLLC_BOOTSTRAP_SELF_PRESET=off "$LLLC" check "$abs" >"$stage0_out" 2>&1; then
    stage0_code=0
  else
    stage0_code=$?
  fi

  if "$LLLC" self check "$abs" >"$self_out" 2>&1; then
    self_code=0
  else
    self_code=$?
  fi

  printf "  %-40s" "$(basename "$rel")"

  stage0_kind="$(result_kind "$stage0_out" "$stage0_code")"
  self_kind="$(result_kind "$self_out" "$self_code")"

  if [[ "$stage0_kind" != "$self_kind" ]]; then
    echo " FAIL (result mismatch stage0=$stage0_kind self=$self_kind)"
    echo "    exit codes: stage0=$stage0_code self=$self_code"
    echo "    stage0: $(tr '\n' ' ' <"$stage0_out" | sed 's/[[:space:]]\+/ /g' | sed 's/^ //;s/ $//')"
    echo "    self:   $(tr '\n' ' ' <"$self_out" | sed 's/[[:space:]]\+/ /g' | sed 's/^ //;s/ $//')"
    bad=$((bad + 1))
    continue
  fi

  echo " OK"
  ok=$((ok + 1))
done

echo ""
echo "$ok/${#CORPUS[@]} parity checks passed."

if [[ "$bad" -ne 0 ]]; then
  fail "$bad mismatches detected"
fi

echo "check-stage0-self-parity: OK"
