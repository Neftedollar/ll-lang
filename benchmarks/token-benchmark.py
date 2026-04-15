#!/usr/bin/env python3
"""
ll-lang Token Efficiency Benchmark
Measures how many LLM tokens ll-lang uses vs F#/TS/Python/Java for equivalent code.
"""

import json
import os
import re
import subprocess
import sys
from datetime import date
from pathlib import Path

# ---------------------------------------------------------------------------
# Setup: ensure tiktoken is available
# ---------------------------------------------------------------------------
try:
    import tiktoken
except ImportError:
    print("tiktoken not found. Installing...")
    subprocess.check_call([sys.executable, "-m", "pip", "install", "tiktoken==0.7.0"])
    import tiktoken

# ---------------------------------------------------------------------------
# Micro-benchmarks (Tier 3) — inline string constants
# ---------------------------------------------------------------------------
MICRO = {
    "sum_type_3_ctor": {
        # v2 canonical syntax: no 'type' keyword
        "lll": 'Shape = Circle Float | Rect Float Float | Empty',
        "fs": 'type Shape = Circle of float | Rect of float * float | Empty',
        "ts": 'type Shape = { tag: "Circle"; value: number } | { tag: "Rect"; w: number; h: number } | { tag: "Empty" }',
        "py": 'from dataclasses import dataclass\n\n@dataclass\nclass Circle:\n    value: float\n\n@dataclass\nclass Rect:\n    w: float\n    h: float\n\nclass Empty:\n    pass\n\nShape = Circle | Rect | Empty',
        "java": 'sealed interface Shape permits Circle, Rect, Empty {}\nrecord Circle(double value) implements Shape {}\nrecord Rect(double w, double h) implements Shape {}\nrecord Empty() implements Shape {}',
        "cs": 'abstract record Shape;\nrecord Circle(double Value) : Shape;\nrecord Rect(double W, double H) : Shape;\nrecord Empty : Shape;',
    },
    "pattern_match": {
        # v2 canonical syntax: no 'fn' keyword, match without 'with', no 'then'
        "lll": 'area(s Shape) =\n  match s\n    | Circle r -> 3.14 * r * r\n    | Rect w h -> w * h\n    | Empty -> 0.0',
        "fs": 'let area s =\n    match s with\n    | Circle r -> 3.14 * r * r\n    | Rect(w, h) -> w * h\n    | Empty -> 0.0',
        "ts": 'function area(s: Shape): number {\n  switch (s.tag) {\n    case "Circle": return 3.14 * s.value * s.value;\n    case "Rect": return s.w * s.h;\n    case "Empty": return 0.0;\n  }\n}',
        "py": 'def area(s: Shape) -> float:\n    match s:\n        case Circle(r):\n            return 3.14 * r * r\n        case Rect(w, h):\n            return w * h\n        case Empty():\n            return 0.0',
        "java": 'static double area(Shape s) {\n    return switch (s) {\n        case Circle(var r) -> 3.14 * r * r;\n        case Rect(var w, var h) -> w * h;\n        case Empty() -> 0.0;\n    };\n}',
        "cs": 'static double Area(Shape s) => s switch {\n    Circle(var r) => 3.14 * r * r,\n    Rect(var w, var h) => w * h,\n    Empty => 0.0,\n    _ => throw new Exception("unreachable")\n};',
    },
    "curried_fn": {
        # v2 canonical syntax: no 'fn' keyword, no return type annotation needed
        "lll": 'add(a Int)(b Int) = a + b',
        "fs": 'let add (a: int64) (b: int64) : int64 = a + b',
        "ts": 'const add = (a: number) => (b: number): number => a + b;',
        "py": 'def add(a: int, b: int) -> int:\n    return a + b',
        "java": 'static long add(long a, long b) { return a + b; }',
        "cs": 'static Func<long, long> Add(long a) => b => a + b;',
    },
    "parametric_adt": {
        # v2 canonical syntax: no 'type' keyword
        "lll": 'Maybe A = Some A | None',
        "fs": "type Maybe<'A> = Some of 'A | None",
        "ts": 'type Maybe<A> = { tag: "Some"; value: A } | { tag: "None" };',
        "py": 'from typing import Generic, TypeVar\nT = TypeVar("T")\nclass Some(Generic[T]):\n    def __init__(self, value: T): self.value = value\nclass Nothing: pass\nMaybe = Some | Nothing',
        "java": 'sealed interface Maybe<A> permits Some, Nothing {}\nrecord Some<A>(A value) implements Maybe<A> {}\nrecord Nothing<A>() implements Maybe<A> {}',
        "cs": 'abstract record Maybe<A>;\nrecord Some<A>(A Value) : Maybe<A>;\nrecord None<A> : Maybe<A>;',
    },
}

