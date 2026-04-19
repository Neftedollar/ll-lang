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

if [[ "$AUTO_INSTALL" == "1" ]]; then
  if [[ "$REINSTALL" == "1" ]]; then
    "$INSTALLER" install --reinstall >/dev/null
  else
    "$INSTALLER" install >/dev/null
  fi
fi

BOOTSTRAP_BIN="$("$INSTALLER" path)"
[[ -x "$BOOTSTRAP_BIN" ]] || fail "resolved bootstrap binary is not executable: $BOOTSTRAP_BIN"

exec "$BOOTSTRAP_BIN" "$@"
