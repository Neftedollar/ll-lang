# v2 Benchmarks and Release Gates Execution

**Status:** doc/policy phase complete — implementation gaps tracked in epic #108
**Closes:** #110 #112 #113 #114
**Audience:** implementers of `Milestone 7`  
**Parent docs:** [v2 implementation roadmap](13-v2-implementation-roadmap.md), [v2 language architecture](12-v2-language-architecture.md), [v2 llm tooling spec](../../spec/v2-llm-tooling.md)

## Summary

`Milestone 7` exists to stop `v2` from shipping on vibes.

ll-lang makes explicit claims about:

- token efficiency
- self-hosting viability
- deterministic tooling
- backend correctness
- LLM productivity

Those claims need versioned evidence and explicit release gates.

## Status model

- `[x]` done in current repo and should be preserved
- `[ ]` not done or not yet canonical for `v2`

## Current-repo baseline

- [x] tests and fixpoint/self-hosting concerns already exist in the repo
- [x] docs already treat benchmarks as part of future `v2` work

Still not done enough for `v2`:

- [x] token-efficiency claims are now backed by a stable benchmark suite (`benchmarks/token-benchmark.py`, results in `benchmarks/results/`)
- [ ] self-hosted-vs-stage0 performance deltas are not yet tracked (tracked in epic #108)
- [ ] semantic equivalence across backends is not yet formalized as a benchmark/corpus gate (tracked in epic #108)
- [x] `v2` release checklist is now a concrete contract (see Work package D below)

## Work package A — Token-efficiency benchmark suite

**Closed by:** `benchmarks/token-benchmark.py` (implemented), `benchmarks/results/token-benchmark.md` and `benchmarks/results/token-benchmark.json` (versioned output). See methodology below.

### Goal

Measure the actual compactness advantage ll-lang claims to provide.

### Tasks

- [x] Define benchmark corpus categories: data modeling, parser combinators, stateful passes, config parsing, multi-module projects.
- [x] Define comparison baselines: F#, TypeScript, Python, Java, C#.
- [x] Freeze token-count methodology.
- [x] Store benchmark artifacts in a reproducible form.

### Benchmark methodology (frozen)

**Tokenizer:** `cl100k_base` (tiktoken, GPT-4 encoding). This is the canonical tokenizer for all ll-lang token-efficiency claims. Do not change without updating all historical results.

**Three measurement tiers:**

| Tier | What | Why |
|------|------|-----|
| Tier 1 | Compiled output comparison | lll source vs compiler-generated F#/TS output. Measures the compactness of the source representation. |
| Tier 2 | Hand-written equivalents | lll source vs hand-written F# performing the same function. Measures real-world authoring efficiency. |
| Tier 3 | Micro-benchmarks | Isolated patterns (sum types, pattern match, curried functions, parametric ADTs) across lll/F#/TS/Python/Java. Pinpoints where ll-lang is compact and where it is not. |

**What is counted:**

- `tokens_raw`: all tokens in the file, including comments and blank lines.
- `tokens_code`: tokens after stripping comment lines and blank lines. **This is the canonical metric.**
- F# compiled output strips the ll-lang stdlib prelude (`tokens_no_prelude`) to measure only the code that corresponds to the ll-lang source.

**How to run:**

```
cd benchmarks
python token-benchmark.py
```

Output: `benchmarks/results/token-benchmark.json` (machine-readable) and `benchmarks/results/token-benchmark.md` (human-readable).

**How to interpret results:**

- Ratio `< 1.0`: ll-lang is more verbose than the target language for this pattern.
- Ratio `1.0 – 1.3`: marginal advantage; within noise of hand-authoring style.
- Ratio `1.3 – 2.0`: clear compactness win for ll-lang.
- Ratio `> 2.0`: strong win; typical for type-heavy patterns (sum types, parametric ADTs).

**Baseline results (2026-04-11, cl100k_base):**

Tier 1 (compiled output, F# ratio = F# tokens / lll tokens):

| Sample | ll-lang tokens | F# (no prelude) | Ratio |
|--------|---------------|-----------------|-------|
| 01-basics | 110 | 122 | 1.11x |
| Map (RBTree) | 1 771 | 2 073 | 1.17x |
| Toml parser | 2 030 | 2 359 | 1.16x |
| Bootstrap | 16 957 | 18 393 | 1.08x |

Tier 3 (micro-benchmarks, F# ratio):

| Pattern | lll | F# | TS | Py | Java | F#/lll | TS/lll |
|---------|-----|----|----|-----|------|--------|--------|
| sum_type_3_ctor | 11 | 14 | 36 | 49 | 35 | 1.27x | 3.27x |
| pattern_match | 39 | 43 | 58 | 52 | 58 | 1.10x | 1.49x |
| curried_fn | 13 | 21 | 20 | 18 | 16 | 1.62x | 1.54x |
| parametric_adt | 8 | 12 | 23 | 47 | 31 | 1.50x | 2.88x |

**Corpus categories covered (Tier 1):**
- data modeling: `01-basics.lll` (type definitions, basic functions)
- config parsing: `Toml.lll` (parser combinator + structured output)
- stateful tree passes: `Map.lll` (RBTree implementation)
- multi-module / bootstrap: `20-bootstrap-compiler.lll` (full compiler bootstrap)

**Comparison baselines:** F# (primary), TypeScript (secondary), Python, Java (micro tier 3).

### Exit criteria

- [x] Token-efficiency claims are backed by versioned corpus data.
- [x] Benchmark methodology is explicit enough for reruns and diffs.

## Work package B — Compile-latency and self-host baselines

**Closed by (doc/policy):** methodology and baseline definition documented below. Actual timing measurements are tracked as an implementation gap in epic #108.

### Goal

Measure the operational cost of the self-host transition.

### Tasks

- [x] Define stage0-vs-self-host timing comparisons.
- [x] Define which build scenarios matter: single-file, multi-module, dependency-bearing, self-build.
- [x] Record stable baselines and variance guidance.

### Baseline definition and measurement approach (frozen)

**Two compiler stages:**

- `stage0`: the compiler built with the F# host toolchain (dotnet build from F# source).
- `self-host`: the compiler built by a previously compiled version of ll-lang (bootstrap scenario).

**Build scenarios to measure (in order of priority):**

| Scenario | Command | Why |
|----------|---------|-----|
| single-file | `lllc build spec/examples/valid/01-basics.lll` | Baseline latency; cold-start cost |
| multi-module stdlib | `lllc build stdlib/src/Map.lll` | Module resolution overhead |
| dependency-bearing | compile a file that imports multiple stdlib modules | Import graph traversal cost |
| self-build | compile `20-bootstrap-compiler.lll` | Full-compiler throughput; most representative |

**Measurement protocol:**
1. Run each scenario 5 times on a warmed process (discard first run).
2. Record median, p90, and max wall-clock time.
3. Record the git commit SHA of both stage0 and self-host at time of measurement.
4. Store results in `benchmarks/results/latency-<YYYY-MM-DD>.json`.

**Variance guidance:**
- A change of `< 10%` on median is within noise; do not flag.
- A change of `10–30%` on median is notable; add a comment in the PR.
- A change of `> 30%` on median is a regression; block or justify explicitly.

**Status:** baseline measurement not yet implemented. Tracked in epic #108. The protocol above is the contract implementers must satisfy.

### Exit criteria

- [x] Contributors can tell whether self-host changes improved or degraded compiler throughput. (Protocol defined; implementation pending.)
- [x] Performance discussions reference data, not anecdotes. (Methodology frozen; first data point is the responsibility of the self-host implementation milestone.)

## Work package C — Semantic equivalence corpus

**Closed by (doc/policy):** corpus requirements and equivalence definition documented below. Corpus population and gate wiring are tracked in epic #108.

### Goal

Ensure stable backends still mean the same thing.

### Tasks

- [x] Define representative corpus for semantic equivalence across supported backends.
- [x] Define what counts as acceptable divergence, if any.
- [x] Tie corpus outputs to release gates and regression review.

### Corpus requirements and equivalence definition (frozen)

**What the corpus must cover:**

| Category | Example files | Backends required |
|----------|--------------|-------------------|
| Primitive arithmetic and comparison | `01-basics.lll` | F#, TS |
| Sum types and pattern matching | `01-basics.lll`, micro samples | F#, TS |
| Parametric types | `Maybe`, `Result` patterns | F#, TS |
| Higher-order functions and closures | stdlib samples | F# |
| Recursive data structures | `Map.lll` (RBTree) | F# |
| Config parsing | `Toml.lll` | F# |
| Full bootstrap | `20-bootstrap-compiler.lll` | F# |

**What constitutes semantic equivalence:**

Semantic equivalence is satisfied if and only if:

1. Both backends compile the same `.lll` source without error.
2. The outputs, when executed against the same inputs, produce identical observable results (stdout, return values, no additional effects).
3. For pure functions: all tested inputs yield equal outputs. No tolerance for divergence on pure code.

**Acceptable divergence (exhaustive list):**

- Floating-point formatting differences across runtimes are acceptable if the numeric value is within `1e-9` relative error.
- Whitespace and newline normalization in string outputs is acceptable.
- Stack traces and error messages are not part of the equivalence contract.

**No other divergence is acceptable.** If a backend produces a different result for a pure function, it is a regression, not a known limitation.

**Gate wiring (policy):**

- Corpus outputs are captured as golden files in `benchmarks/corpus/`.
- A CI check compares compiled + executed output against golden files for every commit that touches a backend code generator.
- To update a golden file, a PR must include an explicit "Semantics change: intentional" note in the PR body and must be approved by a second reviewer.

**Status:** corpus files and CI gate not yet implemented. Tracked in epic #108. The definitions above are the contract.

### Exit criteria

- [x] Backend regressions are observable against a shared corpus. (Definition frozen; population pending.)
- [x] "Compiles" is not mistaken for "preserves language semantics". (Policy established; enforcement pending implementation.)

## Work package D — Release checklist and CI gates

**Closed by (doc/policy):** explicit release checklist with pass/fail criteria below.

### Goal

Turn `v2` release readiness into a concrete contract.

### Tasks

- [x] Define the required benchmark and test gates for `v2`.
- [x] Define which docs/spec checks are release blockers.
- [x] Define artifact publication or storage requirements for benchmark results.
- [x] Wire release checklist into docs and CI naming.

### v2 Release checklist (pass/fail)

All items must be PASS before a `v2.0.0` tag is created.

#### Gate 1 — Test suite (hard blocker)

- [ ] `dotnet test` passes with zero failures and zero skipped tests.
- [ ] No test is marked `[<Ignore>]` or equivalent without a linked issue.

#### Gate 2 — Token-efficiency benchmark (hard blocker)

- [ ] `benchmarks/token-benchmark.py` runs to completion without errors.
- [ ] `benchmarks/results/token-benchmark.json` is committed and dated within 7 days of release.
- [ ] Tier 1 F#/lll ratios for all four samples are documented and show no regression vs. the baseline in this file.

#### Gate 3 — Compile-latency baseline (hard blocker)

- [ ] `benchmarks/results/latency-<date>.json` exists and covers all four build scenarios defined in Work package B.
- [ ] Stage0 vs self-host median latency delta is documented. Any regression `> 30%` is either fixed or has an explicit justification note in the release PR.

#### Gate 4 — Semantic equivalence corpus (hard blocker)

- [ ] `benchmarks/corpus/` contains golden files for all corpus categories defined in Work package C.
- [ ] CI semantic equivalence check passes for all committed golden files.
- [ ] Any intentional semantics change has been reviewed by a second reviewer.

#### Gate 5 — Spec and docs completeness (hard blocker)

- [ ] `spec/grammar.ebnf` covers all syntax used in `spec/examples/valid/`.
- [ ] `docs/language-spec.md` documents all language features exercised in the corpus.
- [ ] No `TODO` or `FIXME` comments remain in `spec/` without a linked issue.

#### Gate 6 — Artifact storage (required, not a blocker for tag)

- [ ] Benchmark JSON results are stored in `benchmarks/results/` and committed.
- [ ] A `CHANGELOG` entry or release notes document the key numbers for `v2.0.0`.
- [ ] Benchmark results are tagged with the compiler git SHA and run date.

#### Warning-only gates (non-blocking for `v2.0.0`, must be tracked)

- Self-host bootstrap round-trip (compile the compiler with itself) — warning if not passing.
- TypeScript backend parity with F# for all Tier 1 samples — warning if not complete.

### CI naming convention

Gates 1–5 map to CI check names:

| Gate | CI check name |
|------|---------------|
| 1 | `test-suite` |
| 2 | `benchmark-token-efficiency` |
| 3 | `benchmark-compile-latency` |
| 4 | `semantic-equivalence-corpus` |
| 5 | `spec-docs-completeness` |

### Exit criteria

- [x] `v2` readiness can be answered by checking named gates.
- [x] Release evidence is reproducible, not manual folklore.

## Recommended implementation order

1. Work package A — token-efficiency benchmarks
2. Work package B — compile/self-host baselines
3. Work package C — semantic equivalence corpus
4. Work package D — release checklist and CI gates

## Definition of done for Milestone 7

`Milestone 7` is done only when all of the following are true:

- [x] token-efficiency benchmark suite exists and is versioned
- [ ] stage0 vs self-host performance baselines exist (tracked in epic #108)
- [ ] semantic equivalence corpus gates stable backends (tracked in epic #108)
- [x] release checklist and CI gates make `v2` readiness explicit

## Questions to clarify after Milestone 7

### Benchmark questions

- Which benchmark corpus examples are most representative of ll-lang's real value proposition?
- Should token counts be measured on raw source, canonicalized formatting, or prompt-ready snippets?

### Performance questions

- What regressions are acceptable during self-host transition versus post-`v2` stabilization?
- Which performance metrics belong in CI versus periodic benchmark runs?

### Release questions

- Which gates are hard blockers for `v2.0.0`, and which are warning-only for the first release?
- How are benchmark artifacts published or stored so they remain comparable over time?

## Non-goals for Milestone 7

- heroic micro-optimization unrelated to product claims
- backend-specific benchmark suites that do not map back to language-level guarantees
- vague "performance improvements" without corpus evidence
