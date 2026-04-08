module LLLang.Tests.ModuleParserTests

open System.IO
open Xunit
open LLLang.AST
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.4: tests for the full-module parser written in ll-lang itself.
// Lives in spec/examples/valid/15-moduleparser-real.lll. This is the
// showcase milestone: lexer + type-decl + fn-decl + expression parsers
// stitched into one program that consumes a whole `module M\n type ...
// \n fn ... = ...` source string and produces a `List[Decl]` AST.
// Two layers of coverage:
//   1. inference round-trip — parses, elaborates, infers without errors.
//   2. runtime — `lllc run` produces the expected pretty form for each
//      decl in the hardcoded driver input (module header, two type
//      decls, three fn decls covering int literal, binary body, and
//      `if-then-else` body).

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``15-moduleparser-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "15-moduleparser-real.lll"
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
let ``15-moduleparser-real.lll runs and pretty-prints a full module AST`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/15-moduleparser-real.lll")
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
    // Phase 7.5a extends the driver to also cover `let` decls at module
    // level and `match`-with-explicit-scrutinee inside `fn` bodies:
    //   "module Examples.Bigger\n
    //    type Maybe A = Some A | None\n
    //    type Color = Red | Green | Blue\n
    //    let answer = 42\n
    //    let zero = 0\n
    //    fn double(x Int) Int = x * 2\n
    //    fn classify(x Int) Int = match x with | 0 -> 0 | _ -> 1\n
    //    fn pickColor(x Int) Color = if x then Red else Green"
    // and pretty-prints the whole module deterministically: the module
    // header on its own line, each type/let/fn decl normalised to the form
    // used by 12/13/14 (ctor args in parens; fn params space-separated;
    // body expressions fully parenthesised; let decls as `let name = expr`;
    // match in expression position as `(match scrut with | p -> e | ...)`).
    let expected =
        [ "module Examples.Bigger"
          "type Maybe (A) = Some(A) | None"
          "type Color = Red | Green | Blue"
          "let answer = 42"
          "let zero = 0"
          "fn double (x: Int) -> Int = (x * 2)"
          "fn classify (x: Int) -> Int = (match x with | 0 -> 0 | _ -> 1)"
          "fn pickColor (x: Int) -> Color = (if x then Red else Green)" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing pretty form: {line}\nstdout: {stdout}\nstderr: {stderr}")
