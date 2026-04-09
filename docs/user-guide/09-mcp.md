# ll-lang MCP Server

ll-lang ships an embedded [Model Context Protocol](https://modelcontextprotocol.io) server so that LLM clients like **Claude Code**, **Cursor**, and **Zed** can drive the compiler through structured tool calls instead of parsing stderr.

```
lllc mcp          # starts the stdio MCP server; runs until stdin closes
```

---

## Setup

### Claude Code

Add to your MCP config (e.g. `~/.config/claude/mcp.json` or the project's `.mcp.json`):

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

If `lllc` is not on `$PATH`, use the full path:
```json
"command": "/usr/local/bin/lllc"
```

### Cursor / Zed / Continue

These clients follow the same stdio MCP pattern. Add `lllc mcp` as a server command in their MCP settings. Refer to each client's documentation for the exact location.

---

## Available tools

### `compile_file`

Compile a `.lll` file end-to-end. Returns structured JSON.

**Input:**
```json
{ "path": "/abs/path/to/foo.lll", "include_output": false }
```
Set `include_output: true` to get the generated F# source in the response.

**Output:**
```json
{ "ok": true,  "errors": [] }
{ "ok": false, "errors": [{"code": "E002", "line": 5, "col": 3, "message": "E002 5:3 UnboundVar foo"}] }
```

---

### `check_file`

Type-check a file **without** running codegen. Faster than `compile_file`.

**Input:** `{ "path": "/abs/path/to/foo.lll" }`  
**Output:** `{ "ok": true, "errors": [] }` or `{ "ok": false, "errors": [...] }`

---

### `run_file`

Compile and run a `.lll` file via `dotnet fsi`. Captures stdout + stderr.

**Input:** `{ "path": "/abs/path/to/foo.lll" }`

**Output:**
```json
{
  "exit_code": 0,
  "stdout": "Hello, ll-lang!\n",
  "stderr": "",
  "errors": []
}
```

> **Warning:** `run_file` executes arbitrary user code. Only use it on files you trust.

---

### `list_errors`

Return all known error codes with names and descriptions.

**Input:** `{}` (no required fields)  
**Output:**
```json
[
  { "code": "E001", "name": "TypeMismatch",  "description": "Expected type A, got type B at a usage site." },
  { "code": "E002", "name": "UnboundVar",    "description": "Identifier not found in scope." },
  ...
]
```

---

### `lookup_error`

Get detailed explanation + minimal repro snippet for a specific error code.

**Input:** `{ "code": "E003" }`  
**Output:**
```json
{
  "found": true,
  "code": "E003",
  "name": "NonExhaustiveMatch",
  "description": "Pattern match does not cover all constructors of a sum type.",
  "example": "-- expect: E003\nmodule Bad\n\ntype Shape = Circle Float | Rect Float Float\n\nfn area(s Shape) Float =\n  | Circle r -> r * r\n"
}
```

---

### `stdlib_search`

Search the stdlib by name or type signature substring.

**Input:** `{ "query": "list" }`  
**Output:**
```json
[
  { "name": "listLen",    "signature": "List[A] -> Int",                    "module": "Std.List", "scope": "stdlib" },
  { "name": "listMap",    "signature": "(A -> B) -> List[A] -> List[B]",    "module": "Std.List", "scope": "stdlib" },
  { "name": "listFilter", "signature": "(A -> Bool) -> List[A] -> List[A]", "module": "Std.List", "scope": "stdlib" },
  ...
]
```

---

### `grammar_lookup`

Get the EBNF production for a grammar rule.

**Input:** `{ "rule": "Expr" }`  
**Output:**
```json
{
  "found": true,
  "rule": "Expr",
  "production": "Expr = ..."
}
```

The grammar is read from `spec/grammar.ebnf` (relative to the binary). Returns `{ "found": false }` if the rule is not found or the file is missing.

---

### `project_info`

Walk up from a path to find `ll.toml` and return project metadata.

**Input:** `{ "path": "/abs/path/to/src/Main.lll" }`  
**Output:**
```json
{
  "root": "/abs/path/to/myapp",
  "manifest": { "name": "myapp", "version": "0.1.0" },
  "modules": [
    { "path": "/abs/path/to/myapp/src/Greet.lll", "module": "Myapp.Greet" },
    { "path": "/abs/path/to/myapp/src/Main.lll",  "module": "Myapp.Main" }
  ],
  "deps": [],
  "platform_use": [],
  "errors": []
}
```

In single-file mode (no `ll.toml`), `root` is `null` and `modules` contains one entry.

---

## How LLM agents should use these tools

| Task | Recommended tool |
|------|-----------------|
| "Does this file type-check?" | `check_file` — fast, no codegen overhead |
| "Compile and show me the F# output" | `compile_file` with `include_output: true` |
| "Run this ll-lang program" | `run_file` |
| "What does E003 mean?" | `lookup_error` |
| "What list functions are available?" | `stdlib_search` with `"query": "list"` |
| "What's the syntax for pattern matching?" | `grammar_lookup` with `"rule": "Pattern"` |
| "What modules are in this project?" | `project_info` |

---

## Transport

stdio only. One process per workspace session. The server blocks until stdin closes (client disconnects). No persistent state — every tool call re-reads files from disk and re-runs the pipeline.

---

## Troubleshooting

### `lllc: command not found`
Set up the alias per [01-installation.md](01-installation.md) or use `dotnet run --project src/LLLangTool -- mcp` instead.

### `grammar_lookup` returns `"found": false` for a valid rule name
The server looks for `spec/grammar.ebnf` relative to the binary. When running via `dotnet run`, the binary lives in `bin/Debug/net10.0/` — the grammar file search walks up 6 levels and should find `spec/`. If it doesn't, set the working directory to the repo root.

### `run_file` is slow
`lllc run` starts a fresh `dotnet fsi` session each time. Cold start is 2–5 s. For faster iteration, use `compile_file` + `dotnet run` on the generated `.fs` file.