# ---------------------------------------------------------------------------
# Tokenizer
# ---------------------------------------------------------------------------
ENC = tiktoken.get_encoding("cl100k_base")


def count_tokens(text: str) -> int:
    return len(ENC.encode(text))


# ---------------------------------------------------------------------------
# Comment / blank stripping
# ---------------------------------------------------------------------------
def strip_inline_comment(line: str, marker: str) -> str:
    """Remove inline comment from a line, keeping code portion."""
    # Handle string literals naively — good enough for token counting
    idx = line.find(marker)
    if idx == -1:
        return line
    return line[:idx].rstrip()


def strip_comments_and_blanks(text: str, lang: str) -> str:
    """Strip comments and blank lines based on language."""
    lines = text.splitlines()
    result = []
    for line in lines:
        if lang == "lll":
            stripped = strip_inline_comment(line, "--")
        elif lang in ("fs", "ts", "java"):
            stripped = strip_inline_comment(line, "//")
        elif lang == "py":
            stripped = strip_inline_comment(line, "#")
        else:
            stripped = line
        if stripped.strip():
            result.append(stripped)
    return "\n".join(result)


def strip_fs_prelude(text: str) -> tuple[str, str]:
    """
    Return (text_without_prelude, prelude_text).
    Looks for lines containing the prelude markers.
    """
    lines = text.splitlines(keepends=True)
    start_idx = None
    end_idx = None
    for i, line in enumerate(lines):
        if "// --- ll-lang stdlib prelude" in line:
            start_idx = i
        if "// --- end prelude ---" in line:
            end_idx = i
            break

    if start_idx is not None and end_idx is not None:
        prelude = "".join(lines[start_idx : end_idx + 1])
        without = "".join(lines[:start_idx] + lines[end_idx + 1 :])
        return without, prelude
    return text, ""


# ---------------------------------------------------------------------------
# Function counting via regex
# ---------------------------------------------------------------------------
FUNC_PATTERNS = {
    "lll": re.compile(r"^fn\s+", re.MULTILINE),
    "fs": re.compile(r"^let\s+", re.MULTILINE),
    "ts": re.compile(r"^(function\b|const\b.*=>)", re.MULTILINE),
    "py": re.compile(r"^def\s+", re.MULTILINE),
    "java": re.compile(r"^(public|private|protected|static|void|int|long|double|String|boolean)\b.*\(", re.MULTILINE),
}


def count_functions(text: str, lang: str) -> int:
    pattern = FUNC_PATTERNS.get(lang)
    if pattern is None:
        return 0
    return len(pattern.findall(text))


# ---------------------------------------------------------------------------
# File metrics
# ---------------------------------------------------------------------------
def file_metrics(path: str, lang: str) -> dict:
    """Compute metrics for a file on disk."""
    try:
        text = Path(path).read_text(encoding="utf-8")
    except FileNotFoundError:
        return None

    raw_lines = text.splitlines()
    total_lines = len(raw_lines)
    raw_tokens = count_tokens(text)
    byte_count = len(text.encode("utf-8"))

    code_text = strip_comments_and_blanks(text, lang)
    code_lines = len(code_text.splitlines())
    code_tokens = count_tokens(code_text)

    result = {
        "path": path,
        "lang": lang,
        "lines": total_lines,
        "code_lines": code_lines,
        "tokens_raw": raw_tokens,
        "tokens_code": code_tokens,
        "bytes": byte_count,
        "functions": count_functions(code_text, lang),
    }

    if lang == "fs":
        text_no_prelude, prelude_text = strip_fs_prelude(text)
        code_no_prelude = strip_comments_and_blanks(text_no_prelude, lang)
        result["tokens_no_prelude"] = count_tokens(code_no_prelude)
        result["prelude_tokens"] = count_tokens(prelude_text) if prelude_text else 0

    return result


def string_metrics(text: str, lang: str, label: str) -> dict:
    """Compute metrics for an in-memory string."""
    raw_tokens = count_tokens(text)
    byte_count = len(text.encode("utf-8"))
    code_text = strip_comments_and_blanks(text, lang)
    code_lines = len(code_text.splitlines())
    code_tokens = count_tokens(code_text)
    return {
        "label": label,
        "lang": lang,
        "lines": len(text.splitlines()),
        "code_lines": code_lines,
        "tokens_raw": raw_tokens,
        "tokens_code": code_tokens,
        "bytes": byte_count,
        "functions": count_functions(code_text, lang),
    }


