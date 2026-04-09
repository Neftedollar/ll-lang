module LLLang.Tests.BootstrapCompilerTests

open System.IO
open Xunit
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.9a: tests for `spec/examples/valid/20-bootstrap-compiler.lll`
// — the first **3-stage bootstrap compiler** written entirely in
// ll-lang itself. The file starts from `17-pipeline-real.lll` verbatim
// (parser + elaborator in one program) and adds a minimal HM-style
// type checker (`TypeExpr` = `TyName Str | TyVar Str`, `typeEq`,
// `inferExprType`, `typeCheck`) that walks the parser's `Expr` AST and
// emits `E001 TypeMismatch ...` errors for arithmetic and if-branch
// type mismatches. `elaborate` is extended to run the new HM pass
// after the name-resolution and exhaustiveness passes, and the
// hardcoded driver source gains a fourth fn `badType(x Int) Int = x
// + "y"` to exercise the HM pass.
//
// Two layers of coverage (same shape as 17 / 19):
//   1. inference round-trip — parses, elaborates, and HM-infers the
//      whole file without errors (smoke test). The `valid corpus
//      infers ok` theory in `HMInferTests.fs` gets a new
//      `20-bootstrap-compiler.lll` row in addition to this fact so
//      the corpus theory still owns the canonical list.
//   2. runtime E2E — `lllc run` on the file emits all four expected
//      error lines from the hardcoded source:
//        E002 UnboundVar undefinedName
//        E003 NonExhaustiveMatch Shape missing Circle
//        E003 NonExhaustiveMatch Shape missing Rect
//        E001 TypeMismatch Int vs Str
//      Substring contains, not exact match, so any trailing
//      whitespace / codegen warnings don't matter.

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``20-bootstrap-compiler.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "20-bootstrap-compiler.lll"
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
let ``20-bootstrap-compiler.lll runs and emits E002 + E003 + E001 for the hardcoded source`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/20-bootstrap-compiler.lll")
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
    // The hardcoded source in `main`:
    //   module M
    //   type Shape = Circle | Rect
    //   fn good(x Int) Int = x + 1
    //   fn bad(x Int) Int = undefinedName
    //   fn shapeBad(s Shape) Int = match s with | 0 -> 1
    //   fn badType(x Int) Int = x + "y"
    //
    // `bad` references an undeclared name (one E002). `shapeBad`
    // matches on a `Shape` value with a single `PInt 0` arm (neither
    // a catch-all nor a named-ctor pattern in 15's AST), so the
    // exhaustiveness pass emits one E003 per Shape ctor. `badType`'s
    // body is `x + "y"`; `seedParams` binds `x -> Int`, and
    // `inferExprType` tags the right operand as `Str`, so `checkArith`
    // emits one `E001 TypeMismatch Int vs Str`.
    let expected =
        [ "E002 UnboundVar undefinedName"
          "E003 NonExhaustiveMatch Shape missing Circle"
          "E003 NonExhaustiveMatch Shape missing Rect"
          "E001 TypeMismatch Int vs Str" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing error line: {line}\nstdout: {stdout}\nstderr: {stderr}")
