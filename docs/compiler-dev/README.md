# ll-lang Compiler Developer Guide

For contributors working on the ll-lang compiler itself. Assumes you know
F# well enough to read it; the compiler is ~3.8k lines across the source
tree and deliberately avoids external parser/inference libraries.

## Contents

1. [Architecture overview](01-architecture-overview.md) — pipeline, project layout, F# compile order
2. [Lexer](02-lexer.md) — tokens, INDENT/DEDENT synthesis, position tracking
3. [Parser](03-parser.md) — recursive descent, expression precedence, quirks
4. [Elaborator](04-elaborator.md) — declared-type checking, E001-E005, exhaustiveness
5. [H-M inference](05-hm-inference.md) — Algorithm W, `Subst`, unify, generalize/instantiate, occurs check
6. [Code generation](06-codegen.md) — F# emission, `[<EntryPoint>]`, `dotnet fsi` fix-ups
7. [Testing](07-testing.md) — xUnit layout, helpers, corpus drivers
8. [Adding an error code](08-adding-an-error-code.md) — end-to-end walkthrough
9. [Self-hosting roadmap](09-self-hosting-roadmap.md) — Phase 7 plan

## Repository layout

```
ll-lang/
├── spec/
│   ├── grammar.ebnf              formal grammar
│   ├── type-system.md            H-M rules, tag system, phantom types
│   ├── error-codes.md            E001..E008 catalog
│   └── examples/
│       ├── valid/                corpus of working programs
│       └── invalid/              programs with expected error codes
├── src/
│   ├── LLLangCompiler/           compiler library (F#)
│   │   ├── Token.fs              Tok type
│   │   ├── Lexer.fs              tokenizer with layout
│   │   ├── AST.fs                untyped surface AST
│   │   ├── Parser.fs             recursive-descent parser
│   │   ├── Elaborator.fs         name resolution, E001-E005, exhaustiveness
│   │   ├── Types.fs              TypeScheme, Subst, generalize, instantiate
│   │   ├── TypedAST.fs           typed AST after inference
│   │   ├── HMInfer.fs            Algorithm W, unify, trait dispatch
│   │   ├── Codegen.fs            F# source emitter
│   │   ├── Compiler.fs           pipeline entry point
│   │   └── LLLangCompiler.fsproj
│   └── LLLangTool/               lllc CLI (build/run commands)
│       ├── Program.fs
│       └── LLLangTool.fsproj
├── tests/
│   └── LLLangTests/              xUnit suite (415 tests)
│       ├── LexerTests.fs           RealLexerTests.fs
│       ├── ParserTests.fs          ArithmeticParserTests.fs
│       │                           TypeParserTests.fs   FnParserTests.fs
│       │                           ExprParserTests.fs   ModuleParserTests.fs
│       ├── ElaboratorTests.fs      ElaboratorRealTests.fs
│       ├── HMInferTests.fs         HMInferRealTests.fs
│       ├── CodegenTests.fs         CodegenRealTests.fs
│       ├── PipelineRealTests.fs
│       ├── StdlibTests.fs
│       └── BootstrapCompilerTests.fs  -- bootstrap compiler corpus
├── docs/                         user guide + compiler-dev guide (this tree)
└── README.md
```

## Build and test

```bash
dotnet build                      # all three projects
dotnet test                       # run xUnit suite (415 tests)
```

The compiler library targets `net10.0` with `LangVersion=preview` and
`Nullable=enable`, and has no external package dependencies. Tests
depend on `xunit 2.6.3` and `Microsoft.NET.Test.Sdk 17.8.0`.

## Conventions

- **No external parser generators.** Lexer and parser are hand-written
  recursive descent for transparency.
- **No mutable global state.** Inference uses a small `InferState` record
  passed through the tree walk.
- **Errors are collected, not raised.** Compiler functions return
  `Result<T, LLError list>`, never throw on a type error.
- **Examples are the source of truth.** Every feature must have a valid
  corpus entry in `spec/examples/valid/` and each error code must have an
  invalid corpus entry in `spec/examples/invalid/`.
