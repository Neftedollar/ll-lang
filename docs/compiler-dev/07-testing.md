# Testing

Project: `tests/LLLangTests/LLLangTests.fsproj` — xUnit 2.6.3.

191 tests across five files, each file corresponding to one compiler
pass:

| File                   | Lines | Covers                                   |
|------------------------|-------|------------------------------------------|
| `LexerTests.fs`        |  ~    | `Lexer.fs` — tokens, INDENT/DEDENT       |
| `ParserTests.fs`       |  ~    | `Parser.fs` — every decl and expr form   |
| `ElaboratorTests.fs`   |  ~    | `Elaborator.fs` — E001-E005, exhaustiveness |
| `HMInferTests.fs`      |  ~    | `HMInfer.fs`, `Types.fs` — unification, generalize, E008 |
| `CodegenTests.fs`      |  ~    | `Codegen.fs` — emitted F# shape          |

## Running

```bash
dotnet test                            # whole suite
dotnet test --filter LexerTests        # one file
dotnet test --filter "ClassName.Method" # one test
```

Tests are pure in-memory — no temp files, no subprocess — so the whole
suite finishes in a few seconds.

## Helper patterns

Each test file defines helpers at the top that are reused across cases.
They are the only "framework" the project has.

### `HMInferTests.fs`

```fsharp
let private inferSrc (src: string) : Result<TypedModule, LLError list> =
    match tokenize src |> Result.bind parseModule with
    | Error e -> failwith $"parse: {e}"
    | Ok m ->
        match elaborate m with
        | Error es -> failwith $"elaborator: {es}"
        | Ok env -> infer m env

let private inferOk (src: string) : TypedModule =
    match inferSrc src with
    | Ok tm -> tm
    | Error es -> failwith $"unexpected hm errors: {es}"

let private inferErrs (src: string) : LLError list =
    match inferSrc src with
    | Ok _ -> []
    | Error es -> es

let private schemeOf (tm: TypedModule) (name: string) : TypeScheme =
    Map.find name tm.Env
```

Usage:

```fsharp
[<Fact>]
let ``fn id has polymorphic scheme`` () =
    let tm = inferOk "module M\nfn id(x A) A = x"
    let sch = schemeOf tm "id"
    Assert.Contains("A", sch.Vars)
```

`inferOk` fails the test if inference errors. `inferErrs` returns the
error list for assertion-based matching. `schemeOf` extracts a
function's inferred scheme for pinning.

### `CodegenTests.fs`

```fsharp
let private codegenSrc (src: string) : string =
    match LLLang.Compiler.compile src with
    | Ok fs -> fs
    | Error es -> failwith $"codegen failed: {es}"
```

Tests assert substring containment on the emitted F# string:

```fsharp
[<Fact>]
let ``TDType sum type emits DU header`` () =
    let src = "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("type Shape =", codegenSrc src)

[<Fact>]
let ``TDType sum type emits multi-arg branch`` () =
    let src = "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("| Rect of float * float", codegenSrc src)
```

## Corpus-driven tests

Both `HMInferTests.fs` and `CodegenTests.fs` read files from the
shared `spec/examples/` tree:

```fsharp
let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

let private readInvalid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/invalid", name))
```

`__SOURCE_DIRECTORY__` is an F# compile-time constant pointing to the
directory of the test file — so the path `../../spec/examples/...`
resolves relative to the test project regardless of where `dotnet test`
is invoked from.

Corpus test skeleton:

```fsharp
[<Fact>]
let ``01-basics.lll type-checks`` () =
    let src = readValid "01-basics.lll"
    inferOk src |> ignore

[<Fact>]
let ``E001-type-mismatch.lll emits E001`` () =
    let src = readInvalid "E001-type-mismatch.lll"
    let errs = inferErrs src
    Assert.Contains(errs, fun e -> e.Code = E001)
```

The invalid corpus files declare their expected code on line 1 as a
comment (`-- expect: E001`). Tests cross-check that the stated code
actually fires.

## Adding a new corpus example

### Valid program

1. Create `spec/examples/valid/NN-feature.lll` with a short module
   demonstrating the feature.
2. Verify it compiles with `lllc build`:
   ```bash
   dotnet run --project src/LLLangTool -- build spec/examples/valid/NN-feature.lll
   ```
3. Add a test in either `HMInferTests.fs` or `CodegenTests.fs`:
   ```fsharp
   [<Fact>]
   let ``NN-feature type-checks`` () =
       let src = readValid "NN-feature.lll"
       inferOk src |> ignore
   ```
4. `dotnet test --filter "NN-feature"` to confirm.

### Invalid program

1. Create `spec/examples/invalid/EXXX-name.lll`.
2. First line is a comment declaring the expected error:
   ```lll
   -- expect: E003
   ```
3. Write the minimal trigger.
4. Add a test:
   ```fsharp
   [<Fact>]
   let ``EXXX-name triggers EXXX`` () =
       let src = readInvalid "EXXX-name.lll"
       let errs = inferErrs src
       Assert.Contains(errs, fun e -> e.Code = EXXX)
   ```

## Organizing tests

Tests within a file are grouped by feature, not by function. Naming
convention uses backtick strings for readability in failure output:

```fsharp
[<Fact>]
let ``EApp curried application infers flex return`` () = ...
```

Avoid setup fixtures — each test is self-contained and calls the
helpers at the top of the file. If a test needs state beyond what the
helpers provide, the helpers are usually the thing to extend.

## Snapshot-style testing

There is no snapshot library. For codegen tests we assert substring
containment (`Assert.Contains`, `Assert.DoesNotContain`) rather than
byte-for-byte equality, so small formatting tweaks don't cascade into
dozens of test updates. If you need exact-shape testing, write a small
assertion explicitly.

## CI

`.github/workflows/build.yml` (referenced in the README badge) runs
`dotnet build` followed by `dotnet test`. All 191 tests must pass on
merges to `main`.