# ---------------------------------------------------------------------------
# Compilation
# ---------------------------------------------------------------------------
PROJECT_ROOT = Path(__file__).parent.parent
LLLC = str(PROJECT_ROOT / "src/LLLangTool/bin/Debug/net10.0/lllc.dll")


def compile_lll(src_path: str, target: str = "fs") -> tuple[bool, str]:
    """
    Compile a .lll file to the given target.
    Returns (success, output_path_or_error).
    """
    src = Path(src_path)
    if target == "fs":
        out_path = src.with_suffix(".fs")
        args = ["dotnet", LLLC, "build", str(src)]
    else:
        ext = f".{target}"
        out_path = src.with_suffix(ext)
        args = ["dotnet", LLLC, "build", f"--target", target, str(src)]

    try:
        result = subprocess.run(
            args,
            capture_output=True,
            text=True,
            timeout=60,
            cwd=str(PROJECT_ROOT),
        )
        if result.returncode == 0 and out_path.exists():
            return True, str(out_path)
        else:
            err = (result.stderr or result.stdout or "unknown error").strip()
            return False, err
    except subprocess.TimeoutExpired:
        return False, "timeout"
    except Exception as e:
        return False, str(e)


# ---------------------------------------------------------------------------
# ASCII bar chart
# ---------------------------------------------------------------------------
def bar_chart(values: dict[str, int], width: int = 40) -> str:
    max_val = max(values.values()) if values else 1
    lines = []
    for label, val in values.items():
        bar_len = int(val / max_val * width)
        bar = "#" * bar_len
        lines.append(f"  {label:<20} {bar:<{width}} {val}")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Main benchmark logic
