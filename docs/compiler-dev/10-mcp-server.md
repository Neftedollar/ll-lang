# MCP Server

`src/LLLangTool/Mcp.fs` implements an in-process Model Context Protocol server
that wraps the ll-lang compiler pipeline in 8 structured tools. Entry point:
`Mcp.runServer ()` in `Program.fs` (routes `lllc mcp`).

## Architecture

```
lllc mcp
   │
   ▼
Mcp.runServer()
   │
   ├─ mcpServer CE (FsMcp.Server)
   │     name "ll-lang"  version "0.8.0"
   │     tool compile_file   → compileFileTool
   │     tool check_file     → checkFileTool
   │     tool run_file       → runFileTool
   │     tool list_errors    → listErrorsTool
   │     tool lookup_error   → lookupErrorTool
   │     tool stdlib_search  → stdlibSearchTool
   │     tool grammar_lookup → grammarLookupTool
   │     tool project_info   → projectInfoTool
   │     useStdio
   │
   └─ Server.run server   (blocks until stdin closes)
```

## Dependencies

`LLLangTool.fsproj` adds:
- `FsMcp.Core` v1.0.0 — `Content`, `McpError`, `TypedTool.define`, `unwrapResult`
- `FsMcp.Server` v1.0.0 — `mcpServer` CE, `Server.run`, `useStdio`

Both packages are already used by the `age-mcp` server in this repo (`my-mcps/age-mcp/`). Same API pattern, proven on .NET 10.

## Tool implementations

All tools follow the same structure:

```fsharp
let toolName (args: ArgsType) : Task<Result<Content list, McpError>> =
    task {
        try
            // ... work ...
            return! ok (sprintf "{...}" ...)
        with ex ->
            return! ok (sprintf "{\"error\":%s}" (js ex.Message))
    }
```

`ok text` is `Task.FromResult(Ok [ Content.text text ])`. Errors are always
returned as structured JSON inside `ok` — never as `McpError`. This lets the
LLM client parse and display errors without special-casing the MCP envelope.

## `compile_file`

Calls `LLLang.Compiler.compile`. Returns `{ok, errors[], fsharp?}`.
The `fsharp` field is only populated when `include_output = true` to avoid
sending multi-KB F# source by default.

## `check_file`

Calls `LLLang.Compiler.check` — the same pipeline as `compile` but stops
before `emit`. Useful as a "does it type-check?" fast gate.

## `run_file`

Same as `cmdRun` in Program.fs but with `RedirectStandardOutput = true` /
`RedirectStandardError = true`. Reads both streams concurrently via
`Task.WhenAll` to avoid the stdout/stderr deadlock. Returns
`{exit_code, stdout, stderr, errors[]}`.

## `list_errors` / `lookup_error`

Static data in `knownErrors : (string * string * string) list`. `lookup_error`
additionally walks `spec/examples/invalid/` to find a `.lll` file whose
first line contains `-- expect: EXXX`.

## `stdlib_search`

Scans `stdlibEntries : (string * string * string) list` — a hand-maintained
mirror of `Elaborator.builtinEnv`. Each entry is `(name, signature, module)`.
Substring match on both name and signature.

**Keep in sync:** when you add a builtin to `Elaborator.builtinEnv`, add a
matching line to `stdlibEntries` in `Mcp.fs`.

## `grammar_lookup`

Finds `spec/grammar.ebnf` by walking up from `AppContext.BaseDirectory`.
Searches for a line matching `<rule> ` (rule followed by space or tab), then
collects until a blank line or the start of the next rule.

## `project_info`

Uses `findProjectRoot` (same walk-upward-for-lll.toml logic as Program.fs) and
then calls `LLLang.ProjectLoader.loadProject`. Falls back to single-file mode
(no lll.toml) gracefully.

## Testing

`tests/LLLangTests/McpTests.fs` tests the compiler functions that the tools
delegate to — not the MCP envelope itself:

- `Compiler.check` on valid/invalid sources
- Agreement between `check` and `compile`
- Stdlib names accessible without import (contract for `stdlib_search`)
- Grammar file presence (contract for `grammar_lookup`)
- Error code examples present in corpus (contract for `lookup_error`)

## Adding a new tool

1. Define an arg type: `type MyToolArgs = { param: string }`
2. Implement: `let myTool (args: MyToolArgs) : Task<Result<Content list, McpError>> = task { ... }`
3. Register in `runServer ()`:
   ```fsharp
   tool (TypedTool.define<MyToolArgs> "my_tool" "description" myTool |> unwrapResult)
   ```
4. Add a test in `McpTests.fs`.

## Security

- Tools that take paths validate the `.lll` extension but do not sandbox.
- `run_file` executes `dotnet fsi` — arbitrary code execution. The tool
  description warns clients; MCP clients typically surface a confirmation UI.
- Never write to stdout except via the FsMcp SDK — anything else corrupts the
  JSON-RPC channel. Log to stderr only.
