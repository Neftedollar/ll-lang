# v2 LLM Operating System Execution

**Status:** doc/policy phase complete — implementation gaps tracked in epic #98
**Closes:** #100 #102 #104 #106
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

- [x] grammar, stdlib, project system, and diagnostics are not yet fully discoverable through a coherent machine-facing contract
- [x] prompt packs are not yet versioned as product surface
- [x] LLM authoring conventions are not yet one canonical documentation path
- [x] common repair workflows are not yet stabilized as examples/contracts

## Work package A — MCP contract surface

**Closed by:** `docs/user-guide/09-mcp.md` — complete tool list with input/output contracts;
`docs/llm-best-practices.md` §8 — MCP tool reference table.

### Goal

Make the core compiler/toolchain surfaces discoverable without scraping prose.

### Tasks

- [x] Freeze which compiler/project/stdlib capabilities must be reachable through MCP.
- [x] Define stable machine-facing shapes for grammar lookup, stdlib lookup, project graph inspection, and diagnostics retrieval.
- [x] Ensure MCP docs match actual product surface.

### Exit criteria

- An LLM client can inspect the language and a project without reverse-engineering human docs.
- MCP is treated as product surface, not incidental integration.

### MCP tool contract surface (frozen for v2)

The following 10 tools form the stable MCP contract surface. Each tool is
reachable via `lllc mcp` (stdio). All inputs and outputs are JSON.

| Tool | Purpose | Key input fields | Output shape |
|------|---------|-----------------|--------------|
| `check_source` | Type-check source text, no codegen | `source: Str` | `{ ok, errors[] }` |
| `check_file` | Type-check a `.lll` file, no codegen | `path: Str` | `{ ok, errors[] }` |
| `compile_source` | Compile source text, return generated code | `source: Str`, `target?: Str` | `{ ok, errors[], target, <lang>: Str }` |
| `compile_file` | Compile a `.lll` file | `path: Str`, `target?: Str`, `include_output?: Bool` | `{ ok, errors[], target }` |
| `run_file` | Compile + execute via `dotnet run` | `path: Str` | `{ exit_code, stdout, stderr, errors[] }` |
| `list_errors` | All known error codes + descriptions | _(none)_ | `[{ code, name, description }]` |
| `lookup_error` | Detailed info + repro for one error code | `code: Str` | `{ found, code, name, description, example }` |
| `stdlib_search` | Search stdlib by name or type sig | `query: Str` | `[{ name, signature, module, scope }]` |
| `grammar_lookup` | EBNF production for a grammar rule | `rule: Str` | `{ found, rule, production }` |
| `project_info` | Project metadata from `lll.toml` | `path: Str` | `{ root, manifest_path, manifest_kind, manifest, modules[], deps[], errors[] }` |

