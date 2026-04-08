module LLLang.Tests.FnParserTests

open System.IO
open Xunit
open LLLang.AST
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.3b: tests for the fn-declaration parser written in ll-lang
// itself. Lives in spec/examples/valid/13-fnparser-real.lll. Two layers
// of coverage:
//   1. inference round-trip — parses, elaborates, infers without errors.
//   2. runtime — `lllc run` produces the expected pretty form for each of
//      the four fn declarations baked into the parser's main input.

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``13-fnparser-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "13-fnparser-real.lll"
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
let ``13-fnparser-real.lll runs and pretty-prints all four fn decls`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/13-fnparser-real.lll")
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
    // The parser walks
    //   "fn add(a Int)(b Int) Int = a + b\n
    //    fn double(x Int) = x * 2\n
    //    fn const(a Int)(b Int) Int = a\n
    //    fn answer() Int = 42"
    // and prints each decl on its own normalised line. Curried param
    // groups print space-separated as `(name: Type)`; nullary fns print
    // `()`; missing return types print as `?`; binary body ops are fully
    // parenthesised; leaves print bare.
    let expected =
        [ "fn add (a: Int) (b: Int) -> Int = (a + b)"
          "fn double (x: Int) -> ? = (x * 2)"
          "fn const (a: Int) (b: Int) -> Int = a"
          "fn answer () -> Int = 42" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing pretty form: {line}\nstdout: {stdout}\nstderr: {stderr}")
