module LLLang.Tests.CodegenTests

open System.IO
open Xunit
open LLLang.Codegen
open LLLang.TypedAST

// ---------- helpers ----------

/// Full pipeline: ll-lang source → emitted F# string. Fails test on any error.
let private codegenSrc (src: string) : string =
    match LLLang.Compiler.compile src with
    | Ok fs -> fs
    | Error es -> failwith $"codegen failed: {es}"

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

// ---------- scaffold ----------

[<Fact>]
let ``emit produces non-empty string for trivial module`` () =
    Assert.True(true)

// ---------- Task 2: type declarations ----------

[<Fact>]
let ``TDType sum type emits DU header`` () =
    let src = "module M\nShape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("type Shape =", codegenSrc src)

[<Fact>]
let ``TDType sum type emits Circle branch with float`` () =
    let src = "module M\nShape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("| Circle of float", codegenSrc src)

[<Fact>]
let ``TDType sum type emits multi-arg branch`` () =
    let src = "module M\nShape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("| Rect of float * float", codegenSrc src)

[<Fact>]
let ``TDType sum type emits zero-arg branch without of`` () =
    let src = "module M\nShape = Circle Float | Rect Float Float | Empty"
    let fs = codegenSrc src
    Assert.Contains("| Empty", fs)
    Assert.DoesNotContain("| Empty of", fs)

[<Fact>]
let ``TDType parametric sum type emits type param`` () =
    let src = "module M\nMaybe A = Some A | None"
    Assert.Contains("type Maybe<'A>", codegenSrc src)

[<Fact>]
let ``TDType record type emits record syntax`` () =
    let src = "module M\nPoint = x Float, y Float"
    let fs = codegenSrc src
    Assert.Contains("type Point = {", fs)
    Assert.Contains("x: float", fs)
    Assert.Contains("y: float", fs)

[<Fact>]
let ``TDTag emits nothing`` () =
    let src = "module M\ntag Meter"
    Assert.DoesNotContain("Meter", codegenSrc src)

// ---------- Task 3: expression emission ----------

[<Fact>]
let ``TELit int emits int64 literal`` () =
    Assert.Contains("42L", codegenSrc "module M\nlet x = 42")

[<Fact>]
let ``TELit float emits float literal`` () =
    Assert.Contains("3.14", codegenSrc "module M\nlet x = 3.14")

[<Fact>]
let ``TELit string emits quoted string`` () =
    Assert.Contains("\"hi\"", codegenSrc "module M\nlet x = \"hi\"")

[<Fact>]
let ``TELit bool true emits true`` () =
    Assert.Contains("true", codegenSrc "module M\nlet x = true")

[<Fact>]
let ``TELit char emits F# char literal`` () =
    Assert.Contains("'a'", codegenSrc "module M\nlet c = 'a'")

[<Fact>]
let ``TELit char newline escape`` () =
    Assert.Contains("'\\n'", codegenSrc "module M\nlet c = '\\n'")

[<Fact>]
let ``TELit char backslash escape`` () =
    Assert.Contains("'\\\\'", codegenSrc "module M\nlet c = '\\\\'")

[<Fact>]
let ``TELit char single quote escape`` () =
    Assert.Contains("'\\''", codegenSrc "module M\nlet c = '\\''")

[<Fact>]
let ``TEApp binary add emits infix`` () =
    let fs = codegenSrc "module M\nadd(a Int)(b Int) Int = a + b"
    Assert.Contains("let add a b =", fs)
    Assert.Contains("(a + b)", fs)

[<Fact>]
let ``TEApp binary equality emits F# = operator`` () =
    Assert.Contains("(a = b)", codegenSrc "module M\neq(a Int)(b Int) Bool = a == b")

[<Fact>]
let ``TEApp binary inequality emits F# <> operator`` () =
    Assert.Contains("(a <> b)", codegenSrc "module M\nneq(a Int)(b Int) Bool = a != b")

[<Fact>]
let ``TELam emits fun syntax`` () =
    Assert.Contains("(fun x -> x)", codegenSrc "module M\nlet f = \\x. x")

[<Fact>]
let ``TEIf emits if-then-else`` () =
    let fs = codegenSrc "module M\nabs(x Int) =\n  if x < 0\n    0\n  else x"
    Assert.Contains("then 0L", fs)
    Assert.Contains("else x", fs)

[<Fact>]
let ``TDFn with no params emits let without parens`` () =
    Assert.Contains("let greeting =", codegenSrc "module M\ngreeting = \"hello\"")