# ---------------------------------------------------------------------------
def run_benchmark():
    results = {
        "generated": str(date.today()),
        "tokenizer": "cl100k_base",
        "tier1": [],
        "tier2": [],
        "tier3": [],
        "per_file_details": [],
    }

    per_file = results["per_file_details"]

    # ------------------------------------------------------------------
    # Tier 1: Compiled output comparison
    # ------------------------------------------------------------------
    print("\n=== Tier 1: Compiled Output Comparison ===\n")

    tier1_samples = [
        {
            "label": "01-basics",
            "lll": str(PROJECT_ROOT / "spec/examples/valid/01-basics.lll"),
            "targets": ["fs", "ts"],
        },
        {
            "label": "Map (RBTree)",
            "lll": str(PROJECT_ROOT / "stdlib/src/Map.lll"),
            "targets": ["fs"],
        },
        {
            "label": "Toml parser",
            "lll": str(PROJECT_ROOT / "stdlib/src/Toml.lll"),
            "targets": ["fs"],
        },
        {
            "label": "Bootstrap",
            "lll": str(PROJECT_ROOT / "spec/examples/valid/20-bootstrap-compiler.lll"),
            "targets": ["fs"],
        },
    ]

    for sample in tier1_samples:
        label = sample["label"]
        lll_path = sample["lll"]
        print(f"  Processing: {label}")

        # Measure .lll source
        lll_m = file_metrics(lll_path, "lll")
        if lll_m is None:
            print(f"    SKIP: .lll file not found: {lll_path}")
            continue
        per_file.append(lll_m)

        row = {
            "label": label,
            "lll_tokens": lll_m["tokens_code"],
            "fs_tokens_no_prelude": None,
            "fs_tokens_with_prelude": None,
            "ts_tokens": None,
            "ratio_fs_lll": None,
        }

        for target in sample["targets"]:
            # Check if compiled output already exists
            src = Path(lll_path)
            ext_map = {"fs": ".fs", "ts": ".ts", "py": ".py", "java": ".java"}
            expected_out = src.with_suffix(ext_map[target])

            if expected_out.exists():
                print(f"    Using existing {target} output: {expected_out.name}")
                success, out_path = True, str(expected_out)
            else:
                print(f"    Compiling to {target}...")
                success, out_path = compile_lll(lll_path, target)

            if success:
                m = file_metrics(out_path, target)
                if m:
                    per_file.append(m)
                    if target == "fs":
                        row["fs_tokens_no_prelude"] = m.get("tokens_no_prelude", m["tokens_code"])
                        row["fs_tokens_with_prelude"] = m["tokens_code"]
                        lll_tok = row["lll_tokens"]
                        fs_tok = row["fs_tokens_no_prelude"]
                        if lll_tok and lll_tok > 0:
                            row["ratio_fs_lll"] = round(fs_tok / lll_tok, 2)
                    elif target == "ts":
                        row["ts_tokens"] = m["tokens_code"]
            else:
                print(f"    Compile to {target} failed: {out_path[:120]}")

        results["tier1"].append(row)
        print(f"    lll={row['lll_tokens']} tokens, fs(no-prelude)={row['fs_tokens_no_prelude']}, ratio={row['ratio_fs_lll']}")

    # ------------------------------------------------------------------
    # Tier 2: Hand-written equivalents
    # ------------------------------------------------------------------
    print("\n=== Tier 2: Hand-written Equivalents ===\n")

    tier2_samples = [
        {
            "label": "TOML parser",
            "lll": str(PROJECT_ROOT / "stdlib/src/Toml.lll"),
            "fs_handwritten": str(PROJECT_ROOT / "src/LLLangCompiler/Manifest.fs"),
        },
    ]

    for sample in tier2_samples:
        label = sample["label"]
        lll_m = file_metrics(sample["lll"], "lll")
        fs_m = file_metrics(sample["fs_handwritten"], "fs")

        if lll_m is None or fs_m is None:
            print(f"  SKIP {label}: file not found")
            continue

        lll_tok = lll_m["tokens_code"]
        fs_tok = fs_m.get("tokens_no_prelude", fs_m["tokens_code"])
        ratio = round(fs_tok / lll_tok, 2) if lll_tok > 0 else None

        row = {
            "label": label,
            "lll_tokens": lll_tok,
            "fs_handwritten_tokens": fs_tok,
            "ratio": ratio,
        }
        results["tier2"].append(row)
        print(f"  {label}: lll={lll_tok}, fs(hand)={fs_tok}, ratio={ratio}")

        # Add to per-file if not already there
        if not any(p["path"] == sample["fs_handwritten"] for p in per_file):
            per_file.append(fs_m)

    # ------------------------------------------------------------------
    # Tier 3: Micro-benchmarks
    # ------------------------------------------------------------------
    print("\n=== Tier 3: Micro-benchmarks ===\n")

    for name, variants in MICRO.items():
        row = {"label": name, "variants": {}}
        for lang, code in variants.items():
            m = string_metrics(code, lang, f"{name}/{lang}")
            row["variants"][lang] = {
                "tokens_raw": m["tokens_raw"],
                "tokens_code": m["tokens_code"],
                "bytes": m["bytes"],
            }
        # Compute ratios vs lll
        lll_tok = row["variants"].get("lll", {}).get("tokens_code", 0)
        for lang in ["fs", "ts", "py", "java", "cs"]:
            if lang in row["variants"] and lll_tok > 0:
                row["variants"][lang]["ratio_vs_lll"] = round(
                    row["variants"][lang]["tokens_code"] / lll_tok, 2
                )
        results["tier3"].append(row)

        lll_t = row["variants"].get("lll", {}).get("tokens_code", "?")
        fs_t = row["variants"].get("fs", {}).get("tokens_code", "?")
        ts_t = row["variants"].get("ts", {}).get("tokens_code", "?")
        cs_t = row["variants"].get("cs", {}).get("tokens_code", "?")
        print(f"  {name}: lll={lll_t}, fs={fs_t}, ts={ts_t}, cs={cs_t}")

    # ------------------------------------------------------------------
    # Write JSON results
    # ------------------------------------------------------------------
    out_dir = PROJECT_ROOT / "benchmarks/results"
    out_dir.mkdir(parents=True, exist_ok=True)

    json_path = out_dir / "token-benchmark.json"
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)
    print(f"\nJSON written: {json_path}")

    # ------------------------------------------------------------------
    # Write Markdown report
    # ------------------------------------------------------------------
    md_path = out_dir / "token-benchmark.md"
    with open(md_path, "w", encoding="utf-8") as f:
        f.write("# ll-lang Token Efficiency Benchmark\n\n")
        f.write(f"*Generated: {results['generated']}*\n")
        f.write(f"*Tokenizer: cl100k_base (GPT-4)*\n\n")

        # --- Tier 1 table ---
        f.write("## Tier 1: Compiled Output Comparison\n\n")
        f.write("| Sample | ll-lang | F# (no prelude) | F# (w/prelude) | TS | Ratio (F#/lll) |\n")
        f.write("|--------|---------|-----------------|----------------|----|----------------|\n")
        for row in results["tier1"]:
            fs_no = row["fs_tokens_no_prelude"] if row["fs_tokens_no_prelude"] is not None else "-"
            fs_w = row["fs_tokens_with_prelude"] if row["fs_tokens_with_prelude"] is not None else "-"
            ts = row["ts_tokens"] if row["ts_tokens"] is not None else "-"
            ratio = row["ratio_fs_lll"] if row["ratio_fs_lll"] is not None else "-"
            f.write(
                f"| {row['label']} | {row['lll_tokens']} | {fs_no} | {fs_w} | {ts} | {ratio} |\n"
            )
        f.write("\n")

        # --- Tier 2 table ---
        f.write("## Tier 2: Hand-written Equivalent\n\n")
        f.write("| Sample | ll-lang | F# (hand-written) | Ratio |\n")
        f.write("|--------|---------|-------------------|-------|\n")
        for row in results["tier2"]:
            f.write(
                f"| {row['label']} | {row['lll_tokens']} | {row['fs_handwritten_tokens']} | {row['ratio']} |\n"
            )
        f.write("\n")

        # --- Tier 3 table ---
        f.write("## Tier 3: Micro-benchmarks\n\n")
        f.write("| Pattern | lll | F# | TS | Py | Java | C# | F#/lll | TS/lll | Py/lll | Java/lll | C#/lll |\n")
        f.write("|---------|-----|----|----|-----|------|-----|--------|--------|--------|----------|--------|\n")
        for row in results["tier3"]:
            v = row["variants"]
            def tok(lang):
                return v.get(lang, {}).get("tokens_code", "-")
            def ratio(lang):
                return v.get(lang, {}).get("ratio_vs_lll", "-")
            f.write(
                f"| {row['label']} | {tok('lll')} | {tok('fs')} | {tok('ts')} | {tok('py')} | {tok('java')} | {tok('cs')}"
                f" | {ratio('fs')} | {ratio('ts')} | {ratio('py')} | {ratio('java')} | {ratio('cs')} |\n"
            )
        f.write("\n")

        # --- Per-file details ---
        f.write("## Per-file Details\n\n")
        f.write("| File | Lines | Code lines | Tokens (raw) | Tokens (code-only) | Bytes | Bytes/token |\n")
        f.write("|------|-------|-----------|-------------|-------------------|-------|-------------|\n")
        for m in results["per_file_details"]:
            fname = Path(m["path"]).name
            bpt = round(m["bytes"] / m["tokens_code"], 1) if m["tokens_code"] > 0 else "-"
            f.write(
                f"| {fname} | {m['lines']} | {m['code_lines']} | {m['tokens_raw']} | {m['tokens_code']} | {m['bytes']} | {bpt} |\n"
            )
        f.write("\n")

    print(f"Markdown written: {md_path}")

    # ------------------------------------------------------------------
    # Stdout summary with ASCII bar chart
    # ------------------------------------------------------------------
    print("\n" + "=" * 60)
    print("SUMMARY: Token counts by sample (ll-lang source)")
    print("=" * 60)
    chart_data = {}
    for row in results["tier1"]:
        chart_data[f"[lll] {row['label']}"] = row["lll_tokens"]
        if row["fs_tokens_no_prelude"] is not None:
            chart_data[f"[fs]  {row['label']}"] = row["fs_tokens_no_prelude"]

    print(bar_chart(chart_data))

    print("\n" + "=" * 60)
    print("SUMMARY: Tier 1 Ratios (F# tokens / ll-lang tokens)")
    print("=" * 60)
    for row in results["tier1"]:
        ratio = row["ratio_fs_lll"]
        ratio_str = f"{ratio:.2f}x" if ratio is not None else "N/A"
        bar = "#" * int((ratio or 0) * 10)
        print(f"  {row['label']:<22} {ratio_str:<8} {bar}")

    print("\n" + "=" * 60)
    print("SUMMARY: Tier 3 Micro-benchmarks (tokens, code-only)")
    print("=" * 60)
    header = f"  {'Pattern':<22} {'lll':>5} {'F#':>5} {'TS':>5} {'Py':>5} {'Java':>5}  F#/lll"
    print(header)
    print("  " + "-" * (len(header) - 2))
    for row in results["tier3"]:
        v = row["variants"]
        def t(lang): return str(v.get(lang, {}).get("tokens_code", "-"))
        def r(lang): return str(v.get(lang, {}).get("ratio_vs_lll", "-"))
        print(
            f"  {row['label']:<22} {t('lll'):>5} {t('fs'):>5} {t('ts'):>5} {t('py'):>5} {t('java'):>5}  {r('fs')}x"
        )

    print("\nDone.")


if __name__ == "__main__":
    run_benchmark()
