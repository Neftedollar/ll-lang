# ll-lang Design Vision

**Status:** vision and research backlog  
**Not authoritative for current behavior**  
**Canonical behavior spec:** [language-spec.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/language-spec.md)

## Purpose

This document records the broader design intent behind ll-lang: why the
language exists, what long-term qualities it should preserve, and which ideas
are worth exploring after the stable `1.x` line.

It is intentionally not the source of truth for the shipped language surface.
Use this document for:

- product motivation
- long-range language design direction
- research candidates for `v2` and beyond
- tradeoff framing when choosing between equally viable implementations

## Core thesis

ll-lang exists to make typed program generation easier for LLM systems without
collapsing into an underspecified DSL.

The core bets remain:

1. **Token efficiency** matters because verbose syntax steals context budget.
2. **Compiler as oracle** matters because fast static feedback beats runtime
   debugging in agent loops.
3. **Unambiguous surface** matters because multiple equivalent syntaxes amplify
   generation errors.
4. **Self-hosting** matters because a language is more credible when it can
   express and extend its own compiler.

## Stable design principles

These principles should survive implementation changes:

- keep the core surface small
- prefer one canonical way to express a thing
- preserve strict evaluation unless laziness is explicit
- move LLM-specific leverage into tooling and docs where possible
- make compiler diagnostics compact and machine-usable
- treat docs as part of the product, not as commentary

## Long-range design targets

These are the intended directions, not promises for the current shipped line.

### 1. Pure ll-lang core

The canonical compiler, stdlib, project resolver, and toolchain logic should be
owned by ll-lang, with host-language bootstrap retained only as stage0 support.

### 2. Compiler-oriented standard library

The stdlib should optimize for:

- parser construction
- state threading
- structured diagnostics
- tree transforms
- project/config loading
- testable backend emission

This is more important than academic completeness.

### 3. LLM operating environment

The language should be surrounded by:

- MCP tools
- prompt packs
- compact repair recipes
- benchmark suites
- executable docs/examples

The goal is not “AI syntax”. The goal is a language that works exceptionally
well inside an AI-assisted development loop.

## Research backlog

These ideas remain interesting, but are explicitly not current source-of-truth
behavior:

- generalized HKT/typeclass solving
- constrained inference beyond the current HM-first model
- capability/effect systems
- S-expression IR or macro notation
- richer optimizer and IR pipeline work
- broader bootstrap independence beyond .NET stage0

Each of these needs its own spec and roadmap before becoming planned work.

## Relationship to v2 planning

For concrete `v2` work, use:

- [compiler-dev/12-v2-language-architecture.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/compiler-dev/12-v2-language-architecture.md)
- [compiler-dev/13-v2-implementation-roadmap.md](/Users/roman/Documents/dev/tens/code/ll-lang/docs/compiler-dev/13-v2-implementation-roadmap.md)
- the `spec/v2-*.md` companion specs

This document stays intentionally lighter-weight and more aspirational than the
tracked engineering plan.
