# ll-lang MCP Server — Design Spec

**Status:** draft, implementation deferred
**Date:** 2026-04-09
**Author:** compiler team
**Supersedes:** —
**Depends on:** project/module system (`2026-04-09-ll-lang-project-system.md`) for multi-file semantics of `project_info`.

## Goal

Ship an in-process Model Context Protocol server as part of the `lllc`
toolchain so that LLM clients (Claude Code, Cursor, Zed, Continue) can
drive the ll-lang compiler through structured JSON-RPC calls instead of
parsing stderr. The compiler already exposes a pure function
`Compiler.compile : string -> Result<string, LLError list>` — the MCP
layer is a thin adapter on top of it.

Non-goals: HTTP/SSE transport, authentication, multi-project workspace
indexing, LSP parity, incremental compilation.

---

## 1. Transport — stdio, one connection per process

**Decision:** stdio only. No HTTP.

Rationale: all current MCP clients spawn local servers over stdio; HTTP
would require TLS, bind-address, and auth decisions we do not need.
Stdio also matches the way `tsserver`, `rust-analyzer`, and `pyright`
are wired. One `lllc mcp` process serves one client for the duration
of the editor session; the client spawns a fresh process per workspace.

The official `ModelContextProtocol` NuGet SDK provides
`WithStdioServerTransport()` out of the box, so no manual JSON-RPC
framing code is needed.

## 2. Entry point — `lllc mcp` subcommand, same binary

**Decision:** extend `src/LLLangTool/Program.fs` with a new subcommand,
slotted alongside the CLI surface defined by the project/module spec
(`lllc build | run | test | mod {init,tidy,add,why} | version`):

```
lllc mcp                      # new — runs until stdin closes
```

`mcp` is a top-level verb like `build` / `test`, not a sub-verb of
`mod`. Routing lives in `Program.fs` next to the project-spec commands;
dispatch precedence is defined by the project/module spec §7, and this
spec adds exactly one row to that table.

Rationale: keeps distribution to a single executable, keeps
`LLLang.Compiler` reachable via direct F# references (no IPC shim
between tool and MCP host), and lets us evolve the CLI and MCP surface
in lockstep. A separate `lllc-mcp` binary would force us to double-pin
the compiler project reference.

**Process lifecycle.** The MCP handler registers the generic host
(`Host.CreateApplicationBuilder`), calls
`AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`,
then awaits `RunAsync()`. On stdin EOF the host completes and `main`
returns `0`. There is no persistent state *in the MCP layer* across
requests — every tool call re-reads files from disk and re-runs the
pipeline through `ProjectLoader` (project/module spec §5). The
`.llcache/` directory owned by `ProjectLoader` is transparently
reused when present; MCP never reads or writes it directly. The full
cold pipeline for a typical file is <100 ms; warm rebuilds hit the
cache.

**Project root discovery.** Tools that take a path (`compile_file`,
`check_file`, `run_file`, `project_info`) use the same walk-upward
`ll.toml` search as `lllc build` (project/module spec §2). If no
`ll.toml` is found, the tool falls back to single-file mode
(project/module spec §8) — the same semantics a bare `lllc build
foo.lll` would get. MCP never invents its own project notion.

## 3. Tool inventory (v0 — 8 tools)

All tools take absolute paths (client-resolved) and return JSON objects
serialized by the SDK. Errors are returned as structured `LLError`
records, not exceptions.

| Tool | Description |
|---|---|
| `compile_file` | Compile a `.lll` file end-to-end; return `{ ok, errors[], fsharp? }`. `fsharp` only populated when `ok && include_output`. |
| `check_file` | Run lexer → parser → elaborator → H-M; skip codegen. Return `{ ok, errors[] }`. Fast path for "does it type-check?". |
| `run_file` | Compile then shell out to `dotnet fsi` (reuses `cmdRun`). Capture stdout/stderr; return `{ exit_code, stdout, stderr, errors[] }`. |
| `list_errors` | Enumerate every known error code with short summaries. Sourced from the compiler's error-code table (currently `E001..E025` spanning lexer/parser/elaborator/H-M/project-loader); the range is not hardcoded in the tool — it enumerates whatever the compiler exposes. Static data per process; no file I/O. |
| `lookup_error` | Given a code like `E003` or `E022`, return long-form explanation + minimal repro snippet from `spec/examples/invalid/`. |
| `stdlib_search` | Substring / prefix match across the compiler's import-resolution scopes, in the order defined by project/module spec §3: built-in stdlib (`Std.*`), enabled `Platform.*` surface, and — when a project root is discovered — local modules and vendored deps. Returns `{ name, signature, module, scope, doc? }` where `scope ∈ {"stdlib","platform","local","vendor"}`. |
| `grammar_lookup` | Given a rule name (`Expr`, `Pattern`, …), return the EBNF production from `spec/grammar.ebnf`. |
| `project_info` | Walk upward from the supplied path (or CWD) to find `ll.toml` per project/module spec §2. Returns `{ root, manifest, modules[], deps[], platform_use[], errors[] }`: `manifest` is the parsed `[project]` table, `modules[]` lists each `.lll` file with its module path + imports + per-file error count, `deps[]` mirrors `ll.sum` entries (`{ path, version, sha256 }`), `platform_use[]` mirrors `[platform].use`. In single-file mode (no `ll.toml` found), `root` is null and only `modules[]` is populated. |

