# ll-lang MCP Server

`lllc mcp` starts a stdio Model Context Protocol server for ll-lang tooling.

Current implementation is **self-hosted**: the F# CLI command forwards to
`lllcself/src/Main.lll` (`mcp` subcommand), and the MCP handlers live in
`lllcself/src/Mcp.lll`.

```bash
lllc mcp
```

The process blocks until stdin closes.

---

## Setup

Add to your MCP config (for example `~/.config/claude/mcp.json` or project
`.mcp.json`):

```json
{
  "mcpServers": {
    "ll-lang": {
      "command": "lllc",
      "args": ["mcp"]
    }
  }
}
```

If `lllc` is not on `$PATH`, use an absolute path to the binary.

---

## Protocol calls

The server handles these JSON-RPC methods:

- `initialize`
- `tools/list`
- `tools/call`

`initialize` response:

```json
{
  "protocolVersion": "2024-11-05",
  "capabilities": { "tools": {} },
  "serverInfo": { "name": "lllcself", "version": "1.0.0" }
}
```

---

## Available tools (28)

Core compile/check:
- `compile_source`, `check_source`, `compile_file`, `check_file`

Diagnostics and repair:
- `diagnose_source`, `diagnose_file`, `explain_error`, `fix_suggest`, `apply_fix_preview`

Formatting and AST/introspection:
- `format_source`, `format_file`, `parse_source`, `typed_ast`

Project-level:
- `project_graph`, `check_project`, `build_project`

Symbol navigation:
- `symbols`, `definition`, `references`

Dependency helpers:
- `mod_add`, `mod_tidy`, `mod_why`

Test helpers:
- `test_list`, `test_run` (structured test discovery/run over `dotnet test`)

Catalog/meta:
- `stdlib_search`, `list_errors`, `lookup_error`, `list_targets`

---

## Tool reference

### `compile_source`

Input:

```json
{ "source": "module M\nmain = 1" }
```

Current response shape:

```json
{ "ok": true, "errors": [], "target": "fsharp", "fs": "module M\n..." }
```

Note: on compile failure, this tool still returns `"ok": true`; diagnostic text
currently appears in `"fs"` (current self-hosted behavior).

### `check_source`

Input:

```json
{ "source": "module M\nmain = 1" }
```

Current response shape:

```json
{ "ok": true, "result": "{\"ok\":true,\"stage\":\"ok\",\"primary_error\":\"\",\"secondary_count\":0}" }
```

`result` is a JSON string payload (not a nested object).

### `compile_file`

Input:

```json
{ "path": "/absolute/path/to/file.lll" }
```

Current response shape:

```json
{ "ok": true, "errors": [], "target": "fsharp", "fs": "module ..." }
```

Missing-file response:

```json
{ "ok": false, "errors": [{ "code": "E000", "message": "File not found: ..." }] }
```

Use absolute paths. Relative paths are resolved from the temporary self-host
run directory and usually fail.

### `check_file`

Input:

```json
{ "path": "/absolute/path/to/file.lll" }
```

Current response shape:

```json
{ "ok": true, "result": "{\"ok\":true,\"stage\":\"ok\",\"primary_error\":\"\",\"secondary_count\":0}" }
```

Missing-file response matches `compile_file` (`E000`).

### `stdlib_search`

Input:

```json
{ "query": "list" }
```

Current response shape:

```json
{
  "tools": [
    { "name": "listMap", "signature": "(A -> B) -> List[A] -> List[B]", "module": "Std.List", "scope": "stdlib" }
  ]
}
```

### `list_errors`

Input:

```json
{}
```

Current response shape:

```json
[
  { "code": "E001", "name": "TypeMismatch", "description": "Expected type A got type B" }
]
```

### `lookup_error`

Input:

```json
{ "code": "E003" }
```

Current response shape:

```json
{ "found": true, "code": "E003", "name": "NonExhaustiveMatch", "description": "Pattern match missing cases" }
```

### `list_targets`

Input:

```json
{}
```

Current response shape:

```json
[
  { "id": "fs", "name": "FSharp", "status": "stable", "extension": ".fs", "description": "F# source files; default target" },
  { "id": "llvm", "name": "LLVM", "status": "experimental", "extension": ".ll", "description": "LLVM IR; subset backend — ADT support partial" }
]
```

### `diagnose_source` / `diagnose_file`

Structured diagnostics for repair loops:

```json
{
  "ok": false,
  "stage": "elaborator",
  "diagnostics": [
    {
      "code": "E002",
      "message": "Unbound variable: undefined",
      "line": 0,
      "col": 0,
      "endLine": 0,
      "endCol": 0,
      "severity": "error",
      "hint": ""
    }
  ]
}
```

### `format_source` / `format_file`

Canonical formatting (newline normalization + trailing-space cleanup):

```json
{
  "ok": true,
  "changed": true,
  "check_only": true,
  "formatted": "module M\nmain = 1\n"
}
```

### `parse_source` / `typed_ast`

`parse_source` returns AST summary (`module`, `decl_count`, `decls[]`).
`typed_ast` returns typed summary baseline (`typed_decls[]`) and diagnostics.

### `project_graph` / `check_project` / `build_project`

- `project_graph`: modules, import edges, topo order, loader errors.
- `check_project`: per-file check diagnostics.
- `build_project`: per-file build summary and output previews.

### `symbols` / `definition` / `references`

Top-level symbol discovery and textual reference lookups over source/file input.
When `root` is provided, these tools can resolve over project files (`src/**/*.lll`)
and return path-aware locations.

### `explain_error` / `fix_suggest` / `apply_fix_preview`

- `explain_error`: code + cause + hints.
- `fix_suggest`: candidate fix IDs.
- `apply_fix_preview`: returns patched source + preview diff.

### `mod_add` / `mod_tidy` / `mod_why`

- `mod_add`: writes dependency line into `lll.toml`.
- `mod_tidy`: self-hosted baseline no-op (structured response).
- `mod_why`: reports manifest presence + direct importers.

### `test_list` / `test_run`

`test_list` returns discovered tests:

```json
{
  "ok": true,
  "supported": true,
  "total": 12,
  "tests": [
    { "name": "LLLangTests.McpTests.mcp initialize returns self-hosted metadata" }
  ],
  "timed_out": false,
  "exit_code": 0
}
```

`test_run` returns structured run summary:

```json
{
  "ok": true,
  "supported": true,
  "total": 1,
  "passed": 1,
  "failed": 0,
  "skipped": 0,
  "tests": [
    { "name": "LLLangTests.McpTests.mcp initialize returns self-hosted metadata", "status": "passed", "duration_ms": 7000, "message": "" }
  ],
  "timed_out": false,
  "exit_code": 0
}
```

---

## Smoke test

```bash
printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"check_source","arguments":{"source":"module M\nmain = 1"}}}' \
  | lllc mcp
```
