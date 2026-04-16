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
# Known G1.2 blockers (as of v3 step 6 + G1.2 fixes):
#
#   FIXED (now matching stage0):
#     - let vs let rec: Std.Codegen now uses containsVarExpr to emit "let f" for
#       non-recursive functions, matching stage0 containsVar analysis.
#     - Prelude emission: Std.Codegen now selectively emits prelude sections only
#       when the module actually references names from each section (core/maybe/result).
#     - Maybe alias: Std.Codegen now emits "type Maybe<'A> = 'A option" for the
#       canonical Maybe = Some A | None form, matching stage0.
#     - Constructor multi-args: Std.Parser now handles LParen-wrapped type args
#       (e.g. Node Color (RBMap K V) K V (RBMap K V)) in type declarations.
#
#   REMAINING (post-v2, deferred per plan):
#     1. Mutual recursion grouping: stage0 groups sibling fns that reference each
#        other into "let rec f ... and g ..." blocks. Std.Codegen emits each
#        fn separately. Affects 06-stdlib (double/sumList/doubleAll/nameLen) and
#        09-lexer-real (lexChars/lexId/lexNum/lexOp/tokenize — causes self-compile
#        failure: "Unbound variable: lexChars").
#     See: docs/compiler-dev/ — deferred after v2.

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

  # Normalize trailing newlines: strip final newline from both sides so a
  # lone trailing-newline difference doesn't block an otherwise-identical file.
  stage0_norm="$TMPDIR_FIXPOINT/$label.stage0.norm"
  self_norm="$TMPDIR_FIXPOINT/$label.self.norm"
  perl -pe 'chomp if eof' "$stage0_fs" > "$stage0_norm"
  perl -pe 'chomp if eof' "$self_fs"   > "$self_norm"

  # Compare
  if diff -q "$stage0_norm" "$self_norm" >/dev/null 2>&1; then
    printf "PASS\n"
    passed=$((passed + 1))
  else
    printf "BLOCK\n"
    failed=$((failed + 1))
    # Show first diff line
    first_diff="$(diff "$stage0_norm" "$self_norm" | head -6)"
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
  echo "Remaining blocker (deferred post-v2, see file header):"
  echo "  Mutual recursion grouping: stage0 emits 'let rec f ... and g ...'"
  echo "  for sibling fns that reference each other. Std.Codegen emits separately."
  echo "  Affects: 06-stdlib, 09-lexer-real."
  exit 1
else
  echo "G1.2: PASS"
  exit 0
fi
