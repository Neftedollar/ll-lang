# ll-lang Project & Module System — Design Spec

**Date:** 2026-04-09
**Status:** Draft, pending implementation (deferred until after Phase 7.10 fixpoint)
**Scope:** Multi-file projects, external deps, CLI surface. Implementation is a
single subsequent phase ("Phase 8: Projects"); this doc makes every decision.

## 1. Project manifest — `ll.toml`

**Decision:** one file at the project root named `ll.toml`, minimal TOML.

Justification: TOML is stable, has a hand-writable grammar (~400 lines), one
file, and we already need to parse strings/lists/tables. Putting the manifest
in `.lll` is seductive but forces the manifest reader to pull in the whole
compiler front end just to read a version string — a circular dependency the
moment `lllc mod tidy` wants to fetch a dep before it has a working compiler.
Keep manifest and code on separate sides of the cut.

```toml
# ll.toml
[project]
name    = "my-app"             # required, becomes root module namespace
version = "0.1.0"              # semver, required
lll     = "0.8"                # min compiler version
entry   = "src/Main.lll"       # optional; defaults to src/Main.lll

[deps]
"github.com/alice/json"    = "v1.2.0"
"github.com/bob/http"      = "v0.4.1"
"git.sr.ht/~c/parser-kit"  = "v2.0.0"

[platform]
# opt-in to platform surface packages; empty = just pure stdlib
use = ["Platform.IO", "Platform.Env"]
```

Only three tables: `project`, `deps`, `platform`. No build profiles, no
feature flags, no workspaces in v1. If we need them later we add tables; we
never rename existing keys.

The manifest parser is a tiny hand-written TOML subset (strings, ints,
tables, arrays of strings). ~300 LoC of F#. No `Tomlyn` package — we stay
dependency-free for the same reason we hand-wrote the parser.

## 2. Directory layout

```
my-app/
├── ll.toml                  manifest (root marker)
├── ll.sum                   lock file (checked in)
├── src/                     first-party sources
│   ├── Main.lll             entry point, module path = my-app.Main
│   └── Foo/
│       └── Bar.lll          module path = my-app.Foo.Bar
├── vendor/                  flattened external deps (checked in)
│   └── github.com/
│       └── alice/
│           └── json/
│               ├── ll.toml
│               └── src/
│                   └── Json.lll
├── bin/                     build artifacts (gitignored)
│   ├── my-app.fs            generated F# source (single file, concatenated)
│   ├── my-app.fsproj        generated, regenerated every build
│   └── my-app.dll           compiled assembly
└── .llcache/                incremental cache (gitignored)
    └── <hash>.tast          cached TypedModule per source file
```

**Root discovery.** `lllc` walks upward from cwd looking for `ll.toml`. First
hit wins; that directory is the project root. If none is found and the CLI
was invoked with a single `.lll` file, falls back to single-file mode
(§8). Anywhere else is an error.

**Vendor.** `vendor/` is checked in, Go-style. No global `~/.lll/pkg` cache
in v1 — simpler, reproducible, no PATH surprises. A global cache can be bolted
on later as a hardlink source for `mod tidy`.

**`bin/` and `.llcache/`** are added to `.gitignore` by `lllc mod init`.

## 3. Module resolution

Source modules declare their path in the first line (`module My.App.Foo`). The
compiler validates that this matches the file's physical location relative to
its owning project's `src/`. Mismatch is error `E020 ModulePathMismatch`.

An `import` statement is resolved in this fixed order:

1. **Built-in stdlib** — `import Std.List`, `import Std.Maybe`. Baked into
   the compiler binary (same as today, shipped as a string blob in
   `Stdlib.fs`). No disk lookup.
2. **Platform surface** — `import Platform.IO.File`. Mapped to a set of F#
   shim modules inside the compiler. Only visible if listed in
   `[platform].use`; otherwise error `E021 PlatformNotEnabled`. Platform
   modules are .NET bindings (File, Env, Process, Net.Http, …). The shim
   implementation lives under `src/LLLangCompiler/Platform/*.fs` and is
   always compiled into `lllc`; the `use` list is pure access control.
3. **Local module** — path starts with the current project's `[project].name`
   or is unqualified. Resolved to `<root>/src/<rest>.lll`.
4. **External dep** — path has 3+ segments and the first two look like a
   host/owner pair (`github.com/alice/json/Parser` → dep
   `github.com/alice/json`, module `Parser`). Matched against `[deps]`; the
   file is read from `vendor/<dep-path>/src/<module>.lll`. If the dep isn't
   in `ll.sum` the build fails with `E022 UnvendoredDep` and a suggestion to
   run `lllc mod tidy`.

