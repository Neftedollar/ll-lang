# v2 LLM Operating System Execution

**Status:** active planning document  
**Audience:** implementers of `Milestone 6`  
**Parent docs:** [v2 implementation roadmap](13-v2-implementation-roadmap.md), [v2 language architecture](12-v2-language-architecture.md), [v2 llm tooling spec](../../spec/v2-llm-tooling.md)

## Summary

`Milestone 6` is where ll-lang becomes not just a language that is easy for
LLMs to write, but a language with an explicit operating environment for LLM
authoring.

The center of gravity is:

- MCP tools
- machine-readable docs/contracts
- prompt packs
- compact repair recipes
- discoverability of grammar, stdlib, project graph, and diagnostics

This milestone does not justify bloating the language grammar. It justifies
making the surrounding tooling intentional.

## Status model

- `[x]` done in current repo and should be preserved
- `[ ]` not done or not yet canonical for `v2`

## Current-repo baseline

- [x] MCP already exists as part of docs/tooling story
- [x] docs already treat LLM use as a first-class design concern
- [x] companion `v2-llm-tooling` spec exists

Still not done enough for `v2`:

- [ ] grammar, stdlib, project system, and diagnostics are not yet fully discoverable through a coherent machine-facing contract
- [ ] prompt packs are not yet versioned as product surface
- [ ] LLM authoring conventions are not yet one canonical documentation path
- [ ] common repair workflows are not yet stabilized as examples/contracts

## Work package A — MCP contract surface

### Goal

Make the core compiler/toolchain surfaces discoverable without scraping prose.

### Tasks

- [ ] Freeze which compiler/project/stdlib capabilities must be reachable through MCP.
- [ ] Define stable machine-facing shapes for grammar lookup, stdlib lookup, project graph inspection, and diagnostics retrieval.
- [ ] Ensure MCP docs match actual product surface.

### Exit criteria

- An LLM client can inspect the language and a project without reverse-engineering human docs.
- MCP is treated as product surface, not incidental integration.

## Work package B — Prompt packs and repair recipes

### Goal

Version the practical guidance that makes ll-lang productive for LLMs.

### Tasks

- [ ] Define canonical prompt-pack format and ownership.
- [ ] Define compact repair recipes for common compiler/project errors.
- [ ] Version prompt packs alongside compiler/language changes.

### Exit criteria

- Prompt guidance is reproducible and version-aware.
- Repair flows are not trapped in ad hoc chat history.

## Work package C — Canonical LLM authoring conventions

### Goal

Describe how ll-lang should be written for compactness and reliability.

### Tasks

- [ ] Publish canonical guidance for preferred idioms, naming, module layout, and error-driven repair loops.
- [ ] Ensure conventions match the actual `v2` syntax and stdlib surface.
- [ ] Keep conventions short enough for repeated LLM use.

### Exit criteria

- There is one canonical “how to write ll-lang well with an LLM” path in docs.
- Conventions align with shipped language and stdlib, not aspirational syntax.

## Work package D — Machine-checked workflow examples

### Goal

Back the LLM workflow story with executable examples.

### Tasks

- [ ] Add machine-checked examples for compile, check, repair, project inspection, and stdlib usage flows.
- [ ] Ensure examples remain small enough to serve as prompt seeds.
- [ ] Keep them synchronized with changing language surface.

### Exit criteria

- LLM workflow docs are backed by examples that actually compile or run through the intended flow.
- Example drift becomes visible early.

## Recommended implementation order

1. Work package A — MCP contract surface
2. Work package B — prompt packs and repair recipes
3. Work package C — canonical authoring conventions
4. Work package D — machine-checked workflow examples

## Definition of done for Milestone 6

`Milestone 6` is done only when all of the following are true:

- grammar, stdlib, project graph, and diagnostics have coherent machine-facing surfaces
- prompt packs and repair recipes are versioned artifacts
- one canonical LLM authoring guide exists
- examples for the main LLM workflows are machine-checked

## Questions to clarify after Milestone 6

### MCP questions

- Which tool outputs must be stable enough to treat as semver-sensitive public surface?
- Do we need a split between human-readable and machine-readable diagnostics, or is one shape enough?

### Prompt-pack questions

- Should prompt packs live with compiler source, docs, or a dedicated tooling directory?
- How much model-specific tuning are we willing to encode versus staying model-agnostic?

### Workflow questions

- Which authoring/repair loops are common enough to deserve first-class examples?
- Are there any docs that still force scraping prose where structured lookup should exist instead?

## Non-goals for Milestone 6

- changing core syntax purely for AI hype
- shipping a model-specific agent framework as part of the language core
- replacing docs with prompts