**Deferred to v1** (explicitly not in v0):

- `inspect_type` — requires stable expression IDs at source positions.
  The elaborator already threads a `PosMap`, but there is no
  "cursor → ExprId" lookup. Add once the parser emits a position index.
- `format_file` — no formatter exists yet.
- `rename_symbol` / `extract_function` — require a multi-file binder
  pass we do not have.

## 4. Resource inventory (4 resources)

MCP resources are URI-addressable read-only blobs the client can prefetch.

| URI | Content | Source |
|---|---|---|
| `lllang://grammar` | The full `spec/grammar.ebnf` file. | Shipped next to the binary; read on first access. |
| `lllang://errors` | Aggregated `spec/error-codes.md` plus each minimal repro. | Composed at process start. |
| `lllang://stdlib` | Markdown index of every stdlib module + signatures. | Generated from the `Elaborator.builtinEnv` table + hand-written docs. |
| `lllang://design` | The language design doc (`docs/superpowers/specs/2026-04-07-ll-lang-design.md`). | File read. |

Resources are surfaced via `[McpServerResource]` on a static F# module.
They exist for discovery: an LLM can pull the grammar once and then
reason locally instead of hammering `grammar_lookup`.

## 5. Prompt inventory (3 prompts)

MCP prompts are parametrized templates the client can render into a
conversation turn.

| Name | Parameters | Behavior |
|---|---|---|
| `explain_ll_error` | `code: string`, `source_snippet: string` | Returns a system + user turn asking the model to explain a given `LLError` in context. |
| `translate_fsharp_to_lllang` | `fsharp_source: string` | Returns a turn asking the model to rewrite the F# snippet as idiomatic ll-lang, using the grammar resource as ground truth. |
| `new_module_scaffold` | `module_path: string`, `intent: string` | Returns a turn asking the model to produce a minimal `module` body matching the stated intent. |

Prompts do **not** invoke the compiler; they only package reusable
system-prompt fragments so LLM clients can expose them as one-click
actions.

## 6. Implementation language — F# now, ll-lang later

**Decision:** implement v0 in F#, in a new file
`src/LLLangCompiler/Mcp.fs` (compile order: after `Compiler.fs`,
before `src/LLLangTool/Program.fs`).

Rationale: the SDK is attribute-driven C#/.NET, F# consumes it cleanly,
and we reuse `LLLang.Compiler.compile` by direct call. Dogfooding the
MCP server in ll-lang is attractive but blocked on (a) self-hosting
fixpoint, (b) ll-lang bindings to .NET `ModelContextProtocol.Server`
attribute APIs, (c) runtime async. Reassessing after Phase 7
bootstrap. The F# implementation is intentionally thin — each tool
is 5–20 lines — so a future ll-lang port is a direct transliteration,
not a rewrite.

Migration path: once self-hosted `lllc` can emit a `.dll` that exposes
`[McpServerTool]` attributes via ll-lang's future FFI, port one tool
(`grammar_lookup`, the simplest) as a spike. Full port is a separate
phase.

## 7. CLI integration

`Program.fs` pattern-matches `argv`. The full dispatch table is owned
by the project/module spec §7 (`build`, `run`, `test`, `mod …`,
`version`); this spec only adds one row:

```
[| "mcp" |]           → Mcp.runStdio () |> Async.RunSynchronously
```

`Mcp.runStdio` lives in `LLLangCompiler.dll` so Program.fs stays tiny.
All existing commands keep their current semantics; no flag or env
var changes. Adding `mcp` does not bring in new runtime dependencies
for `build` / `run` users at runtime — the MCP SDK assemblies are
loaded lazily on first call by the JIT.

**Client config example** (shipped in `docs/user-guide/` in a later
doc, not here):

```json
{
  "mcpServers": {
    "ll-lang": { "command": "lllc", "args": ["mcp"] }
  }
}
```

## 8. Testing strategy

