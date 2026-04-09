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
// Phase 7.9c: the driver no longer hardcodes its source string —
// `main` now reads `spec/examples/valid/20a-bootstrap-input.lll`
// via the `readFile` stdlib fn. The input file content is identical
// to the 7.9b hardcoded source, so the emitted F# output is
// byte-identical to the 7.9b baseline. The tests below assert both
// (a) the codegen output shape and (b) that the file-reading path
// is actually taken, by renaming the input file and asserting the
// compiler fails with a clean error message.
//
// The driver source is a **clean** ll-lang module — no unbound vars,
// no non-exhaustive matches, no type mismatches — so the pipeline
// reaches the codegen pass and the runtime test asserts substrings
// of the emitted F# (`module`, prelude lines, `type`, `let rec`,
// `[<EntryPoint>]`).
//
// Three layers of coverage:
//   1. inference round-trip — parses, elaborates, and HM-infers the
//      whole file without errors (smoke test). The `valid corpus
//      infers ok` theory in `HMInferTests.fs` gets a new
//      `20-bootstrap-compiler.lll` row in addition to this fact so
//      the corpus theory still owns the canonical list.
//   2. runtime E2E — `lllc run` on the file emits the F# source for
//      the clean module loaded from `20a-bootstrap-input.lll`.
//      Substring contains, not exact match, so any trailing
//      whitespace / codegen warnings don't matter.
//   3. file-reading path — temporarily rename the input file, run
//      the bootstrap compiler, assert it fails (proving the source
//      actually flows through `readFile`), then restore.

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private readValid name =
    File.ReadAllText(Path.Combine(repoRoot, "spec/examples/valid", name))

/// Phase 7.9c: runs `lllc run <bootstrap>` with the working directory
/// pinned to the repo root (since the bootstrap compiler now loads
/// its source via a repo-root-relative `readFile` call).
let private runBootstrap () =
    let lllPath =
        Path.Combine(repoRoot, "spec/examples/valid/20-bootstrap-compiler.lll")
    let llcDll =
        Path.Combine(repoRoot, "src/LLLangTool/bin/Debug/net10.0/lllc.dll")
    let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"\"{llcDll}\" run \"{lllPath}\"")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError  <- true
    psi.UseShellExecute        <- false
    psi.WorkingDirectory       <- repoRoot
    use proc = System.Diagnostics.Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    (proc.ExitCode, stdout, stderr)

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
let ``20-bootstrap-compiler.lll runs and emits F# source for the clean module loaded from 20a-bootstrap-input.lll`` () =
    let (_, stdout, stderr) = runBootstrap ()
    // Phase 7.9c: `main` reads the source from
    // `spec/examples/valid/20a-bootstrap-input.lll` via `readFile`.
    // The input is a clean ll-lang module (no unbound vars, no
    // non-exhaustive matches, no type mismatches), so `elaborate`
    // returns an empty error list and the pipeline proceeds to the
    // F# codegen pass:
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

