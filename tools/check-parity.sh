#!/usr/bin/env bash
# tools/check-parity.sh
# Parity test: compare lllc run output against golden corpus files.
# Exit 0 if all corpus entries match their golden files, 1 otherwise.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT="$REPO_ROOT/benchmarks/check-corpus.py"

echo "=== ll-lang corpus parity test ==="
echo ""

if python3 "$SCRIPT" check; then
    echo ""
    echo "PASS: all corpus entries match golden files."
    exit 0
else
    echo ""
    echo "FAIL: one or more corpus entries differ from golden files."
    exit 1
fi
