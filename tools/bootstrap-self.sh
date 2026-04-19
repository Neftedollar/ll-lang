#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCK_FILE="${LLLC_BOOTSTRAP_LOCK:-$ROOT_DIR/bootstrap/lllc-bootstrap.lock.json}"
POLICY_FILE="${LLLC_BOOTSTRAP_POLICY:-$ROOT_DIR/bootstrap/policy.json}"
CACHE_ROOT="${LLLC_BOOTSTRAP_CACHE_DIR:-${XDG_CACHE_HOME:-$HOME/.cache}/lllc/bootstrap}"

usage() {
  cat <<'EOF'
Usage:
  tools/bootstrap-self.sh install [--reinstall]
  tools/bootstrap-self.sh verify
  tools/bootstrap-self.sh path

Environment overrides:
  LLLC_BOOTSTRAP_LOCK        Path to lock file (default: bootstrap/lllc-bootstrap.lock.json)
  LLLC_BOOTSTRAP_POLICY      Path to policy file (default: bootstrap/policy.json)
  LLLC_BOOTSTRAP_CACHE_DIR   Cache directory root (default: ~/.cache/lllc/bootstrap)
  LLLC_BOOTSTRAP_VERSION     Override version key from lock
  LLLC_BOOTSTRAP_URL         Override artifact URL
  LLLC_BOOTSTRAP_SHA256      Override artifact SHA256
  LLLC_BOOTSTRAP_ARCHIVE     Override archive type (tar.gz|zip|raw)
  LLLC_BOOTSTRAP_ENTRY       Override executable entry name
EOF
}

fail() {
  echo "bootstrap-self: $*" >&2
  exit 1
}

need_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "missing required command: $1"
}

need_cmd python3
need_cmd curl

json_get_required() {
  local file="$1"
  local key="$2"
  python3 - "$file" "$key" <<'PY'
import json
import sys

path = sys.argv[1]
key = sys.argv[2]
with open(path, "r", encoding="utf-8") as f:
    data = json.load(f)

cur = data
for part in key.split("."):
    if isinstance(cur, dict) and part in cur:
        cur = cur[part]
    else:
        raise SystemExit(2)

if isinstance(cur, (dict, list)):
    print(json.dumps(cur, separators=(",", ":")))
else:
    print(cur)
PY
}

json_get_optional() {
  local file="$1"
  local key="$2"
  python3 - "$file" "$key" <<'PY'
import json
import sys

path = sys.argv[1]
key = sys.argv[2]
with open(path, "r", encoding="utf-8") as f:
    data = json.load(f)

cur = data
for part in key.split("."):
    if isinstance(cur, dict) and part in cur:
        cur = cur[part]
    else:
        print("")
        raise SystemExit(0)

if isinstance(cur, (dict, list)):
    print(json.dumps(cur, separators=(",", ":")))
else:
    print(cur)
PY
}

sha256_file() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$file" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$file" | awk '{print $1}'
  elif command -v openssl >/dev/null 2>&1; then
    openssl dgst -sha256 "$file" | awk '{print $NF}'
  else
    fail "no sha256 tool available (need sha256sum, shasum, or openssl)"
  fi
}

normalize_arch() {
  local raw="$1"
  case "$raw" in
    x86_64|amd64) echo "x64" ;;
    arm64|aarch64) echo "arm64" ;;
    *) fail "unsupported architecture: $raw" ;;
  esac
}

normalize_os() {
  local raw="$1"
  case "$raw" in
    linux) echo "linux" ;;
    darwin) echo "osx" ;;
    msys*|mingw*|cygwin*|windows_nt) echo "win" ;;
    *) fail "unsupported os: $raw" ;;
  esac
}

compare_semver_gte() {
  local have="$1"
  local want="$2"
  python3 - "$have" "$want" <<'PY'
import re
import sys

def parse(v: str):
    m = re.match(r"^v?(\d+)\.(\d+)\.(\d+)$", v.strip())
    if not m:
        raise SystemExit(2)
    return tuple(int(x) for x in m.groups())

have = parse(sys.argv[1])
want = parse(sys.argv[2])
sys.exit(0 if have >= want else 1)
PY
}

lock_exists_or_fail() {
  [[ -f "$LOCK_FILE" ]] || fail "lock file not found: $LOCK_FILE"
  [[ -f "$POLICY_FILE" ]] || fail "policy file not found: $POLICY_FILE"
}

detect_platform_key() {
  local os arch
  os="$(normalize_os "$(uname -s | tr '[:upper:]' '[:lower:]')")"
  arch="$(normalize_arch "$(uname -m)")"
  echo "${os}-${arch}"
}

