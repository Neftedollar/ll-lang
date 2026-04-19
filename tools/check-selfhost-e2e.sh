#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALLER="$ROOT_DIR/tools/bootstrap-self.sh"
LLLC="$ROOT_DIR/tools/lllc-bootstrap.sh"

fail() {
  echo "check-selfhost-e2e: $*" >&2
  exit 1
}

[[ -x "$INSTALLER" ]] || fail "missing installer: $INSTALLER"
[[ -x "$LLLC" ]] || fail "missing launcher: $LLLC"

if [[ "${LLLC_BOOTSTRAP_REINSTALL:-0}" == "1" ]]; then
  "$INSTALLER" install --reinstall >/dev/null
else
  "$INSTALLER" install >/dev/null
fi

BOOTSTRAP_BIN="$($INSTALLER path)"
[[ -x "$BOOTSTRAP_BIN" ]] || fail "resolved bootstrap binary is not executable: $BOOTSTRAP_BIN"
SELF_MAIN="$ROOT_DIR/lllcself/src/Main.lll"
[[ -f "$SELF_MAIN" ]] || fail "missing self main file: $SELF_MAIN"

tmp_root="$(mktemp -d)"
trap 'rm -rf "$tmp_root"' EXIT

main_file="$tmp_root/Smoke.lll"
cat > "$main_file" <<'SRC'
module Smoke

add(a Int)(b Int) Int = a + b
main() = add 2 3
SRC

run_capture() {
  local label="$1"
  shift
  local out_file="$tmp_root/cmd.out"
  echo "==> $label"
  if "$@" >"$out_file" 2>&1; then
    cat "$out_file"
  else
    cat "$out_file"
    fail "command failed: $label"
  fi
}

run_self_capture() {
  local label="$1"
  shift
  run_capture "$label" env LLLC_SELF_MAIN="$SELF_MAIN" "$LLLC" self "$@"
}

run_self_capture "check single file" check "$main_file"
rg -q '"ok":true' "$tmp_root/cmd.out" || fail "single-file check output mismatch"

run_self_capture "compile fs (single file)" compile --target fs "$main_file"
rg -q 'module Smoke|let add' "$tmp_root/cmd.out" || fail "single-file fs compile output mismatch"

run_self_capture "compile ts (single file)" compile --target ts "$main_file"
rg -q 'function|const|type' "$tmp_root/cmd.out" || fail "single-file ts compile output mismatch"

project_name="sampleapp"
(
  cd "$tmp_root"
  run_capture "new project" "$LLLC" new "$project_name"
)
project_dir="$tmp_root/$project_name"
[[ -f "$project_dir/lll.toml" ]] || fail "new did not create lll.toml"
[[ -f "$project_dir/src/Main.lll" ]] || fail "new did not create src/Main.lll"

(
  cd "$project_dir"
  mkdir -p ../dep/src
  cat > ../dep/lll.toml <<'TOML'
[project]
name = "dep"
version = "0.1.0"
entry = "src/Main.lll"
TOML
  cat > ../dep/src/Main.lll <<'LLL'
module Dep.Main
val() = 1
LLL

  run_capture "mod add" "$LLLC" mod add dep=path:../dep
  rg -q 'Installed [0-9]+ dependencies into vendor/|\"ok\":true' "$tmp_root/cmd.out" || fail "mod add output mismatch"

  run_capture "mod why" "$LLLC" mod why dep
  rg -q 'dependency chain:|\"ok\":true|\"dep\"' "$tmp_root/cmd.out" || fail "mod why output mismatch"

  run_capture "mod tidy" "$LLLC" mod tidy
  rg -q 'Installed [0-9]+ dependencies into vendor/|\"ok\":true' "$tmp_root/cmd.out" || fail "mod tidy output mismatch"

  run_capture "install" "$LLLC" install
  rg -q 'Installed [0-9]+ dependencies into vendor/|\"ok\":true' "$tmp_root/cmd.out" || fail "install output mismatch"

  run_capture "check project" "$LLLC" check .
  rg -q 'Checked project|\"ok\":true|\"stage\":\"ok\"' "$tmp_root/cmd.out" || fail "project check output mismatch"

  run_capture "build project fs" "$LLLC" build --target fs .
  rg -q 'Built project|\"ok\":true' "$tmp_root/cmd.out" || fail "project build output mismatch"
  [[ -f "$project_dir/bin/fsharp/sampleapp.fsproj" || -f "$project_dir/bin/fsharp/sample.fsproj" || -f "$project_dir/bin/fsharp/sampleapp.fs" || -f "$project_dir/bin/fsharp/sample.fs" ]] || fail "project build artifacts missing"
)

python3 - "$LLLC" "$SELF_MAIN" <<'PY'
import json
import os
import subprocess
import sys

lllc = sys.argv[1]
self_main = sys.argv[2]
env = os.environ.copy()
env["LLLC_SELF_MAIN"] = self_main
p = subprocess.Popen(
    [lllc, "mcp"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=True,
    env=env,
)

wire = "\n".join(
    [
        json.dumps({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-03-26", "capabilities": {}, "clientInfo": {"name": "ci", "version": "1"}}}),
        json.dumps({"jsonrpc": "2.0", "method": "initialized", "params": {}}),
        json.dumps({"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}}),
    ]
) + "\n"

stdout = ""
stderr = ""
try:
    stdout, stderr = p.communicate(wire, timeout=90)
except subprocess.TimeoutExpired as ex:
    p.kill()
    out, err = p.communicate()
    stdout = (ex.stdout or "") + (out or "")
    stderr = (ex.stderr or "") + (err or "")

ok_init = False
ok_tools = False
for line in stdout.splitlines():
    line = line.strip()
    if not line:
        continue
    try:
        obj = json.loads(line)
    except Exception:
        continue
    if obj.get("id") == 1 and "result" in obj:
        ok_init = True
    if obj.get("id") == 2 and "result" in obj and isinstance(obj["result"].get("tools"), list):
        ok_tools = True
if not (ok_init and ok_tools):
    if stderr.strip():
        print(stderr, file=sys.stderr)
    raise SystemExit("MCP handshake/tools-list failed")
PY

echo "check-selfhost-e2e: OK"