Because ll-lang identifiers are TitleCase-vs-lowercase sensitive and `/` is
not a legal identifier char, we keep the `import` syntax dotted and use a
leading-segment convention: if the dotted name's first segment contains
dots-that-look-like-a-host, we rewrite the lexer to accept one dotted host
segment followed by identifiers, e.g. `import github.com/alice/json/Parser`
tokenized as `Path("github.com/alice/json", ["Parser"])`. Internally stored
as a 2-tuple `(DepPath option, string list)`.

Grammar tweak (one new production, documented in `spec/grammar.ebnf` at
implementation time):

```
ImportStmt ::= "import" (DepPrefix "/")? ModuleIdent ("." ModuleIdent)*
DepPrefix  ::= host "/" owner "/" repo     -- at least one slash-segment
```

No change to AST other than the `Imports` field becoming
`(DepPath option * string list) list`.

## 4. External dependency model — Go-style

**Add a dep.** User edits `ll.toml` `[deps]` manually, runs `lllc mod tidy`.
`mod tidy` does:

1. Read `ll.toml`. For each dep not already in `vendor/` at the pinned
   version: shell out to `git clone --depth 1 --branch <tag> <url>
   vendor/<path>/` (or `git -C vendor/... fetch + checkout` if it's there at
   the wrong rev). We use git for everything in v1 — no custom registry, no
   protocol negotiation, just "git URL = dep URL".
2. Compute `sha256` over the content of each vendored dep's source tree
   (sorted-file, NUL-separated, excluding `.git`).
3. Walk each dep's `ll.toml` transitively and repeat. Version selection is
   **minimum version selection** (MVS), exactly like Go: if two deps require
   different versions of a third, pick the higher of the two; the manifest's
   own constraint always wins if present.
4. Rewrite `ll.sum` with every direct and transitive dep and its hash.
5. Delete any `vendor/` subtree not referenced by the final dep set.

**Lock file format — `ll.sum`.** One line per dep, sorted:

```
github.com/alice/json v1.2.0 sha256:3f7e9c1a2b4d...
github.com/alice/json/ll.toml v1.2.0 sha256:a1b2c3...
github.com/bob/http v0.4.1 sha256:998877...
github.com/bob/http/ll.toml v0.4.1 sha256:ddeeff...
```

Two lines per dep: one for the source tree content, one for the manifest
alone (so we can validate the dep's own dep list without re-hashing
everything). Exact Go-style `go.sum` layout, trivially parseable with
`split(' ')`.

On every build, `lllc` verifies every entry in `ll.sum` against the current
vendor tree. Any mismatch is a hard error (`E023 VendorHashMismatch`). Users
either run `lllc mod tidy` to accept the new content or revert the vendor
dir.

**CLI:**

- `lllc mod init <name>` — create `ll.toml`, `src/Main.lll`, `.gitignore`.
- `lllc mod tidy` — sync vendor + rewrite `ll.sum` as above.
- `lllc mod add <path>@<version>` — shortcut; writes the line to `ll.toml`
  then calls tidy.

No `mod vendor` (already always vendored), no `mod download` (same), no
`go get -u` equivalent — upgrades are "edit the manifest, run tidy." Deliberate.

## 5. Multi-file compilation

The compiler grows one new stage at the front: **ProjectLoader**. Pipeline becomes:

```
ll.toml → ProjectLoader → [Lexer → Parser → Elaborator → HMInfer] per file → LinkedCodegen → bin/<name>.fs
```

`ProjectLoader`:

1. Reads `ll.toml`, resolves vendor.
2. Globs `src/**/*.lll` + every vendored dep's `src/**/*.lll`. Parses each
   file to get its `module` header + `imports`.
3. Builds a module DAG. Cycle → `E024 ModuleCycle` with the full SCC.
4. Topologically sorts.
5. Runs Elaborator + HMInfer **per file, in topological order**, threading
   a single `Env` through so downstream files see upstream exports.
   Non-exported decls are filtered out of the env before handing to the next
   file. Each `TypedModule` goes into the `.llcache/` directory keyed by
   `(file content hash, manifest hash, upstream env hash)`.
6. `LinkedCodegen` emits one F# file per `TypedModule`, wraps each in
   `module <dotted.path> =`, concatenates in topo order, and writes
   `bin/<project>.fs`. F#'s own compile-order requirement is satisfied for
   free by our topo sort.

**Incremental.** Files whose `(content, upstream-env)` hash is already in
`.llcache/` skip lex/parse/elaborate/infer and just reuse the cached
`TypedModule`. Codegen always runs because it's cheap. A cold build does the
whole graph; a warm build with one file changed re-types only that file and
its descendants. No daemon, no watch mode in v1.

