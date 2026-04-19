#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

VERSION=""
REPO_SLUG="Neftedollar/ll-lang"
OUTPUT_DIR=""
LOCK_OUT=""

usage() {
  cat <<'USAGE'
Usage:
  tools/build-bootstrap-artifacts.sh --version vX.Y.Z [options]

Options:
  --version <tag>      Release tag, e.g. v1.2.2 (required)
  --repo <owner/name>  GitHub repo slug for release URLs (default: Neftedollar/ll-lang)
  --output-dir <dir>   Output directory (default: dist/bootstrap/<version>)
  --lock-out <path>    Lock file path to write (default: bootstrap/lllc-bootstrap.lock.json)

Output:
  - Packages bootstrap binaries for: linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64
  - Writes lock JSON with sha256/url/archive/entry metadata
USAGE
}

fail() {
  echo "build-bootstrap-artifacts: $*" >&2
  exit 1
}

need_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "missing required command: $1"
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
    fail "no sha256 tool available"
  fi
}

escape_json() {
  local s="$1"
  s="${s//\\/\\\\}"
  s="${s//\"/\\\"}"
  printf '%s' "$s"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      shift
      VERSION="${1:-}"
      ;;
    --repo)
      shift
      REPO_SLUG="${1:-}"
      ;;
    --output-dir)
      shift
      OUTPUT_DIR="${1:-}"
      ;;
    --lock-out)
      shift
      LOCK_OUT="${1:-}"
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "unknown argument: $1"
      ;;
  esac
  shift || true
done

[[ -n "$VERSION" ]] || fail "--version is required"

if [[ -z "$OUTPUT_DIR" ]]; then
  OUTPUT_DIR="$ROOT_DIR/dist/bootstrap/$VERSION"
fi
if [[ -z "$LOCK_OUT" ]]; then
  LOCK_OUT="$ROOT_DIR/bootstrap/lllc-bootstrap.lock.json"
fi
OUTPUT_DIR="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$OUTPUT_DIR")"
LOCK_OUT="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$LOCK_OUT")"

PROJECT_PATH="$ROOT_DIR/src/LLLangTool/LLLangTool.fsproj"
if [[ ! -f "$PROJECT_PATH" ]]; then
  PROJECT_PATH="$ROOT_DIR/obsolete/stage0/src/LLLangTool/LLLangTool.fsproj"
fi
[[ -f "$PROJECT_PATH" ]] || fail "cannot find stage0 project (expected src/LLLangTool or obsolete/stage0/src/LLLangTool)"

need_cmd dotnet
need_cmd tar
need_cmd zip
need_cmd python3
mkdir -p "$OUTPUT_DIR"

TMP_ROOT="$(mktemp -d)"
trap 'rm -rf "$TMP_ROOT"' EXIT

RIDS=("linux-x64" "linux-arm64" "osx-x64" "osx-arm64" "win-x64")
SHAS=()
ARCHIVES=()
ENTRIES=()
PKG_NAMES=()
URLS=()

read_lock_field() {
  local lock_file="$1"
  local rid="$2"
  local field="$3"
  python3 - "$lock_file" "$rid" "$field" <<'PY'
import json
import sys

path, rid, field = sys.argv[1], sys.argv[2], sys.argv[3]
try:
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
except Exception:
    print("")
    raise SystemExit(0)

cur = data.get("artifacts", {}).get(rid, {})
val = cur.get(field, "")
if isinstance(val, str):
    print(val)
else:
    print("")
PY
}

