#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if rg -n "FS3261" src tests >/tmp/lllang-fs3261-check.out 2>/dev/null; then
  echo "FS3261 suppression/usage detected; keep nullable warnings fixed instead of suppressing."
  cat /tmp/lllang-fs3261-check.out
  exit 1
fi

echo "No FS3261 suppression found."
