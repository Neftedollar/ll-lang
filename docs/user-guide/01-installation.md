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

191 tests should pass.

## Hello, ll-lang

Create `hello.lll`:

```lll
module Examples.Hello

fn main() = printfn "Hello, ll-lang!"
```

Run it:

```bash
dotnet run --project src/LLLangTool -- run hello.lll
```

Expected output:

```
Hello, ll-lang!
```

## The `llc` alias

The project does not install `llc` to PATH. For convenience, alias it:

```bash
alias llc='dotnet run --project /path/to/ll-lang/src/LLLangTool --'
```

Then the rest of this guide can be followed literally:

```bash
llc run hello.lll
llc build hello.lll    # produces hello.fs
```
