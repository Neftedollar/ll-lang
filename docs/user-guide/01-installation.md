# Installation

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (the compiler targets
  `net10.0` and uses `LangVersion=preview`).
- A POSIX or Windows shell. Examples in this guide assume bash.

Verify:

```bash
dotnet --version
# must report 10.x
```

## Build from source

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
dotnet build
```

Optional: run the test suite.

```bash
dotnet test
```

All tests should pass. The suite grows with each phase; a clean build on
main should report zero failures.

## Hello, ll-lang

Create `hello.lll`:

```lll
module Examples.Hello

main() = printfn "Hello, ll-lang!"
```

Run it:

```bash
dotnet run --project src/LLLangTool -- run hello.lll
```

Expected output:

```
Hello, ll-lang!
```

## The `lllc` alias

The project does not install `lllc` to PATH. For convenience, alias it:

```bash
alias lllc='dotnet run --project /path/to/ll-lang/src/LLLangTool --'
```

Then the rest of this guide can be followed literally:

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
