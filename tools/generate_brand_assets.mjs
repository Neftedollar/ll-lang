#!/usr/bin/env node

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { Resvg } from "@resvg/resvg-js";
import gifenc from "gifenc";
import { PNG } from "pngjs";

const { GIFEncoder, quantize, applyPalette } = gifenc;

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const ROOT = path.resolve(__dirname, "..");

const HERO_OUT = path.join(ROOT, "docs/assets/hero/hero-story.gif");
const POSTER_OUT = path.join(ROOT, "docs/assets/hero/hero-poster.png");
const OG_OUT = path.join(ROOT, "docs/assets/social/og.png");

const colors = {
  paper: "#fffaf1",
  ink: "#101615",
  inkSoft: "#53605d",
  accent: "#f46a1f",
  accentSoft: "#ffcb8c",
  teal: "#127d7a",
  mint: "#7becc3",
  dark: "#111918",
  white: "#ffffff",
};

const lllCode = [
  "module Examples.Basics",
  "",
  "add(a Int)(b Int) = a + b",
  "double(x Int) = x * 2",
  "",
  "clamp(x Int)(lo Int)(hi Int) =",
  "  if x < lo",
  "    lo",
  "  else if x > hi",
  "    hi",
  "  else x",
];

const tsCode = [
  "const add = (a: number) =>",
  "  (b: number): number => (a + b);",
  "",
  "const double = (x: number): number =>",
  "  (x * 2);",
  "",
  "const clamp = (x: number) =>",
  "  (lo: number) => (hi: number): number =>",
  "    ((x < lo) ? lo :",
  "      ((x > hi) ? hi : x));",
];

function escapeXml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function easeOutCubic(value) {
  return 1 - (1 - value) ** 3;
}

function codeBlock({
  x,
  y,
  width,
  height,
  title,
  lines,
  counter,
  counterFill,
  fill,
  stroke,
  labelFill,
  codeFill,
}) {
  const textLines = lines
    .map((line, index) => {
      const yOffset = y + 96 + index * 28;
      return `<text x="${x + 24}" y="${yOffset}" fill="${codeFill}" font-size="20" font-family="'DejaVu Sans Mono', monospace">${escapeXml(
        line
      )}</text>`;
    })
    .join("");

  return `
    <g>
      <rect x="${x}" y="${y}" width="${width}" height="${height}" rx="26" fill="${fill}" stroke="${stroke}" />
      <text x="${x + 24}" y="${y + 34}" fill="${labelFill}" font-size="18" font-weight="700" font-family="'DejaVu Sans', sans-serif">${escapeXml(
        title
      )}</text>
      <rect x="${x + width - 126}" y="${y + 16}" width="102" height="36" rx="18" fill="${counterFill}" />
      <text x="${x + width - 75}" y="${y + 39}" text-anchor="middle" fill="${colors.ink}" font-size="18" font-weight="700" font-family="'DejaVu Sans Mono', monospace">${counter}</text>
      ${textLines}
    </g>
  `;
}

function heroSvg(progress) {
  const eased = easeOutCubic(progress);
  const tsTokens = Math.round(142 * eased);
  const llTokens = Math.round(110 * eased);
  const tighter = (1 + 0.29 * eased).toFixed(2);
  const accentWidth = 260 + Math.round(180 * eased);

  return `
    <svg xmlns="http://www.w3.org/2000/svg" width="1000" height="560" viewBox="0 0 1000 560">
      <defs>
        <filter id="blur-xl"><feGaussianBlur stdDeviation="48" /></filter>
        <filter id="blur-lg"><feGaussianBlur stdDeviation="30" /></filter>
      </defs>
      <rect width="1000" height="560" fill="${colors.paper}" />
      <circle cx="180" cy="90" r="140" fill="${colors.accent}" fill-opacity="0.28" filter="url(#blur-xl)" />
      <circle cx="860" cy="500" r="160" fill="${colors.teal}" fill-opacity="0.22" filter="url(#blur-xl)" />
      <circle cx="790" cy="160" r="120" fill="${colors.accentSoft}" fill-opacity="0.18" filter="url(#blur-lg)" />

      <text x="52" y="58" fill="${colors.ink}" font-size="48" font-weight="700" font-family="'DejaVu Sans', sans-serif">Same example. Fewer tokens.</text>
      <text x="52" y="108" fill="${colors.inkSoft}" font-size="20" font-family="'DejaVu Sans', sans-serif">ll-lang keeps the logic and drops the ceremony.</text>

      <rect x="52" y="136" width="${accentWidth}" height="14" rx="7" fill="${colors.accent}" />
      <text x="52" y="182" fill="${colors.ink}" font-size="18" font-weight="700" font-family="'DejaVu Sans', sans-serif">TypeScript</text>
      <text x="518" y="182" fill="${colors.ink}" font-size="18" font-weight="700" font-family="'DejaVu Sans', sans-serif">ll-lang</text>

      ${codeBlock({
        x: 52,
        y: 200,
        width: 428,
        height: 296,
        title: "TypeScript",
        lines: tsCode,
        counter: tsTokens,
        counterFill: colors.accentSoft,
        fill: "rgba(255,255,255,0.82)",
        stroke: "rgba(16,22,21,0.08)",
        labelFill: colors.ink,
        codeFill: "#4b5653",
      })}

      ${codeBlock({
        x: 518,
        y: 200,
        width: 430,
        height: 296,
        title: "ll-lang",
        lines: lllCode,
        counter: llTokens,
        counterFill: colors.mint,
        fill: "rgba(18,24,24,0.95)",
        stroke: "rgba(255,255,255,0.08)",
        labelFill: colors.paper,
        codeFill: "#eef0e8",
      })}

      <rect x="52" y="504" width="270" height="40" rx="20" fill="rgba(255,255,255,0.8)" />
      <text x="70" y="529" fill="${colors.accent}" font-size="18" font-weight="700" font-family="'DejaVu Sans', sans-serif">${tighter}x tighter than TS</text>
      <text x="944" y="529" text-anchor="end" fill="${colors.inkSoft}" font-size="20" font-family="'DejaVu Sans', sans-serif">benchmarks/results/token-benchmark.md</text>
    </svg>
  `;
}

