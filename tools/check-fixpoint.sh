#!/usr/bin/env bash
# tools/check-fixpoint.sh
#
# Fixpoint / parity test: compare stage0 compiler vs self-hosted compiler on
# the same corpus .lll file.
#
# ── What this tests ──────────────────────────────────────────────────────────
#
#   Both compilers process the same .lll source and must produce F# output
#   that agrees on structural properties:
#     • module declaration present and identical
#     • same set of top-level binding names (let pi, let add, let double, …)
#
# ── Why byte-identical output is NOT yet achievable ──────────────────────────
#
#   Stage0 (lllc build --target fs):
#     - No prelude injected for library files (no `main`)
#     - Functions emitted as `let f a b = ...`  (non-recursive by default)
#     - Integer literals as `2L` (boxed)
#
#   Self-hosted (Std.Compiler.compile via SelfHostedRun):
#     - Always injects the stdlib prelude header
#     - Functions emitted as `let rec f a b = ...`  (Codegen always uses `rec`)
#     - Same integer literal encoding
#
#   Closing this gap requires aligning Std.Codegen with stage0 F# emission
#   rules (omit prelude for library files, drop `rec` when not needed).
#   That is deeper M5 / M6 work.
#
# ── Test approach ────────────────────────────────────────────────────────────
#
#   1. Run stage0:        lllc build --target fs corpus.lll  → stage0.fs
#   2. Run self-hosted:   lllc run tools/run-selfhosted-compile.lll → selfhosted.fs
#   3. Extract binding names from both outputs (lines starting with `let`)
#   4. Compare name sets: PASS if identical, FAIL otherwise
#   5. Verify module header matches in both outputs
#
# Usage:
#   bash tools/check-fixpoint.sh [corpus-file.lll]
#   Default corpus: spec/examples/valid/01-basics.lll
#
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LLLC="$REPO_ROOT/src/LLLangTool/bin/Debug/net10.0/lllc.dll"
CORPUS="${1:-$REPO_ROOT/spec/examples/valid/01-basics.lll}"

TMPDIR_FP="$(mktemp -d)"
trap 'rm -rf "$TMPDIR_FP"' EXIT

STAGE0_FS="$TMPDIR_FP/stage0.fs"
SELFHOSTED_FS="$TMPDIR_FP/selfhosted.fs"
STAGE0_NAMES="$TMPDIR_FP/stage0-names.txt"
SELFHOSTED_NAMES="$TMPDIR_FP/selfhosted-names.txt"
DRIVER_LLL="$TMPDIR_FP/run-selfhosted.lll"

PASS=0
FAIL=0

pass() { echo "PASS  $1"; PASS=$((PASS + 1)); }
fail() { echo "FAIL  $1"; FAIL=$((FAIL + 1)); }

echo "=== ll-lang fixpoint/parity test ==="
echo "corpus: $CORPUS"
echo ""

# ── 1. Build with stage0 ─────────────────────────────────────────────────────

echo "--- stage0: lllc build --target fs ---"

# lllc build writes to a .fs file next to the source; use a tmp copy.
CORPUS_TMP="$TMPDIR_FP/corpus.lll"
cp "$CORPUS" "$CORPUS_TMP"

dotnet "$LLLC" build --target fs "$CORPUS_TMP" >/dev/null 2>&1
cp "$TMPDIR_FP/corpus.fs" "$STAGE0_FS"

if [[ ! -s "$STAGE0_FS" ]]; then
  echo "ERROR: stage0 produced empty output"
  exit 1
fi
echo "stage0 output: $(wc -l < "$STAGE0_FS") lines"

# ── 2. Build with self-hosted compiler ───────────────────────────────────────

echo "--- self-hosted: Std.SelfHostedRun.compileSingleFile ---"

# Write a small ll-lang driver that:
#   - Reads the corpus file with compileSingleFile (path embedded by shell)
#   - Prints the F# output to stdout
#
# Note: lllc run does not forward extra arguments to the compiled binary,
# so we embed the corpus path as a string literal in the driver source.
CORPUS_ABS="$(cd "$(dirname "$CORPUS")" && pwd)/$(basename "$CORPUS")"
# Escape any double-quotes or backslashes in the path for embedding in lll string
CORPUS_ESCAPED="${CORPUS_ABS//\\/\\\\}"
CORPUS_ESCAPED="${CORPUS_ESCAPED//\"/\\\"}"

cat > "$DRIVER_LLL" << DRIVER_EOF
module Fixpoint.Driver

import Std.SelfHostedRun

main =
  out = compileSingleFile "${CORPUS_ESCAPED}"
  printfn out
DRIVER_EOF

# Run from repo root so relative paths (stdlib/) resolve correctly
dotnet "$LLLC" run "$DRIVER_LLL" > "$SELFHOSTED_FS" 2>&1
EXIT_CODE=$?

if [[ $EXIT_CODE -ne 0 ]]; then
  echo "ERROR: self-hosted runner exited $EXIT_CODE"
  cat "$SELFHOSTED_FS"
  exit 1
fi

if [[ ! -s "$SELFHOSTED_FS" ]]; then
  echo "ERROR: self-hosted produced empty output"
  exit 1
fi
echo "self-hosted output: $(wc -l < "$SELFHOSTED_FS") lines"

echo ""

# ── 3. Check: module header present in both ──────────────────────────────────

