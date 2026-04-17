#!/usr/bin/env bash
# tools/llvm-build.sh <file.lll> [output-binary]
#
# End-to-end MVP pipeline: .lll source -> native executable.
#   1. lllc build --target llvm  (emit .ll)
#   2. llvm-add-declares.py      (patch in missing `declare` lines)
#   3. make runtime              (build lllc_runtime.o — cached)
#   4. clang link                (native binary)
#
# Requires: python3, clang, make, plus one of:
#   - `lllc` on PATH (installed via `dotnet tool install -g lllc`
#     or `npm install -g @neftedollar/lllc`)
#   - bootstrap `lllc.dll` at initial-compiler/src/LLLangTool/bin/Debug/net10.0/
#     (requires `dotnet` and a prior `dotnet build initial-compiler`)

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
for tool in clang python3 make; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "error: required tool '$tool' not found in PATH" >&2
    exit 1
  fi
done

# --- Resolve how to invoke lllc ------------------------------------------
# Priority:
#   1. `lllc` on PATH that understands `build --target llvm`
#      (bootstrap `lllc` from `dotnet tool install -g lllc` v1.x,
#       or `@neftedollar/lllc` from npm with native binary installed)
#   2. bootstrap lllc.dll from this repo (dev path)
#   3. fail with install hint
#
# NOTE: the currently-published NuGet `lllc` ships the self-hosted
#       `lllcself` CLI (commands: compile|run|check|...) which does NOT
#       support `build --target llvm`. We probe for its banner and skip
#       past it so the bootstrap fallback can take over.
lllc_supports_llvm_build() {
  # Heuristic: the bootstrap CLI prints "Usage:" when invoked with no args
  # and lists `--target ... llvm`. The self-hosted `lllcself` prints
  # "usage: lllcself ..." and has no `build` subcommand.
  local banner
  banner="$("$1" 2>&1 </dev/null | head -1 || true)"
  case "$banner" in
    *lllcself*) return 1 ;;  # self-hosted — no `build --target llvm`
    Usage:*)    return 0 ;;  # bootstrap-style CLI
    *)          return 1 ;;  # unknown — be conservative
  esac
}

LLLC_CMD=()
LLLC_ON_PATH=""
if command -v lllc >/dev/null 2>&1; then
  LLLC_ON_PATH="$(command -v lllc)"
fi

if [[ -n "$LLLC_ON_PATH" ]] && lllc_supports_llvm_build "$LLLC_ON_PATH"; then
  LLLC_CMD=(lllc)
  LLLC_SOURCE="lllc on PATH ($LLLC_ON_PATH)"
elif [[ -f "$LLLC_DLL" ]]; then
  if [[ -n "$LLLC_ON_PATH" ]]; then
    echo "note: $LLLC_ON_PATH does not support 'build --target llvm'; falling back to bootstrap lllc.dll" >&2
  fi
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: found bootstrap lllc.dll but 'dotnet' is not on PATH" >&2
    echo "hint: install the .NET 10 SDK, or install lllc globally:" >&2
    echo "      dotnet tool install -g lllc" >&2
    echo "      npm install -g @neftedollar/lllc" >&2
    exit 1
  fi
  LLLC_CMD=(dotnet "$LLLC_DLL")
  LLLC_SOURCE="bootstrap lllc.dll ($LLLC_DLL)"
else
  echo "error: lllc not found." >&2
  echo "" >&2
  echo "Install one of:" >&2
  echo "  dotnet tool install -g lllc          # .NET global tool" >&2
  echo "  npm install -g @neftedollar/lllc     # Node / Bun global" >&2
  echo "" >&2
  echo "Or (dev) build the bootstrap compiler in this repo:" >&2
  echo "  dotnet build initial-compiler/src/LLLangTool/LLLangTool.fsproj -c Debug" >&2
  exit 1
fi

echo "[lllc] using: $LLLC_SOURCE"

# --- Sanity-check repo-local assets (runtime + post-processor) ----------
if [[ ! -f "$PATCH_SCRIPT" ]]; then
  echo "error: post-processor not found: $PATCH_SCRIPT" >&2
  echo "hint: the native pipeline needs tools/llvm-add-declares.py from the ll-lang repo." >&2
  echo "      clone: git clone https://github.com/Neftedollar/ll-lang" >&2
  exit 1
fi
if [[ ! -f "$RUNTIME_DIR/lllc_runtime.c" ]] || [[ ! -f "$RUNTIME_DIR/Makefile" ]]; then
  echo "error: C runtime sources not found under: $RUNTIME_DIR" >&2
  echo "hint: the native pipeline needs sdks/Platform.LLVM.SDK/runtime/ from the ll-lang repo." >&2
  echo "      clone: git clone https://github.com/Neftedollar/ll-lang" >&2
  exit 1
fi

# --- Step 1: .lll -> .ll --------------------------------------------------
echo "[1/4] lllc build --target llvm $SRC_ABS"
"${LLLC_CMD[@]}" build --target llvm "$SRC_ABS" >/dev/null

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
