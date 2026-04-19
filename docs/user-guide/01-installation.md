# Installation

## Prerequisites

- A POSIX or Windows shell. Examples in this guide assume bash.
- No .NET required for bootstrap execution.
- Optional: [.NET 10 SDK](https://dotnet.microsoft.com/download) only for
  legacy stage0 build/test workflows.

## Bootstrap-first setup

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
LLLC_BOOTSTRAP_REINSTALL=1 ./tools/check-selfhost-ci.sh
```

## Optional legacy stage0 verification

```bash
dotnet --version   # must report 10.x
dotnet build
dotnet test
```

This path is not required for bootstrap usage; it remains for stage0 regression
coverage.

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

## Optional legacy `lllc` alias (stage0)

If you still need stage0 CLI commands (`new`, project `check`, etc.), alias:

```bash
alias lllc='dotnet run --project /path/to/ll-lang/src/LLLangTool --'
```

Then you can run legacy commands:

```bash
lllc run hello.lll
lllc build hello.lll    # produces hello.fs
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

### Strict no-fallback launcher

Use the launcher for deterministic bootstrap-only execution:

```bash
./tools/lllc-bootstrap.sh check "$PWD/lllcself/src/Main.lll"
```

This path never falls back to stage0. If bootstrap install/resolve fails, the
command fails hard.