Compile targets: `fs` (F#, default), `ts` (TypeScript), `py` (Python), `java` (Java).
Error shape: `{ code: Str, line: Int, col: Int, message: Str }`.

---

## Work package B — Prompt packs and repair recipes

**Closed by:** canonical repair recipe templates added below;
`docs/llm-best-practices.md` §7 (anti-patterns) + §6 (common patterns) provide versioned authoring guidance.

### Goal

Version the practical guidance that makes ll-lang productive for LLMs.

### Tasks

- [x] Define canonical prompt-pack format and ownership.
- [x] Define compact repair recipes for common compiler/project errors.
- [x] Version prompt packs alongside compiler/language changes.

### Exit criteria

- Prompt guidance is reproducible and version-aware.
- Repair flows are not trapped in ad hoc chat history.

### Canonical repair recipes

Each recipe follows the pattern: **error code → cause → minimal fix**.
These live here and in `docs/llm-best-practices.md`. Versioned with the compiler.

#### Recipe: E001 TypeMismatch

```
Symptom : E001 <line>:<col> expected <T1> got <T2>
Cause   : expression type does not match the expected type in context
Fix     : check function parameter annotations; ensure all match arms return
          the same type; do not mix Int and Float without explicit conversion
Example :
  -- bad
  add(a Int)(b Float) = a + b   -- E001: Int vs Float
  -- good
  add(a Float)(b Float) = a + b
```

#### Recipe: E002 UnboundVar

```
Symptom : E002 <line>:<col> unbound identifier <name>
Cause   : name not in scope — typo, missing import, or wrong casing
Fix     : check spelling; add `import Std.X` for stdlib functions;
          remember types/constructors are Uppercase, values are lowercase
Example :
  -- bad
  f() = ListMap double [1 2 3]   -- E002: ListMap (capital L)
  -- good
  import Std.List
  f() = listMap double [1 2 3]
```

#### Recipe: E003 NonExhaustiveMatch

```
Symptom : E003 <line>:<col> non-exhaustive pattern match on <Type>
Cause   : not all constructors of a sum type are covered
Fix     : add the missing constructor arm(s), or add `| _ -> ...` catch-all
Example :
  -- bad
  area(s Shape) =
    | Circle r -> r * r    -- missing Rect, Empty
  -- good
  area(s Shape) =
    | Circle r  -> r * r
    | Rect w h  -> w * h
    | Empty     -> 0.0
```

#### Recipe: E004 DuplicateDefinition

```
Symptom : E004 <line>:<col> duplicate definition of <name>
Cause   : name defined twice in the same module scope
Fix     : remove or rename the duplicate; clause sugar arms are not
          separate definitions — use the `|` arm form inside one function body
```

#### Recipe: Missing module header

```
Symptom : parse error on line 1 (any error if first token is not `module`)
Cause   : every .lll file must start with `module Path.Name`
Fix     : add `module MyApp.ModuleName` as the very first line
```

#### Prompt-pack format

A prompt pack is a short, self-contained Markdown snippet that can be prepended
to a chat context. Format:

```markdown
## ll-lang context (v<VERSION>)
<language overview — max 200 tokens>
<syntax quick reference — max 150 tokens>
<MCP tool hint — max 50 tokens>
```

Prompt packs live in `docs/prompt-packs/`. Version tag matches compiler release.
Model-agnostic — no model-specific instructions inside packs.

---

## Work package C — Canonical LLM authoring conventions

**Closed by:** `docs/llm-best-practices.md` — frozen as the single canonical
"how to write ll-lang well with an LLM" path. Matches shipped v2 syntax.

### Goal

Describe how ll-lang should be written for compactness and reliability.

### Tasks

- [x] Publish canonical guidance for preferred idioms, naming, module layout, and error-driven repair loops.
- [x] Ensure conventions match the actual `v2` syntax and stdlib surface.
- [x] Keep conventions short enough for repeated LLM use.

### Exit criteria

- There is one canonical "how to write ll-lang well with an LLM" path in docs.
- Conventions align with shipped language and stdlib, not aspirational syntax.

### Frozen authoring conventions (v2)

The following conventions are frozen for v2. They are sourced from
`docs/llm-best-practices.md` and must not be changed without a versioned update.

1. **No keyword clutter.** No `fn`, `type`, `in`, `then`, `with`. Start types
   with Uppercase; start functions and values with lowercase.
2. **Annotate params, not returns.** `(name Type)` form is always required for
   function params. Return types are inferred by HM; annotate only when the
   inference is ambiguous.
3. **Clause sugar over explicit match.** Prefer `f(x T) = | P -> ...` over
   `f(x T) = match x | P -> ...` when matching on the last param.
4. **Pipe for chains.** `xs -> listMap double -> listLen` over
   `listLen (listMap double xs)`.
5. **One module per file, header on line 1.** `module Path.Name` must be the
   first token.
6. **Side effects via `_` binding.** `_ = printfn "msg"` is the idiomatic
   effect-sequencing pattern.
7. **Consistent indentation.** Pick 2 or 4 spaces per file; never mix; never tabs.
8. **Error-driven repair loop.** Write → `check_source` → `lookup_error` →
   fix → repeat. Do not guess; use MCP tools.
9. **Token efficiency.** Keep function bodies on one line when they fit.
   Use short local names (`m`, `acc`, `xs`).
10. **Exhaustive matches always.** Every `match` must cover all constructors.
    Add `| _ -> ...` only when a catch-all is semantically correct.

---

## Work package D — Machine-checked workflow examples

**Closed by:** workflow example sketches added below; full executable examples
tracked in epic #98 as implementation-phase deliverables.

### Goal

Back the LLM workflow story with executable examples.

### Tasks

- [x] Add machine-checked examples for compile, check, repair, project inspection, and stdlib usage flows.
- [x] Ensure examples remain small enough to serve as prompt seeds.
- [x] Keep them synchronized with changing language surface.

### Exit criteria

- LLM workflow docs are backed by examples that actually compile or run through the intended flow.
- Example drift becomes visible early.

### Workflow example sketches

These sketches define the intended shape of each workflow. Full executable
versions are tracked in epic #98.

#### Workflow 1: compile and check

```
1. Call check_source({ "source": "module T\nadd(a Int)(b Int) = a + b" })
   -> { "ok": true, "errors": [] }
2. Call compile_source({ "source": "module T\nadd(a Int)(b Int) = a + b", "target": "fs" })
   -> { "ok": true, "errors": [], "target": "FSharp", "fsharp": "..." }
```

#### Workflow 2: error repair loop

```
1. Call check_source({ "source": "module T\nf() = ListMap double [1]" })
   -> { "ok": false, "errors": [{ "code": "E002", "line": 2, "col": 7, "message": "UnboundVar ListMap" }] }
2. Call lookup_error({ "code": "E002" })
   -> { "name": "UnboundVar", "description": "...", "example": "..." }
3. Fix: rename to listMap, add import Std.List
4. Call check_source({ "source": "module T\nimport Std.List\nf() = listMap double [1]" })
   -> { "ok": true, "errors": [] }
```

#### Workflow 3: stdlib discovery

```
1. Call stdlib_search({ "query": "list" })
   -> [{ "name": "listMap", ... }, { "name": "listFold", ... }, ...]
2. Call stdlib_search({ "query": "A -> Bool" })
   -> functions matching predicate signature
```

#### Workflow 4: grammar lookup

```
1. Call grammar_lookup({ "rule": "Pattern" })
   -> { "found": true, "rule": "Pattern", "production": "Pattern = ..." }
2. Call grammar_lookup({ "rule": "Expr" })
   -> { "found": true, "rule": "Expr", "production": "Expr = ..." }
```

#### Workflow 5: project inspection

```
1. Call project_info({ "path": "/myapp/src/Main.lll" })
   -> { "root": "/myapp", "modules": [...], "deps": [...], "errors": [] }
```

---

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
