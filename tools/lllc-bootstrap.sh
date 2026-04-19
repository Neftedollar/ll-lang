#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALLER="${ROOT_DIR}/tools/bootstrap-self.sh"

usage() {
  cat <<'EOF'
Usage:
  tools/lllc-bootstrap.sh <lllcself-command> [args...]

Examples:
  tools/lllc-bootstrap.sh check /abs/path/to/file.lll
  tools/lllc-bootstrap.sh compile /abs/path/to/file.lll
  tools/lllc-bootstrap.sh mcp

Behavior:
  - Uses pinned bootstrap artifact via tools/bootstrap-self.sh.
  - No fallback to stage0 or dotnet-run bridge.
  - Fails hard if bootstrap binary cannot be resolved.

Environment:
  LLLC_BOOTSTRAP_AUTO_INSTALL=0   disable auto-install (default: 1)
  LLLC_BOOTSTRAP_REINSTALL=1      force reinstall before run
  LLLC_BOOTSTRAP_SELF_PRESET=off|safe|all
                                  command routing preset (default: off)
                                  safe => compile/check route to `self`
                                  all  => compile/check/run route to `self`
  LLLC_BOOTSTRAP_SELF_COMMANDS=compile,check,run
                                  explicit command list override
  LLLC_BOOTSTRAP_SELF_VERBOSE=1   print routing decision to stderr
EOF
}

fail() {
  echo "lllc-bootstrap: $*" >&2
  exit 1
}

[[ -x "$INSTALLER" ]] || fail "missing installer: $INSTALLER"

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" || "${1:-}" == "help" || -z "${1:-}" ]]; then
  usage
  exit 0
fi

AUTO_INSTALL="${LLLC_BOOTSTRAP_AUTO_INSTALL:-1}"
REINSTALL="${LLLC_BOOTSTRAP_REINSTALL:-0}"
SELF_PRESET="${LLLC_BOOTSTRAP_SELF_PRESET:-off}"

case "$SELF_PRESET" in
  off|"")
    SELF_COMMANDS_DEFAULT=""
    ;;
  safe)
    SELF_COMMANDS_DEFAULT="compile,check"
    ;;
  all)
    SELF_COMMANDS_DEFAULT="compile,check,run"
    ;;
  *)
    fail "invalid LLLC_BOOTSTRAP_SELF_PRESET='$SELF_PRESET' (expected: off|safe|all)"
    ;;
esac

SELF_COMMANDS="${LLLC_BOOTSTRAP_SELF_COMMANDS:-$SELF_COMMANDS_DEFAULT}"
SELF_VERBOSE="${LLLC_BOOTSTRAP_SELF_VERBOSE:-0}"

should_route_to_self() {
  local cmd="$1"
  [[ -n "$SELF_COMMANDS" ]] || return 1
  [[ ",$SELF_COMMANDS," == *",$cmd,"* ]]
}

if [[ "$AUTO_INSTALL" == "1" ]]; then
  if [[ "$REINSTALL" == "1" ]]; then
    "$INSTALLER" install --reinstall >/dev/null
  else
    "$INSTALLER" install >/dev/null
  fi
fi

BOOTSTRAP_BIN="$("$INSTALLER" path)"
[[ -x "$BOOTSTRAP_BIN" ]] || fail "resolved bootstrap binary is not executable: $BOOTSTRAP_BIN"

if [[ "${1:-}" != "self" ]] && should_route_to_self "${1:-}"; then
  if [[ "$SELF_VERBOSE" == "1" ]]; then
    echo "lllc-bootstrap: routing '${1}' to self-hosted path" >&2
  fi
  set -- self "$@"
fi

exec "$BOOTSTRAP_BIN" "$@"
