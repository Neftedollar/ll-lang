[<Xunit.Collection("Bootstrap Fixture Serial")>]
module LLLang.Tests.BootstrapCompilerTests

open System.IO
open System.Text.RegularExpressions
open Xunit

/// Named mutex that serialises access to the shared bootstrap fixture file
/// (spec/examples/valid/20a-bootstrap-input.lll) across test runs — even
/// concurrent `dotnet test` processes on the same machine.
let private fixtureLock =
    new System.Threading.Mutex(false, "ll-lang-bootstrap-fixture-lock")
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

let private sharedBootstrapInputPath =
    Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")

let private sharedBootstrapBackupPath =
    sharedBootstrapInputPath + ".bak"

let private sharedBootstrapBaseline =
    """module Examples.Clean
Maybe A = Some A | None
inc(x Int) = x + 1
greet() = "hello"
main() = inc 1
"""

let private ensureSharedBootstrapFixturePresent () =
    Directory.CreateDirectory(LLLang.Tests.TestCompat.directoryNameOrCurrent sharedBootstrapInputPath) |> ignore
    if File.Exists sharedBootstrapBackupPath then
        File.Delete(sharedBootstrapBackupPath)
    File.WriteAllText(sharedBootstrapInputPath, sharedBootstrapBaseline)

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
    use proc = LLLang.Tests.TestCompat.startProcess psi
    // Read stdout and stderr concurrently to prevent pipe-buffer deadlock.
    // If the bootstrap writes large output to one pipe while we're blocked
    // reading the other, both sides deadlock. Task.Run drains both in parallel.
    let stdoutTask = System.Threading.Tasks.Task.Run(fun () -> proc.StandardOutput.ReadToEnd())
    let stderrTask = System.Threading.Tasks.Task.Run(fun () -> proc.StandardError.ReadToEnd())
    proc.WaitForExit()
    let stdout = stdoutTask.Result
    let stderr = stderrTask.Result
    (proc.ExitCode, stdout, stderr)

let private normalizeNewlines (s: string) =
    s.Replace("\r\n", "\n").Replace("\r", "\n")

let private firstDiffLine (expected: string) (actual: string) =
    let exp = normalizeNewlines expected |> fun s -> s.Split('\n')
    let act = normalizeNewlines actual |> fun s -> s.Split('\n')
    let minLen = min exp.Length act.Length
    let rec loop i =
        if i >= minLen then
            if exp.Length = act.Length then None else Some i
        elif exp.[i] = act.[i] then loop (i + 1)
        else Some i
    loop 0

let private countRegex (pattern: string) (input: string) : int =
    Regex.Matches(input, pattern, RegexOptions.Multiline ||| RegexOptions.CultureInvariant).Count

