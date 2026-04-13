module LLLang.Tests.PipelineRealTests

open System.IO
open Xunit
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.6 integration: tests for `spec/examples/valid/17-pipeline-real.lll`
// — the first ll-lang program that stitches the Phase 7.5 module
// parser and the Phase 7.6a/b elaborator into a single pipeline. The
// file contains both halves (parser copied verbatim from
// `15-moduleparser-real.lll`, elaborator adapted from
// `16-elaborator-real.lll` to walk 15's richer `Decl` / `Expr` / `Pat`
// AST) plus a `main` that hardcodes a small ll-lang source with
// intentional name-resolution and exhaustiveness issues and runs the
// full lex -> parse -> elaborate flow in-memory.
//
// Two layers of coverage:
//   1. inference round-trip — parses, elaborates, and HM-infers
//      without errors (smoke test, same shape as 15 / 16).
//   2. runtime E2E — `lllc run` prints the three expected error lines
//      produced by the hardcoded source:
//        E002 UnboundVar undefinedName
//        E003 NonExhaustiveMatch Shape missing Circle
//        E003 NonExhaustiveMatch Shape missing Rect

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``17-pipeline-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "17-pipeline-real.lll"
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
let ``17-pipeline-real.lll runs and emits E002 + E003 for the hardcoded source`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/17-pipeline-real.lll")
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
    // The hardcoded source in `main`:
    //   module M
    //   type Shape = Circle | Rect
    //   fn good(x Int) Int = x + 1
    //   fn bad(x Int) Int = undefinedName
    //   fn shapeBad(s Shape) Int = match s with | 0 -> 1
    //
    // `bad` references an undeclared name (one E002). `shapeBad`
    // matches on a `Shape` value with a single `PInt 0` arm (neither a
    // catch-all nor a named-ctor pattern in 15's AST), so the
    // exhaustiveness pass emits one E003 per Shape ctor.
    let expected =
        [ "E002 UnboundVar undefinedName"
          "E003 NonExhaustiveMatch Shape missing Circle"
          "E003 NonExhaustiveMatch Shape missing Rect" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing error line: {line}\nstdout: {stdout}\nstderr: {stderr}")
