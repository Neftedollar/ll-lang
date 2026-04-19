# Getting Started with ll-lang

ll-lang is a statically-typed functional language designed for LLM code generation. Minimal syntax, Hindley-Milner types, compiled = works.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
dotnet --version   # must report 10.x
```

---

## Install

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
dotnet build
dotnet test        # run full test suite (current count in CI)
```

### Set up the `lllc` alias

```bash
alias lllc='dotnet run --project /path/to/ll-lang/src/LLLangTool --'
```

Add to your shell profile to persist it.

---

## Write your first program

```bash
cat > hello.lll <<'EOF'
module Hello

Hello = printfn "Hello, ll-lang!"
EOF

lllc run hello.lll
# Hello, ll-lang!
```

Key rules:
- Every file starts with `module Name`
- Uppercase names = types, lowercase names = values/functions
- No `fn` / `type` keywords needed

---

## Run and build

```bash
lllc run hello.lll          # compile + execute via temporary F# project
lllc build hello.lll        # compile → hello.fs
lllc self check hello.lll   # canonical single-file type-check (LLL path)
lllc check .                # project type-check (reads lll.toml)
```

---

## Create a project

```bash
lllc new myapp
```

Creates:

```
myapp/
├── lll.toml
└── src/
    └── Main.lll
```

`lll.toml` — the project manifest:

```toml
[project]
name = "myapp"
version = "0.1.0"
entry = "src/Main.lll"
```

`src/Main.lll`:

```lll
module Myapp.Main

main() = printfn "Hello from myapp!"
```

Build and run the project:

```bash
cd myapp
lllc build                          # → bin/myapp.fs + bin/myapp.fsproj
dotnet run --project bin/myapp.fsproj
```

---

## Module naming

The module path in a file's header must match its path under `src/`. Examples:

| File | Module header |
|------|--------------|
| `src/Main.lll` | `module Myapp.Main` |
| `src/Util/Parser.lll` | `module Myapp.Util.Parser` |

The compiler emits E020 if they don't match.

---

## Add a second module

```bash
cat > src/Greet.lll <<'EOF'
module Myapp.Greet

export greet(name Str) = strConcat "Hello, " name
EOF
```

Import it from `Main.lll`:

```lll
module Myapp.Main

import Myapp.Greet

main() = printfn (greet "world")
```

`lllc build` topologically sorts all `src/*.lll` files and compiles them in dependency order.

---

## Add dependencies

Dependencies are source-based. Add them to `lll.toml`:

```toml
[project]
name = "myapp"

[deps]
std = { path = "../stdlib" }
```

Then fetch:

```bash
lllc mod tidy
```

`lllc mod tidy` resolves direct + transitive deps into `vendor/` and rewrites
`ll.sum` deterministically so repeated installs are stable (hashing ignores
`.git` metadata inside vendored git dependencies). For same-repo git refs,
resolver selection is deterministic: highest semver tag wins when tags parse as
semver, otherwise lexical ref ordering is used (and `ll.sum` can pin a winner).

---

## Multi-target build

Compile to multiple platforms from one source. Set targets in `lll.toml`:

```toml
[project]
name = "myapp"

[platform]
use = ["fsharp", "typescript"]
```

```bash
lllc build
# bin/fsharp/myapp.fs
# bin/typescript/myapp.ts
```

Or pass `--target` for a one-off:

```bash
lllc build --target ts   myapp.lll   # TypeScript
lllc build --target py   myapp.lll   # Python
lllc build --target java myapp.lll   # Java 21
lllc build --target cs   myapp.lll   # C#
lllc build --target llvm myapp.lll   # LLVM IR (experimental subset)
lllc build --target fs   myapp.lll   # F# (default)
```

---

## Connect the MCP server

ll-lang ships a built-in MCP server. Wire it to Claude Code, Cursor, or Zed — your LLM client gets 10 structured tools for compile, check, run, and stdlib search.

### Claude Code

Add to `~/.config/claude/mcp.json`:

```json
{
  "mcpServers": {
    "ll-lang": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/ll-lang/src/LLLangTool", "--", "mcp"]
    }
  }
}
```

Or if you have the `lllc` alias:

```json
{
  "mcpServers": {
    "ll-lang": { "command": "lllc", "args": ["mcp"] }
  }
}
```

### Available MCP tools

| Tool | What it does |
|------|-------------|
| `check_file` | Type-check a file path, return structured errors |
| `check_source` | Type-check a source string (fastest iteration loop) |
| `compile_file` | Full compile, optionally return generated output |
| `compile_source` | Compile a source string, get generated code |
| `run_file` | Compile and execute, return stdout/stderr |
| `list_errors` | All error codes with descriptions |
| `lookup_error` | One error code → explanation + repro |
| `stdlib_search` | Search stdlib by name or signature |
| `grammar_lookup` | EBNF production for a grammar rule |
| `project_info` | Project metadata from `lll.toml` |

### Recommended LLM workflow

1. Write ll-lang code
2. Call `check_source` to validate
3. If errors, call `lookup_error` on each code
4. If unsure about stdlib, call `stdlib_search "list"` or similar
5. If unsure about syntax, call `grammar_lookup "Expr"`

---

## Error format

All compiler errors are one-liners designed for machine parsing:

```
E001 12:5  TypeMismatch   expected:Str[UserId] got:Str
E002 8:3   UnboundVar     username
E003 15:1  NonExhaustiveMatch  type:Shape missing:Empty
E004 20:9  UnitMismatch   Float[m] Float[s]
E005 7:14  TagViolation   Str[Email] Str[UserId]
```

Format: `EXXX line:col ErrorKind details`. No stack traces.

---

## Next steps

- [docs/language-spec.md](language-spec.md) — full grammar, type system, all constructs
- [docs/llm-best-practices.md](llm-best-practices.md) — patterns for LLM-generated ll-lang code
- [docs/stdlib-reference.md](stdlib-reference.md) — all stdlib modules and functions
- [docs/user-guide/](user-guide/) — deep-dive guides by topic
