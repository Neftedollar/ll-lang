# Why ll-lang

ll-lang is not a general-purpose language. It is optimized for one use case: **LLM agents writing correct code on the first attempt.**

Every design decision is evaluated against that goal.

---

## The problem with mainstream languages for LLM codegen

When an LLM generates Python or TypeScript, two things compound:

1. **Verbose syntax.** `function`, `type`, `interface`, `class`, `return`, `{`, `}`, `;` — ceremony that consumes tokens without encoding logic. An LLM generating a simple ADT plus a pattern match spends half its token budget on structural punctuation.

2. **Late error detection.** Type errors surface at runtime, after execution, often after damage is done. An LLM generating a 200-line module gets no signal until it runs — and the error message is a stack trace written for humans, not for a model to parse programmatically.

The feedback loop is: generate → execute → read prose error → regenerate. Each iteration burns context.

---

## The ll-lang answer

Four properties, all working together:

### 1. Token-efficient syntax

No `fn`, `type`, `let`, `in`, `then`, `with`, `return`, `{}`, `;`. 12 keywords total. Declarations use an uppercase/lowercase naming convention instead of reserved words.

**Benchmark numbers** (cl100k_base tokenizer, from `benchmarks/results/`):

| Pattern | ll-lang | F# | TypeScript | Python | Java |
|---------|---------|-----|------------|--------|------|
| 3-constructor sum type | 11 | 14 | 36 | 49 | 35 |
| Pattern match (3 arms) | 39 | 43 | 58 | 52 | 58 |
| Curried function | 13 | 21 | 20 | 18 | 16 |
| Parametric ADT | 8 | 12 | 23 | 47 | 31 |

On real code (the Map red-black tree implementation): ll-lang is **17% more compact than compiled F#**, and **1.3–5.9× more compact than TypeScript/Python/Java** on type-heavy code.

The bootstrap compiler (2,900+ lines of ll-lang) is 8% more compact than its F# output — an entire compiler, measured end-to-end.

### 2. Compiled = works

The compiler catches at compile time what other languages defer to runtime:

| Error | Code | Example |
|-------|------|---------|
| Type mismatch | `E001` | `E001 12:5 TypeMismatch Str Str[UserId]` |
| Non-exhaustive match | `E003` | `E003 15:1 NonExhaustiveMatch Shape missing:Empty` |
| Unit mismatch | `E004` | `E004 20:9 UnitMismatch Float[m] Float[s]` |
| Tag violation | `E005` | `E005 7:14 TagViolation Str[Email] Str[UserId]` |

No null, no exceptions, no mutable state. If `lllc build` succeeds, the logic is correct by construction.

For an LLM agent, this turns code generation into a tight loop: write → `lllc check` → fix one error code → repeat. No execution required.

### 3. LLM-readable errors

Every error is one line, machine-readable, regex-parseable:

```
EXXX line:col ErrorKind details
```

```
E001 12:5  TypeMismatch   expected:Str[UserId] got:Str   hint:wrap:UserId
E003 15:1  NonExhaustiveMatch  type:Shape missing:Empty
E008 3:1   InfiniteType   var:a cycle:a=List[a]
```

No stack traces. No paragraphs. An LLM can `lookup_error E003` via the MCP server and get the exact fix — no scraping required.

### 4. MCP integration

`lllc mcp` runs a stdio MCP server with 10 tools. Wire it to Claude Code, Cursor, or Zed:

```json
{
  "mcpServers": {
    "lllc": {
      "command": "lllc",
      "args": ["mcp"]
    }
  }
}
```

An agent can now:

```
check_source   → validate syntax + types (no codegen, fast)
compile_source → get generated F#/TS/Py/Java
run_file       → compile + execute
lookup_error   → get explanation + fix for E003
stdlib_search  → find functions by name or type
grammar_lookup → get EBNF production for any grammar rule
```

The agent asks "does this compile?" and gets a structured JSON response with error codes, line numbers, and fix hints. No shell output parsing. No false positives from warnings mixed into stdout.

---

## Self-hosting: the compiler is written in itself

The ll-lang compiler is self-hosted. The entire pipeline — lexer, parser, elaborator, Hindley-Milner inference, and all four codegens — is written in ll-lang (5,857 LOC across 10 stdlib modules).

Bootstrap fixpoint: `compiler₁.fs == compiler₂.fs`. Compile the compiler with itself, compare output byte-for-byte — they match.

This is not just a parlor trick. It means:

- **The language is expressive enough for production compiler work.** Recursive descent parsers, red-black trees, Algorithm W type inference, multi-target code emission — all in ll-lang.
- **The stdlib is real code, not toy examples.** Every idiom in the tutorials works at scale.
- **The compiler validates itself.** Any change to the language that breaks the stdlib breaks the bootstrap test.

The 529-test suite includes 97 inline stdlib tests that run the self-hosted compiler end-to-end on every commit.

---

## Multi-target output

Write logic once, compile to any runtime:

```bash
lllc build --target fs   # F# discriminated unions
lllc build --target ts   # TypeScript sealed interfaces
lllc build --target py   # Python @dataclass + Union
lllc build --target java # Java 21 sealed interfaces + records
```

An LLM agent can prototype in ll-lang (fast iteration, compile-time safety), then ship to whichever runtime the rest of the team uses.

---

## When to reach for ll-lang

ll-lang is the right tool when:

- An LLM agent is generating non-trivial type-safe logic
- You need to compile to multiple runtimes from one source
- You want compile errors, not runtime crashes, as the feedback signal
- Token budget matters (context windows are finite)

ll-lang is not the right tool when:

- You need rich ecosystem integrations (use the target language directly)
- You need async IO, WASM, or native binaries today (on the roadmap)
- The team does not want to add a compiler dependency

---

## Getting started

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang && dotnet build
lllc run hello.lll
```

Tutorials: [01 Hello World](tutorials/01-hello-world.md) · [02 Types and Patterns](tutorials/02-types-and-patterns.md) · [03 Building a Parser](tutorials/03-building-a-parser.md) · [04 Multi-target](tutorials/04-multi-target.md)
