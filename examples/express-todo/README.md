# Express-style TODO API in ll-lang

A minimal Express HTTP server whose routes are declared from `.lll` source
and served by Node/Bun. Demonstrates how to drive a JavaScript library from
ll-lang today, and surfaces the current FFI constraint so the path forward
is visible.

## Run it

```bash
cd examples/express-todo

# 1. compile Main.lll → bin/typescript/ExpressTodo.ts
lllc build --target ts

# 2. install Express + type defs
bun install

# 3. start the server (shim + generated code)
bun run src/runtime.ts &

# 4. hit the routes declared from ll-lang
curl localhost:3000/hello
# → Hello from ll-lang

curl localhost:3000/todos
# → [{"id":1,"text":"write ll-lang"},{"id":2,"text":"ship demo"}]

curl localhost:3000/health
# → ok

kill %1
```

Both `bun run src/runtime.ts` and `node --experimental-strip-types src/runtime.ts`
work; Bun is recommended because it loads `.ts` files natively with no extra
flags.

## What's in each file

| File | Role |
|------|------|
| `src/Main.lll` | ll-lang source: declares routes and starts the server via a tiny command DSL tunnelled through `fetch`. |
| `src/runtime.ts` | Hand-written TypeScript shim. Installs `globalThis.fetch` that speaks the DSL, constructs the Express app, then imports the generated code. |
| `bin/typescript/ExpressTodo.ts` | Output of `lllc build --target ts`. Do not edit by hand. |
| `lll.toml` | ll-lang project manifest. |
| `package.json` | npm manifest; depends on `express`. |

## How the FFI tunnel works

ll-lang 1.x ships a whitelist of `external` names that each backend knows how
to map. For the TypeScript backend the whitelist is exactly:

- `console_log` → `console.log`
- `JSON_parse`  → `JSON.parse`
- `fetch`       → `(globalThis as any).fetch`

Any other `external` declaration fails the project build with
`E026 UnknownExternalMapping`. That means we cannot simply write
`external app_get(path Str)(body Str) Unit` today.

Because `fetch` compiles to a call against `globalThis.fetch`, we can shadow
that global with our own implementation and use URL strings as an
out-of-band command channel:

| URL from ll-lang                    | What the shim does                            |
|-------------------------------------|-----------------------------------------------|
| `route://GET/<path>/<body>`         | `app.get('/<path>', (_req, res) => res.send(<body>))` |
| `listen://<port>`                   | `app.listen(<port>)`                          |

`src/Main.lll` calls two wrapper functions built on `fetch`:

```lll
registerGet(path Str)(body Str) =
  fetch ("route://GET" + path + "/" + body)

startServer(port Int) =
  fetch ("listen://" + intToStr port)
```

`src/runtime.ts` parses those URLs and drives Express for real.

## Known limits (today)

This is a deliberately small slice to prove the transport works end-to-end.
What's missing, and why:

- **Only static GET routes.** The current tunnel sends a string body with
  each `registerGet` call, so the handler is constant. A dynamic handler
  (POST `/todos` that mutates an in-memory store) requires ll-lang to
  receive a request and return a response, which needs either callback
  externals or a bidirectional FFI primitive. Neither is in the 1.x
  whitelist.
- **No mutable state from ll-lang.** ll-lang 1.x has no `IORef`/`State`
  primitive. A real TODO store would live in TypeScript until that exists.
- **No per-file TypeScript externals map.** The whitelist is hardcoded in
  `initial-compiler/src/LLLangCompiler/Platform.fs`. A user-extensible
  mapping (e.g. `[platform.typescript.externals]` in `lll.toml`, or a
  per-project `FFI.lll` that the compiler actually consults) would remove
  the need for the `fetch`-tunnel hack entirely. Tracking: `v2` FFI work.

The example intentionally keeps the workaround visible instead of hiding
it, because the goal is to make the next FFI improvement obvious.

## Why the shim cannot be removed without F# changes

In 2026-04 we investigated whether the demo could be rewritten as a pure
`.lll` program (no `runtime.ts`, `lllc build --target ts` produces a
complete runnable TypeScript file). Every path runs into the F# bootstrap.
Summary of what was tried and why each fails:

**Path A — Route through the self-hosted TS codegen** (`stdlib/src/CodegenTS.lll`).
That emitter is lenient — it prints `declare function <name>(...args: any[]): any;`
for every `DExternal` regardless of name (`stdlib/src/CodegenTS.lll:363`).
Blocker: the self-hosted path is still driven by the F# bootstrap
(`lllc self` → `LLLang.Compiler.compileProjectToModules` → F# `Codegen.emitProjectFiles`,
never reaches the `.lll` codegens). `lllc build --target ts` always uses
the F# `CodegenTS.fs`, which guards with `tryGetExternalTarget TypeScript`
and emits `""` for anything outside the 3-entry whitelist.

