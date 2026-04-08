module LLLang.Tests.ElaboratorRealTests

open System.IO
open Xunit
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.6a: tests for the first slice of the ll-lang elaborator
// written in ll-lang itself. Lives in
// `spec/examples/valid/16-elaborator-real.lll`. This slice does **name
// resolution / free-variable detection only** — a minimal local AST,
// a two-pass `collectDecls` -> `checkDecls` pipeline, and a hardcoded
// test module with two intentionally-unbound `EVar` names.
//
// Two layers of coverage:
//   1. inference round-trip — parses, elaborates, and HM-infers
//      without errors (smoke test).
//   2. runtime E2E — `lllc run` prints one `E002 UnboundVar <name>`
//      line per free var in the hardcoded test module. The clean
//      fns (`add`, `useCtor`) and the non-fn decls (tag, type, let)
//      produce no output lines; only `bad` fires.

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``16-elaborator-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "16-elaborator-real.lll"
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
let ``16-elaborator-real.lll runs and emits E002 for every unbound var`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/16-elaborator-real.lll")
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
    // The hardcoded module has one intentionally-bad fn
    //   fn bad(x) = undefinedName + otherMissing
    // that references two undeclared names. Each reference produces
    // exactly one E002 UnboundVar line. The other fns (`add`,
    // `useCtor`) and the non-fn decls (tag UserId / type Maybe /
    // let answer) all resolve cleanly and contribute no errors.
    let expected =
        [ "E002 UnboundVar undefinedName"
          "E002 UnboundVar otherMissing" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing error line: {line}\nstdout: {stdout}\nstderr: {stderr}")
