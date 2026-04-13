module LLLangTests.McpTests

open System.IO
open Xunit
open LLLang.Compiler
open LLLang.Platform
open LLLang.Elaborator

/// Helpers

let private validSrc =
    """module Test.Valid

add(a Int)(b Int) Int = a + b
main() Int = add 1 2
"""

let private invalidSrc =
    """module Test.Invalid

main() Int = undefinedFunc 42
"""

let private typeMismatchSrc =
    """module Test.TypeMismatch

f(x Str) Int = x
"""

// ─── check function ───────────────────────────────────────────────────────────

[<Fact>]
let ``check returns Ok for valid source`` () =
    let result = check validSrc
    Assert.Equal(Ok (), result)

[<Fact>]
let ``check returns Error for unbound variable`` () =
    match check invalidSrc with
    | Error es ->
        Assert.NotEmpty(es)
        let codes = es |> List.map (fun e -> e.Code)
        Assert.Contains(E002, codes)
    | Ok () -> Assert.True(false, "Expected error but got Ok")

[<Fact>]
let ``check returns Error for type mismatch`` () =
    match check typeMismatchSrc with
    | Error es ->
        Assert.NotEmpty(es)
        let codes = es |> List.map (fun e -> e.Code)
        Assert.Contains(E001, codes)
    | Ok () -> Assert.True(false, "Expected error but got Ok")

[<Fact>]
let ``check is faster gate: succeeds without codegen`` () =
    // check should succeed on the same source that compile succeeds on
    let result = check validSrc
    Assert.Equal(Ok (), result)

[<Fact>]
let ``check and compile agree on valid source`` () =
    let checkResult = check validSrc
    let compileResult = compile validSrc
    match checkResult, compileResult with
    | Ok (), Ok _ -> ()  // both succeed
    | Error es, Ok _ -> Assert.True(false, sprintf "check failed but compile succeeded: %A" es)
    | Ok (), Error es -> Assert.True(false, sprintf "compile failed but check succeeded: %A" es)
    | Error ce, Error ce2 ->
        Assert.Equal(List.length ce, List.length ce2)

[<Fact>]
let ``check and compile agree on invalid source`` () =
    let checkResult = check invalidSrc
    let compileResult = compile invalidSrc
    match checkResult, compileResult with
    | Error _, Error _ -> ()  // both fail
    | Ok (), _ -> Assert.True(false, "check should have failed")
    | _, Ok _ -> Assert.True(false, "compile should have failed")

[<Fact>]
let ``checkTarget validates external mappings per target`` () =
    let src =
        """module Test.External

external fetch(url Str) Promise[Response]
opaque Response
opaque Promise[A]
main() Int = 0
"""
    match checkTarget TypeScript src with
    | Ok () -> ()
    | Error es -> Assert.True(false, sprintf "TypeScript checkTarget should succeed: %A" es)

    match checkTarget FSharp src with
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = E026)
    | Ok () -> Assert.True(false, "FSharp checkTarget should fail with E026")

// ─── compile function ─────────────────────────────────────────────────────────

[<Fact>]
let ``compile returns F# source for valid input`` () =
    match compile validSrc with
    | Ok fs ->
        Assert.Contains("let add", fs)
        Assert.Contains("let main", fs)
    | Error es -> Assert.True(false, sprintf "Expected Ok but got errors: %A" es)

[<Fact>]
let ``compile returns errors for undefined variable`` () =
    match compile invalidSrc with
    | Error es ->
        Assert.NotEmpty(es)
        Assert.True(es |> List.exists (fun e -> e.Code = E002))
    | Ok _ -> Assert.True(false, "Expected error")

[<Fact>]
let ``compile produces module header in output`` () =
    match compile validSrc with
    | Ok fs -> Assert.Contains("module Test.Valid", fs)
    | Error es -> Assert.True(false, sprintf "%A" es)

// ─── stdlib_search backing data ───────────────────────────────────────────────
// These verify the names documented in Mcp.stdlibEntries are accurate by
// compiling small programs that use them.

[<Fact>]
let ``listMap is in scope without import`` () =
    let src = """module Test.Stdlib

double(xs List[Int]) List[Int] = listMap (\x. x * 2) xs
"""
    Assert.Equal(Ok (), check src)

