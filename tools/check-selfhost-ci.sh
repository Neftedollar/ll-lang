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
  "$ROOT_DIR/spec/examples/valid/30-file-io-external.lll"
  "$ROOT_DIR/spec/examples/valid/hello.lll"
)

for file in "${FILES[@]}"; do
  [[ -f "$file" ]] || fail "missing file in self-host suite: $file"
  check_file "$file"
done

echo "check-selfhost-ci: OK (${#FILES[@]} files)"
