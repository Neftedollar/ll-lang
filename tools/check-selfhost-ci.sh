#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALLER="$ROOT_DIR/tools/bootstrap-self.sh"

fail() {
  echo "check-selfhost-ci: $*" >&2
  exit 1
}

[[ -x "$INSTALLER" ]] || fail "missing installer: $INSTALLER"

if [[ "${LLLC_BOOTSTRAP_REINSTALL:-0}" == "1" ]]; then
  "$INSTALLER" install --reinstall >/dev/null
else
  "$INSTALLER" install >/dev/null
fi

BOOTSTRAP_BIN="$("$INSTALLER" path)"
[[ -x "$BOOTSTRAP_BIN" ]] || fail "resolved bootstrap binary is not executable: $BOOTSTRAP_BIN"
SELF_MAIN="$ROOT_DIR/lllcself/src/Main.lll"
[[ -f "$SELF_MAIN" ]] || fail "missing self main file: $SELF_MAIN"

check_file() {
  local file="$1"
  local output

  echo "==> check $file"
  output="$(LLLC_SELF_MAIN="$SELF_MAIN" "$BOOTSTRAP_BIN" self check "$file" 2>&1 || true)"
  printf '%s\n' "$output"

  if ! printf '%s' "$output" | grep -q '"ok":true'; then
    fail "compiler reported non-ok result for $file"
  fi
}

check_invalid_file() {
  local file="$1"
  local expected_code="$2"
  local e031_op="${3:-+}"
  local e031_left_src="${4:-FxConflictA}"
  local e031_right_src="${5:-FxConflictB}"
  local e031_left_fixity="${6:-left,6}"
  local e031_right_fixity="${7:-right,7}"
  local output

  echo "==> check invalid $file (expect $expected_code)"
  output="$(LLLC_SELF_MAIN="$SELF_MAIN" "$BOOTSTRAP_BIN" self check "$file" 2>&1 || true)"
  printf '%s\n' "$output"

  printf '%s' "$output" | grep -q '"ok":false' || fail "invalid fixture unexpectedly succeeded: $file"
  printf '%s' "$output" | grep -q "\"primary_error\":\"$expected_code " || fail "invalid fixture missing expected code $expected_code: $file"
  printf '%s' "$output" | grep -q '"secondary_count":0' || fail "invalid fixture produced non-deterministic secondary diagnostics: $file"
  if [[ "$expected_code" == "E031" ]]; then
    printf '%s' "$output" | grep -Fq "FixityConflict op:${e031_op}" || fail "E031 missing operator context (${e031_op}): $file"
    printf '%s' "$output" | grep -Fq "${e031_left_src}" || fail "E031 missing source module ${e031_left_src}: $file"
    printf '%s' "$output" | grep -Fq "${e031_right_src}" || fail "E031 missing source module ${e031_right_src}: $file"
    printf '%s' "$output" | grep -Fq "(${e031_left_fixity})" || fail "E031 missing competing fixity (${e031_left_fixity}): $file"
    printf '%s' "$output" | grep -Fq "(${e031_right_fixity})" || fail "E031 missing competing fixity (${e031_right_fixity}): $file"
    printf '%s' "$output" | grep -Fq "sources:${e031_left_src}(${e031_left_fixity}) vs ${e031_right_src}(${e031_right_fixity})" || fail "E031 is not deterministic/canonical: $file"
  fi
}

