module LLLang.Tests.ArithmeticParserTests

open System.IO
open Xunit
open LLLang.AST
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.2: tests for the recursive-descent arithmetic parser written
// in ll-lang itself. Lives in spec/examples/valid/11-parser-real.lll.
// Two layers of coverage:
//   1. inference round-trip — parses, elaborates, infers without errors.
//   2. runtime — `lllc run` produces the precedence-correct pretty form
//      for the parser's main input `"1 + 2 * 3"`.

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``11-parser-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "11-parser-real.lll"
    match tokenize src |> Result.bind parseModuleWithPos with
    | Error e -> Assert.Fail($"parse: {e}")
    | Ok (m, pm) ->
        match elaborate pm m with
        | Error es -> Assert.Fail($"elaborator: {es}")
        | Ok (m', env) ->
            match infer pm m' env with
            | Error es -> Assert.Fail($"infer: {es}")
            | Ok tm -> Assert.NotNull(tm.Env)

[<Fact>]
let ``11-parser-real.lll runs and prints precedence-correct AST for 1 + 2 * 3`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/11-parser-real.lll")
    let llcDll =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../src/LLLangTool/bin/Debug/net10.0/lllc.dll")
    let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"\"{llcDll}\" run \"{lllPath}\"")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError  <- true
    psi.UseShellExecute        <- false
    use proc = LLLang.Tests.TestCompat.startProcess psi
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    // For input "1 + 2 * 3" the parser must produce (1 + (2 * 3)) — the
    // inner Mul groups before Add because * binds tighter than +.
    let expected = "(1 + (2 * 3))"
    Assert.True(
        stdout.Contains(expected),
        $"precedence-correct pretty form not found.\nexpected: {expected}\nstdout: {stdout}\nstderr: {stderr}")