[<Fact>]
let ``TDLet emits let binding`` () =
    let fs = codegenSrc "module M\nlet pi = 3.14159"
    Assert.Contains("let pi =", fs)
    Assert.Contains("3.14159", fs)

// ---------- Task 4: match and patterns ----------

[<Fact>]
let ``TEMatch emits match with`` () =
    let src = "module M\nShape = Circle Float | Empty\narea(s Shape) =\n  | Circle r -> r\n  | Empty -> 0.0"
    let fs = codegenSrc src
    Assert.Contains("match s with", fs)

[<Fact>]
let ``TEMatch emits branch arms`` () =
    let src = "module M\nShape = Circle Float | Empty\narea(s Shape) =\n  | Circle r -> r\n  | Empty -> 0.0"
    let fs = codegenSrc src
    Assert.Contains("| Circle(r) ->", fs)
    Assert.Contains("| Empty ->", fs)

[<Fact>]
let ``PWild pattern emits underscore`` () =
    let src = "module M\nColor = Red Int | Blue\nf(x Color) =\n  | Red _ -> 1\n  | Blue -> 2"
    Assert.Contains("| Red(_) ->", codegenSrc src)

[<Fact>]
let ``PCon single-arg pattern emits parenthesized variable`` () =
    let src = "module M\nMaybe A = Some A | None\nunwrap(m Maybe[Int]) =\n  | Some x -> x\n  | None -> 0"
    Assert.Contains("| Some(x) ->", codegenSrc src)

[<Fact>]
let ``PTuple pattern emits F# tuple pattern`` () =
    // Requires untyped-param support for `fn fst(p)` so inference can
    // discover the tuple shape from the match pattern.
    let src = "module M\nfst(p) =\n  | (a, b) -> a"
    let fs = codegenSrc src
    Assert.Contains("| (a, b) ->", fs)

[<Fact>]
let ``PTuple pattern with wildcard emits (a, _)`` () =
    let src = "module M\nfst(p) =\n  | (a, _) -> a"
    let fs = codegenSrc src
    Assert.Contains("| (a, _) ->", fs)

// --- Phase 7.1.5: cons patterns + cons expressions + match-as-expression ---

[<Fact>]
let ``PCons pattern emits F# (h :: t)`` () =
    let src = "module M\nfirst(xs) =\n  | h :: t -> h"
    let fs = codegenSrc src
    Assert.Contains("(h :: t)", fs)

[<Fact>]
let ``ECons expression emits F# (h :: t)`` () =
    let src = "module M\nlet xs = 1 :: [2 3]"
    let fs = codegenSrc src
    Assert.Contains("(1L :: ", fs)

[<Fact>]
let ``match-as-expression emits F# match-with`` () =
    let src =
        "module M\n" +
        "label(n Int) Str =\n" +
        "  match n | 0 -> \"zero\" | _ -> \"other\""
    let fs = codegenSrc src
    Assert.Contains("match n with", fs)
    Assert.Contains("| 0L ->", fs)
    Assert.Contains("| _ ->", fs)
    Assert.Contains("\"zero\"", fs)
    Assert.Contains("\"other\"", fs)

// ---------- Task 5: top-level module emission ----------

[<Fact>]
let ``module header is emitted`` () =
    Assert.Contains("module Examples.Basics", codegenSrc "module Examples.Basics\nlet x = 1")

[<Fact>]
let ``multiple declarations emitted in order`` () =
    let src = "module M\nlet a = 1\nlet b = 2\nlet c = 3"
    let fs = codegenSrc src
    Assert.True(fs.IndexOf("let a =") < fs.IndexOf("let b ="))
    Assert.True(fs.IndexOf("let b =") < fs.IndexOf("let c ="))

[<Fact>]
let ``fn main gets EntryPoint attribute`` () =
    let src = "module M\nmain() = 0"
    let fs = codegenSrc src
    Assert.Contains("[<EntryPoint>]", fs)
    Assert.Contains("let main (argv: string[]) =", fs)

[<Fact>]
let ``non-main fn does not get EntryPoint`` () =
    Assert.DoesNotContain("[<EntryPoint>]", codegenSrc "module M\nadd(a Int)(b Int) Int = a + b")

// ---------- Task 6: compiler pipeline ----------

[<Fact>]
let ``compile returns Ok for valid source`` () =
    match LLLang.Compiler.compile "module M\nlet x = 42" with
    | Ok fs -> Assert.NotEmpty(fs)
    | Error es -> Assert.Fail($"Expected Ok but got Error: {es}")

[<Fact>]
let ``compile returns Error for invalid source`` () =
    match LLLang.Compiler.compile "module M\nlet x = undefinedVariable" with
    | Ok _ -> Assert.Fail("Expected Error but got Ok")
    | Error es -> Assert.NotEmpty(es)

