# ll-lang User Guide

A short tour of ll-lang for programmers (human or LLM) who want to write `.lll`
programs and run them on .NET.

ll-lang is a statically typed functional language with Hindley-Milner inference,
algebraic data types, traits, semantic tags, and unit algebra. It compiles to F#
source, which is then built or executed on .NET 10.

## Contents

1. [Installation](01-installation.md) — build the compiler, run hello world
2. [Syntax](02-syntax.md) — modules, `let`, `fn`, lambdas, `if`, `match`, `type`, `tag`, `trait`, `impl`
3. [Types and inference](03-types-and-inference.md) — H-M, polymorphism, when to annotate
4. [Tags and units](04-tags-and-units.md) — newtype-style tags, phantom types, unit algebra
5. [Traits](05-traits.md) — declarations, impls, dispatch
6. [Modules](06-modules.md) — module headers, imports, current limitations
7. [Error codes](07-error-codes.md) — E001..E008 catalog with reproducible examples
8. [CLI](08-cli.md) — `lllc build`, `lllc run`, emitted `.fs` files
9. [LLM prompting](09-llm-prompting.md) — short guide on prompting an LLM to write ll-lang

## Status

Phases 1 to 5 are complete. The compiler lexes, parses, elaborates, performs
H-M inference, and emits F# source. Planned but not yet implemented:

- Standard library (`Std.List`, `Std.Maybe`, etc.) is referenced by the spec
  but not available at runtime. Only `printfn` is wired as a builtin.
- Multi-file modules. Each `.lll` file is compiled in isolation; cross-file
  imports are parsed but not resolved.
- String operations, file IO, numeric tower beyond `Int`/`Float`.
- Non-.NET backends.