function ogSvg() {
  return `
    <svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630">
      <defs>
        <filter id="blur-xl"><feGaussianBlur stdDeviation="58" /></filter>
        <filter id="blur-lg"><feGaussianBlur stdDeviation="34" /></filter>
      </defs>
      <rect width="1200" height="630" fill="#0c1212" />
      <circle cx="180" cy="84" r="180" fill="${colors.accent}" fill-opacity="0.30" filter="url(#blur-xl)" />
      <circle cx="1080" cy="540" r="220" fill="${colors.teal}" fill-opacity="0.22" filter="url(#blur-xl)" />
      <circle cx="940" cy="120" r="120" fill="${colors.accentSoft}" fill-opacity="0.16" filter="url(#blur-lg)" />

      <rect x="74" y="64" width="92" height="92" rx="24" fill="${colors.accent}" />
      <rect x="96" y="82" width="12" height="42" rx="6" fill="${colors.paper}" />
      <rect x="116" y="82" width="12" height="42" rx="6" fill="${colors.paper}" />
      <rect x="136" y="116" width="12" height="12" rx="6" fill="${colors.paper}" />
      <text x="194" y="120" fill="${colors.paper}" font-size="46" font-weight="700" font-family="'DejaVu Sans Mono', monospace">ll-lang</text>

      <text x="76" y="222" fill="${colors.paper}" font-size="62" font-weight="700" font-family="'DejaVu Sans', sans-serif">Statically-typed functional language</text>
      <text x="76" y="290" fill="${colors.paper}" font-size="62" font-weight="700" font-family="'DejaVu Sans', sans-serif">for LLM code generation</text>
      <text x="76" y="346" fill="#e5e9e3" font-size="26" font-family="'DejaVu Sans', sans-serif">Compact syntax, compile-time feedback, and MCP tooling for agent workflows.</text>

      <rect x="76" y="398" width="216" height="48" rx="24" fill="rgba(255,255,255,0.12)" />
      <text x="184" y="428" text-anchor="middle" fill="${colors.accentSoft}" font-size="18" font-weight="700" font-family="'DejaVu Sans', sans-serif">110 tokens in ll-lang</text>

      <rect x="650" y="86" width="462" height="438" rx="28" fill="rgba(255,255,255,0.08)" stroke="rgba(255,255,255,0.18)" />
      <text x="680" y="126" fill="${colors.accentSoft}" font-size="18" font-weight="700" font-family="'DejaVu Sans', sans-serif">Snippet</text>
      <text x="680" y="174" fill="${colors.paper}" font-size="24" font-family="'DejaVu Sans Mono', monospace">module Hello</text>
      <text x="680" y="258" fill="${colors.paper}" font-size="24" font-family="'DejaVu Sans Mono', monospace">Hello = printfn "Hello, ll-lang!"</text>
      <text x="680" y="336" fill="#c7f5de" font-size="24" font-family="'DejaVu Sans Mono', monospace">./tools/lllc-bootstrap.sh run hello.lll</text>
      <text x="680" y="378" fill="#c7f5de" font-size="24" font-family="'DejaVu Sans Mono', monospace"># Hello, ll-lang!</text>
      <text x="680" y="464" fill="#c4cec8" font-size="26" font-family="'DejaVu Sans', sans-serif">github.com/Neftedollar/ll-lang</text>
    </svg>
  `;
}

function renderSvg(svg, width, height) {
  const resvg = new Resvg(svg, {
    fitTo: { mode: "width", value: width },
    background: "rgba(255,255,255,0)",
    font: {
      loadSystemFonts: true,
      defaultFontFamily: "DejaVu Sans",
      defaultMonospaceFontFamily: "DejaVu Sans Mono",
    },
  });
  return resvg.render().asPng();
}

async function writePng(filePath, buffer) {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  await fs.writeFile(filePath, buffer);
}

async function generateHero() {
  const frameCount = 18;
  const width = 1000;
  const height = 560;
  const pngFrames = [];

  for (let index = 0; index < frameCount; index += 1) {
    const progress = index < 12 ? index / 12 : 1;
    pngFrames.push(renderSvg(heroSvg(progress), width, height));
  }

  await writePng(POSTER_OUT, pngFrames.at(-1));

  const encoder = GIFEncoder();
  for (let index = 0; index < pngFrames.length; index += 1) {
    const { data } = PNG.sync.read(pngFrames[index]);
    const palette = quantize(data, 256, { format: "rgba4444" });
    const indexed = applyPalette(data, palette, "rgba4444");
    encoder.writeFrame(indexed, width, height, {
      palette,
      delay: index < 14 ? 90 : 140,
      repeat: 0,
    });
  }
  encoder.finish();

  await fs.mkdir(path.dirname(HERO_OUT), { recursive: true });
  await fs.writeFile(HERO_OUT, Buffer.from(encoder.bytes()));
}

async function generateOg() {
  await writePng(OG_OUT, renderSvg(ogSvg(), 1200, 630));
}

async function main() {
  await generateHero();
  await generateOg();
  console.log(`wrote ${path.relative(ROOT, HERO_OUT)}`);
  console.log(`wrote ${path.relative(ROOT, POSTER_OUT)}`);
  console.log(`wrote ${path.relative(ROOT, OG_OUT)}`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