echo "==> building bootstrap artifacts from $PROJECT_PATH"
for rid in "${RIDS[@]}"; do
  echo "==> publish $rid"
  work_dir="$TMP_ROOT/$rid"
  publish_out="$work_dir/out"
  obj_dir="$work_dir/obj"
  mkdir -p "$publish_out" "$obj_dir"

  entry="lllc"
  archive_ext="tar.gz"
  published="$publish_out/publish/lllc"

  if [[ "$rid" == win-* ]]; then
    entry="lllc.exe"
    archive_ext="zip"
    published="$publish_out/publish/lllc.exe"
  fi

  publish_log="$work_dir/publish.log"
  if dotnet publish "$PROJECT_PATH" \
      -c Release \
      -r "$rid" \
      --self-contained true \
      -p:PublishSingleFile=true \
      -p:PublishTrimmed=false \
      -p:BaseIntermediateOutputPath="$obj_dir/" \
      -p:OutputPath="$publish_out/" \
      -nologo >"$publish_log" 2>&1; then
    true
  else
    echo "WARN  publish failed for $rid; trying lock fallback from $LOCK_OUT"
    old_url="$(read_lock_field "$LOCK_OUT" "$rid" "url")"
    old_sha="$(read_lock_field "$LOCK_OUT" "$rid" "sha256")"
    old_archive="$(read_lock_field "$LOCK_OUT" "$rid" "archive")"
    old_entry="$(read_lock_field "$LOCK_OUT" "$rid" "entry")"
    if [[ -n "$old_url" && -n "$old_sha" && -n "$old_archive" && -n "$old_entry" ]]; then
      ARCHIVES+=("$old_archive")
      ENTRIES+=("$old_entry")
      PKG_NAMES+=("fallback-$rid")
      SHAS+=("$old_sha")
      URLS+=("$old_url")
      idx=$((${#SHAS[@]} - 1))
      echo "   fallback $rid sha256=${SHAS[$idx]} url=${URLS[$idx]}"
      continue
    fi
    cat "$publish_log" >&2
    fail "publish failed for $rid and no fallback entry found in lock"
  fi

  [[ -f "$published" ]] || fail "published binary missing for $rid: $published"

  package_name="lllc-$rid.$archive_ext"
  package_path="$OUTPUT_DIR/$package_name"

  stage_dir="$work_dir/pkg"
  mkdir -p "$stage_dir"
  cp "$published" "$stage_dir/$entry"

  if [[ "$archive_ext" == "tar.gz" ]]; then
    tar -C "$stage_dir" -czf "$package_path" "$entry"
  else
    (
      cd "$stage_dir"
      zip -q "$package_path" "$entry"
    )
  fi

  sha_val="$(sha256_file "$package_path")"
  SHAS+=("$sha_val")
  ARCHIVES+=("$archive_ext")
  ENTRIES+=("$entry")
  PKG_NAMES+=("$package_name")
  URLS+=("https://github.com/$REPO_SLUG/releases/download/$VERSION/$package_name")
  idx=$((${#SHAS[@]} - 1))
  echo "   $package_name sha256=${SHAS[$idx]} url=${URLS[$idx]}"
done

lock_tmp="$TMP_ROOT/lllc-bootstrap.lock.json"

{
  echo '{'
  echo "  \"version\": \"$(escape_json "$VERSION")\","
  echo '  "schema": "v1",'
  echo '  "artifacts": {'
  for i in "${!RIDS[@]}"; do
    rid="${RIDS[$i]}"
    archive="${ARCHIVES[$i]}"
    entry="${ENTRIES[$i]}"
    sha="${SHAS[$i]}"
    url="${URLS[$i]}"
    cat <<JSON
    "$rid": {
      "url": "$(escape_json "$url")",
      "sha256": "$(escape_json "$sha")",
      "archive": "$(escape_json "$archive")",
      "entry": "$(escape_json "$entry")"
    }$( [[ "$i" -lt "$((${#RIDS[@]} - 1))" ]] && echo "," )
JSON
  done
  echo '  }'
  echo '}'
} > "$lock_tmp"

mkdir -p "$(dirname "$LOCK_OUT")"
cp "$lock_tmp" "$LOCK_OUT"

echo "==> lock written: $LOCK_OUT"
echo "==> artifacts dir: $OUTPUT_DIR"
