module LLLang.Tests.CodegenRealTests

open System.IO
open Xunit
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.8a: tests for `spec/examples/valid/19-codegen-real.lll` —
// the first slice of F# codegen written in ll-lang itself. The file
// defines a tiny `TExpr` / `TDecl` AST (TEInt / TEStr / TEVar / TEAdd
// / TEApp / TELet; `TDFn Str List[Str] TExpr`) and a `showTExpr` /
// `showDecl` family that walks it and emits F# source strings,
// mirroring the host's `src/LLLangCompiler/Codegen.fs` `emitExpr` /
// `emitDecl` for the supported shapes. `main` hardcodes four `TDFn`s
// covering every supported TExpr shape and prints one line per decl.
//
// Two layers of coverage:
//   1. inference round-trip — parses, elaborates, and HM-infers the
//      whole file without errors (smoke test, same shape as 15 / 16 /
//      17 / 18). The `valid corpus infers ok` theory in
//      `HMInferTests.fs` gets a new `19-codegen-real.lll` row in
//      addition to this fact so the corpus theory still owns the
//      canonical list.
//   2. runtime E2E — `lllc run` on the file emits all four expected
//      F# source lines. Substring contains, not exact match, so
//      trailing whitespace / newline differences don't matter.

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``19-codegen-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "19-codegen-real.lll"
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
let ``19-codegen-real.lll runs and emits F# source lines for each TDFn`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/19-codegen-real.lll")
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
    // Seven TDFn shapes exercised by `main`:
    //   inc      — TEAdd + TEVar + TEInt (Phase 7.8a)
    //   greet    — TEStr + empty-params branch (Phase 7.8a)
    //   addOne   — TELet (with nested TEAdd / TEVar / TEInt) (Phase 7.8a)
    //   callInc  — TEApp + TEVar (Phase 7.8a)
    //   choose   — TEIf (Phase 7.8b)
    //   double   — TELam bound at top level (Phase 7.8b)
    //   classify — TEMatch with PInt + PWild + TEStr branches (Phase 7.8b)
    // Plus three TDType sum decls (Phase 7.8c):
    //   Maybe — parametric `<'A>`, arg-bearing + nullary ctors
    //   Shape — no type params, mixed nullary and multi-arg TAName ctors
    //   Pair  — two-arg TAVar ctor ensuring the `*` join shape
    let expected =
        [ "let inc x = (x + 1L)"
          "let greet = \"hello\""
          "let addOne x = (let y = (x + 1L) in y)"
          "let callInc x = (inc x)"
          "let choose b = (if b then 1L else 2L)"
          "let double = (fun x -> (x + x))"
          "let classify x = (match x with | 0L -> \"zero\" | _ -> \"other\")"
          "type Maybe<'A> ="
          "    | Some of 'A"
          "    | None"
          "type Shape ="
          "    | Circle"
          "    | Rect of int64 * int64"
          "    | Empty"
          "type Pair<'A, 'B> ="
          "    | MkPair of 'A * 'B" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing emitted line: {line}\nstdout: {stdout}\nstderr: {stderr}")
