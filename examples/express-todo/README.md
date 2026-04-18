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

## Rebuild after editing `Main.lll`

```bash
lllc build --target ts
bun run src/runtime.ts
```

No change to `runtime.ts` is needed unless you add a new DSL command.

## References

- Issue: [#151](https://github.com/Neftedollar/ll-lang/issues/151)
- TypeScript external whitelist: `initial-compiler/src/LLLangCompiler/Platform.fs`
- E026 docs: `docs/language-spec.md` §2.8
