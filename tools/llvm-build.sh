#!/usr/bin/env bash
# tools/llvm-build.sh <file.lll> [output-binary]
#
# End-to-end MVP pipeline: .lll source -> native executable.
#   1. lllc build --target llvm  (emit .ll)
#   2. llvm-add-declares.py      (patch in missing `declare` lines)
#   3. make runtime              (build lllc_runtime.o — cached)
#   4. clang link                (native binary)
#
# Requires: dotnet (to run lllc.dll), python3, clang, make.

set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <file.lll> [output-binary]" >&2
  exit 2
fi

SRC="$1"
if [[ ! -f "$SRC" ]]; then
  echo "error: source not found: $SRC" >&2
  exit 1
fi

# Resolve paths up front so the script works from any cwd.
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC_ABS="$(cd "$(dirname "$SRC")" && pwd)/$(basename "$SRC")"
BASENAME="$(basename "$SRC_ABS" .lll)"
DIR="$(dirname "$SRC_ABS")"
OUT="${2:-${DIR}/${BASENAME}}"

LLLC_DLL="$REPO_ROOT/initial-compiler/src/LLLangTool/bin/Debug/net10.0/lllc.dll"
RUNTIME_DIR="$REPO_ROOT/sdks/Platform.LLVM.SDK/runtime"
RUNTIME_OBJ="$RUNTIME_DIR/lllc_runtime.o"
PATCH_SCRIPT="$REPO_ROOT/tools/llvm-add-declares.py"

# --- Tool presence checks -------------------------------------------------
for tool in clang python3 dotnet make; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "error: required tool '$tool' not found in PATH" >&2
    exit 1
  fi
done

if [[ ! -f "$LLLC_DLL" ]]; then
  echo "error: lllc.dll not built at $LLLC_DLL" >&2
  echo "hint: run 'dotnet build initial-compiler' first" >&2
  exit 1
fi

# --- Step 1: .lll -> .ll --------------------------------------------------
echo "[1/4] lllc build --target llvm $SRC_ABS"
dotnet "$LLLC_DLL" build --target llvm "$SRC_ABS" >/dev/null

LL_FILE="${DIR}/${BASENAME}.ll"
PATCHED_LL="${DIR}/${BASENAME}.patched.ll"

if [[ ! -f "$LL_FILE" ]]; then
  echo "error: expected $LL_FILE was not produced by lllc" >&2
  exit 1
fi

# --- Step 2: patch in missing `declare` lines ----------------------------
echo "[2/4] patching missing declares -> $PATCHED_LL"
python3 "$PATCH_SCRIPT" "$LL_FILE" > "$PATCHED_LL"

# --- Step 3: build (or reuse) C runtime object ---------------------------
echo "[3/4] building runtime ($RUNTIME_OBJ)"
make -C "$RUNTIME_DIR" -s lllc_runtime.o

# --- Step 4: link with clang ---------------------------------------------
echo "[4/4] clang -> $OUT"
clang -o "$OUT" "$PATCHED_LL" "$RUNTIME_OBJ"

echo "Built: $OUT"
