module LLLang.Tests.JsonParserTests

open System.IO
open Xunit
open LLLang.FParsecParser
open LLLang.Elaborator
open LLLang.HMInfer

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``22-json-parser-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "22-json-parser-real.lll"
    match parseModuleWithPos src with
    | Error e -> Assert.Fail($"parse: {e}")
    | Ok (m, pm) ->
        match elaborate pm m with
        | Error es -> Assert.Fail($"elaborator: {es}")
        | Ok (m', env) ->
            match infer pm m' env with
            | Error es -> Assert.Fail($"infer: {es}")
            | Ok tm -> Assert.NotNull(tm.Env)

[<Fact>]
let ``22-json-parser-real.lll runs and reports expected positive and negative cases`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/22-json-parser-real.lll")
    let llcDll =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../src/LLLangTool/bin/Debug/net10.0/lllc.dll")
    let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"\"{llcDll}\" run \"{lllPath}\"")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    use proc = LLLang.Tests.TestCompat.startProcess psi
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()

    let expected =
        [ "OK pos-null"
          "OK pos-bool"
          "OK pos-int"
          "OK pos-exp"
          "OK pos-str-esc"
          "OK pos-u-basic"
          "OK pos-u-surrogate"
          "OK pos-array"
          "OK pos-object"
          "OK rt-num"
          "OK rt-str-esc"
          "OK rt-u-surrogate"
          "OK rt-array"
          "OK rt-object"
          "OK util-float-to-str"
          "OK neg-leading-zero"
          "OK neg-bad-exp"
          "OK neg-bad-frac"
          "OK neg-bad-escape"
          "OK neg-lone-high-surrogate"
          "OK neg-lone-low-surrogate"
          "OK neg-missing-comma"
          "OK neg-trailing-garbage" ]

    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing runtime line: {line}\nstdout: {stdout}\nstderr: {stderr}")

    Assert.DoesNotContain("FAIL ", stdout)