/// Run `f` while holding the named mutex that guards the shared bootstrap
/// fixture file. Times out after 30 s — enough for any normal test run.
let private withFixtureLock (f: unit -> unit) =
    if not (fixtureLock.WaitOne(30000)) then
        failwith "timeout waiting for bootstrap fixture lock — is another test run stuck?"
    try f ()
    finally fixtureLock.ReleaseMutex()

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
    File.Move(inputPath, backupPath, true)
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
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll can self-compile its own source fixture and emit core pipeline markers`` () =
    // Self-hosting parity smoke:
    // run bootstrap compiler with its OWN source as input fixture.
    // This is intentionally a structural guard (not byte-equality):
    // if parser/elaborator/codegen regresses, the emitted output will
    // usually lose one of these core markers long before finer-grained
    // parity checks are inspected.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let bootstrapSourcePath =
        Path.Combine(repoRoot, "spec/examples/valid/20-bootstrap-compiler.lll")
    let backupPath = inputPath + ".bak"
    use _lock = fixtureLockLease ()
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists bootstrapSourcePath, $"missing source: {bootstrapSourcePath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(bootstrapSourcePath, inputPath)
    try
        let (exitCode, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.True((exitCode = 0), $"expected self-compile run to succeed; exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
        Assert.DoesNotContain("readFile failed:", combined)
        Assert.Contains("[<EntryPoint>]", stdout)
        // Core parser/elaborator/codegen pipeline markers expected in
        // compiler output compiled from the bootstrap source itself.
        Assert.True(
            stdout.Contains "let rec parseFnDecl" || stdout.Contains "and parseFnDecl",
            $"expected emitted output to contain parseFnDecl marker; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "let rec parseFnBody" || stdout.Contains "and parseFnBody",
            $"expected emitted output to contain parseFnBody marker; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "let rec parseExpr" || stdout.Contains "and parseExpr",
            $"expected emitted output to contain parseExpr marker; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "let rec emitPrelude" || stdout.Contains "and emitPrelude",
            $"expected emitted output to contain emitPrelude marker; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete(inputPath)
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll self-compile output matches fixpoint snapshot compiler1-latest.fs`` () =
    // Strict fixpoint regression gate:
    // compiler output produced from bootstrap source must match the
    // canonical snapshot checked into docs/compiler-dev/fixpoint-snapshots.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let bootstrapSourcePath =
        Path.Combine(repoRoot, "spec/examples/valid/20-bootstrap-compiler.lll")
    let snapshotPath =
        Path.Combine(repoRoot, "docs/compiler-dev/fixpoint-snapshots/compiler1-latest.fs")
    let backupPath = inputPath + ".bak"
    use _lock = fixtureLockLease ()
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists bootstrapSourcePath, $"missing source: {bootstrapSourcePath}")
    Assert.True(File.Exists(snapshotPath), $"missing snapshot: {snapshotPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(bootstrapSourcePath, inputPath)
    try
        let (exitCode, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.True((exitCode = 0), $"expected self-compile run to succeed; exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
        let expected = File.ReadAllText(snapshotPath)
        let expectedN = normalizeNewlines expected
        let actualN = normalizeNewlines stdout
        if expectedN <> actualN then
            let diffLine = firstDiffLine expectedN actualN |> Option.defaultValue 0
            let expLines = expectedN.Split('\n')
            let actLines = actualN.Split('\n')
            let expLine = if diffLine < expLines.Length then expLines.[diffLine] else "<EOF>"
            let actLine = if diffLine < actLines.Length then actLines.[diffLine] else "<EOF>"
            Assert.True(
                false,
                $"fixpoint snapshot mismatch at line {diffLine + 1}\nexpected: {expLine}\nactual:   {actLine}\nstdout bytes={actualN.Length} expected bytes={expectedN.Length}\ncombined:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete(inputPath)
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll self-compile output satisfies structural fixpoint metrics contract`` () =
    // Independent contract over the emitted compiler text shape.
    // This guards accidental snapshot churn that preserves superficial
    // behaviour but changes fixpoint structure/formatting.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let bootstrapSourcePath =
        Path.Combine(repoRoot, "spec/examples/valid/20-bootstrap-compiler.lll")
    let backupPath = inputPath + ".bak"
    use _lock = fixtureLockLease ()
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists bootstrapSourcePath, $"missing source: {bootstrapSourcePath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(bootstrapSourcePath, inputPath)
    try
        let (exitCode, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.True((exitCode = 0), $"expected self-compile run to succeed; exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
        let normalized = normalizeNewlines stdout
        let lineCount = normalized.Split('\n').Length - 1
        let byteCount = normalized.Length
        let letCount = countRegex @"^let " normalized
        let andCount = countRegex @"^and " normalized

        Assert.Equal(602, lineCount)
        Assert.Equal(56438, byteCount)
        Assert.Equal(24, letCount)
        Assert.Equal(240, andCount)

        Assert.DoesNotContain("| ?", normalized)
        Assert.Equal(1, countRegex @"^\[<EntryPoint>\]$" normalized)
        Assert.True(
            normalized.Contains("let rec parseExpr") || normalized.Contains("and parseExpr"),
            $"expected parseExpr marker in self-compiled output; combined:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete(inputPath)
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll diagnostics contract: unbound var includes stable E002 payload`` () =
    // Diagnostics parity guard: bootstrap should surface a stable,
    // machine-detectable ll-lang error payload on semantic failure.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let backupPath = inputPath + ".bak"
    let badProgram =
        """module Probe.Bad
main() = missingFn 1
"""
    use _lock = fixtureLockLease ()
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    File.Move(inputPath, backupPath, true)
    File.WriteAllText(inputPath, badProgram)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.Contains("E002 UnboundVar missingFn", combined)
        Assert.DoesNotContain("Unhandled exception", combined)
        Assert.DoesNotContain("Stack overflow", combined)
        Assert.DoesNotContain("module Probe.Bad", stdout)
    finally
        if File.Exists inputPath then File.Delete(inputPath)
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

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
    File.Move(inputPath, backupPath, true)
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
            File.Move(backupPath, inputPath, true)

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
    File.Move(inputPath, backupPath, true)
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
            File.Move(backupPath, inputPath, true)

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
    File.Move(inputPath, backupPath, true)
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
            File.Move(backupPath, inputPath, true)

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
    File.Move(inputPath, backupPath, true)
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
            File.Move(backupPath, inputPath, true)

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
    // Asserts codegen reaches the F# output (contains `let rec
    // choose`, `and flag`, `let main`, `[<EntryPoint>]`, and both
    // `true` and `false` as substrings). The `let rec` / `and` spine
    // is an incidental consequence of the 7.8e mutual-recursion
    // grouping — any run of two or more consecutive non-`main` fn
    // decls gets wrapped in a single `let rec ... and ...` block.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let boolPath =
        Path.Combine(repoRoot, "spec/examples/valid/20f-bootstrap-input-bool-lits.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists boolPath,  $"missing fixture: {boolPath}")
    File.Move(inputPath, backupPath, true)
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
            stdout.Contains "let rec choose",
            $"expected emitted F# to contain `let rec choose`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "and flag",
            $"expected emitted F# to contain `and flag`; stdout:\n{combined}")
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
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll accepts char literals in fn body (Phase 7.9i)`` () =
    // Phase 7.9i: before the fix, the bootstrap compiler's lexer
    // silently dropped `'` as an unknown char (falling through to the
    // catch-all `else lexChars rest` in `lexChars`), mangling any
    // source that used char literals like `'='` / `'"'` / `'('`.
    // Without char literal support, the bootstrap compiler can't
    // parse a single real lexer written in ll-lang — every non-trivial
    // lexer uses char literals.
    //
    // The fix adds a `TChar Char` lexer token, a `lexCharLit` helper
    // (consumes 1 char, expects closing `'`, emits `TChar ch`), an
    // `EChar Char` AST variant, a `TChar c :: rest -> (EChar c, rest)`
    // arm in `parseAtom`, and `EChar _ -> []` / `EChar _ -> TyName
    // "Char"` arms threaded through `checkExpr` / `inferExprType` /
    // `typeCheck` / `showExpr` / `emitExpr`. No escape handling in
    // this slice — `'\n'` / `'\\'` / `'\''` stay in 7.9j.
    //
    // Fixture `20g-bootstrap-input-char-lit.lll` exercises `'='` in
    // an `if ... == ... then n else 0` expression via
    // `fn sym(n Int) Int = if '=' == '=' then n else 0`.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let charPath =
        Path.Combine(repoRoot, "spec/examples/valid/20g-bootstrap-input-char-lit.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists charPath,  $"missing fixture: {charPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(charPath, inputPath)
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
            stdout.Contains "let rec sym" || stdout.Contains "let sym",
            $"expected emitted F# to contain `let sym` or `let rec sym`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "let main",
            $"expected emitted F# to contain `let main`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "'='",
            $"expected emitted F# to contain `'='` char literal; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll accepts char escape sequences in fn body (Phase 7.9j)`` () =
    // Phase 7.9j: before the fix, the bootstrap compiler's `lexCharLit`
    // treated `\` as a regular char, so `'\n'` attempted to lex as
    // `'\` + a continuation that doesn't match the closing quote,
    // falling into the lenient drop path and mangling the source.
    // Without escape handling, the bootstrap compiler can't lex its
    // OWN source — `20-bootstrap-compiler.lll` uses `'\n'` on line 471
    // (`if c == '\n' then ...`) and `'\\'` on line 486. Hard blocker
    // for the self-host fixpoint.
    //
    // The fix extends `lexCharLit` to detect a `\` as the first char
    // after the opening quote and dispatch to a `decodeEscape` helper
    // that maps `n` → '\n', `t` → '\t', `\` → '\\', `'` → '\''.
    // No AST / parser / codegen changes — `TChar` / `EChar` are
    // unchanged. Only 4 escapes supported; `\r` / `\0` / `\u...` and
    // string-literal escapes deferred.
    //
    // Fixture `20h-bootstrap-input-char-esc.lll` exercises `'\n'` and
    // `'\\'` via two `if ... == ... then n else 0` comparisons.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let charPath =
        Path.Combine(repoRoot, "spec/examples/valid/20h-bootstrap-input-char-esc.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists charPath,  $"missing fixture: {charPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(charPath, inputPath)
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
            stdout.Contains "let rec nl" || stdout.Contains "let nl",
            $"expected emitted F# to contain `let nl` or `let rec nl`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "and bs" || stdout.Contains "let bs",
            $"expected emitted F# to contain `and bs` or `let bs`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "and main" || stdout.Contains "let main",
            $"expected emitted F# to contain `and main` or `let main`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "'\\n'",
            $"expected emitted F# to contain `'\\n'` char literal; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "'\\\\'",
            $"expected emitted F# to contain `'\\\\'` char literal; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll accepts string literal escape sequences (Phase 7.9m)`` () =
    // Phase 7.9m: before the fix, the bootstrap compiler's `lexStr`
    // via `takeStrBody` treated `\` as a regular char and terminated
    // on the first `"`, so `"hello\nworld"` either mangled the body
    // or — more importantly — its `emitStr` injected the decoded
    // newline raw into the emitted F# source, breaking round-trip.
    // Without string escape handling the bootstrap can't round-trip
    // its own source, which composes output via `strConcat` calls
    // containing literal `"\n"` fragments.
    //
    // The fix extends `takeStrBody` to detect `\` in the body and
    // consume+decode the following char via `decodeEscape` (reused
    // from 7.9j), and adds an `emitStrBody` walker + `encodeEscape`
    // helper so `emitStr` re-encodes `\n` / `\t` / `\\` / `\"` back
    // to their escaped form in the emitted F# source. Only 4 escapes
    // supported; `\r` / `\0` / `\u...` deferred.
    //
    // Fixture `20k-bootstrap-input-str-esc.lll` exercises all three
    // critical string escapes in a single literal — `\"` (escaped
    // quote, which must NOT terminate the string) and `\n` (decoded
    // newline, which must NOT be injected raw into the emitted F#) —
    // via `fn greet() Str = "say \"hi\"\n"`.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let strPath =
        Path.Combine(repoRoot, "spec/examples/valid/20k-bootstrap-input-str-esc.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists strPath,   $"missing fixture: {strPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(strPath, inputPath)
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
            stdout.Contains "let rec greet" || stdout.Contains "let greet",
            $"expected emitted F# to contain `let greet` or `let rec greet`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        // Escaped quote `\"` must round-trip — without the fix,
        // the lexer terminates the string at the first unescaped `"`
        // so the rest of the body is mis-lexed and the greet fn
        // fails to parse / elaborate.
        Assert.True(
            stdout.Contains "\"say \\\"hi\\\"\\n\"",
            $"expected emitted F# to contain `\"say \\\"hi\\\"\\n\"` string literal; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll accepts constructor patterns in match arms (Phase 7.9l)`` () =
    // Phase 7.9l: before the fix, the bootstrap compiler's
    // `parsePrimaryPat` had no `TUpper`-headed arm, so constructor
    // patterns like `| Some n -> ...` and `| None -> ...` fell through
    // to the `PWild` catch-all, losing the constructor name entirely
    // and effectively turning every ctor arm into a wildcard.
    //
    // The fix adds a new `PCon Str List[Pat]` variant to the `Pat`
    // type, a `TUpper name :: rest -> parseCtorArgs name rest` arm in
    // `parsePrimaryPat`, helper fns `parseCtorArgs` / `parsePatArgs` /
    // `parsePatArgsCons` that eagerly consume a sequence of atomic
    // sub-patterns (var / wildcard / int / empty-list) until hitting a
    // non-pattern-starter token, and threads `PCon` through
    // `showPat` / `patBinders` / `patIsCatchAll` / `emitPat`. F#
    // discriminated-union patterns use the shape `(Name arg1 arg2)`,
    // same bracketing as the bootstrap's existing `PCons` emission.
    //
    // Fixture `20i-bootstrap-input-ctor-pat.lll` exercises both
    // `Some n` (ctor with one arg binder) and `None` (nullary ctor)
    // in a `match m with | Some n -> n | None -> 0` expression.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let ctorPath =
        Path.Combine(repoRoot, "spec/examples/valid/20i-bootstrap-input-ctor-pat.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists ctorPath,  $"missing fixture: {ctorPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(ctorPath, inputPath)
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
            stdout.Contains "let rec unwrap" || stdout.Contains "let unwrap",
            $"expected emitted F# to contain `let unwrap` or `let rec unwrap`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "| (Some n) ->",
            $"expected emitted F# to contain `| (Some n) ->`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "| (None) ->",
            $"expected emitted F# to contain `| (None) ->`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll accepts multi-line let-in and match arm layout (Phase 7.9.newlines)`` () =
    // Phase 7.9.newlines: before the fix, the bootstrap compiler's
    // `parseLetIn` and `parseArms` peeked at the next token without
    // first calling `skipNewlines`, so any intervening `TNewline`
    // caused silent mis-parse.
    //
    // Bundles two sibling bugs — Neftedollar/ll-lang#8 (parseLetIn)
    // and #12 (parseArms) — because the root cause is identical and
    // one surgery closes both.
    //
    // Bug 1 (#8): `let x = 0 in` followed by a newline before the
    // body caused `parseExpr` to be called on `TNewline :: ...`,
    // garbling the body and cascading into `E002 UnboundVar`.
    //
    // Bug 2 (#12): a multi-line match
    //   match m with
    //     | Some n -> n
    //     | None -> 0
    // silently dropped the `| None` arm because after the first arm
    // body, a `TNewline` intervened before the next `TBar`, so
    // `parseArms` terminated at the `TNewline :: TBar :: _` peek.
    //
    // The fix threads `skipNewlines` through `parseLetIn` (after
    // `TEq` and after `TKwIn`) and `parseArms` (at entry, so the
    // `TBar :: _` peek sees through leading newlines).
    //
    // Fixture `20j-bootstrap-input-layout.lll` exercises BOTH paths:
    //   * `let fallback = 0 in` + newline + `match m with` (bug #8)
    //   * multi-line arms `| Some n -> n` / `| None -> fallback` (#12)
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let layoutPath =
        Path.Combine(repoRoot, "spec/examples/valid/20j-bootstrap-input-layout.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,  $"missing fixture: {inputPath}")
    Assert.True(File.Exists layoutPath, $"missing fixture: {layoutPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(layoutPath, inputPath)
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
            stdout.Contains "let rec describe" || stdout.Contains "let describe",
            $"expected emitted F# to contain `let describe` or `let rec describe`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "let main" || stdout.Contains "and main",
            $"expected emitted F# to contain `let main` or `and main`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "| (Some n) ->",
            $"expected emitted F# to contain `| (Some n) ->` (Some arm survived); stdout:\n{combined}")
        Assert.True(
            stdout.Contains "| (None) ->",
            $"expected emitted F# to contain `| (None) ->` (None arm survived); stdout:\n{combined}")
        Assert.True(
            stdout.Contains "fallback",
            $"expected emitted F# to contain `fallback` (let-in body survived); stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll skips `--` line comments (Phase 7.9n)`` () =
    // Phase 7.9n: before the fix, the bootstrap compiler's `lexChars`
    // had zero handling for `--` line comments — it just hit the `-`
    // arm, went into `lexMinusOrArrow`, emitted a `TMinus`, and kept
    // lexing the comment body as tokens. Since `parseDecls` doesn't
    // know what to do with a floating `TMinus` between decls, a
    // single commented line desynced the stream and every subsequent
    // decl was silently dropped (emitted F# contained only module
    // header + prelude).
    //
    // Discovered via fixpoint probe: the bootstrap compiler's own
    // source (~190 lines of header comments before the first real
    // decl) can't be read by itself without line-comment support,
    // blocking Phase 7.10 fixpoint. A 3-line fixture
    //   module Examples.Clean
    //   -- comment
    //   fn main() Int = 42
    // produced empty output pre-fix.
    //
    // The fix extends `lexMinusOrArrow` into `lexMinusOrDashOrArrow`
    // which peeks the char after `-`: if it's another `-`, hand off
    // to `skipLineComment` which eats chars up to and including `\n`
    // then recurses into `lexChars`; otherwise fall through to the
    // existing `-> vs -` logic. Block comments `{- -}` and doc-comment
    // markers remain deliberately out of scope.
    //
    // Fixture `20l-bootstrap-input-comments.lll` exercises three
    // comment positions: full-line at top, trailing after a decl
    // body (`fn greet() Str = "hi"  -- trailing`), and another
    // full-line between decls.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let commentsPath =
        Path.Combine(repoRoot, "spec/examples/valid/20l-bootstrap-input-comments.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,    $"missing fixture: {inputPath}")
    Assert.True(File.Exists commentsPath, $"missing fixture: {commentsPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(commentsPath, inputPath)
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
            stdout.Contains "let rec greet" || stdout.Contains "let greet",
            $"expected emitted F# to contain `let greet` or `let rec greet`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "and main" || stdout.Contains "let main",
            $"expected emitted F# to contain `let main` or `and main`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "\"hi\"",
            $"expected emitted F# to contain `\"hi\"` (greet body survived past trailing comment); stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll parses multi-line type decl with leading bar (Phase 7.9o)`` () =
    // Phase 7.9o: before the fix, the bootstrap compiler's
    // `parseTypeDecl` / `parseCtors` / `parseCtorsTail` trio had
    // no newline tolerance around the constructor list. The
    // multi-line form
    //     type Color =
    //       | Red
    //       | Green
    //       | Blue
    // has a `TNewline` immediately after `TEq` and a leading `TBar`
    // before the FIRST ctor. The pre-fix code passed those tokens
    // straight to `parseCtor`, which saw `TNewline :: TBar :: ...`
    // (not `TUpper _`) and fell through to `MkCtor "?" []`, emitting
    // `| ?` and terminating the ctor list after one bogus entry.
    //
    // Discovered via the fixpoint probe after Phase 7.9n shipped
    // line-comment support — the first "real" thing the bootstrap
    // compiler's own source defines is `type Token = | TKwModule
    // | TKwType ...`, which is exactly this multi-line form. The
    // probe output showed `type Token =\n    | ?` and stopped,
    // confirming the blocker.
    //
    // The fix has three sites, all in the type-decl parser:
    //   1. `parseTypeDecl` — skipNewlines after `TEq`
    //   2. `parseCtors` — strip optional leading `TBar` (+ newlines)
    //      so the first ctor in a multi-line list parses cleanly
    //   3. `parseCtorsTail` — skipNewlines before the `TBar :: _`
    //      peek so inter-ctor newlines don't terminate the list
    //      early; also skipNewlines after `TBar` before each
    //      `parseCtor` call
    //
    // The single-line form `type Maybe A = Some A | None` must
    // keep working (tests in 7.9l rely on it) — the fixes are
    // strictly additive (skipNewlines over zero newlines is a
    // no-op, optional leading bar doesn't break ctors that lack one).
    //
    // Fixture `20m-bootstrap-input-type-layout.lll` exercises
    // `type Color =\n  | Red\n  | Green\n  | Blue` and a `label`
    // fn that pattern-matches over it — the multi-line match body
    // already works as of Phase 7.9.newlines.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let layoutPath =
        Path.Combine(repoRoot, "spec/examples/valid/20m-bootstrap-input-type-layout.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,  $"missing fixture: {inputPath}")
    Assert.True(File.Exists layoutPath, $"missing fixture: {layoutPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(layoutPath, inputPath)
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
        Assert.False(
            combined.Contains "| ?",
            $"expected NO `| ?` placeholder (signals broken ctor parsing); combined:\n{combined}")
        Assert.True(
            stdout.Contains "| Red" || stdout.Contains "Red of",
            $"expected emitted F# to contain `| Red` or `Red of`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "| Green" || stdout.Contains "Green of",
            $"expected emitted F# to contain `| Green` or `Green of`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "| Blue" || stdout.Contains "Blue of",
            $"expected emitted F# to contain `| Blue` or `Blue of`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "let rec label" || stdout.Contains "let label",
            $"expected emitted F# to contain `let label` or `let rec label`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll resolves charToInt via stdlibNames (Phase 7.9p)`` () =
    // Phase 7.9p: the bootstrap compiler's `stdlibNames` list in
    // `elaborate`/`checkDecls` seeds the initial name environment
    // with every stdlib builtin a user program can call without
    // importing. Before 7.9p, the list held only 12 names
    // (`strConcat`, `strLen`, `print`, `printfn`, `listMap`,
    // `listLen`, `listAppend`, `listIsEmpty`, `listFold`, `readFile`,
    // `true`, `false`). The bootstrap compiler's OWN source uses
    // `charToInt` inside `lexChars` / `isUpperChar` — a name the
    // host elaborator knows as a builtin but which the bootstrap's
    // mirror list did not. Running the bootstrap on its own source
    // (the fixpoint probe) produced a 26-byte stdout containing
    // exactly `E002 UnboundVar charToInt`, blocking every
    // downstream codegen line.
    //
    // Discovered via the fixpoint probe after Phase 7.9o (commit
    // 1d1fca0) unblocked the parser by adding newline tolerance
    // around `type` decl layout. With the parser clean, the
    // elaborator's name-resolution pass became the next blocker
    // and the probe started surfacing UnboundVar errors for
    // stdlib names the bootstrap itself references.
    //
    // Fix: extend the bootstrap's `stdlibNames` list with
    // `charToInt`. Since the bootstrap's minimal HM pass is
    // lenient and `checkExpr` only verifies the name is bound,
    // adding the bare name (no type) is enough to clear the
    // cascade and let the pipeline reach the codegen pass.
    //
    // Fixture `20n-bootstrap-input-stdlib-full.lll` calls
    // `charToInt` from a user fn body. Before the fix, running
    // the bootstrap on this fixture surfaces
    // `E002 UnboundVar charToInt`; after the fix, the elaborator
    // accepts the name and the codegen pass emits a `let demo`
    // plus the `[<EntryPoint>]` main wrapper.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let stdlibPath =
        Path.Combine(repoRoot, "spec/examples/valid/20n-bootstrap-input-stdlib-full.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,  $"missing fixture: {inputPath}")
    Assert.True(File.Exists stdlibPath, $"missing fixture: {stdlibPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(stdlibPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002 UnboundVar charToInt",
            $"expected NO E002 UnboundVar charToInt; combined:\n{combined}")
        Assert.False(
            combined.Contains "E002 UnboundVar",
            $"expected NO E002 UnboundVar at all; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let rec demo" || stdout.Contains "let demo",
            $"expected emitted F# to contain `let demo` or `let rec demo`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll parses multi-line if-then-else and keeps emitting subsequent fns (Phase 7.9q)`` () =
    // Phase 7.9q: before the fix, the bootstrap compiler's `parseIf`
    // had no newline tolerance around `then`/`else`. A multi-line
    // if form
    //   if c < 65 then 0
    //   else if c > 90 then 0
    //   else 1
    // tokenised as `... TInt 0 TNewline TKwElse ...`. `parseIf`
    // parsed the condition + `then` branch, then its `TKwElse :: r`
    // match arm missed the leading `TNewline` and fell through,
    // so `parseExpr` was called on `TNewline :: TKwElse :: ...`.
    // `parseAtom`'s wildcard returned `(EInt 0, toks)` without
    // consuming, and `parseIf` returned `EIf cond thenE (EInt 0)`
    // with the stray `TKwElse` still on the stream. Back in
    // `parseDecls` (after the enclosing `parseFnDecl` returned),
    // `skipNewlines` ate the newline but the head was now `TKwElse`,
    // which matched no decl arm — the wildcard `| _ -> []` fired
    // and **every remaining decl was silently dropped**.
    //
    // Discovered via fixpoint probe on its own source: after 7.9p
    // cleared the last E002, the probe output jumped from 26 bytes
    // to ~2253 bytes but halted mid-emission after the FIRST non-
    // main fn (`isUpperChar`) — whose `else if` body is the first
    // multi-line if in the bootstrap. Surface symptom looked like
    // a codegen walker halt; root cause was the parser truncating
    // the decl list.
    //
    // Fix: `parseIf` calls `skipNewlines` before checking for
    // `TKwThen` / `TKwElse`, mirroring the host parser's
    // `skipNewlines c` calls at the same positions. Also
    // `skipNewlines` after matching them, so `then\n body` and
    // `else\n body` layouts both parse cleanly.
    //
    // Fixture `20o-bootstrap-input-multifn.lll` exercises a single
    // multi-line `if-then-else-if-then-else` body followed by four
    // more non-main fns. Before the fix, only the first fn emits
    // (as a singleton `let`, not `let rec`) and everything after
    // is dropped. After the fix, all five non-main fns collapse
    // into a `let rec one ... and two ... and five ...` block
    // and the `[<EntryPoint>]` main wrapper is emitted.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let multifnPath =
        Path.Combine(repoRoot, "spec/examples/valid/20o-bootstrap-input-multifn.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,   $"missing fixture: {inputPath}")
    Assert.True(File.Exists multifnPath, $"missing fixture: {multifnPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(multifnPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.True(
            stdout.Contains "let rec one" || stdout.Contains "let one",
            $"expected emitted F# to contain `let one` or `let rec one`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "and five" || stdout.Contains "let five",
            $"expected emitted F# to contain `let five` or `and five`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "and main" || stdout.Contains "let main",
            $"expected emitted F# to contain `let main` or `and main`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll resolves charIsDigit via stdlibNames (Phase 7.9r)`` () =
    // Phase 7.9r: after 7.9q cleared the parser blocker by adding
    // newline tolerance to `parseIf`, the fixpoint probe revealed
    // four remaining `E002 UnboundVar` errors:
    //   E002 UnboundVar charIsDigit
    //   E002 UnboundVar p
    //   E002 UnboundVar cs
    //   E002 UnboundVar List
    //
    // This slice clears the first one (`charIsDigit`). The bootstrap
    // compiler's own source uses `charIsDigit` (the host elaborator
    // knows it as a builtin) but the bootstrap's mirror `stdlibNames`
    // list did not include it. Same shape as the 7.9p `charToInt`
    // fix: add the bare name to the flat name list, the minimal HM
    // pass accepts it, and the elaborator stops emitting E002 for
    // user programs that call `charIsDigit`.
    //
    // Fixture `20p-bootstrap-input-digit.lll` calls `charIsDigit`
    // from a user fn body inside an `if`. Before the fix, running
    // the bootstrap on this fixture surfaces
    // `E002 UnboundVar charIsDigit`; after the fix, the elaborator
    // accepts the name and the codegen pass emits a `let isNum`
    // plus the `[<EntryPoint>]` main wrapper.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let digitPath =
        Path.Combine(repoRoot, "spec/examples/valid/20p-bootstrap-input-digit.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists digitPath, $"missing fixture: {digitPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(digitPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002 UnboundVar charIsDigit",
            $"expected NO E002 UnboundVar charIsDigit; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let rec isNum" || stdout.Contains "let isNum",
            $"expected emitted F# to contain `let isNum` or `let rec isNum`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "charIsDigit",
            $"expected emitted F# to contain `charIsDigit`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll emits list literal expressions (Phase 7.10a)`` () =
    // Phase 7.10a: before this slice, the bootstrap's `parseAtom` had
    // NO arm for `TLBrack` in expression position — every list literal
    // (`[]`, `[c]`, `[1 2 3]`) silently fell through to the `(EInt 0,
    // toks)` wildcard WITHOUT consuming the `[` token. The downstream
    // parser then desynced, and fns like `takeIdCont` / `dropIdCont`
    // (which use `listAppend [c] (...)`) emitted malformed match
    // bodies. This slice adds two new `Expr` variants (`ENil` /
    // `ECons`) plus parser + elaborator + HM + codegen plumbing,
    // mirroring how `PNil` / `PCons` work in the Pat hierarchy.
    //
    // Fixture `20r-bootstrap-input-lists.lll` calls `head [1 2 3]`.
    // Pre-fix, the bootstrap would silently produce a truncated /
    // malformed body for `main`. Post-fix, the emitted F# contains
    // a proper cons chain `(1L :: (2L :: (3L :: [])))`.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let listsPath =
        Path.Combine(repoRoot, "spec/examples/valid/20r-bootstrap-input-lists.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists listsPath, $"missing fixture: {listsPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(listsPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002; combined:\n{combined}")
        Assert.False(
            combined.Contains "E001",
            $"expected NO E001; combined:\n{combined}")
        Assert.False(
            combined.Contains "error",
            $"expected NO `error`; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let rec head" || stdout.Contains "let head",
            $"expected emitted F# to contain `let head` or `let rec head`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "(1L :: (2L :: (3L :: [])))",
            $"expected emitted F# to contain `(1L :: (2L :: (3L :: [])))`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll emits if expression in match arm body (Phase 7.10b)`` () =
    // Phase 7.10b: before this slice, `parseArmBody` in the bootstrap
    // fell straight through to `parseCompare`, which SKIPS the special-
    // form dispatch (`parseIf` / `parseLetIn` / `parseMatch` / `parseLam`)
    // that lives in `parseExpr`. So a match arm whose body starts with
    // `if ...` hit `parseCompare` → `parseAddSub` → ... → `parseAtom`,
    // which has no `TKwIf` arm, fell into the wildcard `(EInt 0, toks)`,
    // and the rest of the tokens desynced — the body became literal
    // `0` and every subsequent decl silently dropped out.
    //
    // The surgical fix dispatches `TKwIf` / `TKwLet` at the top of
    // `parseArmBody` (the two cases needed by the bootstrap's own
    // source) while preserving `parseCompare`-level parsing for all
    // other forms so a nested `match` inside an arm body can still not
    // accidentally grab a sibling `| ...` arm.
    //
    // Fixture `20s-bootstrap-input-arm-if.lll` has a match whose first
    // arm body is `if 1 == 1 then 10 else 20`. Pre-fix, `describe` got
    // an `EInt 0` body. Post-fix, the emitted F# contains the proper
    // `if ... then 10L else 20L` form.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let armIfPath =
        Path.Combine(repoRoot, "spec/examples/valid/20s-bootstrap-input-arm-if.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists armIfPath, $"missing fixture: {armIfPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(armIfPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002; combined:\n{combined}")
        Assert.False(
            combined.Contains "E001",
            $"expected NO E001; combined:\n{combined}")
        Assert.False(
            combined.Contains "error",
            $"expected NO `error`; combined:\n{combined}")
        Assert.True(
            stdout.Contains "describe",
            $"expected emitted F# to contain `describe`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "if ",
            $"expected emitted F# to contain `if `; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "10L",
            $"expected emitted F# to contain `10L`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "20L",
            $"expected emitted F# to contain `20L`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll resolves strToInt via stdlibNames (Phase 7.10c)`` () =
    // Phase 7.10c: the bootstrap's `stdlibNames` mirror list omitted
    // `strToInt` — the host elaborator knows it as a builtin, and the
    // bootstrap's own source already uses it inside `lexNum`, but
    // running the bootstrap on any user program that calls `strToInt`
    // surfaced `E002 UnboundVar strToInt`. Same shape as the 7.9p/7.9r
    // slices (`charToInt` / `charIsDigit`): add the bare name to the
    // flat name list so the minimal HM pass accepts it.
    //
    // Fixture `20t-bootstrap-input-strtoint.lll` calls `strToInt` from
    // a user fn body. Pre-fix, the elaborator emits
    // `E002 UnboundVar strToInt`; post-fix, the elaborator accepts the
    // name and codegen emits a `let useIt` plus the `[<EntryPoint>]`
    // main wrapper.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let strToIntPath =
        Path.Combine(repoRoot, "spec/examples/valid/20t-bootstrap-input-strtoint.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists strToIntPath, $"missing fixture: {strToIntPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(strToIntPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002 UnboundVar strToInt",
            $"expected NO E002 UnboundVar strToInt; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let rec useIt" || stdout.Contains "let useIt",
            $"expected emitted F# to contain `let useIt` or `let rec useIt`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll supports string literal patterns (Phase 7.10d)`` () =
    // Phase 7.10d: the bootstrap's `Pat` type and `parsePrimaryPat` /
    // `showPat` / `patBinders` / `patIsCatchAll` / `emitPat` cascade
    // did not handle `PStr Str` — a string literal as a match pattern.
    // Arms like `| "module" -> TKwModule` fell through to `PWild` in
    // `parsePrimaryPat` (the catch-all `| _ -> (PWild, toks)`) which
    // collapsed later arms into a single wildcard. This blocks the
    // fixpoint probe at `classifyIdent` in 20-bootstrap-compiler.lll.
    //
    // Fixture `20u-bootstrap-input-str-pat.lll` matches a `Str` scrut
    // against `"one"` / `"two"` / `_`. Pre-fix, the bootstrap drops
    // both literal arms; post-fix, `| "one"` and `| "two"` appear
    // verbatim in the emitted F#.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let strPatPath =
        Path.Combine(repoRoot, "spec/examples/valid/20u-bootstrap-input-str-pat.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists strPatPath, $"missing fixture: {strPatPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(strPatPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002; combined:\n{combined}")
        Assert.False(
            combined.Contains "E001",
            $"expected NO E001; combined:\n{combined}")
        Assert.False(
            combined.Contains "error",
            $"expected NO `error`; combined:\n{combined}")
        Assert.True(
            stdout.Contains "| \"one\"",
            $"expected emitted F# to contain `| \"one\"`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "| \"two\"",
            $"expected emitted F# to contain `| \"two\"`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll resolves strChars/strFromChars/intToStr/charIsSpace/listReverse via stdlibNames (Phase 7.10e)`` () =
    // Phase 7.10e: a small stdlib audit. The bootstrap's `stdlibNames`
    // mirror list was missing five host builtins that the bootstrap
    // itself calls from its lexer and emitter — `strChars` (tokenize,
    // emitStrBody), `strFromChars` (lexId / lexStr / lexCharLit /
    // emitChar / emitStrBody), `intToStr` (emitPat / emitExpr /
    // emitInt), `charIsSpace` (lexChars whitespace skip), and
    // `listReverse` (emitFlushPendingNonEmpty). Running the bootstrap
    // on any user program that calls them surfaced the first one as
    // `E002 UnboundVar`. Same shape as the 7.9p/7.9r/7.10c slices: add
    // the bare names to the flat name list so the minimal HM pass
    // accepts them.
    //
    // Fixture `20v-bootstrap-input-strchars.lll` exercises all five
    // builtins from user fn bodies. Pre-fix, the elaborator emits
    // `E002 UnboundVar strChars` (and stops on the first one reached);
    // post-fix, all five resolve and codegen emits the user fns plus
    // the `[<EntryPoint>]` main wrapper.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let strCharsPath =
        Path.Combine(repoRoot, "spec/examples/valid/20v-bootstrap-input-strchars.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists strCharsPath, $"missing fixture: {strCharsPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(strCharsPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002 UnboundVar strChars",
            $"expected NO E002 UnboundVar strChars; combined:\n{combined}")
        Assert.False(
            combined.Contains "E002 UnboundVar strFromChars",
            $"expected NO E002 UnboundVar strFromChars; combined:\n{combined}")
        Assert.False(
            combined.Contains "E002 UnboundVar intToStr",
            $"expected NO E002 UnboundVar intToStr; combined:\n{combined}")
        Assert.False(
            combined.Contains "E002 UnboundVar charIsSpace",
            $"expected NO E002 UnboundVar charIsSpace; combined:\n{combined}")
        Assert.False(
            combined.Contains "E002 UnboundVar listReverse",
            $"expected NO E002 UnboundVar listReverse; combined:\n{combined}")
        Assert.True(
            stdout.Contains "rebuilt s =",
            $"expected emitted F# to contain `rebuilt s =`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "strChars",
            $"expected emitted F# to reference `strChars`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "strFromChars",
            $"expected emitted F# to reference `strFromChars`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "listReverse",
            $"expected emitted F# to reference `listReverse`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "intToStr",
            $"expected emitted F# to reference `intToStr`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "charIsSpace",
            $"expected emitted F# to reference `charIsSpace`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll desugars clause-sugar fn bodies (Phase 7.10g)`` () =
    // Phase 7.10g: the bootstrap's `parseFnDecl` called `parseExpr`
    // directly on the fn body, and `parseExpr` had no `TBar :: _`
    // dispatch — so any fn whose body used clause-sugar
    //   fn f(x T) U =
    //     | Pat1 -> body1
    //     | Pat2 -> body2
    // fell through to `parseCompare -> ... -> parseAtom`, saw the
    // leading `TBar` as an unknown atom, and returned `(EInt 0, toks)`
    // with zero tokens consumed. Every clause-sugar fn in the
    // bootstrap's own source silently became `fn f args = 0`. The
    // 7.10f fixpoint probe surfaced this as `E002 UnboundVar lexChars`
    // x2 because the two surviving references live in explicit-match
    // fn bodies; every other caller of `lexChars` had its body rewritten
    // to `EInt 0` and the reference vanished along with it.
    //
    // Fix: a new `parseFnBody` helper called between `parseFnDecl` and
    // `parseExpr`. If the token stream (after `skipNewlines`) starts
    // with `TBar`, it calls `parseArms` and constructs `EMatch
    // lastParamVar pats bodies` — mirroring the host compiler's Phase
    // 4 elaborator clause-sugar desugaring (which scrutinises the
    // LAST curried param). Otherwise falls through to `parseExpr`.
    //
    // Fixture `20w-bootstrap-input-clause.lll` exercises
    //   fn first(xs List[Int]) Int =
    //     | x :: _ -> x
    //     | _ -> 0
    // Pre-fix, the bootstrap emits `let first xs = 0L` (clause sugar
    // dropped). Post-fix, it emits a real `match xs with ...`.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let clausePath =
        Path.Combine(repoRoot, "spec/examples/valid/20w-bootstrap-input-clause.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,  $"missing fixture: {inputPath}")
    Assert.True(File.Exists clausePath, $"missing fixture: {clausePath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(clausePath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002; combined:\n{combined}")
        Assert.False(
            combined.Contains "E001",
            $"expected NO E001; combined:\n{combined}")
        Assert.False(
            combined.Contains "error",
            $"expected NO `error`; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let first" || stdout.Contains "let rec first",
            $"expected emitted F# to contain `let first` or `let rec first`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "match",
            $"expected emitted F# to contain `match` (proving desugaring emitted a match expr); stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll emits tuple literal expressions (Phase 7.10q)`` () =
    // Phase 7.10q: before this slice, the bootstrap compiler had no
    // `TComma` token, no `ETuple2` AST node, and `parseAtom` only handled
    // `(e)` paren-grouping (not `(e1, e2)` tuples). Every tuple site in
    // the bootstrap's own source — the `(value, leftover)` pairs returned
    // by every parseX helper — was therefore malformed: `,` was silently
    // dropped by the lexer, so `(a, b)` tokenised as `TLParen a b TRParen`
    // and `parseAtom` just returned `a` with `b TRParen` still on the
    // stream.
    //
    // The fix has four sites:
    //   1. Add `TComma` to the `Token` ADT.
    //   2. Emit `TComma` in `lexChars` for `,`.
    //   3. Add `ETuple2 Expr Expr` to the `Expr` ADT.
    //   4. In `parseAtom`, after parsing the first sub-expression inside
    //      `(`, call `parseAtomParenTail` which checks for a `TComma`:
    //      if present, parse the second element and return `ETuple2`;
    //      otherwise return the plain paren-grouped expression.
    //   5. Update `parseLetIn`'s tuple-destructuring arm to use
    //      `TComma` between the two var names.
    //   6. Thread `ETuple2` through `showExpr` / `checkExpr` /
    //      `inferExprType` / `typeCheck` / `emitExpr`.
    //
    // Fixture `20x-bootstrap-input-tuple.lll` defines `fn pair() = (1, 2)`
    // and calls it from `main`. Pre-fix, the `,` was dropped so the
    // bootstrap parsed `(1 2)` as application `(EApp (EInt 1) (EInt 2))`
    // and emitted `(1L 2L)` — malformed F#. Post-fix, the emitted F#
    // contains `(1L, 2L)`.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let tuplePath =
        Path.Combine(repoRoot, "spec/examples/valid/20x-bootstrap-input-tuple.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,  $"missing fixture: {inputPath}")
    Assert.True(File.Exists tuplePath,  $"missing fixture: {tuplePath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(tuplePath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002; combined:\n{combined}")
        Assert.False(
            combined.Contains "E001",
            $"expected NO E001; combined:\n{combined}")
        Assert.False(
            combined.Contains "error",
            $"expected NO `error`; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let pair" || stdout.Contains "let rec pair",
            $"expected emitted F# to contain `let pair` or `let rec pair`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "(1L, 2L)",
            $"expected emitted F# to contain `(1L, 2L)` (tuple literal); stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

// Phase 7.10r historical blocker tests.
// These used to be planned/blocked checks and are now active regressions
// guarding fixpoint parity between compiler_1.fs (host output) and
// compiler_2.fs (bootstrap output).

[<Fact>]
let ``20y-bootstrap-input-prelude.lll: bootstrap prelude contains listFold and listReverse (Phase 7.10r blocker 1)`` () =
    // Blocker 1: Prelude parity.
    // The bootstrap emits only 6 stdlib bindings in its prelude
    // (print, printfn, readFile, strConcat, strLen, listAppend at
    // minimum) but the bootstrap's own compiled output (compiler_2.fs)
    // calls listFold, listReverse, listIsEmpty, strChars, strFromChars,
    // intToStr, charToInt, charIsDigit, charIsSpace — none of which
    // appear in the emitted prelude. When compiler_2.fs is then compiled
    // by F# and run, it fails with UnboundVar-style errors.
    //
    // Desired behaviour: the bootstrap's `emitPrelude` function emits ALL
    // stdlib builtins that appear in `stdlibNames` so that any program
    // the bootstrap compiles is self-contained.
    //
    // This test runs the bootstrap on `20y-bootstrap-input-prelude.lll`
    // (a module that calls `listFold` and `listReverse`) and asserts that
    // the emitted F# prelude contains definitions for both functions.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let preludePath =
        Path.Combine(repoRoot, "spec/examples/valid/20y-bootstrap-input-prelude.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,   $"missing fixture: {inputPath}")
    Assert.True(File.Exists preludePath, $"missing fixture: {preludePath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(preludePath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002 UnboundVar; combined:\n{combined}")
        // The emitted prelude must contain listFold and listReverse so
        // that a downstream F# compiler can compile the output without
        // missing-binding errors.
        Assert.True(
            stdout.Contains "listFold",
            $"expected emitted prelude to define `listFold`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "listReverse",
            $"expected emitted prelude to define `listReverse`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20y-bootstrap-input-mutrec.lll: mutually recursive fns across type boundary produce correct let-rec grouping (Phase 7.10r blocker 2)`` () =
    // Blocker 2: Binding count parity.
    // compiler_1.fs (F# host compiling the bootstrap) emits 275 bindings;
    // compiler_2.fs (bootstrap compiling itself) emits only 238 — a
    // difference of 37. The suspected cause is that the bootstrap's
    // `emitStep` resets the pending-fn accumulator whenever it sees a
    // `DType` decl, while the F# host compiler's grouping logic does
    // not. This causes the bootstrap to split `let rec ... and ...`
    // blocks that the host compiler keeps joined, producing more
    // singleton `let` bindings and a different total count.
    //
    // Desired behaviour: a run of fn decls interrupted only by a type
    // decl should still be emitted as a single `let rec ... and ...`
    // group (or at minimum produce the same binding count as the host).
    //
    // This test runs the bootstrap on `20y-bootstrap-input-mutrec.lll`
    // (three mutually recursive fns) and asserts the emitted F# wraps
    // all three in one `let rec / and / and` block.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let mutrecPath =
        Path.Combine(repoRoot, "spec/examples/valid/20y-bootstrap-input-mutrec.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,  $"missing fixture: {inputPath}")
    Assert.True(File.Exists mutrecPath, $"missing fixture: {mutrecPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(mutrecPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002 UnboundVar; combined:\n{combined}")
        // All three non-main fns must appear in a single let-rec block:
        // `let rec isEven ... \nand isOdd ... \nand parity ...`
        Assert.True(
            stdout.Contains "let rec isEven",
            $"expected emitted F# to contain `let rec isEven`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "and isOdd",
            $"expected emitted F# to contain `and isOdd` (same rec group); stdout:\n{combined}")
        Assert.True(
            stdout.Contains "and parity",
            $"expected emitted F# to contain `and parity` (same rec group); stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20y-bootstrap-input-fmt.lll: each fn in a multi-fn module is emitted on its own separate line (Phase 7.10r blocker 3)`` () =
    // Blocker 3: Format parity.
    // For byte-identical fixpoint, compiler_1.fs and compiler_2.fs must
    // produce the same whitespace layout. The F# host compiler's
    // `emitGroupedDecls` emits a blank line between each top-level block
    // AND emits each `and` clause on its own line with consistent
    // indentation. The bootstrap's current emission concatenates
    // everything inline (each fn body on a single line, minimal spacing).
    //
    // Desired behaviour: the bootstrap emits each fn declaration on its
    // own line, separated from the next by a blank line — matching the
    // host's output format.
    //
    // This test runs the bootstrap on `20y-bootstrap-input-fmt.lll`
    // (four non-main fns + main) and asserts:
    //   1. `inc`, `dec`, `double` appear in the output on separate lines
    //      (i.e. the output contains at least two newlines between them)
    //   2. The `and` keyword for each continuation fn starts at the
    //      beginning of its own line (`\nand `)
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let fmtPath =
        Path.Combine(repoRoot, "spec/examples/valid/20y-bootstrap-input-fmt.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath, $"missing fixture: {inputPath}")
    Assert.True(File.Exists fmtPath,   $"missing fixture: {fmtPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(fmtPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002 UnboundVar; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let rec inc",
            $"expected emitted F# to contain `let rec inc`; stdout:\n{combined}")
        // Each `and` clause must start on its own line so the output
        // is multi-line (matches the host compiler's format).
        Assert.True(
            stdout.Contains "\nand dec",
            $"expected `and dec` to start on its own line (`\\nand dec`); stdout:\n{combined}")
        Assert.True(
            stdout.Contains "\nand double",
            $"expected `and double` to start on its own line (`\\nand double`); stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll emits TError for unknown chars instead of silently dropping (Bug #7)`` () =
    // Bug #7 fix: the bootstrap lexer's main loop (`lexChars`) previously
    // fell through to `else lexChars rest` for unrecognised characters,
    // silently dropping them. The fix adds a `TError Char` variant to the
    // Token type and changes the catch-all to emit `TError c` so that
    // unknown chars are visible to the parser (which treats them as
    // non-atom-starters via the `| _ -> false` arm of `isAtomStart`).
    //
    // This test confirms that a program containing `@` inside a string
    // literal (handled by `lexStr`, not by the `lexChars` catch-all)
    // still compiles without errors. The `@` in a string is safe because
    // `lexStr` collects all chars until the closing `"`, so it never
    // reaches the `lexChars` unknown-char arm.
    //
    // Fixture: `20z-bootstrap-input-unknown-char.lll`.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let unknownPath =
        Path.Combine(repoRoot, "spec/examples/valid/20z-bootstrap-input-unknown-char.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,    $"missing fixture: {inputPath}")
    Assert.True(File.Exists unknownPath,  $"missing fixture: {unknownPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(unknownPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002 UnboundVar; combined:\n{combined}")
        Assert.False(
            combined.Contains "E001",
            $"expected NO E001 TypeMismatch; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let rec tagged" || stdout.Contains "let tagged",
            $"expected emitted F# to contain `let tagged` or `let rec tagged`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "\"@symbol\"",
            $"expected emitted F# to contain `\"@symbol\"`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)

[<Fact>]
let ``20-bootstrap-compiler.lll lexCharLit and lexCharEsc return TEnd at EOF instead of empty list (Bug #14)`` () =
    // Bug #14 fix: `lexCharLit` and `lexCharEsc` previously returned `[]`
    // (empty token list) when they hit EOF before seeing a closing `'`.
    // Returning `[]` is subtly wrong: the parser interprets an empty list
    // as "no more tokens" without a `TEnd` sentinel, which can cause
    // mid-parse confusion because the recursive descent expects the stream
    // to terminate with `TEnd`. The fix changes both `| _ -> []` arms to
    // `| _ -> [TEnd]` so the parser always sees a clean terminator.
    //
    // This test compiles a program that uses `'\n'` (a valid escape), and
    // verifies the bootstrap accepts it without errors. The `lexCharEsc`
    // EOF path is exercised implicitly: the bootstrap's own source
    // contains `'\n'` on many lines, and a clean compile confirms
    // `lexCharEsc` works end-to-end. Prior to the Bug #14 fix, `lexCharEsc`
    // returning `[]` on an empty escape list would have caused the parse to
    // treat the remaining input as if it started fresh with no prior TEnd.
    //
    // Fixture: `20z-bootstrap-input-unterminated-char.lll`.
    let inputPath =
        Path.Combine(repoRoot, "spec/examples/valid/20a-bootstrap-input.lll")
    let unterminatedPath =
        Path.Combine(repoRoot, "spec/examples/valid/20z-bootstrap-input-unterminated-char.lll")
    let backupPath = inputPath + ".bak"
    Assert.True(File.Exists inputPath,        $"missing fixture: {inputPath}")
    Assert.True(File.Exists unterminatedPath, $"missing fixture: {unterminatedPath}")
    File.Move(inputPath, backupPath, true)
    File.Copy(unterminatedPath, inputPath)
    try
        let (_, stdout, stderr) = runBootstrap ()
        let combined = stdout + stderr
        Assert.False(
            combined.Contains "E002",
            $"expected NO E002 UnboundVar; combined:\n{combined}")
        Assert.False(
            combined.Contains "E001",
            $"expected NO E001 TypeMismatch; combined:\n{combined}")
        Assert.True(
            stdout.Contains "let rec check" || stdout.Contains "let check",
            $"expected emitted F# to contain `let check` or `let rec check`; stdout:\n{combined}")
        Assert.True(
            stdout.Contains "[<EntryPoint>]",
            $"expected emitted F# to contain `[<EntryPoint>]`; stdout:\n{combined}")
    finally
        if File.Exists inputPath then File.Delete inputPath
        if File.Exists backupPath then
            File.Move(backupPath, inputPath, true)
