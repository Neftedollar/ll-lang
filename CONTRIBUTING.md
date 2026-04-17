# Contributing to ll-lang

Thanks for your interest! ll-lang is a small compiler project — contributions welcome at any level.

## Quick orientation

```
spec/             formal grammar (EBNF), error codes, example corpus
src/LLLangCompiler/  compiler library (F#): Lexer, Parser, Elaborator, HMInfer, Codegen*
src/LLLangTool/   lllc CLI + MCP server
stdlib/src/       self-hosted stdlib in ll-lang (~6000 LOC)
tests/LLLangTests/ xUnit test suite
docs/             user guide + compiler developer docs
```

## Set up

Requires [.NET 10](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
dotnet build
dotnet test
```

## Ways to contribute

### Add a stdlib function

1. Open `stdlib/src/<Module>.lll` (e.g. `Std.List`, `Std.Str`, `Std.Map`)
2. Add your function with at least one test using `Std.Test`
3. Run: `dotnet src/LLLangTool/bin/Debug/net10.0/lllc.dll run stdlib/src/<Module>.lll`

Example test pattern:
```lll
-- at the bottom of the module file
main() =
  testEq "myFunc basic" (myFunc 42) 42
  testEq "myFunc edge" (myFunc 0) 0
```

### Fix a compiler bug

1. Add a test case under `spec/examples/invalid/` (name: `NN-description.lll`, first line: `-- expect: EXXX`)
2. Or add an xUnit test in `tests/LLLangTests/`
3. Fix the relevant file in `src/LLLangCompiler/`
4. Run `dotnet test` to verify

### Add codegen for a new construct

All codegen targets live in parallel:
- `src/LLLangCompiler/Codegen.fs` — F#
- `src/LLLangCompiler/CodegenTS.fs` — TypeScript
- `src/LLLangCompiler/CodegenPy.fs` — Python
- `src/LLLangCompiler/CodegenJava.fs` — Java
- `src/LLLangCompiler/CodegenCSharp.fs` — C#

A change to one usually needs all five. Add a parity test to `benchmarks/` if the construct is non-trivial.

### Improve documentation

User guide lives in `docs/user-guide/`. Each file maps to a topic:
- `01-installation.md`, `02-quickstart.md`, ..., `08-cli.md`, `09-mcp.md`

## Error code format

All diagnostics must follow `EXXX line:col Name details` — one line, parseable by regex.
Reserve new codes in `spec/error-codes.md` before using them.

## PR checklist

- [ ] `dotnet test` passes (green CI badge)
- [ ] New language feature: updated `spec/grammar.ebnf` + example in `spec/examples/valid/`
- [ ] New stdlib function: self-test in the module file
- [ ] New error code: entry in `spec/error-codes.md`
- [ ] Codegen change: all 5 targets updated + parity test

## Code style

- F#: follow existing conventions, `<Nullable>enable</Nullable>` — no new nullness warnings
- ll-lang: use `import Std.X` — never copy types (`Maybe`, `List`, etc.) into new files
- Tests: prefer xUnit `[<Fact>]` for new test files

## Questions?

Open a [GitHub Discussion](https://github.com/Neftedollar/ll-lang/discussions) or an issue with the `question` label.
