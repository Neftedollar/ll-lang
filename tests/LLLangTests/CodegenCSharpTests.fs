module LLLang.Tests.CodegenCSharpTests

open System
open System.IO
open System.Diagnostics
open Xunit
open LLLang.Elaborator
open LLLang.Compiler
open LLLang.Platform

let private csSrc (src: string) : string =
    match compileToCSharp src with
    | Ok cs -> cs
    | Error es -> failwith $"C# codegen failed: {es}"

let private runProc (cwd: string) (exe: string) (args: string list) : int * string * string =
    let psi = ProcessStartInfo(exe)
    psi.WorkingDirectory <- cwd
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    for arg in args do
        psi.ArgumentList.Add(arg)
    use proc = LLLang.Tests.TestCompat.startProcess psi
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    (proc.ExitCode, stdout, stderr)

let private toolExists (exe: string) : bool =
    let (code, _, _) = runProc __SOURCE_DIRECTORY__ "sh" ["-lc"; "command -v " + exe + " >/dev/null 2>&1"]
    code = 0

[<Fact>]
let ``CSharp: compileToCSharp produces non-empty output`` () =
    let src = "module M\nlet x = 42"
    let cs = csSrc src
    Assert.False(System.String.IsNullOrWhiteSpace(cs))

[<Fact>]
let ``CSharp: output has C# backend header`` () =
    let src = "module M\nlet x = 1"
    let cs = csSrc src
    Assert.Contains("ll-lang C# backend", cs)

[<Fact>]
let ``CSharp: compileTarget CSharp produces same as compileToCSharp`` () =
    let src = "module M\nid(x Int) Int = x"
    let a = compileToCSharp src
    let b = compileTarget CSharp src
    Assert.Equal(a, b)

[<Fact>]
let ``CSharp: compileTarget CSharp does not emit F# let rec`` () =
    let src = "module M\nid(x Int) Int = x"
    match compileTarget CSharp src with
    | Ok cs ->
        Assert.DoesNotContain("\nlet rec", cs)
        Assert.Contains("public static class", cs)
    | Error es -> failwith $"unexpected error: {es}"

[<Fact>]
let ``CSharp: top-level numeric lets preserve literal values`` () =
    let src = "module M\nlet answer = 42\nlet lucky = 7"
    let cs = csSrc src
    Assert.Contains("public static readonly long answer = 42L;", cs)
    Assert.Contains("public static readonly long lucky = 7L;", cs)

[<Fact>]
let ``CSharp: arithmetic function body emits real expression`` () =
    let src = "module M\nadd(x Int)(y Int) Int = x + y"
    let cs = csSrc src
    Assert.Contains("Func<long, long> add(long x)", cs)
    Assert.Contains("y => (x + y)", cs)

[<Fact>]
let ``CSharp: symbolic operators lower without raw symbolic identifiers`` () =
    let src =
        "module M\n"
        + "main() Int =\n"
        + "  x = 5 >>= (\\n. n + 1)\n"
        + "  y = x <|> 0\n"
        + "  z = y >> 7\n"
        + "  z\n"
    let cs = csSrc src
    Assert.DoesNotContain(">>=", cs)
    Assert.DoesNotContain("<|>", cs)
    Assert.DoesNotContain(" >> ", cs)

[<Fact>]
let ``CSharp: prelude includes null-based Maybe helper runtime`` () =
    let src = "module M\nMaybe A = Some A | None\nf(m Maybe[Int]) Int = maybeWithDefault 0 m"
    let cs = csSrc src
    Assert.DoesNotContain("LLSome", cs)
    Assert.DoesNotContain("LLNone", cs)
    Assert.Contains("maybeWithDefault", cs)

[<Fact>]
let ``CSharp: prelude is omitted when stdlib helpers are not used`` () =
    let src = "module M\nid(x Int) Int = x"
    let cs = csSrc src
    Assert.DoesNotContain("// --- ll-lang stdlib (C#) ---", cs)

[<Fact>]
let ``CSharp: external console_log emits Console.WriteLine wrapper`` () =
    let src = "module M\nexternal console_log(msg Str) Unit"
    let cs = csSrc src
    Assert.Contains("public static void console_log(string msg) { Console.WriteLine(msg); }", cs)

