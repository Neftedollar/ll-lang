# ll-lang: a compiler that compiles itself in ~2600 lines of F#

`ll-lang` now reaches a self-hosting fixpoint: `compiler₁.fs == compiler₂.fs`.

It also emits multiple host targets from the same source language: `fs`, `ts`, `py`, `java`, `cs`, and experimental `llvm`.

## Copy/paste check

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
./tools/bootstrap-self.sh install
BOOTSTRAP_BIN="$(./tools/bootstrap-self.sh path)"
"$BOOTSTRAP_BIN" check "$PWD/lllcself/src/Main.lll"
```

Expected result: `OK`

## 24-second self-host demo

![24-second self-host cycle: bootstrap install, multi-target compile, and compiler self-check](https://raw.githubusercontent.com/Neftedollar/ll-lang/main/docs/assets/demo/self-host-cycle.gif)

## Why this is interesting

- The compiler CLI under `lllcself/src/` is **2589 lines** today.
- The bootstrap path uses a pinned artifact verified against `bootstrap/lllc-bootstrap.lock.json`.
- The same toolchain already emits F#, TypeScript, Python, Java, C#, and experimental LLVM IR.

## More context

- Repo: https://github.com/Neftedollar/ll-lang
- Longer write-up: https://dev.to/neftedollar/the-2600-line-compiler-that-compiles-itself-and-emits-f-typescript-python-java-and-c-49lh