**Path B — Use `opaque` types + dynamic dispatch.** Opaque types let us
declare `opaque ExpressApp` without mapping, but *creating* one still
requires a callable external (`external expressInit() ExpressApp`),
which hits the whitelist. Wall is the same.

**Path C — Use only the 3 whitelisted externals.**
`fetch`, `console_log`, and `JSON_parse` are the only callable names. The
current `runtime.ts` shim already pushes this to the limit: it hijacks
`globalThis.fetch` with a URL command DSL. Anything beyond that (import
`express`, bind `app.get`, call `app.listen`) requires either a TS file
that runs before the generated module — which is exactly the shim — or
F#-level support for module-scope statements in ll-lang (there is none;
`.lll` only produces `const` bindings at the top level).

**Path D — Have the compiler read `sdks/Platform.TypeScript.SDK/src/FFI.lll`.**
That file exists (it duplicates the 3-entry map in ll-lang form,
`sdks/Platform.TypeScript.SDK/src/FFI.lll:6-10`) and was clearly intended
to be the extensible mapping. However, the F# `Platform.fs` registry
loader only reads `lll.toml` and `meta.toml` (`[sdk]` and `[build]`
tables) — it never consults `FFI.lll`. The file is documentation-only
today. Honoring it is a pure F# change.

**Path E — Extend `lll.toml` with `[platform.typescript.externals]`.**
`Manifest.fs` only parses `[project]`, `[deps]`, and `[platform].use`.
Unknown sections are silently ignored (`Manifest.fs:173`). Adding a new
section requires F# changes.

**Path F — Emit raw TS from `.lll` (imports, globals, side-effects).**
`CodegenTS.fs` maps every decl to `const`, `function`, or `type`
declarations. There is no syntax in the language and no AST node that
emits a statement, a module-level `import`, or an assignment to
`globalThis`. Adding one requires F# changes.

### What definitively blocks the pure-`.lll` path

The elaborator itself accepts any `external` name (`Elaborator.fs:217`).
The rejection lives in `Compiler.fs:72-78`:

```fsharp
let private validateExternalMappingsForTarget (target: Target) (pm: PosMap) (m: LLModule) : LLError list =
    m.Decls
    |> List.choose (fun (decl, _isExported) ->
        match decl with
        | DExternal sigRecord when hasExternalTarget target sigRecord.Name = false ->
            Some (externalMappingError target pm sigRecord)
        | _ -> None)
```

Every build path (`cmdBuildFile`, `cmdBuildProject`, `cmdCheckFile`,
`cmdCheckProject`, `cmdRunSelf`) flows through
`compileProjectToModulesForTarget` → `compileFileWithEnvForTarget` →
`inferModuleForTarget`, which calls `validateExternalMappingsForTarget`.
There is no flag or `lll.toml` setting that bypasses this function. And
even if we bypassed it, `CodegenTS.fs:594-609` emits `""` for unmapped
externals — the generated TS would reference undefined names.

### Conclusion

Removing the `runtime.ts` shim while keeping the demo written in `.lll`
requires one of the following F# edits:

1. **Read `sdks/Platform.TypeScript.SDK/src/FFI.lll` at build time** and
   merge its entries into `typeScriptExternalTargetMap` (closes #155
   Proposal C).
2. **Honor `[platform.typescript.externals]` in `lll.toml`** as an
   additive per-project map (closes #155 Proposal B).
3. **Relax `validateExternalMappingsForTarget`** to accept unknown names
   and let `CodegenTS.fs` emit the external's name verbatim (closes #155
   Proposal A).

All three are bootstrap compiler changes, tracked under
[#155](https://github.com/Neftedollar/ll-lang/issues/155). Until one of
them lands, the `fetch`-tunnel + `runtime.ts` shim is the least-worst
way to demo Express from `.lll`, which is why this example ships with
the shim visible instead of hiding it behind `lllc` plumbing.

## Rebuild after editing `Main.lll`

```bash
lllc build --target ts
bun run src/runtime.ts
```

No change to `runtime.ts` is needed unless you add a new DSL command.

## References

- Issue: [#151](https://github.com/Neftedollar/ll-lang/issues/151) —
  Express.js demo
- Issue: [#155](https://github.com/Neftedollar/ll-lang/issues/155) —
  FFI whitelist blocker (root cause)
- TypeScript external whitelist: `initial-compiler/src/LLLangCompiler/Platform.fs:65-71`
- E026 enforcement: `initial-compiler/src/LLLangCompiler/Compiler.fs:72-78`
- E026 docs: `docs/language-spec.md` §2.8