[<Fact>]
let ``CSharp: external JSON_parse emits JsonSerializer.Deserialize wrapper`` () =
    let src =
        "module M\n"
        + "opaque Any\n"
        + "external JSON_parse(s Str) Any\n"
        + "let _ = JSON_parse \"{\\\"a\\\": 1}\"\n"
    let cs = csSrc src
    Assert.Contains("public static Any JSON_parse(string s) => System.Text.Json.JsonSerializer.Deserialize<object>(s);", cs)

[<Fact>]
let ``CSharp: JSON_parse declaration adds using System.Text.Json`` () =
    let src =
        "module M\n"
        + "opaque Any\n"
        + "external JSON_parse(s Str) Any\n"
        + "let _ = JSON_parse \"{\\\"a\\\": 1}\"\n"
    let cs = csSrc src
    Assert.Contains("using System.Text.Json;", cs)

[<Fact>]
let ``CSharp: modules without JSON_parse do not add System.Text.Json`` () =
    let src = "module M\nlet x = 1"
    let cs = csSrc src
    Assert.DoesNotContain("using System.Text.Json;", cs)

[<Fact>]
let ``CSharp: unknown external declaration raises E026`` () =
    let src = "module M\nexternal host_log(msg Str) Unit"
    match compileToCSharp src with
    | Ok cs -> failwith $"unexpected success: {cs}"
    | Error es ->
        let e = es |> List.exactlyOne
        Assert.Equal(E026, e.Code)
        Assert.Contains("UnknownExternalMapping", e.Message)
        Assert.Contains("target:csharp", e.Message)
        Assert.Contains("name:host_log", e.Message)

[<Fact>]
let ``CSharp: zero-arg constructor in expression emits object construction`` () =
    let src = "module M\nColor = Red | Green\nscore(c Color) Int = match c | Red -> 1 | Green -> 2\nmain() Int = score Red"
    let cs = csSrc src
    Assert.Contains("new Red()", cs)

[<Fact>]
let ``CSharp: match with constructor payload emits typed guarded accessor flow`` () =
    let src = "module M\nJson = JNull | JNum Int\nkind(v Json) Int = match v | JNull -> 0 | JNum n -> n\nmain() Int = kind (JNum 1)"
    let cs = csSrc src
    Assert.DoesNotContain("public static long kind(Json v) => 0L;", cs)
    Assert.Contains("__ll_match is JNum", cs)
    Assert.Contains("__ll_case_1._0", cs)

[<Fact>]
let ``CSharp: main sequencing preserves side effects at runtime`` () =
    if not (toolExists "dotnet") then
        ()
    else
        let src =
            "module Smoke\n"
            + "main() Int =\n"
            + "  _ = printfn \"smoke\"\n"
            + "  0\n"
        let cs = csSrc src
        Assert.Contains("public static int Main(string[] args)", cs)
        Assert.DoesNotContain("=> 0;", cs)

        let tempRoot = Path.Combine(Path.GetTempPath(), "lll-cs-main-smoke-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempRoot) |> ignore
        try
            let csproj =
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + "    <OutputType>Exe</OutputType>\n"
                + "    <TargetFramework>net10.0</TargetFramework>\n"
                + "    <ImplicitUsings>enable</ImplicitUsings>\n"
                + "    <Nullable>enable</Nullable>\n"
                + "  </PropertyGroup>\n"
                + "</Project>\n"
            File.WriteAllText(Path.Combine(tempRoot, "Smoke.csproj"), csproj)
            File.WriteAllText(Path.Combine(tempRoot, "Program.cs"), cs)
            let (runCode, runOut, runErr) = runProc tempRoot "dotnet" ["run"; "--project"; "Smoke.csproj"]
            Assert.True((runCode = 0), $"dotnet run failed\nstdout:\n{runOut}\nstderr:\n{runErr}\nsource:\n{cs}")
            Assert.Contains("smoke", runOut)
        finally
            try Directory.Delete(tempRoot, true) with _ -> ()
