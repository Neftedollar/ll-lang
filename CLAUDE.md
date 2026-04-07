# ll-lang — Compiler

Language: F#  
Target: .NET 8  
Build: `dotnet build`  
Test: `dotnet test`

## Structure

- `spec/` — formal grammar, type system rules, example corpus
- `src/LLLangCompiler/` — compiler source (Phase 2+)
- `tests/LLLangTests/` — test suite (Phase 2+)

## Spec

All compiler behaviour is driven by `spec/grammar.ebnf` and the example corpus
in `spec/examples/`. Invalid examples declare `-- expect: EXXX` on line 1.

## Key design decisions

See `../../docs/superpowers/specs/2026-04-07-ll-lang-design.md`
