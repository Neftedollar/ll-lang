module LLLang.Tests.HMInferRealTests

open System.IO
open Xunit
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.7a: tests for `spec/examples/valid/18-hminfer-real.lll` —
// the first slice of Hindley-Milner inference written in ll-lang
// itself. The file defines a minimal `TypeExpr` (TyName / TyVar /
// TyFn), a parallel-list `Subst`, an `applyType` walker, and a
// `unify : TypeExpr -> TypeExpr -> Maybe[Subst]` mirroring the F#
// host's `HMInfer.unify` in `src/LLLangCompiler/HMInfer.fs`. `main`
// runs five hardcoded `unify` cases — three "ok" / "ok bound" lines
// and two "mismatch" lines — covering each shape exactly once.
//
// Two layers of coverage:
//   1. inference round-trip — parses, elaborates, and HM-infers the
//      whole file without errors (smoke test, same shape as 15 / 16 /
//      17). The `valid corpus infers ok` theory in `HMInferTests.fs`
//      gets a new `18-hminfer-real.lll` row in addition to this fact
//      so the corpus theory still owns the canonical list.
//   2. runtime E2E — `lllc run` on the file emits all five expected
//      `t<N> unify ... ok|mismatch` lines. Substring contains, not
//      exact match, so trailing whitespace / newline differences
//      don't matter.

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``18-hminfer-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "18-hminfer-real.lll"
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
let ``18-hminfer-real.lll runs and emits unify + inferExpr result lines`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/18-hminfer-real.lll")
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
    // Phase 7.7a covered the unify tests t1-t5. Phase 7.7b extends `main`
    // with inference tests t6-t10 that exercise `inferExpr` against
    // literal / addition / var-in-env / application-in-env / app-mismatch
    // expression shapes:
    //   t1: unify Int Int                          -> ok
    //   t2: unify Int Str                          -> mismatch
    //   t3: unify a Int                            -> ok bound a
    //   t4: unify (Int -> Str) (Int -> Str)        -> ok
    //   t5: unify (Int -> Str) (Int -> Bool)       -> mismatch
    //   t6: infer (EInt 42)                        -> Int
    //   t7: infer (EAdd 1 2)                       -> Int
    //   t8: infer (EVar "x") {x: Int}              -> Int
    //   t9: infer (EApp double (EInt 5))           -> Int
    //   t10: infer (EApp double (EStr "x"))        -> ERROR (unify fail)
    let expected =
        [ "t1 unify Int Int ok"
          "t2 unify Int Str mismatch"
          "t3 unify a Int ok bound a"
          "t4 unify (Int -> Str) (Int -> Str) ok"
          "t5 unify (Int -> Bool) (Int -> Str) mismatch"
          "t6 infer 42 : Int"
          "t7 infer (1 + 2) : Int"
          "t8 infer x in env : Int"
          "t9 infer (double 5) in env : Int"
          "t10 infer (double \"x\") in env : ERROR" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing result line: {line}\nstdout: {stdout}\nstderr: {stderr}")
