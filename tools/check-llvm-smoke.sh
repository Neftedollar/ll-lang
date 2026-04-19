#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALLER="$ROOT_DIR/tools/bootstrap-self.sh"
LLLC="$ROOT_DIR/tools/lllc-bootstrap.sh"

fail() {
  echo "check-llvm-smoke: $*" >&2
  exit 1
}

[[ -x "$INSTALLER" ]] || fail "missing installer: $INSTALLER"
[[ -x "$LLLC" ]] || fail "missing launcher: $LLLC"

if [[ "${LLLC_BOOTSTRAP_REINSTALL:-0}" == "1" ]]; then
  "$INSTALLER" install --reinstall >/dev/null
else
  "$INSTALLER" install >/dev/null
fi
SELF_MAIN="$ROOT_DIR/lllcself/src/Main.lll"
[[ -f "$SELF_MAIN" ]] || fail "missing self main file: $SELF_MAIN"

tmp_root="$(mktemp -d)"
trap 'rm -rf "$tmp_root"' EXIT

src_file="$ROOT_DIR/spec/examples/valid/01-basics.lll"
[[ -f "$src_file" ]] || fail "missing source file: $src_file"

out_file="$tmp_root/output.ll"
if LLLC_SELF_MAIN="$SELF_MAIN" "$LLLC" self compile --target llvm "$src_file" >"$out_file" 2>"$tmp_root/compile.err"; then
  cat "$tmp_root/compile.err"
  cat "$out_file"
else
  cat "$tmp_root/compile.err"
  cat "$out_file"
  fail "llvm build command failed"
fi

rg -q '^; ll-lang LLVM IR output|^define ' "$out_file" || fail "llvm output does not look like IR"

if command -v llc >/dev/null 2>&1; then
  echo "==> llc smoke"
  llc "$tmp_root/output.ll" -o "$tmp_root/output.s"
  [[ -f "$tmp_root/output.s" ]] || fail "llc did not produce assembly"
else
  echo "check-llvm-smoke: INFO llc not found; IR generation smoke only"
fi

echo "check-llvm-smoke: OK"
