module LLLang.Tests.BootstrapCompilerTests

open System.IO
open Xunit
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.9b: tests for `spec/examples/valid/20-bootstrap-compiler.lll`
// — the first **4-stage bootstrap compiler** written entirely in
// ll-lang itself: parser + elaborator + minimal HM type checker +
// **F# code emission**. Phase 7.9a shipped the 3-stage error reporter;
// Phase 7.9b extends it with a codegen pass that walks the parser's
// `List[Decl]` AST and emits an F# source file (module header,
// stdlib prelude, type decls, `let rec` fn group, `[<EntryPoint>]`
// main wrapper) — turning the file into a true compiler that emits
// source rather than just listing errors.
//
// The hardcoded driver source is a **clean** ll-lang module — no
// unbound vars, no non-exhaustive matches, no type mismatches — so
// the pipeline reaches the codegen pass and the runtime test asserts
// substrings of the emitted F# (`module`, prelude lines, `type`,
// `let rec`, `[<EntryPoint>]`).
//
// Two layers of coverage (same shape as 17 / 19):
//   1. inference round-trip — parses, elaborates, and HM-infers the
//      whole file without errors (smoke test). The `valid corpus
//      infers ok` theory in `HMInferTests.fs` gets a new
//      `20-bootstrap-compiler.lll` row in addition to this fact so
//      the corpus theory still owns the canonical list.
//   2. runtime E2E — `lllc run` on the file emits the F# source for
//      the clean hardcoded module. Substring contains, not exact
//      match, so any trailing whitespace / codegen warnings don't
//      matter.

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
let ``20-bootstrap-compiler.lll runs and emits F# source for the clean hardcoded module`` () =
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
    // Phase 7.9b: the hardcoded source in `main` is now intentionally
    // clean (no unbound vars, no non-exhaustive matches, no type
    // mismatches), so `elaborate` returns an empty error list and the
    // pipeline proceeds to the F# codegen pass:
    //   module Examples.Clean
    //   type Maybe A = Some A | None
    //   fn inc(x Int) Int = x + 1
    //   fn greet() Str = "hello"
    //   fn main() Int = inc 1
    //
    // The emitted F# source contains the module header, the auto-
    // generated stdlib prelude block, the `Maybe<'A>` sum-type decl,
    // a `let rec ... and ...` group of plain fns, and an
    // `[<EntryPoint>]`-wrapped `main` fn. Substring contains, not
    // exact match.
    let expected =
        [ "module Examples.Clean"
          "// --- ll-lang stdlib prelude (auto-generated) ---"
          "let print (s: string) = System.Console.Write(s)"
          "// --- end prelude ---"
          "type Maybe<'A> ="
          "    | Some of 'A"
          "    | None"
          "let rec inc x = (x + 1L)"
          "and greet = \"hello\""
          "[<EntryPoint>]"
          "let main (argv: string[]) =" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing line: {line}\nstdout: {stdout}\nstderr: {stderr}")
