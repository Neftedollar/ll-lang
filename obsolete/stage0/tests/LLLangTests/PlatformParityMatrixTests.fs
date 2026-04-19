module LLLang.Tests.PlatformParityMatrixTests

open System
open System.IO
open System.Diagnostics
open System.Text.RegularExpressions
open Xunit

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private lllcDll =
    Path.Combine(repoRoot, "src/LLLangTool/bin/Debug/net10.0/lllc.dll")

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

let private runLllc (cwd: string) (args: string list) : int * string * string =
    runProc cwd "dotnet" (lllcDll :: args)

let private toolExists (exe: string) : bool =
    let (code, _, _) = runProc repoRoot "sh" ["-lc"; "command -v " + exe + " >/dev/null 2>&1"]
    code = 0

[<Fact>]
let ``platform parity matrix: core cases compile and pass target-native checks`` () =
    Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")

    let tempRoot = Path.Combine(Path.GetTempPath(), "lll-parity-matrix-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(tempRoot) |> ignore
    try
        let cases =
            [ ("arith",
               "module Case.Arith\n\nadd(x Int)(y Int) Int = x + y\nmain() Int = add 2 3\n")
              ("if_expr",
               "module Case.If\n\nabsLike(x Int) Int = if x > 0\n  x\nelse 0 - x\nmain() Int = absLike 2\n")
              ("adt_match",
               "module Case.Adt\n\nColor = Red | Green\nscore(c Color) Int = match c\n  | Red -> 1\n  | Green -> 2\nmain() Int = score Red\n")
              ("adt_payload_match",
               "module Case.AdtPayload\n\nJson = JNull | JNum Int\nkind(v Json) Int = match v\n  | JNull -> 0\n  | JNum n -> n\nmain() Int = kind (JNum 1)\n")
              ("tuple_match",
               "module Case.Tuple\n\nmain() Int = match (1, 2)\n  | (a, _) -> a\n")
              ("str_match",
               "module Case.Str\n\nisOk(s Str) Bool = match s\n  | \"ok\" -> true\n  | _ -> false\nmain() Bool = isOk \"ok\"\n")
              ("impl_method",
               "module Case.Impl\n\ntrait Show T =\n  show(x T) Str\nBox = MkBox Int\nimpl Show Box =\n  show(x Box) Str = \"box\"\nuseShow(x Box) Str = show x\nmain() Int = 0\n")
              ("constrained_dispatch",
               "module Case.Constrained\n\ntrait Functor F =\n  map(f A->B)(fa F[A]) F[B]\nMaybe A = Some A | None\nimpl Functor Maybe =\n  map(f A->B)(fa Maybe[A]) Maybe[B] =\n    | Some a -> Some (f a)\n    | None -> None\ntransform[F: Functor](xs F[Int])(f Int->Int) F[Int] = map f xs\nmain() Int = 0\n")
              ("external_opaque",
               "module Case.ExternalOpaque\n\nexternal console_log(msg Str) Unit\nopaque Buffer\ngreet(name Str) Unit = console_log name\nmain() Int = 0\n")
              ]

        let targets =
            [ ("fs", ".fs")
              ("ts", ".ts")
              ("py", ".py")
              ("java", ".java")
              ("cs", ".cs")
              ("llvm", ".ll") ]

        for (caseName, src) in cases do
            let srcPath = Path.Combine(tempRoot, caseName + ".lll")
            File.WriteAllText(srcPath, src)

            for (target, ext) in targets do
                let (exitCode, stdout, stderr) = runLllc tempRoot ["build"; "--target"; target; srcPath]
                Assert.True(
                    (exitCode = 0),
                    $"lllc build failed for case={caseName} target={target}\nstdout:\n{stdout}\nstderr:\n{stderr}")

                let outPath = LLLang.Tests.TestCompat.changeExtensionOrInput srcPath ext
                Assert.True(File.Exists(outPath), $"missing output for case={caseName} target={target}: {outPath}")

                match target with
                | "fs" ->
                    let fsproj = LLLang.Tests.TestCompat.changeExtensionOrInput srcPath ".fsproj"
                    Assert.True(File.Exists(fsproj), $"missing fsharp project for case={caseName}: {fsproj}")
                    let (code, so, se) = runProc tempRoot "dotnet" ["build"; "--nologo"; "--verbosity"; "quiet"; fsproj]
                    Assert.True((code = 0), $"fsharp build failed for case={caseName}\nstdout:\n{so}\nstderr:\n{se}")
                    if caseName = "adt_match" then
                        let fsText = File.ReadAllText(outPath)
                        Assert.Contains("type Color =", fsText)
                        Assert.Contains("| Red", fsText)
                    if caseName = "adt_payload_match" then
                        let fsText = File.ReadAllText(outPath)
                        Assert.Contains("type Json =", fsText)
                        Assert.Contains("| JNum of int64", fsText)
                        Assert.Contains("JNum(n) -> n", fsText)
                    if caseName = "impl_method" then
                        let fsText = File.ReadAllText(outPath)
                        Assert.Contains("let show_Box", fsText)
                    if caseName = "constrained_dispatch" then
                        let fsText = File.ReadAllText(outPath)
                        Assert.Contains("let map_Maybe", fsText)
                        Assert.Contains("let transform", fsText)
                        Assert.Contains("(map_Maybe f) xs", fsText)
                    if caseName = "external_opaque" then
                        let fsText = File.ReadAllText(outPath)
                        Assert.Contains("type Buffer = obj", fsText)
                        Assert.Contains("let console_log", fsText)
                | "cs" ->
                    let csproj = LLLang.Tests.TestCompat.changeExtensionOrInput srcPath ".csproj"
                    Assert.True(File.Exists(csproj), $"missing csharp project for case={caseName}: {csproj}")
                    let (code, so, se) = runProc tempRoot "dotnet" ["build"; "--nologo"; "--verbosity"; "quiet"; csproj]
                    Assert.True((code = 0), $"csharp build failed for case={caseName}\nstdout:\n{so}\nstderr:\n{se}")
                    if caseName = "adt_match" then
                        let csText = File.ReadAllText(outPath)
                        Assert.Contains("new Red()", csText)
                    if caseName = "adt_payload_match" then
                        let csText = File.ReadAllText(outPath)
                        Assert.Contains("__ll_match is JNum", csText)
                        Assert.Contains("__ll_case_1._0", csText)
                    if caseName = "impl_method" then
                        let csText = File.ReadAllText(outPath)
                        Assert.Contains("show_Box(", csText)
                    if caseName = "constrained_dispatch" then
                        let csText = File.ReadAllText(outPath)
                        Assert.Contains("map_Maybe", csText)
                        Assert.Contains("transform", csText)
                    if caseName = "external_opaque" then
                        let csText = File.ReadAllText(outPath)
                        Assert.Contains("Buffer", csText)
                        Assert.Contains("console_log", csText)
                | "ts" ->
                    if toolExists "tsc" then
                        let (code, so, se) = runProc tempRoot "tsc" [outPath; "--target"; "es2022"; "--module"; "esnext"; "--noEmit"]
                        Assert.True((code = 0), $"typescript check failed for case={caseName}\nstdout:\n{so}\nstderr:\n{se}")
                    if caseName = "adt_match" then
                        let tsText = File.ReadAllText(outPath)
                        Assert.Contains("type Color = { _tag:", tsText)
                        Assert.Contains("const Red: Color", tsText)
                    if caseName = "adt_payload_match" then
                        let tsText = File.ReadAllText(outPath)
                        Assert.Contains("v?._tag === `JNum`", tsText)
                        Assert.Contains("const n = ", tsText)
                    if caseName = "impl_method" then
                        let tsText = File.ReadAllText(outPath)
                        Assert.Contains("const show_Box =", tsText)
                    if caseName = "constrained_dispatch" then
                        let tsText = File.ReadAllText(outPath)
                        Assert.Contains("const map_Maybe =", tsText)
                        Assert.Contains("const transform =", tsText)
                | "py" ->
                    if toolExists "python3" then
                        let (code, so, se) = runProc tempRoot "python3" ["-m"; "py_compile"; outPath]
                        Assert.True((code = 0), $"python check failed for case={caseName}\nstdout:\n{so}\nstderr:\n{se}")
                    if caseName = "adt_match" then
                        let pyText = File.ReadAllText(outPath)
                        Assert.Contains("@dataclass(frozen=True)", pyText)
                        Assert.Contains("class Red", pyText)
                    if caseName = "adt_payload_match" then
                        let pyText = File.ReadAllText(outPath)
                        Assert.Contains("_tag == \"JNull\"", pyText)
                        Assert.Contains("(lambda n: n)(", pyText)
                        Assert.Contains("._0)", pyText)
                    if caseName = "impl_method" then
                        let pyText = File.ReadAllText(outPath)
                        Assert.Contains("def show_Box(", pyText)
                    if caseName = "constrained_dispatch" then
                        let pyText = File.ReadAllText(outPath)
                        Assert.Contains("def map_Maybe(", pyText)
                        Assert.Contains("def transform(", pyText)
                | "java" ->
                    let javaText = File.ReadAllText(outPath)
                    if toolExists "javac" then
                        let classMatch = Regex.Match(javaText, @"public\s+class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)")
                        let compilePath =
                            if classMatch.Success then
                                let expected = classMatch.Groups.["name"].Value + ".java"
                                let expectedPath = Path.Combine(tempRoot, expected)
                                if StringComparer.Ordinal.Equals(LLLang.Tests.TestCompat.fileNameOrEmpty outPath, expected) then
                                    outPath
                                else
                                    Assert.True(File.Exists(expectedPath), $"missing class-aligned java output for case={caseName}: {expectedPath}")
                                    expectedPath
                            else
                                outPath
                        let (code, so, se) = runProc tempRoot "javac" [compilePath]
                        Assert.True((code = 0), $"java check failed for case={caseName}\nstdout:\n{so}\nstderr:\n{se}")
                    if caseName = "adt_match" then
                        Assert.Contains("sealed interface Color", javaText)
                        Assert.Contains("record Red", javaText)
                    if caseName = "adt_payload_match" then
                        Assert.Contains("sealed interface Json", javaText)
                        Assert.Contains("record JNum(Long _0)", javaText)
                        Assert.Contains("((Json.JNum) v)._0()", javaText)
                    if caseName = "impl_method" then
                        Assert.Contains("show_Box(", javaText)
                    if caseName = "constrained_dispatch" then
                        Assert.Contains("map_Maybe(", javaText)
                        Assert.Contains("transform(", javaText)
                | "llvm" ->
                    if caseName = "adt_match" then
                        let llText = File.ReadAllText(outPath)
                        Assert.Contains("define ptr @__ll_alloc", llText)
                        Assert.Contains("icmp eq i64", llText)
                    if caseName = "tuple_match" then
                        let llText = File.ReadAllText(outPath)
                        Assert.Contains("call ptr @__ll_alloc(i64 -1", llText)
                        Assert.Contains("icmp eq ptr %", llText)
                    if caseName = "str_match" then
                        let llText = File.ReadAllText(outPath)
                        Assert.Contains("@.str0 = private unnamed_addr constant", llText)
                        Assert.Contains("call i32 @strcmp(ptr", llText)
                    if caseName = "impl_method" then
                        let llText = File.ReadAllText(outPath)
                        Assert.Contains("define ptr @show_Box(", llText)
                    if caseName = "constrained_dispatch" then
                        let llText = File.ReadAllText(outPath)
                        Assert.Contains("define ptr @map_Maybe(", llText)
                        Assert.Contains("define ptr @transform(", llText)
                    if toolExists "llvm-as" then
                        let bcPath = LLLang.Tests.TestCompat.changeExtensionOrInput outPath ".bc"
                        let (code, so, se) = runProc tempRoot "llvm-as" [outPath; "-o"; bcPath]
                        Assert.True((code = 0), $"llvm-as failed for case={caseName}\nstdout:\n{so}\nstderr:\n{se}")
                | _ -> ()
    finally
        try Directory.Delete(tempRoot, true) with _ -> ()