STAGE0_MODULE=$(grep -m1 '^module ' "$STAGE0_FS" || true)
SELFHOSTED_MODULE=$(grep -m1 '^module ' "$SELFHOSTED_FS" || true)

if [[ -z "$STAGE0_MODULE" ]]; then
  fail "stage0 output has no 'module' declaration"
else
  pass "stage0 has module declaration: $STAGE0_MODULE"
fi

if [[ -z "$SELFHOSTED_MODULE" ]]; then
  fail "self-hosted output has no 'module' declaration"
else
  pass "self-hosted has module declaration: $SELFHOSTED_MODULE"
fi

if [[ "$STAGE0_MODULE" == "$SELFHOSTED_MODULE" ]]; then
  pass "module declarations are identical: $STAGE0_MODULE"
else
  fail "module declarations differ"
  echo "      stage0:      $STAGE0_MODULE"
  echo "      self-hosted: $SELFHOSTED_MODULE"
fi

# ── 4. Check: binding names match ────────────────────────────────────────────
#
# Extract top-level binding names from lines like:
#   let foo a b =        → foo
#   let rec foo a b =    → foo
#   let pi = ...         → pi
#
# We strip 'rec' so both formats normalise to the same name.

extract_names() {
  grep '^let ' "$1" \
    | sed 's/^let rec /let /' \
    | sed 's/^let \([a-zA-Z_][a-zA-Z0-9_]*\).*/\1/' \
    | sort -u
}

extract_names "$STAGE0_FS" > "$STAGE0_NAMES"
extract_names "$SELFHOSTED_FS" > "$SELFHOSTED_NAMES"

STAGE0_COUNT=$(wc -l < "$STAGE0_NAMES" | tr -d ' ')
SELFHOSTED_COUNT=$(wc -l < "$SELFHOSTED_NAMES" | tr -d ' ')

echo ""
echo "--- binding name comparison ---"
echo "stage0 bindings ($STAGE0_COUNT): $(tr '\n' ' ' < "$STAGE0_NAMES")"
echo "self-hosted bindings ($SELFHOSTED_COUNT): $(tr '\n' ' ' < "$SELFHOSTED_NAMES")"

# Prelude names: stage0 inlines these as `let` bindings; self-hosted uses
# `open LLLang.Prelude` so they don't appear as `let` lines.  Both are valid
# and structurally equivalent — exclude prelude names from the comparison.
PRELUDE_NAMES_RE='^(abs|absf|charIsAlpha|charIsDigit|charIsSpace|charToInt|exit|fileExists|floatToStr|getArgs|intToChar|intToStr|listAppend|listAt|listConcat|listFilter|listFold|listHead|listIsEmpty|listLen|listMap|listReverse|listTail|ll_dirList|ll_processRun|max|maybeBind|maybeMap|maybeWithDefault|min|print|printfn|readFile|sqrt|strChars|strConcat|strContains|strFromChars|strIndexOf|strLen|strReverse|strSlice|strSplit|strToFloat|strToInt|strTrim|writeFile)$'

# Names in stage0 but not self-hosted (after excluding known prelude names)
MISSING=$(comm -23 "$STAGE0_NAMES" "$SELFHOSTED_NAMES" | grep -v '^$' \
        | grep -vE "$PRELUDE_NAMES_RE" || true)
# Extra names in self-hosted beyond stage0 (after excluding known prelude names)
EXTRA=$(comm -13 "$STAGE0_NAMES" "$SELFHOSTED_NAMES" | grep -v '^$' \
        | grep -vE "$PRELUDE_NAMES_RE" || true)

if [[ -z "$MISSING" ]]; then
  pass "all stage0 user bindings are present in self-hosted output"
else
  fail "user bindings in stage0 but missing from self-hosted: $MISSING"
fi

if [[ -z "$EXTRA" ]]; then
  pass "no unexpected extra user bindings in self-hosted output"
else
  # Extra user bindings in self-hosted are informational (might be fine)
  echo "INFO  extra user bindings in self-hosted (beyond stage0): $EXTRA"
fi

# ── 5. Byte-identical check (expected to fail until deeper M5 alignment) ─────

echo ""
echo "--- byte-identical diff (expected to differ until M5 F# emission aligned) ---"
if diff -q "$STAGE0_FS" "$SELFHOSTED_FS" >/dev/null 2>&1; then
  pass "FULL FIXPOINT ACHIEVED: outputs are byte-identical"
else
  DIFF_LINES=$(diff "$STAGE0_FS" "$SELFHOSTED_FS" | wc -l | tr -d ' ' || true)
  echo "INFO  outputs differ by $DIFF_LINES diff lines (expected at this stage)"
  echo "INFO  gap: self-hosted injects prelude + uses 'let rec'; stage0 does not"
  echo "INFO  to close: align Std.Codegen prelude injection and rec emission with stage0"
  # Not a FAIL — this is a known, documented gap
fi

# ── Summary ───────────────────────────────────────────────────────────────────

echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
echo ""
if [[ $FAIL -eq 0 ]]; then
  echo "PASS — structural fixpoint parity confirmed."
  echo "       (Full byte-identical fixpoint awaits M5 Codegen alignment.)"
  exit 0
else
  echo "FAIL — $FAIL check(s) did not pass."
  exit 1
fi
