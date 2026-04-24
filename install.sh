#!/usr/bin/env sh
# ll-lang one-line installer
# Usage: curl -sSL https://raw.githubusercontent.com/Neftedollar/ll-lang/main/install.sh | sh
# Or:    curl -sSL https://raw.githubusercontent.com/Neftedollar/ll-lang/main/install.sh | LLLC_INSTALL_DIR=/usr/local/bin sh

set -eu

LOCK_URL="https://raw.githubusercontent.com/Neftedollar/ll-lang/main/bootstrap/lllc-bootstrap.lock.json"
INSTALL_DIR="${LLLC_INSTALL_DIR:-$HOME/.local/bin}"

info()  { printf '\033[1;34m[lllc]\033[0m %s\n' "$*"; }
ok()    { printf '\033[1;32m[lllc]\033[0m %s\n' "$*"; }
err()   { printf '\033[1;31m[lllc]\033[0m %s\n' "$*" >&2; }
die()   { err "$*"; exit 1; }

need() {
  command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"
}

need curl
need python3

# Detect OS + arch
OS="$(uname -s)"
ARCH="$(uname -m)"

case "${OS}-${ARCH}" in
  Linux-x86_64)  KEY="linux-x64"   ;;
  Linux-aarch64) KEY="linux-arm64" ;;
  Darwin-x86_64) KEY="osx-x64"    ;;
  Darwin-arm64)  KEY="osx-arm64"  ;;
  *)             die "Unsupported platform: ${OS} ${ARCH}. Windows is not supported by this installer." ;;
esac

info "Platform: ${OS} ${ARCH} (key: ${KEY})"
info "Fetching lock file from ${LOCK_URL} ..."

LOCK="$(curl -sSfL "$LOCK_URL")" || die "Failed to fetch lock file from ${LOCK_URL}"

# Parse JSON with python3
VERSION="$(printf '%s' "$LOCK" | python3 -c "
import json,sys
d = json.load(sys.stdin)
print(d['version'])
")" || die "Failed to parse lock file"

URL="$(printf '%s' "$LOCK" | python3 -c "
import json,sys
d = json.load(sys.stdin)
print(d['artifacts']['${KEY}']['url'])
")" || die "Platform key '${KEY}' not found in lock file"

SHA256="$(printf '%s' "$LOCK" | python3 -c "
import json,sys
d = json.load(sys.stdin)
print(d['artifacts']['${KEY}']['sha256'])
")" || die "sha256 not found for platform '${KEY}'"

ARCHIVE="$(printf '%s' "$LOCK" | python3 -c "
import json,sys
d = json.load(sys.stdin)
print(d['artifacts']['${KEY}']['archive'])
")" || die "archive type not found for platform '${KEY}'"

info "Installing lllc ${VERSION} ..."

# Download to temp dir
TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT

ARCHIVE_FILE="${TMPDIR}/lllc-artifact.${ARCHIVE}"
info "Downloading ${URL} ..."
curl -sSfL --progress-bar -o "$ARCHIVE_FILE" "$URL" || die "Download failed: ${URL}"

# Verify sha256
info "Verifying sha256 ..."
ACTUAL="$(python3 -c "
import hashlib, sys
with open(sys.argv[1], 'rb') as f:
    h = hashlib.sha256(f.read()).hexdigest()
print(h)
" "$ARCHIVE_FILE")"

if [ "$ACTUAL" != "$SHA256" ]; then
  die "sha256 mismatch!
  expected: ${SHA256}
  got:      ${ACTUAL}
Aborting install."
fi

ok "sha256 verified"

# Extract
case "$ARCHIVE" in
  tar.gz)
    tar -xzf "$ARCHIVE_FILE" -C "$TMPDIR"
    BIN="${TMPDIR}/lllc"
    ;;
  zip)
    unzip -q "$ARCHIVE_FILE" -d "$TMPDIR"
    BIN="${TMPDIR}/lllc"
    ;;
  raw)
    BIN="$ARCHIVE_FILE"
    ;;
  *)
    die "Unknown archive type: ${ARCHIVE}"
    ;;
esac

[ -f "$BIN" ] || die "Binary not found after extraction (looked for: ${BIN})"
chmod +x "$BIN"

# Install to target directory
mkdir -p "$INSTALL_DIR"
DEST="${INSTALL_DIR}/lllc"
cp "$BIN" "$DEST"
chmod +x "$DEST"

ok "Installed: ${DEST} (${VERSION})"

# Check PATH
case ":${PATH}:" in
  *":${INSTALL_DIR}:"*) ;;
  *)
    info ""
    info "NOTE: ${INSTALL_DIR} is not in your PATH."
    info "Add the following line to your shell profile (~/.bashrc, ~/.zshrc, etc.):"
    info ""
    info "  export PATH=\"\$HOME/.local/bin:\$PATH\""
    info ""
    ;;
esac

ok "Done! Run: lllc --version"
