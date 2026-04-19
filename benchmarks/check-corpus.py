#!/usr/bin/env python3
"""
ll-lang Semantic Equivalence Corpus Check

Compiles and runs a curated set of .lll files through lllc run and
compares output to golden files in benchmarks/corpus/.

Modes:
  update   Run corpus files, write golden outputs.
  check    Run corpus files, diff against golden outputs.
  list     Show corpus entries and golden-file status.

Flags:
  --strict  Fail immediately on first mismatch (check mode only).

Usage:
  python3 benchmarks/check-corpus.py update
  python3 benchmarks/check-corpus.py check
  python3 benchmarks/check-corpus.py check --strict
  python3 benchmarks/check-corpus.py list
"""

import os
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).parent.parent
LLLC_BOOTSTRAP = REPO_ROOT / "tools/lllc-bootstrap.sh"
CORPUS_DIR = REPO_ROOT / "benchmarks/corpus"

# Curated corpus: (label, relative-path, expected-exit-code)
# All files must print only OK/FAIL/Done lines (self-test format).
CORPUS = [
    ("06-stdlib",              "spec/examples/valid/06-stdlib.lll",              0),
    ("07-text-processing",     "spec/examples/valid/07-text-processing.lll",     0),
    ("09-lexer-real",          "spec/examples/valid/09-lexer-real.lll",          0),
    ("21-multi-param-types",   "spec/examples/valid/21-multi-param-types.lll",   0),
    ("24-pipeline-v2",         "spec/examples/valid/24-pipeline-v2.lll",         0),
    ("25-llm-repair-workflow", "spec/examples/valid/25-llm-repair-workflow.lll", 0),
]


def golden_path(label: str) -> Path:
    return CORPUS_DIR / f"{label}.golden"


def run_lll(rel_path: str) -> tuple[int, str]:
    """Run a .lll file via lllc run. Returns (exit_code, stdout)."""
    src_path = str(REPO_ROOT / rel_path)
    try:
        if not LLLC_BOOTSTRAP.exists():
            return -1, f"ERROR: missing bootstrap launcher: {LLLC_BOOTSTRAP}"
        env = dict(os.environ)
        result = subprocess.run(
            [str(LLLC_BOOTSTRAP), "run", src_path],
            capture_output=True,
            text=True,
            timeout=120,
            cwd=str(REPO_ROOT),
            env=env,
        )
        return result.returncode, result.stdout
    except subprocess.TimeoutExpired:
        return -1, "TIMEOUT"
    except Exception as e:
        return -1, f"ERROR: {e}"


def cmd_list() -> int:
    print(f"{'Label':<30} {'File':<50} {'Golden'}")
    print("-" * 90)
    for label, path, _ in CORPUS:
        gp = golden_path(label)
        status = "YES" if gp.exists() else "missing"
        print(f"{label:<30} {path:<50} {status}")
    return 0


def cmd_update() -> int:
    CORPUS_DIR.mkdir(parents=True, exist_ok=True)
    ok = 0
    fail = 0
    for label, path, expected_exit in CORPUS:
        print(f"  {label} ...", end=" ", flush=True)
        code, out = run_lll(path)
        if code != expected_exit:
            print(f"SKIP (exit {code} ≠ {expected_exit})")
            fail += 1
            continue
        gp = golden_path(label)
        gp.write_text(out, encoding="utf-8")
        lines = out.count("\n")
        print(f"written ({lines} lines)")
        ok += 1
    print(f"\n{ok} golden files written, {fail} skipped.")
    return 0 if fail == 0 else 1


def cmd_check(strict: bool = False) -> int:
    if not CORPUS_DIR.exists():
        print("ERROR: no corpus directory. Run 'update' first.")
        return 2

    passed = 0
    failed = 0
    missing = 0

    for label, path, expected_exit in CORPUS:
        gp = golden_path(label)
        if not gp.exists():
            print(f"  MISSING golden: {label}")
            missing += 1
            if strict:
                print("PARITY: FAILED (missing golden — run 'update' first)")
                return 1
            continue

        print(f"  {label} ...", end=" ", flush=True)
        code, out = run_lll(path)

        if code != expected_exit:
            print(f"FAIL (exit {code} ≠ {expected_exit})")
            failed += 1
            if strict:
                total = passed + failed + missing
                print(f"\nPARITY: {passed}/{total} corpus entries match golden")
                return 1
            continue

        expected = gp.read_text(encoding="utf-8")
        if out == expected:
            print("OK")
            passed += 1
        else:
            print("FAIL (output mismatch)")
            # Show first differing line
            exp_lines = expected.splitlines()
            got_lines = out.splitlines()
            for i, (e, g) in enumerate(zip(exp_lines, got_lines)):
                if e != g:
                    print(f"    line {i+1}: expected={e!r}")
                    print(f"    line {i+1}:      got={g!r}")
                    break
            if len(exp_lines) != len(got_lines):
                print(f"    line count: expected={len(exp_lines)} got={len(got_lines)}")
            failed += 1
            if strict:
                total = passed + failed + missing
                print(f"\nPARITY: {passed}/{total} corpus entries match golden")
                return 1

    total = passed + failed + missing
    print(f"\n{passed}/{total} passed, {failed} failed, {missing} missing goldens.")

    if missing > 0:
        print("Run 'update' to populate missing golden files.")

    all_match = failed == 0 and missing == 0
    print(f"PARITY: {passed}/{total} corpus entries match golden")

    return 0 if all_match else 1


def main() -> int:
    args = sys.argv[1:]
    cmd = args[0] if args else "list"
    strict = "--strict" in args

    if cmd == "list":
        return cmd_list()
    elif cmd == "update":
        return cmd_update()
    elif cmd == "check":
        return cmd_check(strict=strict)
    else:
        print(f"Unknown command: {cmd}")
        print("Usage: check-corpus.py [list|update|check] [--strict]")
        return 2


if __name__ == "__main__":
    sys.exit(main())
