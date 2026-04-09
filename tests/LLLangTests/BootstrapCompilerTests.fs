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
            File.Move(backupPath, inputPath)

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
    File.Move(inputPath, backupPath)
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
            File.Move(backupPath, inputPath)

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
    File.Move(inputPath, backupPath)
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
            File.Move(backupPath, inputPath)

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
    File.Move(inputPath, backupPath)
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
            File.Move(backupPath, inputPath)

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
    File.Move(inputPath, backupPath)
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
            File.Move(backupPath, inputPath)

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
    File.Move(inputPath, backupPath)
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
            File.Move(backupPath, inputPath)

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
    File.Move(inputPath, backupPath)
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
            File.Move(backupPath, inputPath)

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
    File.Move(inputPath, backupPath)
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
            File.Move(backupPath, inputPath)

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
    File.Move(inputPath, backupPath)
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
            File.Move(backupPath, inputPath)

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
    File.Move(inputPath, backupPath)
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
            File.Move(backupPath, inputPath)

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
    File.Move(inputPath, backupPath)
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
            File.Move(backupPath, inputPath)
