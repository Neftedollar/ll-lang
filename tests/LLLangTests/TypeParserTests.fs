module LLLang.Tests.TypeParserTests

open System.IO
open Xunit
open LLLang.AST
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.3a: tests for the type-declaration parser written in ll-lang
// itself. Lives in spec/examples/valid/12-typeparser-real.lll. Two layers
// of coverage:
//   1. inference round-trip — parses, elaborates, infers without errors.
//   2. runtime — `lllc run` produces the expected pretty form for each of
//      the four type declarations baked into the parser's main input.

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``12-typeparser-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "12-typeparser-real.lll"
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
let ``12-typeparser-real.lll runs and pretty-prints all four type decls`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/12-typeparser-real.lll")
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
    //   "type Maybe A = Some A | None\n
    //    type Result A E = Ok A | Err E\n
    //    type Shape = Circle | Rect | Empty\n
    //    type Wrapped = MkWrapped Int"
    // and prints each decl on its own normalised line. Type params print
    // in their own parens; ctor args wrap in `(a, b)`; nullary ctors print
    // bare.
    let expected =
        [ "type Maybe (A) = Some(A) | None"
          "type Result (A) (E) = Ok(A) | Err(E)"
          "type Shape = Circle | Rect | Empty"
          "type Wrapped = MkWrapped(Int)" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing pretty form: {line}\nstdout: {stdout}\nstderr: {stderr}")
