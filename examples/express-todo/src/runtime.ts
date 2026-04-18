// runtime.ts — Express shim for ll-lang FFI tunnel
//
// ll-lang 1.x only whitelists `console_log`, `JSON_parse`, and `fetch` as
// TypeScript externals. To wire an Express app from .lll without patching
// the compiler, Main.lll calls `fetch(url)` with a tiny command DSL that
// this module interprets before the generated Main.ts executes.
//
// Command DSL understood here:
//   route://GET/<path>/<body>   → app.get('/<path>', (_req, res) => res.send(<body>))
//   listen://<port>             → app.listen(<port>)
//
// Paths may contain additional slashes (e.g. /a/b); the parser treats the
// last `/`-separated segment as the response body.

import express from "express";

const app = express();
app.use(express.json());

type Route = { method: "GET"; path: string; body: string };
const routes: Route[] = [];

function parseRouteUrl(url: string): Route | null {
  // Expected: route://GET/<path-with-slashes>/<body>
  const prefix = "route://GET";
  if (!url.startsWith(prefix)) return null;
  const rest = url.slice(prefix.length);
  if (!rest.startsWith("/")) return null;
  // Split from the right: last segment is body, everything before is path.
  const lastSlash = rest.lastIndexOf("/");
  if (lastSlash <= 0) return null;
  const path = rest.slice(0, lastSlash);
  const body = rest.slice(lastSlash + 1);
  return { method: "GET", path, body };
}

(globalThis as any).fetch = (url: string) => {
  if (url.startsWith("route://")) {
    const r = parseRouteUrl(url);
    if (!r) {
      console.error(`[runtime] could not parse route URL: ${url}`);
      return "";
    }
    app.get(r.path, (_req, res) => res.send(r.body));
    routes.push(r);
    console.log(`[runtime] registered ${r.method} ${r.path} → ${r.body}`);
    return "ok";
  }

  if (url.startsWith("listen://")) {
    const port = parseInt(url.slice("listen://".length), 10);
    if (Number.isNaN(port)) {
      console.error(`[runtime] bad listen URL: ${url}`);
      return "";
    }
    app.listen(port, () => {
      console.log(`[runtime] listening on http://localhost:${port}`);
      for (const r of routes) {
        console.log(`  ${r.method} ${r.path}`);
      }
    });
    return "ok";
  }

  console.error(`[runtime] unknown fetch command: ${url}`);
  return "";
};

// Load the compiled ll-lang program. Dynamic import runs after the shim is
// installed so `globalThis.fetch` is already in place when Main.ts's main()
// executes at the bottom of the generated file.
await import("../bin/typescript/ExpressTodo.ts");
