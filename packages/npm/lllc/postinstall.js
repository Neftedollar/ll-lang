#!/usr/bin/env node
"use strict";

const https = require("https");
const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const VERSION = require("./package.json").version;
const REPO = "Neftedollar/ll-lang";
const BASE_URL = `https://github.com/${REPO}/releases/download/v${VERSION}`;

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
    `lllc: unsupported platform ${p}/${a}. ` +
      `Install via "dotnet tool install -g lllc" instead.`,
  );
}

function download(url, dest) {
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(dest);
    const get = (u) => {
      https
        .get(u, (res) => {
          if (res.statusCode === 301 || res.statusCode === 302) {
            return get(res.headers.location);
          }
          if (res.statusCode !== 200) {
            return reject(new Error(`HTTP ${res.statusCode} downloading ${u}`));
          }
          res.pipe(file);
          file.on("finish", () => file.close(resolve));
        })
        .on("error", reject);
    };
    get(url);
  });
}

async function main() {
  const { rid, ext } = getRid();
  const archive = `lllc-${rid}.${ext}`;
  const url = `${BASE_URL}/${archive}`;
  const nativeDir = path.join(__dirname, "bin", "native");
  const archivePath = path.join(nativeDir, archive);

  fs.mkdirSync(nativeDir, { recursive: true });

  console.log(`lllc: downloading ${url}`);
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

  const binaryName = process.platform === "win32" ? "lllc.exe" : "lllc";
  const binaryPath = path.join(nativeDir, binaryName);
  if (process.platform !== "win32") {
    fs.chmodSync(binaryPath, 0o755);
  }

  console.log(`lllc: installed to ${binaryPath}`);
}

main().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