Three layers:

1. **Unit tests** (`tests/LLLangTests/McpTests.fs`): call the F# tool
   methods directly, bypassing stdio and JSON entirely. For each tool,
   assert `(input → output)` on a corpus fixture from
   `spec/examples/`. This is the bulk of the tests and keeps the
   sub-second test suite invariant.
2. **Envelope fixtures** (`tests/LLLangTests/fixtures/mcp/*.json`):
   request/response pairs verifying the SDK's JSON shape matches
   what clients expect. A small harness feeds a request, reads the
   response, compares against the expected blob (with regex masking
   of timestamps / paths). Five fixtures max: `initialize`,
   `tools/list`, `tools/call:compile_file`, `resources/list`,
   `prompts/list`.
3. **End-to-end smoke** (CI only, not in default `dotnet test`): spawn
   `lllc mcp`, pipe a hand-rolled `initialize` + `tools/call` over
   stdio, assert exit code 0 and response schema. Guarded behind an
   `LLLANG_MCP_SMOKE=1` env var so laptop runs stay fast.

No mock MCP client library — we drive the server with newline-delimited
JSON files and a 30-line F# harness.

## 9. Dependencies

Added NuGet packages in `src/LLLangCompiler/LLLangCompiler.fsproj`:

- `ModelContextProtocol` — official SDK (`/modelcontextprotocol/csharp-sdk`).
  Provides attribute-based tool discovery, stdio transport,
  JSON-RPC framing. Confirmed to exist at cutoff; pin an explicit
  version during implementation and run `dotnet list package
  --vulnerable` before committing.
- `Microsoft.Extensions.Hosting` — transitive requirement of the SDK
  for `Host.CreateApplicationBuilder`.

No other deps. The SDK uses `System.Text.Json` which is already in
.NET 10. No community shim, no manual JSON-RPC implementation.

## 10. Security considerations

**Trust model:** the MCP server runs as the invoking user and has the
same filesystem authority as any editor plugin. Tools that take paths
(`compile_file`, `check_file`, `run_file`, `project_info`) do **not**
sandbox — they honor whatever path the client sends. This matches how
`tsserver` and `rust-analyzer` operate.

Concrete rules:

1. **No path escaping.** We resolve the given path, verify the file
   has `.lll` extension (for the compile family), and reject
   otherwise. No chroot, no deny-list. The editor is trusted.
2. **`run_file` is the sharp edge.** It shells out to `dotnet fsi`
   which executes arbitrary user code. The client **must** treat
   `run_file` as the equivalent of a terminal command and surface
   confirmation UI. We document this prominently in the tool
   description (the SDK forwards descriptions to clients, which
   typically prompt for execution confirmation automatically). A
   future `dry_run` flag can gate execution.
3. **Resource URIs are read-only and fixed.** They only expose files
   under the compiler's own installation directory.
4. **Log to stderr only** (SDK default with stdio transport). Never
   emit anything on stdout that is not a JSON-RPC frame — this would
   corrupt the channel. Tests enforce by capturing stdout and
   asserting it only contains valid JSON-RPC objects.
5. **No secrets in errors.** `LLError.Message` already avoids echoing
   file content; we keep that discipline when serializing.

## Out of scope (explicitly)

- HTTP/SSE transport
- Incremental / in-memory compilation cache
- Cursor-position queries (`inspect_type`, `hover`, `complete`)
- Multi-root workspace support
- Writing the MCP server in ll-lang (revisit post-bootstrap)
- LSP bridging — a separate concern; LSP and MCP solve different
  problems and the overlap is <30%.

## Delivery

Single PR against `main` after the project/module system spec lands.
Estimated size: ~400 LoC F# + ~200 LoC tests. Implementation gated on
the maintainer having verified the `ModelContextProtocol` NuGet
package version and API shape at implementation time — any drift from
the snippets in §2 and §9 is a research step, not a design change.

---

## Reconciliation

Reconciled with sibling spec `2026-04-09-ll-lang-project-system.md` on
2026-04-09. Areas checked: CLI surface (union with `mod`/`test`),
subcommand routing, project root discovery, file layout
(`ll.toml`/`vendor/`/`bin/`/`.llcache/`), process lifecycle vs
`ProjectLoader`, dependency model (`ll.sum` in `project_info`),
stdlib/Platform/local/vendor resolution order in `stdlib_search`,
config file interaction (MCP has none — no collision), error model
(no hardcoded `E0xx` range), tool inventory coverage of project
structure, terminology (`module`/`project`/`dep`/`import`). Edits
applied to this spec only; project/module spec left unchanged
per the "project-system owns CLI + layout + deps" boundary.
