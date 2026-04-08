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
    let src = "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("type Shape =", codegenSrc src)

[<Fact>]
let ``TDType sum type emits Circle branch with float`` () =
    let src = "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("| Circle of float", codegenSrc src)

[<Fact>]
let ``TDType sum type emits multi-arg branch`` () =
    let src = "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("| Rect of float * float", codegenSrc src)

[<Fact>]
let ``TDType sum type emits zero-arg branch without of`` () =
    let src = "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    let fs = codegenSrc src
    Assert.Contains("| Empty", fs)
    Assert.DoesNotContain("| Empty of", fs)

[<Fact>]
let ``TDType parametric sum type emits type param`` () =
    let src = "module M\ntype Maybe A = Some A | None"
    Assert.Contains("type Maybe<'A>", codegenSrc src)

[<Fact>]
let ``TDType record type emits record syntax`` () =
    let src = "module M\ntype Point = x Float, y Float"
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
let ``TEApp binary add emits infix`` () =
    let fs = codegenSrc "module M\nfn add(a Int)(b Int) Int = a + b"
    Assert.Contains("let add a b =", fs)
    Assert.Contains("(a + b)", fs)

[<Fact>]
let ``TEApp binary equality emits F# = operator`` () =
    Assert.Contains("(a = b)", codegenSrc "module M\nfn eq(a Int)(b Int) Bool = a == b")

[<Fact>]
let ``TEApp binary inequality emits F# <> operator`` () =
    Assert.Contains("(a <> b)", codegenSrc "module M\nfn neq(a Int)(b Int) Bool = a != b")

[<Fact>]
let ``TELam emits fun syntax`` () =
    Assert.Contains("(fun x -> x)", codegenSrc "module M\nlet f = \\x. x")

[<Fact>]
let ``TEIf emits if-then-else`` () =
    let fs = codegenSrc "module M\nfn abs(x Int) = if x < 0 then 0 else x"
    Assert.Contains("then 0L", fs)
    Assert.Contains("else x", fs)

[<Fact>]
let ``TDFn with no params emits let without parens`` () =
    Assert.Contains("let greeting =", codegenSrc "module M\nfn greeting = \"hello\"")

[<Fact>]
let ``TDLet emits let binding`` () =
    let fs = codegenSrc "module M\nlet pi = 3.14159"
    Assert.Contains("let pi =", fs)
    Assert.Contains("3.14159", fs)

// ---------- Task 4: match and patterns ----------

[<Fact>]
let ``TEMatch emits match with`` () =
    let src = "module M\ntype Shape = Circle Float | Empty\nfn area(s Shape) =\n  | Circle r -> r\n  | Empty -> 0.0"
    let fs = codegenSrc src
    Assert.Contains("match s with", fs)

[<Fact>]
let ``TEMatch emits branch arms`` () =
    let src = "module M\ntype Shape = Circle Float | Empty\nfn area(s Shape) =\n  | Circle r -> r\n  | Empty -> 0.0"
    let fs = codegenSrc src
    Assert.Contains("| Circle r ->", fs)
    Assert.Contains("| Empty ->", fs)

[<Fact>]
let ``PWild pattern emits underscore`` () =
    let src = "module M\ntype Color = Red Int | Blue\nfn f(x Color) =\n  | Red _ -> 1\n  | Blue -> 2"
    Assert.Contains("| Red _ ->", codegenSrc src)

[<Fact>]
let ``PCon single-arg pattern emits bare variable`` () =
    let src = "module M\ntype Maybe A = Some A | None\nfn unwrap(m Maybe[Int]) =\n  | Some x -> x\n  | None -> 0"
    Assert.Contains("| Some x ->", codegenSrc src)

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
    let src = "module M\nfn main() = 0"
    let fs = codegenSrc src
    Assert.Contains("[<EntryPoint>]", fs)
    Assert.Contains("let main (argv: string[]) =", fs)

[<Fact>]
let ``non-main fn does not get EntryPoint`` () =
    Assert.DoesNotContain("[<EntryPoint>]", codegenSrc "module M\nfn add(a Int)(b Int) Int = a + b")

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
