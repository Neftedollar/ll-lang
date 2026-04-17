#!/usr/bin/env node
"use strict";
/**
 * Download the native lllc binary for the current platform.
 * Run via: lllc --install-binary
 * Or require() from cli.cjs.
 */

const https = require("https");
const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const VERSION = require("../package.json").version;
const REPO = "Neftedollar/ll-lang";
const BASE_URL = `https://github.com/${REPO}/releases/download/v${VERSION}`;
const ALLOWED_HOSTS = new Set(["github.com", "objects.githubusercontent.com"]);

function getRid() {
  const p = process.platform;
  const a = process.arch;
  if (p === "linux" && a === "x64") return { rid: "linux-x64", ext: "tar.gz" };
  if (p === "linux" && a === "arm64")
    return { rid: "linux-arm64", ext: "tar.gz" };
  if (p === "darwin" && a === "x64") return { rid: "osx-x64", ext: "tar.gz" };
  if (p === "darwin" && a === "arm64")
    return { rid: "osx-arm64", ext: "tar.gz" };
  if (p === "win32" && a === "x64") return { rid: "win-x64", ext: "zip" };
  throw new Error(
    `lllc: unsupported platform ${p}/${a}.\n` +
      `Install via "dotnet tool install -g lllc" instead.`,
  );
}

function validateUrl(url) {
  let parsed;
  try {
    parsed = new URL(url);
  } catch {
    throw new Error(`lllc: invalid URL: ${url}`);
  }
  if (parsed.protocol !== "https:")
    throw new Error(`lllc: only HTTPS downloads permitted`);
  if (!ALLOWED_HOSTS.has(parsed.hostname))
    throw new Error(`lllc: download host not allowed: ${parsed.hostname}`);
  return parsed;
}

function download(url, dest) {
  validateUrl(url);
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(dest);
    const get = (u) => {
      validateUrl(u);
      https
        .get(u, (res) => {
          if (res.statusCode === 301 || res.statusCode === 302) {
            return get(res.headers.location);
          }
          if (res.statusCode !== 200) {
            return reject(new Error(`HTTP ${res.statusCode} from ${u}`));
          }
          res.pipe(file);
          file.on("finish", () => file.close(resolve));
        })
        .on("error", (e) => {
          fs.unlink(dest, () => {});
          reject(e);
        });
    };
    get(url);
  });
}

async function main() {
  const { rid, ext } = getRid();
  const archive = `lllc-${rid}.${ext}`;
  const url = `${BASE_URL}/${archive}`;
  const nativeDir = path.join(__dirname, "..", "bin", "native");
  const archivePath = path.join(nativeDir, archive);

  fs.mkdirSync(nativeDir, { recursive: true });

  console.error(`lllc: downloading ${url}`);
  await download(url, archivePath);

  if (ext === "tar.gz") {
    execSync(`tar -xzf "${archivePath}" -C "${nativeDir}"`);
  } else {
    execSync(
      `powershell -Command "Expand-Archive -Force -Path '${archivePath}' -DestinationPath '${nativeDir}'"`,
      { shell: true },
    );
  }

  fs.unlinkSync(archivePath);

  if (process.platform !== "win32") {
    const binaryPath = path.join(nativeDir, "lllc");
    fs.chmodSync(binaryPath, 0o755);
    console.error(`lllc: native binary installed to ${binaryPath}`);
  } else {
    console.error(
      `lllc: native binary installed to ${path.join(nativeDir, "lllc.exe")}`,
    );
  }
}

main().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
