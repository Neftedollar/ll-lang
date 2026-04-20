#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LLLC="$ROOT_DIR/tools/lllc-bootstrap.sh"

fail() {
  echo "check-selfhost-multimodule: $*" >&2
  exit 1
}

[[ -x "$LLLC" ]] || fail "missing launcher: $LLLC"

tmp_root="$(mktemp -d)"
trap 'rm -rf "$tmp_root"' EXIT

project_dir="$tmp_root/graphdemo"
mkdir -p "$project_dir/src"

cat > "$project_dir/lll.toml" <<'TOML'
[project]
name = "graphdemo"
version = "0.1.0"
entry = "src/Main.lll"
TOML

cat > "$project_dir/src/Main.lll" <<'LLL'
module Graphdemo.Main
import Graphdemo.Flow
import Graphdemo.Math

main() Int = 0
LLL

cat > "$project_dir/src/Flow.lll" <<'LLL'
module Graphdemo.Flow
import Graphdemo.Const

flowSeed() Int = 7
LLL

cat > "$project_dir/src/Math.lll" <<'LLL'
module Graphdemo.Math
import Graphdemo.Const

square(n Int) Int = n * n
LLL

cat > "$project_dir/src/Const.lll" <<'LLL'
module Graphdemo.Const

baseValue Int = 42
LLL

echo "==> self check (multi-module project)"
check_out="$("$LLLC" self check "$project_dir" 2>&1 || true)"
printf '%s\n' "$check_out"

grep -q '"ok":true' <<<"$check_out" || fail "self check did not report ok=true"
grep -q '"ok":false' <<<"$check_out" && fail "self check reported ok=false"

echo "==> self build --target py (multi-module project)"
build_out="$("$LLLC" self build --target py "$project_dir" 2>&1 || true)"
printf '%s\n' "$build_out"

grep -q '"ok":false' <<<"$build_out" && fail "self build returned error JSON"
grep -q '# Module: Graphdemo.Main' <<<"$build_out" || fail "missing generated Main module"
grep -q '# Module: Graphdemo.Flow' <<<"$build_out" || fail "missing generated Flow module"
grep -q '# Module: Graphdemo.Math' <<<"$build_out" || fail "missing generated Math module"
grep -q '# Module: Graphdemo.Const' <<<"$build_out" || fail "missing generated Const module"

echo "check-selfhost-multimodule: OK"