[<Fact>]
let ``compile produces module header in output`` () =
    match LLLang.Compiler.compile "module Examples.Hello\nlet x = 1" with
    | Ok fs -> Assert.Contains("module Examples.Hello", fs)
    | Error es -> Assert.Fail($"Expected Ok but got Error: {es}")

[<Fact>]
let ``compile hello.lll returns Ok`` () =
    let src = readValid "hello.lll"
    match LLLang.Compiler.compile src with
    | Ok fs ->
        Assert.Contains("module Examples.Hello", fs)
        Assert.Contains("[<EntryPoint>]", fs)
        Assert.Contains("let main", fs)
    | Error es -> Assert.Fail($"Expected Ok but got Error: {es}")

[<Fact>]
let ``compile hello.lll output contains printfn call`` () =
    let src = readValid "hello.lll"
    match LLLang.Compiler.compile src with
    | Ok fs -> Assert.Contains("printfn", fs)
    | Error es -> Assert.Fail($"Expected Ok but got Error: {es}")

// ---------- Task 7: CLI tool ----------

[<Fact>]
let ``lllc build writes .fs file next to source`` () =
    // Arrange: write a temp .lll file
    let tmpDir = System.IO.Path.GetTempPath()
    let lllPath = System.IO.Path.Combine(tmpDir, "test_cli.lll")
    let fsPath  = System.IO.Path.Combine(tmpDir, "test_cli.fs")
    System.IO.File.WriteAllText(lllPath, "module Tmp.Test\nlet x = 99")
    if System.IO.File.Exists(fsPath) then System.IO.File.Delete(fsPath)

    // Act: run lllc build via dotnet against the built DLL
    let llcDll =
        System.IO.Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../src/LLLangTool/bin/Debug/net10.0/lllc.dll")
    let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"\"{llcDll}\" build \"{lllPath}\"")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError  <- true
    psi.UseShellExecute        <- false
    use proc = System.Diagnostics.Process.Start(psi)
    proc.WaitForExit()

    // Assert
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    Assert.Equal(0, proc.ExitCode)
    Assert.True(System.IO.File.Exists(fsPath), $"Expected {fsPath} to exist. stdout={stdout} stderr={stderr}")
    let content = System.IO.File.ReadAllText(fsPath)
    Assert.Contains("module Tmp.Test", content)

// ---------- Task 8: corpus round-trip ----------

[<Fact>]
let ``compile 01-basics.lll returns Ok`` () =
    let src = readValid "01-basics.lll"
    match LLLang.Compiler.compile src with
    | Ok fs -> Assert.NotEmpty(fs)
    | Error es -> Assert.Fail($"01-basics.lll failed: {es}")

[<Fact>]
let ``compile 01-basics.lll output contains module header`` () =
    let src = readValid "01-basics.lll"
    match LLLang.Compiler.compile src with
    | Ok fs -> Assert.Contains("module Examples.Basics", fs)
    | Error es -> Assert.Fail($"01-basics.lll failed: {es}")

[<Fact>]
let ``compile 01-basics.lll output contains let pi`` () =
    let src = readValid "01-basics.lll"
    match LLLang.Compiler.compile src with
    | Ok fs -> Assert.Contains("let pi =", fs)
    | Error es -> Assert.Fail($"01-basics.lll failed: {es}")

[<Fact>]
let ``compile 01-basics.lll output contains let add`` () =
    let src = readValid "01-basics.lll"
    match LLLang.Compiler.compile src with
    | Ok fs -> Assert.Contains("let add ", fs)
    | Error es -> Assert.Fail($"01-basics.lll failed: {es}")

[<Fact>]
let ``compile 02-adts.lll returns Ok`` () =
    let src = readValid "02-adts.lll"
    match LLLang.Compiler.compile src with
    | Ok fs -> Assert.NotEmpty(fs)
    | Error es -> Assert.Fail($"02-adts.lll failed: {es}")

[<Fact>]
let ``compile 02-adts.lll output contains type Shape`` () =
    let src = readValid "02-adts.lll"
    match LLLang.Compiler.compile src with
    | Ok fs -> Assert.Contains("type Shape =", fs)
    | Error es -> Assert.Fail($"02-adts.lll failed: {es}")

[<Fact>]
let ``compile 02-adts.lll output contains type Point record`` () =
    let src = readValid "02-adts.lll"
    match LLLang.Compiler.compile src with
    | Ok fs -> Assert.Contains("type Point = {", fs)
    | Error es -> Assert.Fail($"02-adts.lll failed: {es}")

