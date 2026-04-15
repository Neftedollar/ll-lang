# v2 Benchmarks and Release Gates Execution

**Status:** active planning document  
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

- [ ] token-efficiency claims are not yet backed by a stable benchmark suite
- [ ] self-hosted-vs-stage0 performance deltas are not yet tracked
- [ ] semantic equivalence across backends is not yet formalized as a benchmark/corpus gate
- [ ] `v2` release checklist is not yet a concrete CI-backed contract

## Work package A — Token-efficiency benchmark suite

### Goal

Measure the actual compactness advantage ll-lang claims to provide.

### Tasks

- [ ] Define benchmark corpus categories: data modeling, parser combinators, stateful passes, config parsing, multi-module projects.
- [ ] Define comparison baselines: F#, TypeScript, Python, Java, C#.
- [ ] Freeze token-count methodology.
- [ ] Store benchmark artifacts in a reproducible form.

### Exit criteria

- Token-efficiency claims are backed by versioned corpus data.
- Benchmark methodology is explicit enough for reruns and diffs.

## Work package B — Compile-latency and self-host baselines

### Goal

Measure the operational cost of the self-host transition.

### Tasks

- [ ] Define stage0-vs-self-host timing comparisons.
- [ ] Define which build scenarios matter: single-file, multi-module, dependency-bearing, self-build.
- [ ] Record stable baselines and variance guidance.

### Exit criteria

- Contributors can tell whether self-host changes improved or degraded compiler throughput.
- Performance discussions reference data, not anecdotes.

## Work package C — Semantic equivalence corpus

### Goal

Ensure stable backends still mean the same thing.

### Tasks

- [ ] Define representative corpus for semantic equivalence across supported backends.
- [ ] Define what counts as acceptable divergence, if any.
- [ ] Tie corpus outputs to release gates and regression review.

### Exit criteria

- Backend regressions are observable against a shared corpus.
- “Compiles” is not mistaken for “preserves language semantics”.

## Work package D — Release checklist and CI gates

### Goal

Turn `v2` release readiness into a concrete contract.

### Tasks

- [ ] Define the required benchmark and test gates for `v2`.
- [ ] Define which docs/spec checks are release blockers.
- [ ] Define artifact publication or storage requirements for benchmark results.
- [ ] Wire release checklist into docs and CI naming.

### Exit criteria

- `v2` readiness can be answered by checking named gates.
- Release evidence is reproducible, not manual folklore.

## Recommended implementation order

1. Work package A — token-efficiency benchmarks
2. Work package B — compile/self-host baselines
3. Work package C — semantic equivalence corpus
4. Work package D — release checklist and CI gates

## Definition of done for Milestone 7

`Milestone 7` is done only when all of the following are true:

- token-efficiency benchmark suite exists and is versioned
- stage0 vs self-host performance baselines exist
- semantic equivalence corpus gates stable backends
- release checklist and CI gates make `v2` readiness explicit

## Questions to clarify after Milestone 7

### Benchmark questions

- Which benchmark corpus examples are most representative of ll-lang’s real value proposition?
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
- vague “performance improvements” without corpus evidence
