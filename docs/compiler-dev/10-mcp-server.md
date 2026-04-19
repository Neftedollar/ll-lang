# MCP Server

Current MCP implementation is self-hosted in ll-lang:

- `lllcself/src/Mcp.lll` — JSON parser/serializer, JSON-RPC dispatch, tool handlers
- `lllcself/src/Main.lll` — CLI command routing (`mcp`)

The F# CLI entrypoint (`src/LLLangTool/Program.fs`) routes `lllc mcp` to the
self-hosted command via `cmdRunSelf ["mcp"]`.

## Runtime flow

```text
lllc mcp
  │
  ▼
Program.fs: cmdRunSelf ["mcp"]
  │
  ▼
compile lllcself/src/Main.lll to temp F# project
  │
  ▼
run generated self-hosted binary with args: mcp
  │
  ▼
lllcself/src/Mcp.lll handles JSON-RPC over stdio
```

## Supported JSON-RPC methods

- `initialize`
- `tools/list`
- `tools/call`

`initialize` returns:

- `protocolVersion = "2024-11-05"`
- `serverInfo.name = "lllcself"`
- `serverInfo.version = "1.0.0"`

## Tool contract (current)

`tools/list` currently returns **28** tools.

### Core compile/check

- `compile_source`, `check_source`
- `compile_file`, `check_file`

### Diagnostics/repair

- `diagnose_source`, `diagnose_file`
- `explain_error`
- `fix_suggest`, `apply_fix_preview`

### Formatting/AST

- `format_source`, `format_file`
- `parse_source`, `typed_ast`

### Project graph/build

- `project_graph`
- `check_project`, `build_project`

### Symbol navigation

- `symbols`, `definition`, `references`

### Dependency helpers

- `mod_add`, `mod_tidy`, `mod_why`

### Test helpers

- `test_list`, `test_run`

### Existing utility surface

- `stdlib_search`
- `list_errors`, `lookup_error`
- `list_targets`

## Tool behavior notes

- `compile_source` and `compile_file` return:
  - `{"ok": true, "errors": [], "target": "fsharp", "fs": "..."}`
- `check_source` and `check_file` return:
  - `{"ok": true, "result": "<json-string-from-checkCompact>"}`
- file tools emit `E000` if path is missing.
- file paths should be absolute for stable behavior (self-host runs in temp dir).
- `test_list` / `test_run` currently return a structured `supported=false` baseline
  in self-hosted runtime (no process spawn).

## Main loop shape

`mcpLoop` currently:

1. Reads full stdin (`readFile "/dev/stdin"`).
2. Splits by newline into JSON-RPC messages.
3. Processes each line independently.
4. Writes newline-delimited JSON responses.

This is a batch-on-stdin model, suitable for current MCP integration tests.

## Tests

`tests/LLLangTests/McpTests.fs` is the integration contract suite against
`dotnet run --project src/LLLangTool -- mcp` and verifies:

- `initialize` metadata
- `tools/list` inventory
- core `tools/call` smoke
- diagnostics/format/typed AST behavior
- project graph cycle detection
- project-scope symbol/definition/reference lookup
- dependency helper roundtrip in a temp project
- file tool behavior on absolute paths
- target inventory and status mapping