FILES=(
  "$ROOT_DIR/lllcself/src/Main.lll"
  "$ROOT_DIR/lllcself/src/Mcp.lll"
  "$ROOT_DIR/spec/examples/valid/01-basics.lll"
  "$ROOT_DIR/spec/examples/valid/02-adts.lll"
  "$ROOT_DIR/spec/examples/valid/03-tags.lll"
  "$ROOT_DIR/spec/examples/valid/04-traits.lll"
  "$ROOT_DIR/spec/examples/valid/06-stdlib.lll"
  "$ROOT_DIR/spec/examples/valid/07-text-processing.lll"
  "$ROOT_DIR/spec/examples/valid/08-lexer-poc.lll"
  "$ROOT_DIR/spec/examples/valid/10-multiline-sum.lll"
  "$ROOT_DIR/spec/examples/valid/16-elaborator-real.lll"
  "$ROOT_DIR/spec/examples/valid/18-hminfer-real.lll"
  "$ROOT_DIR/spec/examples/valid/19-codegen-real.lll"
  "$ROOT_DIR/spec/examples/valid/20a-bootstrap-input.lll"
  "$ROOT_DIR/spec/examples/valid/20b-bootstrap-input-maybe.lll"
  "$ROOT_DIR/spec/examples/valid/20c-bootstrap-input-stdlib.lll"
  "$ROOT_DIR/spec/examples/valid/20d-bootstrap-input-eqeq.lll"
  "$ROOT_DIR/spec/examples/valid/20e-bootstrap-input-ltgt.lll"
  "$ROOT_DIR/spec/examples/valid/20f-bootstrap-input-bool-lits.lll"
  "$ROOT_DIR/spec/examples/valid/20g-bootstrap-input-char-lit.lll"
  "$ROOT_DIR/spec/examples/valid/20h-bootstrap-input-char-esc.lll"
  "$ROOT_DIR/spec/examples/valid/20i-bootstrap-input-ctor-pat.lll"
  "$ROOT_DIR/spec/examples/valid/20j-bootstrap-input-layout.lll"
  "$ROOT_DIR/spec/examples/valid/20k-bootstrap-input-str-esc.lll"
  "$ROOT_DIR/spec/examples/valid/20l-bootstrap-input-comments.lll"
  "$ROOT_DIR/spec/examples/valid/20m-bootstrap-input-type-layout.lll"
  "$ROOT_DIR/spec/examples/valid/20n-bootstrap-input-stdlib-full.lll"
  "$ROOT_DIR/spec/examples/valid/20o-bootstrap-input-multifn.lll"
  "$ROOT_DIR/spec/examples/valid/20p-bootstrap-input-digit.lll"
  "$ROOT_DIR/spec/examples/valid/20r-bootstrap-input-lists.lll"
  "$ROOT_DIR/spec/examples/valid/20s-bootstrap-input-arm-if.lll"
  "$ROOT_DIR/spec/examples/valid/20t-bootstrap-input-strtoint.lll"
  "$ROOT_DIR/spec/examples/valid/20u-bootstrap-input-str-pat.lll"
  "$ROOT_DIR/spec/examples/valid/20v-bootstrap-input-strchars.lll"
  "$ROOT_DIR/spec/examples/valid/20w-bootstrap-input-clause.lll"
  "$ROOT_DIR/spec/examples/valid/20x-bootstrap-input-tuple.lll"
  "$ROOT_DIR/spec/examples/valid/20y-bootstrap-input-fmt.lll"
  "$ROOT_DIR/spec/examples/valid/20y-bootstrap-input-mutrec.lll"
  "$ROOT_DIR/spec/examples/valid/20y-bootstrap-input-prelude.lll"
  "$ROOT_DIR/spec/examples/valid/20z-bootstrap-input-unknown-char.lll"
  "$ROOT_DIR/spec/examples/valid/20z-bootstrap-input-unterminated-char.lll"
  "$ROOT_DIR/spec/examples/valid/21-multi-param-types.lll"
  "$ROOT_DIR/spec/examples/valid/23-external-opaque.lll"
  "$ROOT_DIR/spec/examples/valid/26-operators-precedence.lll"
  "$ROOT_DIR/spec/examples/valid/27-fixity-decls.lll"
  "$ROOT_DIR/spec/examples/valid/28-custom-symbolic-fixity.lll"
  "$ROOT_DIR/stdlib/src/Operators.lll"
  "$ROOT_DIR/spec/examples/valid/30-file-io-external.lll"
  "$ROOT_DIR/spec/examples/valid/hello.lll"
)

for file in "${FILES[@]}"; do
  [[ -f "$file" ]] || fail "missing file in self-host suite: $file"
  check_file "$file"
done

INVALID_CASES=(
  "$ROOT_DIR/spec/examples/invalid/E027-invalid-fixity-assoc.lll|E027"
  "$ROOT_DIR/spec/examples/invalid/E028-invalid-fixity-precedence.lll|E028"
  "$ROOT_DIR/spec/examples/invalid/E029-duplicate-fixity.lll|E029"
  "$ROOT_DIR/spec/examples/invalid/E030-reserved-fixity-operator.lll|E030"
  "$ROOT_DIR/spec/examples/invalid/E030-custom-fixity-too-long.lll|E030"
  "$ROOT_DIR/spec/examples/invalid/E031-fixity-conflict-imports.lll|E031"
  "$ROOT_DIR/spec/examples/invalid/E031-fixity-conflict-imports-swapped.lll|E031"
  "$ROOT_DIR/spec/examples/invalid/E031-fixity-conflict-custom-imports.lll|E031|%%|FxCustomConflictA|FxCustomConflictB|left,6|right,7"
)

for case in "${INVALID_CASES[@]}"; do
  IFS='|' read -r file expected_code e031_op e031_left_src e031_right_src e031_left_fixity e031_right_fixity <<<"$case"
  [[ -f "$file" ]] || fail "missing invalid fixture in self-host suite: $file"
  check_invalid_file "$file" "$expected_code" "$e031_op" "$e031_left_src" "$e031_right_src" "$e031_left_fixity" "$e031_right_fixity"
done

echo "check-selfhost-ci: OK (${#FILES[@]} valid files, ${#INVALID_CASES[@]} invalid files)"
