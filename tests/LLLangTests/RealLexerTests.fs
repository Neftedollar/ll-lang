module LLLang.Tests.RealLexerTests

open System.IO
open Xunit
open LLLang.AST
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.1: tests for the real ll-lang lexer written in ll-lang itself.
// Lives in spec/examples/valid/09-lexer-real.lll. Two layers of coverage:
//   1. inference round-trip — parses, elaborates, infers without errors.
//   2. runtime — `lllc run` produces the expected token-stream output for
//      the input baked into the lexer's `main` (`fn add(a)(b) = a + b`).

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``09-lexer-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "09-lexer-real.lll"
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
let ``09-lexer-real.lll runs and prints tokens for fn add(a)(b) = a + b`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/09-lexer-real.lll")
    let llcDll =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../src/LLLangTool/bin/Debug/net10.0/lllc.dll")
    let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"\"{llcDll}\" run \"{lllPath}\"")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError  <- true
    psi.UseShellExecute        <- false
    use proc = System.Diagnostics.Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    // The lexer's main tokenizes `fn add(a)(b) = a + b` and joins the
    // rendered token names with spaces. Each constructor maps via tokenName.
    let expected = "kw:fn id:add ( id:a ) ( id:b ) = id:a + id:b"
    Assert.True(
        stdout.Contains(expected),
        $"expected token stream not found.\nexpected: {expected}\nstdout: {stdout}\nstderr: {stderr}")
