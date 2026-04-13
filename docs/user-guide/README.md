# ll-lang User Guide

A practical guide for programmers (human or LLM) who want to write `.lll`
programs, compile them with `lllc`, and target F#, TypeScript, Python, Java,
C#, or LLVM IR.

ll-lang is a statically typed functional language with Hindley-Milner inference,
algebraic data types, traits, semantic tags, and unit algebra.

## Contents

1. [Installation](01-installation.md) — build the compiler, run hello world
2. [Syntax](02-syntax.md) — modules, bindings, lambdas, `if`, `match`, ADTs, traits
3. [Types and inference](03-types-and-inference.md) — H-M, polymorphism, annotations
4. [Tags and units](04-tags-and-units.md) — tags, phantom types, unit algebra
5. [Traits](05-traits.md) — declarations, impls, constrained generics, dispatch
6. [Modules](06-modules.md) — module headers, imports, project mode (`lll.toml`)
7. [Error codes](07-error-codes.md) — compact diagnostics (`E001..E008`, `E026`)
8. [CLI](08-cli.md) — build/check/run/new/install/reverse/self commands
9. [MCP server](09-mcp.md) — `lllc mcp` tools for LLM clients
10. [Compilation targets](10-targets.md) — `--target fs|ts|py|java|cs|llvm`
11. [Java target details](11-java-target.md) — Java backend mapping details
12. [LLM prompting](09-llm-prompting.md) — short guide for ll-lang prompting

## Status

Compiler phases 1 to 10 are complete. Current stable surface includes:

- End-to-end pipeline (`lexer -> parser -> elaborator -> HM infer -> codegen`)
- Multi-file projects with `lll.toml` and target selection
- Self-hosted stdlib/compiler bootstrap flow
- MCP server for structured LLM tooling
- Targets: F#, TypeScript, Python, Java, C#, LLVM IR

Current caveat:

- LLVM backend is intentionally subset-based compared to the primary F# backend.