[<Fact>]
let ``20-bootstrap-compiler.lll actually reads its source from 20a-bootstrap-input.lll (file-reading path)`` () =
    // Phase 7.9c: prove that the source flows through `readFile` and
    // not a stale hardcoded string. We temporarily rename the input
    // file out of the way, run the bootstrap compiler, and assert
    // that it fails (because `readFile` on a missing path throws and
    // lllc reports the exception). Then we restore the file so the
    // rest of the suite is unaffected.
    //
    // Use a try/finally to make sure the rename is always undone,
    // even if the assertion fails.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    File.Move(inputPath, backupPath)
    try
        let (exitCode, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        // The host compiler catches the IO exception in `cmdRun`
        // (`Program.fs`) and prints `lllc: <message>` to stderr with
        // exit code 1. Accept either a non-zero exit code or the
        // absence of the codegen output markers as proof that
        // readFile was actually called.
        Assert.True(
            exitCode <> 0 || not (stdout.Contains "module Examples.Clean"),
            $"expected bootstrap run to fail when input is missing; exit={exitCode}\nstdout: {stdout}\nstderr: {stderr}")
        Assert.False(
            stdout.Contains "let rec inc x = (x + 1L)",
            $"expected no codegen output when input is missing; stdout:\n{combined}")
    finally
        if File.Exists backupPath then
            File.Move(backupPath, inputPath)

[<Fact>]
let ``20-bootstrap-compiler.lll accepts stdlib builtin strConcat in fn body (Phase 7.9e)`` () =
    // Phase 7.9e: before the fix, `elaborate` started with an empty
    // `MkEnv []` env, so any fn body that called a stdlib builtin like
    // `strConcat` / `listMap` / `readFile` fired `E002 UnboundVar
    // <name>`. The fix seeds `env0` with the stdlib builtin names via
    // a new `stdlibNames` helper. This test swaps the 20a input for a
    // variant that exercises `strConcat "hi " n` in a fn body and
    // asserts the pipeline reaches the codegen pass.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let stdlibInputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20c-bootstrap-input-stdlib.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,       $"missing fixture: {inputPath}")
    Assert.True(File.Exists stdlibInputPath, $"missing fixture: {stdlibInputPath}")
    File.Move(inputPath, backupPath)
    File.Copy(stdlibInputPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002 UnboundVar error; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let greet n",
            $"expected emitted F# to contain `let greet n`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "strConcat",
            $"expected emitted F# to reference strConcat; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath)

[<Fact>]
let ``20-bootstrap-compiler.lll accepts == operator in fn body (Phase 7.9f)`` () =
    // Phase 7.9f: before the fix, the bootstrap compiler's lexer only
    // emitted `TEq` for a single `=` and had no double-`=` token, so a
    // fn body like `if 1 == 1 then 0 else 1` could not parse. The fix
    // adds a two-char `TEqEq` token via a new `lexEqOrEqEq` helper,
    // introduces `EEq Expr Expr` in the AST, slots `parseCompare` into
    // the precedence cascade between `parseExpr` and `parseAddSub`,
    // threads `EEq` through checkExpr / inferExprType / showExpr, and
    // emits `(<l> = <r>)` from emitExpr (F# uses single `=` for
    // equality). This test swaps the 20a input for a variant whose
    // main body uses `1 == 1` and asserts the pipeline reaches codegen.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let eqeqPath =
        Path.Combine(repoRoot, "spec/examples/valid/20d-bootstrap-input-eqeq.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists eqeqPath,  $"missing fixture: {eqeqPath}")
    File.Move(inputPath, backupPath)
    File.Copy(eqeqPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002 UnboundVar error; combined:\n{combined}")
        Assert.False(
            combined.Contains "error",
            $"expected NO error output; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let main",
            $"expected emitted F# to contain `let main`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "(1L = 1L)",
            $"expected emitted F# to contain `(1L = 1L)`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath)

[<Fact>]
let ``20-bootstrap-compiler.lll accepts < and > operators in fn body (Phase 7.9g)`` () =
    // Phase 7.9g: before the fix, the bootstrap compiler's lexer had
    // no `TLt` / `TGt` tokens, so a fn body like
    // `if n < 0 then 0 - n else n` could not parse. The fix adds two
    // single-char tokens `TLt` / `TGt` in the `lexChars` main loop
    // (no helper needed — no two-char forms in this slice, and the
    // `-` vs `->` collision is handled by `lexMinusOrArrow` so the
    // standalone `>` arm only fires when `>` is not preceded by `-`),
    // introduces `ELt Expr Expr` / `EGt Expr Expr` in the AST as
    // siblings of `EEq`, adds two new arms in `parseCompareTail`
    // reusing the 7.9f precedence layer, and threads `ELt` / `EGt`
    // through checkExpr / inferExprType / typeCheck / showExpr /
    // emitExpr. This test swaps the 20a input for a variant whose
    // fn bodies use `n < 0` / `n > 0` and asserts codegen reaches
    // both comparison emissions.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let ltgtPath =
        Path.Combine(repoRoot, "spec/examples/valid/20e-bootstrap-input-ltgt.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists ltgtPath,  $"missing fixture: {ltgtPath}")
    File.Move(inputPath, backupPath)
    File.Copy(ltgtPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002 UnboundVar error; combined:\n{combined}")
        Assert.False(
            combined.Contains "error",
            $"expected NO error output; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let rec neg",
            $"expected emitted F# to contain `let rec neg`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "and pos",
            $"expected emitted F# to contain `and pos`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "let main",
            $"expected emitted F# to contain `let main`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "(n < 0L)",
            $"expected emitted F# to contain `(n < 0L)`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "(n > 0L)",
            $"expected emitted F# to contain `(n > 0L)`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath)

[<Fact>]
let ``20-bootstrap-compiler.lll parses Maybe[Int] return type and emits main (bracket-form types in fn signatures)`` () =
    // Phase 7.9d: the bootstrap compiler's parseParamGroups /
    // parseReturnType only accepted bare `Upper` type names. A fn with
    // a bracket-form return type like `Maybe[Int]` caused the parser to
    // consume only the head `Maybe`, leaving `[Int] = Some v` in the
    // token stream, which then desynchronised the outer decl loop and
    // silently dropped the subsequent `main` decl entirely.
    //
    // This test swaps the 20a input for a variant that exercises the
    // bracket-form return type and asserts that the emitted F# source
    // contains `wrap`, `let main`, and `[<EntryPoint>]` — proving
    // `main` is no longer dropped.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let maybePath =
        Path.Combine(repoRoot, "spec/examples/valid/20b-bootstrap-input-maybe.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,  $"missing fixture: {inputPath}")
    Assert.True(File.Exists maybePath,  $"missing fixture: {maybePath}")
    File.Move(inputPath, backupPath)
    File.Copy(maybePath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.True(
            stdout.Contains "wrap",
            $"expected emitted F# to contain `wrap`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "let main",
            $"expected emitted F# to contain `let main`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath)

[<Fact>]
let ``20-bootstrap-compiler.lll accepts true and false literals in fn body (Phase 7.9h)`` () =
    // Phase 7.9h: before the fix, the bootstrap compiler's elaborator
    // fired `E002 UnboundVar true` / `E002 UnboundVar false` for any
    // program that used bare `true` / `false` literals. The bootstrap
    // lexer already produces `TLower "true"` / `TLower "false"`,
    // `parseAtom` already wraps them in `EVar "true"` / `EVar "false"`,
    // `emitExpr`'s `EVar x -> x` arm already emits them as valid F#
    // boolean literals, and `inferExprType`'s `EVar name ->
    // typeEnvLookup env name` arm already returns `TyVar "?"` for
    // unknown names (which `typeEq` short-circuits on). The ONLY gap
    // was the elaborator's name-scope check: `"true"` / `"false"`
    // were not in `stdlibNames`, so `checkExpr`'s `EVar name` arm
    // fired E002. The fix is two strings appended to `stdlibNames`.
    //
    // This test swaps the 20a input for a variant with two fns:
    // `choose(n Int)` does `if n > 0 then 1 else 0` (smoke test that
    // the existing `<` / `>` from 7.9g still works under the new
    // stdlib env), and `flag()` does `if true == false then 1 else 0`
    // — exercising both `true` and `false` as bare identifiers in
    // expression position (then/else branch of an EEq comparison).
    // Single-line fn bodies only: the bootstrap parser's `parseLetIn`
    // doesn't tolerate a `TNewline` between `in` and the body expr
    // (unrelated multi-line bug, out of scope for this slice).
    // Asserts codegen reaches the F# output (contains `let choose`,
    // `let main`, `[<EntryPoint>]`, and both `true` and `false` as
    // substrings).
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let boolPath =
        Path.Combine(repoRoot, "spec/examples/valid/20f-bootstrap-input-bool-lits.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists boolPath,  $"missing fixture: {boolPath}")
    File.Move(inputPath, backupPath)
    File.Copy(boolPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002 UnboundVar error; combined:\n{combined}")
        Assert.False(
            combined.Contains "E001",
            $"expected NO E001 TypeMismatch error; combined:\n{combined}")
        Assert.False(
            combined.Contains "error",
            $"expected NO error output; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let choose",
            $"expected emitted F# to contain `let choose`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "let main",
            $"expected emitted F# to contain `let main`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "true",
            $"expected emitted F# to contain `true`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "false",
            $"expected emitted F# to contain `false`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath)
