module LLLang.Tests.ElaboratorRealTests

open System.IO
open Xunit
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.6a + 7.6b: tests for the early slices of the ll-lang
// elaborator written in ll-lang itself. Lives in
// `spec/examples/valid/16-elaborator-real.lll`. 7.6a shipped **name
// resolution / free-variable detection** (E002); 7.6b adds
// **constructor-coverage exhaustiveness** (E003) over top-level
// clause-sugar fn bodies whose last param is a named sum type.
//
// Two layers of coverage:
//   1. inference round-trip — parses, elaborates, and HM-infers
//      without errors (smoke test).
//   2. runtime E2E — `lllc run` prints E002 + E003 lines for the
//      hardcoded test module. The clean fns (`add`, `useCtor`,
//      `shapeGood`, `shapeWild`) and the non-fn decls (tag, types,
//      let) produce no output lines; only `bad` fires E002 (twice)
//      and `shapeBad` fires E003 (once, missing Empty).

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
let ``16-elaborator-real.lll runs and emits E002 and E003 for every error`` () =
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
    // The hardcoded module has:
    //   fn bad(x) = undefinedName + otherMissing      -- two E002s
    //   fn shapeBad(s Shape) = | Circle -> 1 | Rect -> 2  -- one E003 (missing Empty)
    // The other fns all resolve cleanly (name resolution + ctor
    // coverage): `add`, `useCtor`, `shapeGood` (exhaustive over
    // Shape), and `shapeWild` (catch-all `_` arm). `useCtor` has an
    // empty last-param type name, so exhaustiveness skips it.
    let expected =
        [ "E002 UnboundVar undefinedName"
          "E002 UnboundVar otherMissing"
          "E003 NonExhaustiveMatch Shape missing Empty" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing error line: {line}\nstdout: {stdout}\nstderr: {stderr}")