[<Fact>]
let ``listFold is in scope without import`` () =
    let src = """module Test.Stdlib2

sum(xs List[Int]) Int = listFold (\acc. \x. acc + x) 0 xs
"""
    Assert.Equal(Ok (), check src)

[<Fact>]
let ``strLen is in scope without import`` () =
    let src = """module Test.Stdlib3

f(s Str) Int = strLen s
"""
    Assert.Equal(Ok (), check src)

[<Fact>]
let ``maybeMap is in scope without import`` () =
    let src = """module Test.Stdlib4

f(m Maybe[Int]) Maybe[Int] = maybeMap (\x. x + 1) m
"""
    Assert.Equal(Ok (), check src)

// ─── grammar_lookup backing file ─────────────────────────────────────────────

[<Fact>]
let ``spec/grammar.ebnf exists`` () =
    // The grammar file must be present in the repo (grammar_lookup tool depends on it)
    let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
    let grammarPath = Path.Combine(repoRoot, "spec", "grammar.ebnf")
    Assert.True(File.Exists(grammarPath), sprintf "grammar.ebnf not found at %s" grammarPath)

[<Fact>]
let ``spec/grammar.ebnf contains Expr rule`` () =
    let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
    let grammarPath = Path.Combine(repoRoot, "spec", "grammar.ebnf")
    if File.Exists(grammarPath) then
        let content = File.ReadAllText(grammarPath)
        Assert.Contains("Expr", content)

// ─── lookup_error backing data ────────────────────────────────────────────────

[<Fact>]
let ``spec/examples/invalid contains E001 example`` () =
    let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
    let invalidDir = Path.Combine(repoRoot, "spec", "examples", "invalid")
    if Directory.Exists(invalidDir) then
        let files = Directory.GetFiles(invalidDir, "*.lll")
        let hasE001 =
            files |> Array.exists (fun f ->
                try
                    let first = File.ReadLines(f) |> Seq.tryHead |> Option.defaultValue ""
                    first.Contains("expect: E001")
                with _ -> false)
        Assert.True(hasE001, "No E001 example found in spec/examples/invalid/")

[<Fact>]
let ``compile on type mismatch produces E001 error`` () =
    let src = """module Test.E001

f(x Int) Str = x
"""
    match compile src with
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code)
        Assert.Contains(E001, codes)
    | Ok _ -> Assert.True(false, "Expected E001 error")

// ─── multi-target compile ─────────────────────────────────────────────────────

[<Fact>]
let ``compileTarget TypeScript produces TS output for valid source`` () =
    match compileTarget TypeScript validSrc with
    | Ok ts ->
        Assert.Contains("const", ts)
        Assert.DoesNotContain("let rec", ts)
    | Error es -> Assert.True(false, sprintf "compileTarget TS failed: %A" es)

[<Fact>]
let ``compileTarget Python produces Python output for valid source`` () =
    match compileTarget Python validSrc with
    | Ok py ->
        Assert.Contains("def ", py)
        Assert.DoesNotContain("let rec", py)
    | Error es -> Assert.True(false, sprintf "compileTarget Py failed: %A" es)

[<Fact>]
let ``compileTarget Java produces Java output for valid source`` () =
    match compileTarget Java validSrc with
    | Ok java ->
        Assert.Contains("public static", java)
        Assert.DoesNotContain("let rec", java)
    | Error es -> Assert.True(false, sprintf "compileTarget Java failed: %A" es)

[<Fact>]
let ``compileTarget CSharp produces C# output for valid source`` () =
    match compileTarget CSharp validSrc with
    | Ok cs ->
        Assert.Contains("public static class", cs)
        Assert.DoesNotContain("let rec", cs)
    | Error es -> Assert.True(false, sprintf "compileTarget CSharp failed: %A" es)

[<Fact>]
let ``compileTarget LLVM produces LLVM output for valid source`` () =
    match compileTarget LLVM validSrc with
    | Ok ll ->
        Assert.Contains("define", ll)
        Assert.DoesNotContain("let rec", ll)
    | Error es -> Assert.True(false, sprintf "compileTarget LLVM failed: %A" es)

[<Fact>]
let ``compileTarget FSharp does not produce Python output`` () =
    match compileTarget FSharp validSrc with
    | Ok fs ->
        Assert.DoesNotContain("def ", fs)
        Assert.DoesNotContain("from __future__", fs)
    | Error es -> Assert.True(false, sprintf "compileTarget FS failed: %A" es)
