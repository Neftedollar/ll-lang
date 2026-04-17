# Tutorial 05: Compile ll-lang to a Native Binary

Take a `.lll` source file, compile it through LLVM IR + `clang`, and get a standalone native executable for ARM64 (Apple Silicon) or x86_64 (Linux). **No .NET runtime needed at runtime** — the output is a plain POSIX executable.

> Status: **MVP / experimental.** The LLVM backend supports a growing subset of ll-lang — see [Supported features](#supported-features) below. For the stable, full-language targets (F#/TS/Python/Java/C#) use [Tutorial 04](./04-multi-target.md).

## What the pipeline does

```
hello.lll
  │
  │  lllc build --target llvm           (compiler: emits LLVM IR)
  ▼
hello.ll
  │
  │  tools/llvm-add-declares.py         (post-processor: patches IR)
  ▼
hello.patched.ll
  │
  │  clang + lllc_runtime.o             (link: native object + C runtime)
  ▼
hello      ←  native binary, runs on bare OS
```

`tools/lllc-native` is the one-shot wrapper that runs all four stages.

## Prerequisites

Four things:

### 1. `lllc` — the ll-lang compiler

Install one of:

```bash
# .NET global tool (requires .NET 10 SDK)
dotnet tool install -g lllc

# npm / Bun global (requires Bun — https://bun.sh)
npm install -g @neftedollar/lllc
```

> **Note (MVP caveat):** the currently-published NuGet `lllc` v1.2.0 is the self-hosted front-end; it handles `compile` / `check` / `run` but does not yet expose `build --target llvm`. `tools/llvm-build.sh` detects this and transparently falls back to the bootstrap compiler when you build from a checkout of the repo. The next NuGet publish will add `--target llvm` support to the tool on PATH.

### 2. `clang`

| Platform | Install |
|---|---|
| macOS | Xcode Command Line Tools: `xcode-select --install` |
| Debian/Ubuntu | `sudo apt-get install -y clang` |
| Fedora | `sudo dnf install -y clang` |

### 3. Python 3.8+

Used to run `tools/llvm-add-declares.py` (the IR post-processor). Most systems already have it; verify with `python3 --version`.

### 4. The ll-lang repo (for the runtime + tooling)

The C runtime (`sdks/Platform.LLVM.SDK/runtime/lllc_runtime.c`) and the IR post-processor (`tools/llvm-add-declares.py`) are **essential artefacts** but are not yet packaged. For the MVP you clone the repo to get them:

```bash
git clone https://github.com/Neftedollar/ll-lang
cd ll-lang
```

> **Roadmap:** these will be published as a separate `lllc-llvm-sdk` package so external users no longer need a checkout. Track progress on issue [#146](https://github.com/Neftedollar/ll-lang/issues/146).

## Hello world

From the repo root:

```bash
cat > hello.lll <<'EOF'
module Hello

main() = printfn "Hello, native!"
EOF

tools/lllc-native hello.lll ./hello
./hello
```

Expected output:

```
Hello, native!
```

The binary is ~50 KB and links against nothing but libc. Copy it to another machine with the same architecture and it will run there too.

## What's happening under the hood

```
[lllc] using: bootstrap lllc.dll (…/initial-compiler/…/lllc.dll)
[1/4] lllc build --target llvm hello.lll        ← emits hello.ll
[2/4] patching missing declares -> hello.patched.ll
[3/4] building runtime (.../lllc_runtime.o)
[4/4] clang -> ./hello
Built: ./hello
```

1. **`lllc build --target llvm`** compiles `.lll` → `.ll` (LLVM textual IR).
2. **`llvm-add-declares.py`** patches the IR: adds missing `declare` lines, fixes instruction-form GEPs, renames `@main` so the C runtime can wrap `argv`, repairs stale `phi` predecessors. See `sdks/Platform.LLVM.SDK/README.md` for the full list of patches.
3. **`make lllc_runtime.o`** builds the C runtime (`printfn`, `strConcat`, `intToStr`, `__ll_alloc`, `read_line`, `ll_getArgs`, …). Cached via `make` — only rebuilds when the source changes.
4. **`clang`** links the patched IR with the runtime object into a native binary.

## Supported features

Validated against examples `spec/examples/valid/36-llvm-*.lll` through `48-llvm-*.lll` (17 programs today). Highlights:

| Feature | Example | Status |
|---|---|---|
| `printfn "literal"` | `36-llvm-native-hello.lll` | works |
| Integer arithmetic + user-defined functions | `37-llvm-arith.lll` | works |
| `if` / `else` with integer comparison | `38-llvm-conditional.lll` | works |
| `strConcat`, `intToStr` | 37, 38, 39 | works (via C runtime) |
| Recursion (`factorial`) | `40-llvm-recursion.lll` | works |
| ADTs + `match` (`Maybe`, `Shape`, `List`) | `41`, `42`, `43` | works |
| Nested ADTs (`List[Maybe[Int]]`) | `45-llvm-list-of-maybe.lll` | works |
| File I/O (`readFile`, `writeFile`) | `46`, `47` | works |
| CLI args (`getArgs`) | `48-llvm-cli-args.lll` | works |

Full feature table with caveats: [`sdks/Platform.LLVM.SDK/README.md`](../../sdks/Platform.LLVM.SDK/README.md).

## Known limitations

The LLVM backend is an experimental subset. Big-ticket items still unsupported or requiring workarounds:

- **Higher-order built-ins** (`listMap`, `listFold`, `listFilter` when passed as values to `main`) are silently dropped by the frozen codegen. Workaround: write a recursive user-defined helper (see `44-llvm-list-map.lll` for `mapList`).
- **`_ =` bindings** are dropped. Use a named binding when sequencing side-effects: `r1 = printfn s1` not `_ = printfn s1`.
- **ADT match-arm ordering:** list nullary constructors before multi-field ones (the codegen dereferences tail pointers unconditionally).
- **Traits, modules, IO monad** — not yet supported by the LLVM backend. Use target `fs` / `ts` / `py` / `java` / `cs` for full-language programs.
- **GC** — stubbed (raw `malloc`). Fine for short-lived CLI tools; not production-ready for long-running services.
- **Tail-call optimisation** — none.

For the canonical per-example limitation matrix, see the "Known limitations" section of `sdks/Platform.LLVM.SDK/README.md`.

## Troubleshooting

### `error: lllc not found.`

Nothing on PATH supports `build --target llvm`, and no bootstrap `lllc.dll` was found.

- Install the .NET global tool: `dotnet tool install -g lllc`, **or**
- Install the npm package: `npm install -g @neftedollar/lllc`, **or**
- Build the bootstrap from the repo: `dotnet build initial-compiler/src/LLLangTool/LLLangTool.fsproj -c Debug`.

### `note: /…/lllc does not support 'build --target llvm'; falling back to bootstrap lllc.dll`

Informational, not an error. It means the `lllc` on your PATH is the self-hosted wrapper (`lllcself`) published today, which does not yet speak `--target llvm`. The script falls through to the bootstrap `lllc.dll` in `initial-compiler/`. No action needed.

### `error: required tool 'clang' not found in PATH`

Install clang:

- macOS: `xcode-select --install`
- Ubuntu/Debian: `sudo apt-get install -y clang`
- Fedora: `sudo dnf install -y clang`

### `error: post-processor not found: …/tools/llvm-add-declares.py`

You're running `tools/lllc-native` from outside a checkout of the ll-lang repo. For the MVP, clone the repo:

```bash
git clone https://github.com/Neftedollar/ll-lang
cd ll-lang
tools/lllc-native your-file.lll
```

### `python3: command not found` or syntax errors from `llvm-add-declares.py`

The post-processor needs Python 3.8+. Check `python3 --version`. On systems that only have `python` (2.x), install Python 3 from your package manager or [python.org](https://www.python.org/downloads/).

### `warning: overriding the module target triple with arm64-apple-macosx…`

Benign. `lllc` emits the IR with a generic triple; `clang` swaps in the host triple. Output binary is correct.

### Program compiles but crashes at runtime with a null-pointer dereference

Most common cause: an ADT match arm using `_ =` as a binding (silently dropped) or a nullary constructor listed after a multi-field one. See [Known limitations](#known-limitations).

## Next steps

- Browse the worked examples in `spec/examples/valid/36-llvm-*.lll` through `48-llvm-*.lll`.
- Read the SDK internals: [`sdks/Platform.LLVM.SDK/README.md`](../../sdks/Platform.LLVM.SDK/README.md).
- Compare against the stable multi-target backends: [Tutorial 04](./04-multi-target.md).
