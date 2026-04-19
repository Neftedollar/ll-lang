# Installation

## Prerequisites

- A POSIX or Windows shell. Examples in this guide assume bash.
- No .NET required for bootstrap execution.

## Bootstrap-first setup

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
LLLC_BOOTSTRAP_REINSTALL=1 ./tools/check-selfhost-ci.sh
```

Stage0 code is archived under `obsolete/stage0` and not part of default CI or
daily workflows.

## Hello, ll-lang

Create `hello.lll`:

```lll
module Examples.Hello

main() = printfn "Hello, ll-lang!"
```

Run it:

```bash
./tools/lllc-bootstrap.sh run hello.lll
```

Expected output:

```
Hello, ll-lang!
```

## Bootstrap installer (pinned + sha256)

To bootstrap from a downloadable compiler artifact on a clean machine:

```bash
./tools/bootstrap-self.sh install
./tools/bootstrap-self.sh verify
BOOTSTRAP_BIN="$(./tools/bootstrap-self.sh path)"
"$BOOTSTRAP_BIN" check "$PWD/lllcself/src/Main.lll"
```

Notes:
- Artifact metadata is pinned in `bootstrap/lllc-bootstrap.lock.json`.
- Integrity is enforced via `sha256` verification before extraction.
- Use `./tools/bootstrap-self.sh install --reinstall` to refresh cache.
- To regenerate release artifacts + lock file, run:
  - `./tools/build-bootstrap-artifacts.sh --version vX.Y.Z`

### Strict no-fallback launcher

Use the launcher for deterministic bootstrap-only execution:

```bash
./tools/lllc-bootstrap.sh check "$PWD/lllcself/src/Main.lll"
```

This path never falls back to stage0. If bootstrap install/resolve fails, the
command fails hard.