resolve_config() {
  lock_exists_or_fail

  local platform_key lock_schema policy_schema lock_version min_version
  platform_key="$(detect_platform_key)"
  lock_schema="$(json_get_required "$LOCK_FILE" "schema")"
  policy_schema="$(json_get_required "$POLICY_FILE" "schema")"
  lock_version="$(json_get_required "$LOCK_FILE" "version")"
  min_version="$(json_get_optional "$POLICY_FILE" "minimum_version")"

  [[ "$lock_schema" == "$policy_schema" ]] || fail "schema mismatch: lock=$lock_schema policy=$policy_schema"
  if [[ -n "$min_version" ]]; then
    if ! compare_semver_gte "$lock_version" "$min_version"; then
      fail "lock version $lock_version is below minimum $min_version"
    fi
  fi

  BOOTSTRAP_VERSION="${LLLC_BOOTSTRAP_VERSION:-$lock_version}"
  ARTIFACT_URL="${LLLC_BOOTSTRAP_URL:-$(json_get_required "$LOCK_FILE" "artifacts.${platform_key}.url")}"
  ARTIFACT_SHA256="${LLLC_BOOTSTRAP_SHA256:-$(json_get_required "$LOCK_FILE" "artifacts.${platform_key}.sha256")}"
  ARTIFACT_ARCHIVE="${LLLC_BOOTSTRAP_ARCHIVE:-$(json_get_optional "$LOCK_FILE" "artifacts.${platform_key}.archive")}"
  ENTRY_NAME="${LLLC_BOOTSTRAP_ENTRY:-$(json_get_optional "$LOCK_FILE" "artifacts.${platform_key}.entry")}"

  if [[ -z "$ARTIFACT_ARCHIVE" ]]; then
    case "$ARTIFACT_URL" in
      *.tar.gz|*.tgz) ARTIFACT_ARCHIVE="tar.gz" ;;
      *.zip) ARTIFACT_ARCHIVE="zip" ;;
      *) ARTIFACT_ARCHIVE="raw" ;;
    esac
  fi
  [[ -n "$ENTRY_NAME" ]] || ENTRY_NAME="lllc"

  INSTALL_DIR="${CACHE_ROOT}/${BOOTSTRAP_VERSION}/$(detect_platform_key)"
  DOWNLOAD_NAME="$(basename "$ARTIFACT_URL")"
  ARTIFACT_PATH="${INSTALL_DIR}/${DOWNLOAD_NAME}"
  EXTRACT_DIR="${INSTALL_DIR}/extracted"
  BIN_PATH="${INSTALL_DIR}/${ENTRY_NAME}"
  META_PATH="${INSTALL_DIR}/install.json"
}

verify_sha() {
  local actual
  actual="$(sha256_file "$ARTIFACT_PATH")"
  [[ "$actual" == "$ARTIFACT_SHA256" ]] || fail "sha256 mismatch for ${ARTIFACT_PATH}: expected=${ARTIFACT_SHA256} actual=${actual}"
}

ensure_archive_tools() {
  case "$ARTIFACT_ARCHIVE" in
    tar.gz) need_cmd tar ;;
    zip) need_cmd unzip ;;
    raw) ;;
    *) fail "unsupported archive format: $ARTIFACT_ARCHIVE" ;;
  esac
}

extract_artifact() {
  rm -rf "$EXTRACT_DIR"
  mkdir -p "$EXTRACT_DIR"

  case "$ARTIFACT_ARCHIVE" in
    tar.gz) tar -xzf "$ARTIFACT_PATH" -C "$EXTRACT_DIR" ;;
    zip) unzip -q "$ARTIFACT_PATH" -d "$EXTRACT_DIR" ;;
    raw) cp "$ARTIFACT_PATH" "$EXTRACT_DIR/$ENTRY_NAME" ;;
  esac
}

install_binary() {
  local found
  if [[ -f "$EXTRACT_DIR/$ENTRY_NAME" ]]; then
    found="$EXTRACT_DIR/$ENTRY_NAME"
  else
    found="$(find "$EXTRACT_DIR" -type f -name "$ENTRY_NAME" | head -n 1 || true)"
  fi
  [[ -n "$found" ]] || fail "could not locate entry '$ENTRY_NAME' after extraction"

  cp "$found" "$BIN_PATH"
  if [[ "$ENTRY_NAME" != *.exe ]]; then
    chmod +x "$BIN_PATH"
  fi

  cat > "$META_PATH" <<EOF
{
  "version": "${BOOTSTRAP_VERSION}",
  "url": "${ARTIFACT_URL}",
  "sha256": "${ARTIFACT_SHA256}",
  "archive": "${ARTIFACT_ARCHIVE}",
  "entry": "${ENTRY_NAME}"
}
EOF
}

install_impl() {
  local reinstall="${1:-0}"
  resolve_config
  ensure_archive_tools

  mkdir -p "$INSTALL_DIR"
  if [[ "$reinstall" -eq 0 && -f "$BIN_PATH" && -f "$ARTIFACT_PATH" ]]; then
    verify_sha
    echo "$BIN_PATH"
    return 0
  fi

  local tmp
  tmp="$(mktemp)"
  trap 'rm -f "${tmp:-}"' RETURN

  curl -fsSL "$ARTIFACT_URL" -o "$tmp"
  mv "$tmp" "$ARTIFACT_PATH"
  verify_sha
  extract_artifact
  install_binary
  ln -sfn "$INSTALL_DIR" "${CACHE_ROOT}/current-$(detect_platform_key)"
  echo "$BIN_PATH"
}

verify_impl() {
  resolve_config
  [[ -f "$BIN_PATH" ]] || fail "bootstrap binary not installed: $BIN_PATH"
  [[ -f "$ARTIFACT_PATH" ]] || fail "artifact missing: $ARTIFACT_PATH"
  verify_sha
  echo "ok: $BIN_PATH"
}

path_impl() {
  resolve_config
  [[ -f "$BIN_PATH" ]] || fail "bootstrap binary not installed: $BIN_PATH (run install first)"
  echo "$BIN_PATH"
}

main() {
  local cmd="${1:-}"
  case "$cmd" in
    install)
      shift || true
      if [[ "${1:-}" == "--reinstall" ]]; then
        install_impl 1
      elif [[ -z "${1:-}" ]]; then
        install_impl 0
      else
        fail "unknown option for install: ${1}"
      fi
      ;;
    verify)
      verify_impl
      ;;
    path)
      path_impl
      ;;
    ""|-h|--help|help)
      usage
      ;;
    *)
      fail "unknown command: $cmd"
      ;;
  esac
}

main "$@"
