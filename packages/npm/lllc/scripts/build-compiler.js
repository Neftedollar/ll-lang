#!/usr/bin/env node
/**
 * CI helper: patch stdlib/src/Compiler.ts for library use, then produce:
 *   lib/compiler.ts   — patched TypeScript (used by Bun natively)
 *
 * lllc ships compiler.ts and requires Bun — no CJS bundle needed.
 *
 * Usage (from repo root, after `lllc build --target ts stdlib/src/Compiler.lll`):
 *   node packages/npm/lllc/scripts/build-compiler.js
 */
"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..", "..", "..", "..");
const SRC_TS = path.join(REPO_ROOT, "stdlib", "src", "Compiler.ts");
const LIB_DIR = path.join(__dirname, "..", "lib");
const OUT_TS = path.join(LIB_DIR, "compiler.ts");

if (!fs.existsSync(SRC_TS)) {
  console.error(
    `build-compiler: ${SRC_TS} not found.\n` +
      "  Run: dotnet lllc.dll build --target ts stdlib/src/Compiler.lll",
  );
  process.exit(1);
}

// Public API exported from Compiler.lll
const EXPORTS = [
  "compile",
  "compileWithInfer",
  "collectAllErrors",
  "nextBlockerFull",
  "compileProject",
  "tokenEstimate",
  "lookupSymbol",
];

let content = fs.readFileSync(SRC_TS, "utf8");

// ── 1. Deduplicate identical `const X = ...` declarations ─────────────────────
// The TypeScript codegen inlines stdlib definitions for each import chain,
// producing duplicate `const check = ...`, `const boolStr = ...`, etc.
// Keep the first occurrence of each (name, body) pair; skip exact duplicates.
// Mismatched bodies (same name, different body) are kept and may cause esbuild
// errors — that would indicate a genuine naming collision that needs a proper fix.
{
  const seen = new Map(); // name → body
  const lines = content.split("\n");
  const out = [];
  for (const line of lines) {
    const m = line.match(/^const (\w+)\s*=/);
    if (m) {
      const name = m[1];
      if (seen.has(name)) {
        if (seen.get(name) === line) continue; // exact dup → skip
        // different body: keep (will surface as an esbuild error if it's a re-decl)
      } else {
        seen.set(name, line);
      }
    }
    out.push(line);
  }
  content = out.join("\n");
}

// ── 2. Replace bare `main();` with a guard ────────────────────────────────────
content = content.replace(
  /^main\(\);$/m,
  "if (typeof require !== 'undefined' && typeof module !== 'undefined' && require.main === module) main();",
);

// ── 3. Append CommonJS / Bun exports ─────────────────────────────────────────
content +=
  `\n// CommonJS / Bun exports — added by build-compiler.js\n` +
  `if (typeof module !== 'undefined') {\n` +
  `  module.exports = { ${EXPORTS.join(", ")} };\n` +
  `}\n`;

fs.mkdirSync(LIB_DIR, { recursive: true });

// Write patched TypeScript — used directly by Bun (runs TS natively)
fs.writeFileSync(OUT_TS, content);
console.log(`build-compiler: wrote ${OUT_TS}  (Bun runtime)`);
