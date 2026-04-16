---
name: Feature request
about: Propose a new language feature, stdlib addition, CLI command, or compiler improvement
title: "[feat] "
labels: enhancement
assignees: ''
---

## Summary

One or two sentences describing the feature.

## Motivation

Why is this needed? What problem does it solve?

- For language features: show the code you cannot write today and why that matters.
- For stdlib: what computation is currently awkward without this?
- For tooling (CLI, MCP): what workflow does this unblock?

## Proposed design

How would the feature look from the user's perspective?

**ll-lang syntax (if a language feature):**

```
module Examples.NewFeature

-- show what you want to be able to write
```

**CLI usage (if a tooling feature):**

```bash
lllc <new-command> ...
```

**MCP tool (if an MCP feature):**

Describe the tool name, inputs, and structured output.

## Alternatives considered

What workarounds exist today? Why are they insufficient?

## Affected areas

Check all that apply:

- [ ] Language syntax / grammar (`spec/grammar.ebnf`)
- [ ] Type system / inference (`HMInfer.fs`, `Types.fs`)
- [ ] Elaborator / static checks (`Elaborator.fs`)
- [ ] F# codegen (`Codegen.fs`)
- [ ] TypeScript codegen (`CodegenTS.fs`)
- [ ] Python codegen (`CodegenPy.fs`)
- [ ] Java codegen (`CodegenJava.fs`)
- [ ] C# codegen (`CodegenCS.fs`)
- [ ] LLVM codegen (`CodegenLLVM.fs`)
- [ ] Stdlib (`stdlib/*.lll`)
- [ ] CLI (`src/LLLangTool/`)
- [ ] MCP server (`Mcp.fs`)
- [ ] Documentation

## Additional context

Links to related issues, language papers, or prior art in other languages.
