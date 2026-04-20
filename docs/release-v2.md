# ll-lang v2 Release Gates

This document is the binding checklist for `v2` readiness. A gate is either
**PASS** or **BLOCK**. No partial states. All PASS before the tag is cut.

## Readiness references

- [Release Readiness Evidence](./release/readiness-evidence.md)
- [Stage0-Only Matrix](./release/stage0-only-matrix.md)
- [CI Exceptions Policy](./release/ci-exceptions-policy.md)

---

## G1 — Self-hosted compiler gates

| # | Gate | Status | How to verify |
|---|------|--------|---------------|
| G1.1 | Self-hosted compiler compiles `20-bootstrap-compiler.lll` without errors | PASS | `bash tools/check-fixpoint.sh spec/examples/valid/20-bootstrap-compiler.lll` |
| G1.2 | `compiler₁.fs == compiler₂.fs` (fixpoint byte-identical) | BLOCK | prelude format gap: self-hosted emits `open LLLang.Prelude`; stage0 inlines it. Deferred to M5/M6 alignment. |
| G1.3 | All `stdlib/src/*.lll` compile without errors under `lllc run` | PASS | `for f in stdlib/src/*.lll; do lllc run "$f"; done` — all 33 modules OK |
| G1.4 | `spec/examples/valid/24-pipeline-v2.lll` passes all 4 tests | PASS | `lllc run spec/examples/valid/24-pipeline-v2.lll` |
| G1.5 | `spec/examples/valid/25-llm-repair-workflow.lll` passes all 7 tests | PASS | `lllc run spec/examples/valid/25-llm-repair-workflow.lll` |

## G2 — Milestone completion

| # | Gate | Status | Reference |
|---|------|--------|-----------|
| G2.0 | M0 (spec freeze) all items `[x]` | PASS | roadmap §M0 |
| G2.1 | M1 (compiler boundaries) all items `[x]` | PASS | roadmap §M1 |
| G2.2 | M2 (project & deps) all items `[x]` | PASS | roadmap §M2 |
| G2.3 | M3 (stdlib foundation) all items `[x]` | PASS | roadmap §M3 |
| G2.4 | M4 (syntax ergonomics) all items `[x]` | PASS | roadmap §M4 |
| G2.5 | M5 (self-host transition) — policy items `[x]`; "compile itself" `[ ]` | PARTIAL | file I/O FFI added (PR #127); remaining: byte-identical fixpoint (let rec + prelude alignment) |
| G2.6 | M6 (LLM operating system) all items `[x]` | PASS | roadmap §M6 |
| G2.7 | M7 (benchmarks & release gates) all items `[x]` | PARTIAL | this doc |

## G3 — Language correctness

| # | Gate | Status | How to verify |
|---|------|--------|---------------|
| G3.1 | All `spec/examples/valid/*.lll` run without errors | PASS | `for f in spec/examples/valid/*.lll; do lllc run "$f"; done` — 0 failures |
| G3.2 | All `spec/examples/invalid/*.lll` trigger the expected error code | BLOCK | manual audit: xUnit Category=Corpus filter not implemented |
| G3.3 | xUnit test suite green (`dotnet test`) | BLOCK | 752 pass, 1 pre-existing failure: `BootstrapPlatformCompilerEmitTests` (C# backend type-erasure issue with `RBMap<object,object>`) |
| G3.4 | No `fn`, `type`, `in`, `then`, `with` keywords in ll-lang docs code blocks | PASS | audited `docs/user-guide/` — fixed stale `fn` in 07-error-codes.md |

## G4 — Stdlib correctness

| # | Gate | Status | How to verify |
|---|------|--------|---------------|
| G4.1 | All stdlib modules with `main` pass their self-tests | PASS | All 33 stdlib modules run cleanly; `Std.Test` FAILs are expected (testing failure-detection) |
| G4.2 | `Std.Compiler` 11-test suite passes | PASS | `lllc run stdlib/src/Compiler.lll` — 11/11 OK |
| G4.3 | `Std.CompilerLoader` 12-test suite passes | PASS | `lllc run stdlib/src/CompilerLoader.lll` — 12/12 OK |

## G5 — Benchmark thresholds

| # | Gate | Threshold | How to verify |
|---|------|-----------|---------------|
| G5.1 | Token ratio F#/lll ≥ 1.05 for `Toml` (Tier 1) | ≥ 1.05 | `python3 benchmarks/token-benchmark.py` |
| G5.2 | Token ratio F#/lll ≥ 1.05 for `Bootstrap` (Tier 1) | ≥ 1.05 | `python3 benchmarks/token-benchmark.py` |
| G5.3 | Micro `sum_type_3_ctor` F#/lll ≥ 1.2 | ≥ 1.2 | `python3 benchmarks/token-benchmark.py` |
| G5.4 | Micro `parametric_adt` F#/lll ≥ 1.2 | ≥ 1.2 | `python3 benchmarks/token-benchmark.py` |

Current benchmark results: see `benchmarks/results/token-benchmark.md`.

## G6 — Documentation

| # | Gate | Status | How to verify |
|---|------|--------|---------------|
| G6.1 | `docs/user-guide/09-llm-prompting.md` uses only v2 syntax | PASS | audit |
| G6.2 | `docs/llm-best-practices.md` uses only v2 syntax | PASS | audit |
| G6.3 | `docs/prompt-packs/v2-minimal.md` exists | PASS | `ls docs/prompt-packs/` |
| G6.4 | `spec/error-codes.md` lists all codes with `EXXX line:col Name` format | BLOCK | `cat spec/error-codes.md` |
| G6.5 | `docs/user-guide/09-mcp.md` matches actual MCP tool inventory | PASS | compare with `lllcself/src/Mcp.lll` |

## G7 — MCP contract

| # | Gate | Status | How to verify |
|---|------|--------|---------------|
| G7.1 | All 28 MCP tools respond to valid input | BLOCK | `lllc mcp` integration contract |
| G7.2 | `check_source` returns `{"ok":true}` on valid ll-lang | BLOCK | MCP test |
| G7.3 | `lookup_error` for each registered code returns `"found":true` | BLOCK | MCP test |

---

## Current v2 status

```
M0  ████████████ PASS
M1  ████████████ PASS
M2  ████████████ PASS
M3  ████████████ PASS
M4  ████████████ PASS
M5  ████████░░░░ PARTIAL (file I/O FFI gap)
M6  ████████████ PASS
M7  █████░░░░░░░ PARTIAL (latency + equivalence corpus + release gates)
```

**Blocking for v2 tag:** G1.1, G3.1–G3.3, G4.1–G4.3, G5.1–G5.4, G6.4.
**Nice-to-have before tag:** G1.1, G7.1–G7.3, full M7 completion.

---

## How to use this document

1. Before cutting the `v2` tag, verify every BLOCK gate is PASS.
2. PARTIAL gates are acceptable for `v2` only when their gap is documented
   and tracked — see M5 gap note above.
3. After tagging, move this document to `docs/release-contract-v2.md`.
