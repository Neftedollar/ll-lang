# ll-lang v2 LLM Tooling Contract

**Status:** planned  
**Scope:** defines where LLM-specific value belongs in the `v2` architecture.

## Summary

`v2` keeps the core language small and pushes LLM leverage into tooling,
documentation, and stable machine-facing interfaces. The objective is not to
embed an “AI language” into the grammar. The objective is to make ll-lang the
best language for an LLM to author, compile, repair, and inspect.

## Design rule

LLM-facing features should live outside the core syntax unless there is a
demonstrably high payoff for correctness or token reduction.

The preferred layers are:

- MCP tools
- compact diagnostics
- predictable stdlib naming
- prompt packs / repair recipes
- token and iteration benchmarks
- executable docs/examples

## Required v2 tooling surfaces

### 1. MCP

The MCP layer should be rich enough that an LLM client can discover:

- grammar and syntax rules
- stdlib functions and signatures
- project manifest and dependency graph information
- compiler errors and their meanings
- compile/check/run/build capabilities
- benchmark outputs where relevant

The MCP contract should prefer structured data over prose-shaped shell output.

### 2. Prompt packs

`v2` should version prompt packs and repair recipes alongside the compiler.

These should cover at minimum:

- minimal ll-lang syntax refresher
- common error-code repair hints
- stdlib naming conventions
- project/dependency workflow
- compiler-authoring conventions

### 3. LLM-facing docs

The user guide should contain canonical LLM-authoring documentation:

- how to write compact ll-lang
- how to use the MCP server
- how to react to error codes
- how to discover stdlib and project info

The docs must be treated as part of the product surface, not as incidental
notes.

### 4. Benchmarks

`v2` should measure at least:

- token count efficiency against comparison languages
- compile latency
- repair-loop iteration count on seeded failures
- cross-target semantic stability on representative programs

These benchmarks are part of the LLM-tooling story because “good for LLMs” must
be demonstrable, not only intuitive.

## Non-goals for v2

The following are not required for the `v2` baseline:

- language-level prompt directives
- embedded agent instructions in the core grammar
- autonomous planning semantics in source code
- a macro system justified only by prompt convenience

Such features require a separate design and a clear proof that they beat docs +
MCP + stdlib + diagnostics on total complexity.

## Documentation obligations

Any change to LLM-facing tooling should update at least one of:

- `docs/user-guide/09-mcp.md`
- `docs/user-guide/09-llm-prompting.md`
- `docs/llm-best-practices.md`
- benchmark docs or published benchmark artifacts

## Validation targets

The LLM-tooling contract is not complete without:

- MCP contract tests
- docs examples that compile or check successfully
- regression samples for prompt-pack repair flows
- benchmark artifacts that can be reproduced or regenerated deterministically
