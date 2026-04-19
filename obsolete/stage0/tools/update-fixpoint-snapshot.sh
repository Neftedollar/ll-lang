#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
INPUT_FILE="$ROOT_DIR/spec/examples/valid/20a-bootstrap-input.lll"
BOOTSTRAP_SRC="$ROOT_DIR/spec/examples/valid/20-bootstrap-compiler.lll"
SNAPSHOT_FILE="$ROOT_DIR/docs/compiler-dev/fixpoint-snapshots/compiler1-latest.fs"
TOOL_DLL="$ROOT_DIR/obsolete/stage0/src/LLLangTool/bin/Debug/net10.0/lllc.dll"
BACKUP_FILE="${INPUT_FILE}.bak.snapshot"

if [[ ! -f "$BOOTSTRAP_SRC" ]]; then
  echo "missing bootstrap source: $BOOTSTRAP_SRC" >&2
  exit 1
fi

if [[ ! -f "$TOOL_DLL" ]]; then
  echo "lllc tool not built yet; building solution first..."
  dotnet build "$ROOT_DIR/obsolete/stage0/LLLang.sln" --nologo >/dev/null
fi

cp "$INPUT_FILE" "$BACKUP_FILE"
cleanup() {
  if [[ -f "$BACKUP_FILE" ]]; then
    mv -f "$BACKUP_FILE" "$INPUT_FILE"
  fi
}
trap cleanup EXIT

cp "$BOOTSTRAP_SRC" "$INPUT_FILE"
dotnet "$TOOL_DLL" run "$BOOTSTRAP_SRC" > "$SNAPSHOT_FILE"

# Normalize to LF and ensure exactly one trailing newline.
# shellcheck disable=SC2016
perl -0777 -e '$_ = <>; s/\r\n/\n/g; s/\r/\n/g; s/\n*\z/\n/s; print $_;' "$SNAPSHOT_FILE" > "${SNAPSHOT_FILE}.tmp"
mv -f "${SNAPSHOT_FILE}.tmp" "$SNAPSHOT_FILE"

line_count=$(wc -l < "$SNAPSHOT_FILE" | tr -d ' ')
byte_count=$(wc -c < "$SNAPSHOT_FILE" | tr -d ' ')
let_count=$(rg -n '^let ' "$SNAPSHOT_FILE" | wc -l | tr -d ' ')
and_count=$(rg -n '^and ' "$SNAPSHOT_FILE" | wc -l | tr -d ' ')

echo "updated snapshot: $SNAPSHOT_FILE"
echo "lines=$line_count bytes=$byte_count bindings=$((let_count + and_count))"
