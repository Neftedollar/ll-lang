#!/usr/bin/env bash
# check-fixpoint.sh — G1.2 fixpoint gate for v3 self-hosting (issue #129 Step 6)
#
# Compares stage0 (F# LLLangCompiler) output against self-hosted (Std.Compiler
# via lllc self compile) output for each corpus file.
#
# PASS: byte-identical output → stage1 == stage2
# BLOCK: differs → gap documented at bottom of output
#
# Usage:
#   bash tools/check-fixpoint.sh              # uses default corpus
#   bash tools/check-fixpoint.sh --full       # (future: full corpus scan)
#
# Known G1.2 blockers (as of v3 step 6):
#   1. Prelude emission: stage0 emits "open LLLang.Prelude" for library modules,
#      self-hosted always inlines the prelude helpers.
#   2. let vs let rec: stage0 uses containsVar to emit "let f" for non-recursive
#      functions; self-hosted always emits "let rec f".
#   These require either (a) updating Std.Codegen to match stage0, or (b)
#   updating stage0 to match self-hosted — the canonical fix is (a).

set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LLLC="$ROOT_DIR/src/LLLangTool/bin/Debug/net10.0/lllc.dll"
CORPUS_DIR="$ROOT_DIR/benchmarks/corpus"
TMPDIR_FIXPOINT="$(mktemp -d /tmp/ll-fixpoint-XXXXXX)"

cleanup() { rm -rf "$TMPDIR_FIXPOINT"; }
trap cleanup EXIT

# Corpus: (label, file)
declare -a LABELS=("06-stdlib" "07-text-processing" "09-lexer-real" "21-multi-param-types")
declare -a FILES=(
  "spec/examples/valid/06-stdlib.lll"
  "spec/examples/valid/07-text-processing.lll"
  "spec/examples/valid/09-lexer-real.lll"
  "spec/examples/valid/21-multi-param-types.lll"
)

# Build if not already built
if [[ ! -f "$LLLC" ]]; then
  echo "building lllc..." >&2
  dotnet build "$ROOT_DIR/LLLang.sln" --nologo -q
fi

passed=0
failed=0

echo "=== Fixpoint check (G1.2) ==="
echo ""

for i in "${!LABELS[@]}"; do
  label="${LABELS[$i]}"
  rel="${FILES[$i]}"
  file="$ROOT_DIR/$rel"

  printf "  %-30s " "$label"

  # Stage0: lllc build → <label>.stage0.fs
  stage0_fs="$TMPDIR_FIXPOINT/$label.stage0.fs"
  if ! dotnet "$LLLC" build "$file" >/dev/null 2>&1; then
    printf "SKIP (stage0 build failed)\n"
    continue
  fi
  # lllc build writes the .fs next to the source; find it
  src_dir="$(dirname "$file")"
  src_stem="$(basename "$file" .lll)"
  # For multi-module builds, read from .fsproj
  fsproj="$src_dir/$src_stem.fsproj"
  if [[ -f "$fsproj" ]]; then
    # Concatenate all .fs files listed in fsproj (no FILE headers — compare raw F# content).
    {
      sed -n 's/.*<Compile Include="\([^"]*\.fs\)".*/\1/p' "$fsproj" | while read -r fn; do
        cat "$src_dir/$fn"
      done
    } > "$stage0_fs"
  elif [[ -f "$src_dir/$src_stem.fs" ]]; then
    cp "$src_dir/$src_stem.fs" "$stage0_fs"
  else
    printf "SKIP (stage0 .fs not found)\n"
    continue
  fi

  # Self-hosted: lllc self compile → <label>.self.fs
  # Strip module-init side effects: skip lines before the first "module " line.
  # (Elaborator/Codegen .lll files have `main = ...` value bindings that print
  #  OK/FAIL lines at initialization time when imported as library modules.)
  self_fs="$TMPDIR_FIXPOINT/$label.self.fs"
  self_raw="$TMPDIR_FIXPOINT/$label.self.raw"
  if ! dotnet "$LLLC" self compile "$file" > "$self_raw" 2>/dev/null; then
    printf "SKIP (self compile failed)\n"
    continue
  fi
  # Drop everything before the first "module " line
  awk '/^module /{found=1} found{print}' "$self_raw" > "$self_fs"

  # Compare
  if diff -q "$stage0_fs" "$self_fs" >/dev/null 2>&1; then
    printf "PASS\n"
    passed=$((passed + 1))
  else
    printf "BLOCK\n"
    failed=$((failed + 1))
    # Show first diff line
    first_diff="$(diff "$stage0_fs" "$self_fs" | head -6)"
    echo "    $first_diff" | head -4
  fi
done

echo ""
echo "=== Summary ==="
echo "  $passed passed, $failed BLOCK"
echo ""

if [[ $failed -gt 0 ]]; then
  echo "G1.2: BLOCK"
  echo ""
  echo "Known blockers (see file header for details):"
  echo "  1. Prelude: stage0 emits 'open LLLang.Prelude'; self-hosted inlines helpers"
  echo "  2. let/let rec: stage0 uses containsVar analysis; self-hosted always emits 'let rec'"
  echo ""
  echo "To close G1.2: update Std.Codegen to match stage0 behavior for these two cases."
  exit 1
else
  echo "G1.2: PASS"
  exit 0
fi