**Why not batch into one module.** Elaborator and HMInfer already assume
one `LLModule` at a time and carry their own `Env`. Keeping the file
boundary matches what they expect and avoids rewriting unification to track
per-file source spans. The cost is one `Env` merge per file, which is O(n)
in exports — negligible.

## 6. Build outputs

- `lllc build` (inside a project): writes `bin/<name>.fs`,
  `bin/<name>.fsproj` (generated, deterministic, regenerated every build),
  then shells out to `dotnet build bin/<name>.fsproj -c Release`. Final
  artifact: `bin/<name>.dll` (+ `bin/<name>` launcher script on Unix,
  `.cmd` on Windows).
- `lllc build` (single-file mode, §8): unchanged — writes `<file>.fs` next to
  the source.
- `lllc run`: project mode builds then `dotnet bin/<name>.dll`; single-file
  mode keeps the `dotnet fsi` hack.
- `lllc test`: project mode discovers `tests/**/*.lll`, compiles them along
  with `src/`, emits a generated `TestRunner.fs` that calls every `fn test_*`
  in topological order, prints `PASS`/`FAIL`, exits non-zero on any failure.
  Dead simple — no xUnit binding, no assertions framework beyond a stdlib
  `assert : Bool -> Str -> Unit`. We can add fixtures later.

All generated files live under `bin/`; nothing is ever written next to source
files in project mode. `bin/` is safe to `rm -rf` at any time.

## 7. CLI surface

```
lllc build [path]              build project (cwd) or single file
lllc run   [path] [-- args…]   build+run
lllc test  [path]              run tests
lllc mod   init <name>         scaffold new project
lllc mod   tidy                sync vendor + ll.sum
lllc mod   add  <path>@<ver>   add a dep
lllc mod   why  <path>         explain why a dep is in the graph
lllc version                   print compiler version
```

No `fmt`, no `check`, no `clean` (just `rm -rf bin .llcache`). `mod why` is
trivially useful and almost free given we already have the dep DAG.

Flags kept to the absolute minimum: `-v` (verbose), `--no-cache` (bypass
`.llcache/`), `-o <path>` (override output dir, build only). No `--target`
or `--release` yet — release is the only mode.

## 8. Backward compatibility — single-file mode

**Yes, existing single-file `.lll` programs keep working unchanged.**

Rule: if `lllc build foo.lll` is invoked **and** walking upward from `foo.lll`
finds no `ll.toml` before hitting the filesystem root, we stay in the current
legacy single-file path: parse, elaborate, infer, codegen to `foo.fs`
next to the source. The entire project system is a no-op in this mode.

This matters because (a) the 415-test corpus under `spec/examples/` must
stay green, (b) the self-hosting bootstrap at Phase 7.9q is in flight and
must not be disturbed, and (c) the user guide's tutorial is a series of
single-file examples. Adding a project layer is strictly additive.

The single-file path also loses `import Foo.Bar` resolution for anything
outside stdlib — same as today. Imports beyond `Std.*` and `Platform.*`
inside single-file mode are an error (`E025 NoProjectForImport`, with a
hint to run `lllc mod init`).

---

## Implementation order (non-binding sketch)

1. TOML subset parser + manifest data model.
2. `lllc mod init` + `.gitignore` + empty `src/Main.lll`.
3. ProjectLoader stage (single project, no deps) — unblocks multi-file.
4. LinkedCodegen + `bin/` generation + `.fsproj` template.
5. `.llcache/` incremental reuse.
6. Vendor fetch (git clone) + `ll.sum` + `mod tidy` + `mod add`.
7. MVS resolver.
8. `Platform.*` shims + `[platform].use` gate.
9. `lllc test` runner.

Each step is its own PR with its own error codes and corpus entries. Phase
4 above (first four steps) is the minimum viable project system; everything
after is bolt-on.

---

## Reconciliation

Reconciled with sibling spec `2026-04-09-ll-lang-mcp-server.md` on
2026-04-09. This spec is the owner of CLI structure, file layout, and
dependency resolution; the MCP spec conforms. No edits were required
here — the MCP spec was amended to reference §2 (root discovery), §3
(import resolution order), §5 (`ProjectLoader`, `.llcache/`), §7 (CLI
dispatch table), and §8 (single-file fallback), and to expose
`ll.sum`-shaped dep info through its `project_info` tool. Areas
verified: CLI union, file layout, project root discovery, process
lifecycle, dependency model, config, error codes, tool inventory,
terminology.
