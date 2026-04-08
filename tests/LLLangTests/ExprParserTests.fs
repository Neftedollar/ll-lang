module LLLang.Tests.ExprParserTests

open System.IO
open Xunit
open LLLang.AST
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.3c: tests for the full-expression parser written in ll-lang
// itself. Lives in spec/examples/valid/14-exprparser-real.lll. Two layers
// of coverage:
//   1. inference round-trip — parses, elaborates, infers without errors.
//   2. runtime — `lllc run` produces the expected fully-parenthesised
//      pretty form for each of the five expression kinds baked into the
//      parser's main input (let-in, if-then-else, match, lambda, app).

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``14-exprparser-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "14-exprparser-real.lll"
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
let ``14-exprparser-real.lll runs and pretty-prints all five expression kinds`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/14-exprparser-real.lll")
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
    // The parser walks five single-line expression inputs:
    //   "let x = 1 in (x + 2)"
    //   "if x then 1 else 2"
    //   "match x with | 0 -> \"zero\" | _ -> \"other\""
    //   "\\y. (y + 1)"
    //   "f x y"
    // and pretty-prints each in fully-parenthesised form (binary ops,
    // application, lambda, let-in, if, and match all wrap in parens so
    // precedence / associativity are visually obvious).
    let expected =
        [ "(let x = 1 in (x + 2))"
          "(if x then 1 else 2)"
          "(match x with | 0 -> \"zero\" | _ -> \"other\")"
          "(fun y -> (y + 1))"
          "((f x) y)" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing pretty form: {line}\nstdout: {stdout}\nstderr: {stderr}")
