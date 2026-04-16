# Contributing to ll-lang

Thank you for your interest in contributing. This document covers everything you need to get from zero to a merged pull request.

---

## Table of Contents

1. [Development Setup](#development-setup)
2. [Architecture Overview](#architecture-overview)
3. [Running Tests](#running-tests)
4. [How to Add a Test](#how-to-add-a-test)
5. [How to Add a Stdlib Function](#how-to-add-a-stdlib-function)
6. [PR Guidelines](#pr-guidelines)
7. [Where to Find Help](#where-to-find-help)

---

## Development Setup

**Requirements:**
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (preview builds work; check `dotnet --version`)
- Git

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
dotnet build
dotnet test
```

All tests should be green. The CI runs two jobs:

- `release-core` — the full stable test suite (all tests except experimental)
- `experimental` — reverse transpiler and LLVM backend tests (allowed to fail without blocking)

If `dotnet test` reports failures on a clean checkout, please [open an issue](https://github.com/Neftedollar/ll-lang/issues).

---

## Architecture Overview

The compiler is a linear pipeline. Each stage is a separate F# module in `src/LLLangCompiler/`.

```
Source (.lll)
    │
    ▼ Lexer.fs
    │   Tokenizes the source file. Emits synthetic INDENT and DEDENT tokens
    │   to represent significant indentation without introducing braces.
    │
    ▼ Parser.fs
    │   Recursive-descent parser. Consumes the token stream and produces an
    │   untyped surface AST (AST.fs). No type information at this stage.
    │
    ▼ Elaborator.fs
    │   Name resolution and declared-type checking. Catches tag violations,
    │   non-exhaustive matches, and unit mismatches before type inference.
    │   Produces error codes E001–E005.
    │
    ▼ HMInfer.fs
    │   Hindley-Milner type inference (Algorithm W). Unification, let-
    │   generalization, and trait dispatch. Produces a TypedAST (TypedAST.fs).
    │   Errors: E006 (no trait impl), E008 (occurs check).
    │
    ▼ Codegen*.fs
    │   One emitter per target:
    │     Codegen.fs      → F# (default)
    │     CodegenTS.fs    → TypeScript
    │     CodegenPy.fs    → Python
    │     CodegenJava.fs  → Java 21
    │     CodegenCS.fs    → C#
    │     CodegenLLVM.fs  → LLVM IR (experimental)
    │
    ▼ Compiler.fs
        End-to-end pipeline. Wires all stages together and dispatches to the
        correct codegen based on the --target flag.
```

**CLI layer** (`src/LLLangTool/`):
- `Program.fs` — entry point, parses CLI arguments, calls `Compiler.fs`
- `Mcp.fs` — MCP stdio server, exposes 10 structured tools to LLM clients

**Stdlib** (`stdlib/`): 10 `.lll` files written in ll-lang itself (self-hosted). The compiler bootstraps by compiling these before compiling user code.

**Tests** (`tests/LLLangTests/`): xUnit suite, one file per compiler stage or feature area.

**Spec** (`spec/`): Formal grammar (`grammar.ebnf`), type system rules, and the example corpus (`spec/examples/valid/` and `spec/examples/invalid/`). The corpus drives `CorpusInvalidTests.fs`.

---

## Running Tests

Run the full stable suite:

```bash
dotnet test
```

Run only a specific test file by class name:

```bash
dotnet test --filter "FullyQualifiedName~LexerTests"
dotnet test --filter "FullyQualifiedName~HMInferTests"
```

Run experimental tests (LLVM / reverse transpiler):

```bash
dotnet test --filter "FullyQualifiedName~ReverseTranspilerTests|FullyQualifiedName~CodegenLLVMTests"
```

The CI also runs two extra checks before build:
- `./tools/check-doc-contract.sh` — verifies doc contract files are up to date
- `./tools/check-no-fs3261-suppression.sh` — blocks accidental nullability suppression

Both run automatically in CI. Run them locally if you are touching docs or F# source.

---

## How to Add a Test

Tests live in `tests/LLLangTests/`. Each file covers one stage or feature area. Pick the closest existing file or add a new one.

### Unit test (xUnit)

```fsharp
// tests/LLLangTests/LexerTests.fs  (or any relevant *Tests.fs)
module LLLang.Tests.LexerTests

open Xunit
open LLLang.Lexer

[<Fact>]
let ``my new behaviour`` () =
    let result = tokenize "someInput"
    Assert.Equal(expected, result)
```

Key helpers available in most test files:

- `toks src` — tokenize and return token types, filtering whitespace
- `parse src` — parse and return the AST
- `compile src` — run the full pipeline, return `Result<string, string>`

### Corpus test (spec/examples)

For invalid programs, add a `.lll` file under `spec/examples/invalid/` with the expected error code on line 1:

```
-- expect: E003
module Examples.Incomplete

area(s Shape) Float =
  match s with
  | Circle r -> 3.14
```

`CorpusInvalidTests.fs` picks up all files in that directory automatically.

For valid programs (regression / snapshot), add a `.lll` file under `spec/examples/valid/`. These are exercised by `PipelineRealTests.fs`.

---

## How to Add a Stdlib Function

The stdlib is **written in ll-lang**, not F#. Adding a new built-in means adding it to the appropriate `.lll` file in `stdlib/`, not patching the compiler.

**Step 1 — Identify the right module.**

| Need | Module |
|------|--------|
| Collection / key-value | `stdlib/Map.lll` |
| Config parsing | `stdlib/Toml.lll` |
| String / token manipulation | `stdlib/Lexer.lll` |
| AST helpers | `stdlib/Parser.lll` or `stdlib/Elaborator.lll` |
| Full pipeline utilities | `stdlib/Compiler.lll` |

**Step 2 — Write the function in ll-lang.**

```
-- stdlib/Map.lll  (example: adding a `size` helper)

size(m Map[K, V]) Int =
  match m with
  | Empty         -> 0
  | Node _ l _ r  -> 1 + size l + size r
```

Follow the style of the surrounding code. Export any public function with `export`:

```
export size
```

**Step 3 — Add a test.**

Add a test in `tests/LLLangTests/StdlibTests.fs` (or a relevant `Stdlib*Tests.fs` file):

```fsharp
[<Fact>]
let ``Map.size empty is 0`` () =
    let src = """
module Test
import Map
main() = printfn (intToStr (Map.size Map.empty))
"""
    let result = runPipeline src
    Assert.Contains("0", result)
```

**Step 4 — Run the tests and check CI passes.**

```bash
dotnet test --filter "FullyQualifiedName~StdlibTests"
dotnet test   # full suite
```

---

## PR Guidelines

### Branch naming

```
feat/<short-description>       new feature
fix/<issue-number>-<slug>      bug fix
chore/<what>                   tooling, CI, formatting
docs/<what>                    documentation only
refactor/<what>                internal restructuring
```

### Commit style

We use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(parser): support multi-line string literals
fix(elaborator): correctly report column in E003
chore(ci): add .NET 10 preview channel pin
docs(stdlib): document Map.size
```

Keep commits focused. One logical change per commit.

### PR size

- **Preferred:** < 400 lines changed
- **Acceptable:** 400–800 lines if the change is cohesive (e.g., a whole new codegen target)
- **Large PRs:** split into smaller ones or open a discussion first

### Checklist before requesting review

- [ ] `dotnet build` passes with no warnings (`-warnaserror` is on)
- [ ] `dotnet test` passes (stable suite)
- [ ] New behaviour has at least one test
- [ ] If you touched error codes, `spec/error-codes.md` is updated
- [ ] If you changed the grammar, `spec/grammar.ebnf` is updated

### What to expect

- CI runs automatically on every PR
- We aim to review within a few days
- Feedback is focused on correctness and consistency with the language design

---

## Where to Find Help

- **GitHub Issues** — bugs, unexpected behaviour, feature requests:
  [github.com/Neftedollar/ll-lang/issues](https://github.com/Neftedollar/ll-lang/issues)

- **GitHub Discussions** — questions, design ideas, show-and-tell:
  [github.com/Neftedollar/ll-lang/discussions](https://github.com/Neftedollar/ll-lang/discussions)

- **Docs** — start with [`docs/compiler-dev/`](docs/compiler-dev/) for compiler internals,
  [`docs/user-guide/`](docs/user-guide/) for the language itself,
  and [`docs/language-spec.md`](docs/language-spec.md) for the formal spec.