[<Theory>]
[<InlineData("01-basics.lll")>]
[<InlineData("02-adts.lll")>]
[<InlineData("hello.lll")>]
let ``all valid corpus files produce non-empty output with module header`` (filename: string) =
    let src = readValid filename
    match LLLang.Compiler.compile src with
    | Ok fs ->
        Assert.True(fs.Length > 0, $"{filename}: empty output")
        Assert.Contains("module ", fs)
    | Error es -> Assert.Fail($"{filename} failed: {es}")

[<Fact>]
let ``hello world runs via lllc run and prints Hello ll-lang!`` () =
    let lllPath =
        System.IO.Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/hello.lll")
    let llcDll =
        System.IO.Path.Combine(
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
    Assert.True(stdout.Contains("Hello, ll-lang!"),
                $"Expected stdout to contain 'Hello, ll-lang!'. stdout={stdout} stderr={stderr}")

// --- Phase 7.1.5: runtime — cons via lllc run ---

/// Compile and run an inline ll-lang module via the lllc tool. Returns stdout.
let private runLLLangSrc (src: string) : string =
    let tmpDir = System.IO.Path.GetTempPath()
    let lllPath = System.IO.Path.Combine(tmpDir, $"test_{System.Guid.NewGuid()}.lll")
    System.IO.File.WriteAllText(lllPath, src)
    try
        let llcDll =
            System.IO.Path.Combine(
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
        if proc.ExitCode <> 0 then
            failwith $"lllc run failed: stderr={stderr}, stdout={stdout}"
        stdout
    finally
        try System.IO.File.Delete(lllPath) with _ -> ()

[<Fact>]
let ``runtime: cons pattern in fn match returns head`` () =
    let src =
        "module Tmp.ConsHead\n" +
        "first(xs List[Int]) Int =\n" +
        "  | h :: _ -> h\n" +
        "  | _ -> 0\n" +
        "main() = printfn (intToStr (first [1 2 3]))"
    let stdout = runLLLangSrc src
    Assert.Contains("1", stdout)

[<Fact>]
let ``runtime: cons expression builds list matched by literal`` () =
    let src =
        "module Tmp.ConsBuild\n" +
        "main() =\n" +
        "  let xs = 1 :: 2 :: 3 :: [4 5]\n" +
        "  printfn (intToStr (listLen xs))"
    let stdout = runLLLangSrc src
    Assert.Contains("5", stdout)

[<Fact>]
let ``runtime: match-as-expression in let binding`` () =
    let src =
        "module Tmp.MatchExpr\n" +
        "main() =\n" +
        "  let v = match 0 | 0 -> \"zero\" | _ -> \"other\"\n" +
        "  printfn v"
    let stdout = runLLLangSrc src
    Assert.Contains("zero", stdout)

// --- Phase 7.1.6: let pattern destructuring ---

[<Fact>]
let ``codegen: nested let-tuple in fn body emits let (a, b) =`` () =
    // Tuple values enter via fn params; emitted body should use a tuple-pat let.
    let src =
        "module M\n" +
        "addPair(p) Int =\n" +
        "  let (a, b) = p\n" +
        "  a + b"
    let fs = codegenSrc src
    Assert.Contains("let (a, b) =", fs)

[<Fact>]
let ``codegen: let wildcard emits let _ =`` () =
    let src =
        "module M\n" +
        "f() Int =\n" +
        "  let _ = 99\n" +
        "  1"
    let fs = codegenSrc src
    Assert.Contains("let _ =", fs)

[<Fact>]
let ``runtime: let wildcard destructuring discards rhs`` () =
    // ll-lang has no surface tuple literal, so the cleanest runtime
    // exercise of let-pat is the wildcard form: bind nothing, evaluate
    // and discard the RHS, then return the body. End-to-end this
    // confirms the parser, type inference, and codegen for ELetPat /
    // TELetPat all line up.
    let src =
        "module Tmp.LetWild\n" +
        "run() Int =\n" +
        "  let _ = 99\n" +
        "  7\n" +
        "main() = printfn (intToStr run)"
    let stdout = runLLLangSrc src
    Assert.Contains("7", stdout)

// --- Phase 7.2.1: surface tuple literal expressions ---

[<Fact>]
let ``codegen: tuple literal (1, 2) emits F# (1L, 2L)`` () =
    let src = "module M\nlet p = (1, 2)"
    let fs = codegenSrc src
    Assert.Contains("(1L, 2L)", fs)

[<Fact>]
let ``codegen: tuple literal three elements emits comma-separated`` () =
    let src = "module M\nlet t = (1, 2, 3)"
    let fs = codegenSrc src
    Assert.Contains("(1L, 2L, 3L)", fs)

[<Fact>]
let ``codegen: tuple literal + destructure in fn body`` () =
    let src =
        "module M\n" +
        "run() Int =\n" +
        "  let p = (1, 2)\n" +
        "  let (a, b) = p\n" +
        "  a"
    let fs = codegenSrc src
    Assert.Contains("(1L, 2L)", fs)
    Assert.Contains("let (a, b) =", fs)

[<Fact>]
let ``runtime: tuple literal + destructure end-to-end prints first elem`` () =
    let src =
        "module Tmp.TupLit\n" +
        "pair(a Int)(b Int) = (a, b)\n" +
        "fst(p) Int =\n" +
        "  let (a, b) = p\n" +
        "  a\n" +
        "main() =\n" +
        "  let p = pair 1 2\n" +
        "  printfn (intToStr (fst p))"
    let stdout = runLLLangSrc src
    Assert.Contains("1", stdout)

// --- Phase 7.2.2: atom[Tag] vs list-literal disambiguation ---
//
// Pre-fix the parser ate the `[TMinus]` after `cons TPlus` as a tag suffix
// on the constructor `TPlus`, producing `cons (ETagged TPlus "TMinus")`. The
// fix gates `ETagged` to literal atoms only, so a bracketed Con / Var / App
// becomes a fresh list-literal argument. End-to-end exercise: build, infer,
// codegen, run, observe the printed list length.

[<Fact>]
let ``runtime: cons CON [LIT] passes a single-element list as a fresh arg`` () =
    let src =
        "module Tmp.TagAmbig\n" +
        "Token = TPlus | TMinus\n" +
        "cons2(t Token)(ts List[Token]) List[Token] = t :: ts\n" +
        "main() =\n" +
        "  let xs = cons2 TPlus [TMinus]\n" +
        "  printfn (intToStr (listLen xs))"
    let stdout = runLLLangSrc src
    Assert.Contains("2", stdout)

// --- Phase 7.1.6: multi-line sum type runtime ---

[<Fact>]
let ``runtime: 10-multiline-sum.lll prints id:foo`` () =
    let lllPath =
        System.IO.Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/10-multiline-sum.lll")
    let llcDll =
        System.IO.Path.Combine(
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
    Assert.True(stdout.Contains("id:foo"),
                $"Expected stdout to contain 'id:foo'. stdout={stdout} stderr={stderr}")

// --- Phase 7.3a bugfix (bug 1): clause-sugar arm body scoping ---------

[<Fact>]
let ``Bug1 runtime: clause-sugar wildcard arm with multi-line let-in returns 30`` () =
    // End-to-end exercise of the bug 1 reproducer: a multi-line
    // `let .. in` chain inside the wildcard arm body. Before the fix
    // the bindings escaped the arm scope and the program failed to
    // compile. After the fix parseBlockExpr folds the chain into the
    // arm body and the program runs and prints 30.
    let src =
        "module Tmp.Bug1Scope\n" +
        "Tag = A | B\n" +
        "f(t Tag) Int =\n" +
        "  | A -> 1\n" +
        "  | _ ->\n" +
        "    let p = (10, 20)\n" +
        "    let (x, y) = p\n" +
        "    x + y\n" +
        "main() = printfn (intToStr (f B))"
    let stdout = runLLLangSrc src
    Assert.Contains("30", stdout)

// --- Phase 7.3a bugfix (bug 2): list-literal patterns + arm preservation ---

[<Fact>]
let ``Bug2 runtime: mixed cons / empty-list / wildcard arms all reached`` () =
    // End-to-end exercise of the bug 2 reproducer. Before the fix only
    // the first arm survived codegen (the `| []` arm crashed pattern
    // parsing and the remaining arms were silently dropped), so calling
    // the empty case hit a runtime MatchFailure. After the fix every
    // arm reaches codegen: `f [TEnd]` → 1, `f []` → 2, `f [TMore]` → 3,
    // and the total of 1 + 2 + 3 = 6 confirms all three arms run.
    let src =
        "module Tmp.Bug2Arms\n" +
        "Token = TEnd | TMore\n" +
        "f(toks List[Token]) Int =\n" +
        "  | TEnd :: _ -> 1\n" +
        "  | [] -> 2\n" +
        "  | _ -> 3\n" +
        "main() =\n" +
        "  let a = f [TEnd]\n" +
        "  let b = f []\n" +
        "  let c = f [TMore]\n" +
        "  printfn (intToStr (a + b + c))"
    let stdout = runLLLangSrc src
    Assert.Contains("6", stdout)
