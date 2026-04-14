#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

status=0

check_pattern() {
  local pattern="$1"
  local label="$2"
  local tmp
  tmp="$(mktemp)"

  if rg -n --no-heading --color=never \
    --glob '!docs/superpowers/specs/**' \
    --glob '!docs/compiler-dev/fixpoint-snapshots/**' \
    --glob '!**/bin/**' \
    --glob '!**/obj/**' \
    -e "${pattern}" \
    README.md docs spec >"${tmp}"; then
    echo "DOC CONTRACT VIOLATION: ${label}"
    cat "${tmp}"
    echo
    status=1
  fi

  rm -f "${tmp}"
}

check_pattern '\.ll-deps\b' "replace legacy dependency path .ll-deps with vendor/ + ll.sum"
check_pattern '\binstall_package\b|\blist_targets\b' "remove stale MCP tool names"
check_pattern '\b12\s+keywords\b|\b12-keyword\b' "keyword count drift (language no longer 12-keyword)"
check_pattern '\b`fn`,\s*`type`,\s*`let`\b' "outdated phrasing: let is now a keyword"
check_pattern 'supports three compilation targets' "outdated target matrix wording (ll-lang supports more than 3 targets)"
check_pattern '\b[0-9]{2,4}\s+tests?\b' "avoid hardcoded test counts (refer to CI instead)"

if [[ "${status}" -ne 0 ]]; then
  exit 1
fi

echo "Doc contract check passed."
