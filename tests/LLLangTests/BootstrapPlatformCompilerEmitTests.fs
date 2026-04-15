module LLLang.Tests.BootstrapPlatformCompilerEmitTests

open System
open System.IO
open System.Diagnostics
open Xunit

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private lllcDll =
    Path.Combine(repoRoot, "src/LLLangTool/bin/Debug/net10.0/lllc.dll")

let private runLllc (cwd: string) (args: string list) : int * string * string =
    let psi = ProcessStartInfo("dotnet")
    psi.WorkingDirectory <- cwd
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.ArgumentList.Add(lllcDll)
    for arg in args do
        psi.ArgumentList.Add(arg)
    use proc = LLLang.Tests.TestCompat.startProcess psi
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    (proc.ExitCode, stdout, stderr)

[<Literal>]
let private quietDotnetVerbosity = "quiet"

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
    try
        let (code, _, _) = runProc repoRoot "sh" ["-lc"; "command -v " + exe + " >/dev/null 2>&1"]
        code = 0
    with _ -> false

let private envFlagIsOne (name: string) : bool =
    String.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal)

[<Literal>]
let private tempPrefix = "lll-bootstrap-platform-emit-"

let rec private copyDirRecursive (srcDir: string) (dstDir: string) =
    Directory.CreateDirectory(dstDir) |> ignore
    for file in Directory.GetFiles(srcDir) do
        let target = Path.Combine(dstDir, LLLang.Tests.TestCompat.fileNameOrEmpty file)
        File.Copy(file, target, true)
    for sub in Directory.GetDirectories(srcDir) do
        let target = Path.Combine(dstDir, LLLang.Tests.TestCompat.fileNameOrEmpty sub)
        copyDirRecursive sub target

[<Literal>]
let private stdlibSrcRel = "stdlib/src"

[<Literal>]
let private compilerRel = "stdlib/src/Compiler.lll"

[<Fact>]
let ``stdlib Compiler.lll emits compiler artifacts for every Platform.*.SDK target`` () =
    Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
    let realCompilerPath = Path.Combine(repoRoot, compilerRel)
    Assert.True(File.Exists(realCompilerPath), $"missing source file {realCompilerPath}")

    let tempRoot = Path.Combine(Path.GetTempPath(), tempPrefix + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(tempRoot) |> ignore
    try
        let copiedStdlib = Path.Combine(tempRoot, stdlibSrcRel)
        copyDirRecursive (Path.Combine(repoRoot, stdlibSrcRel)) copiedStdlib
        let compilerSrcPath = Path.Combine(tempRoot, compilerRel)

        let targets =
            [ ("Platform.FSharp.SDK", ".fs", "module Std.Compiler", "open LLLang.Prelude")
              ("Platform.Java.SDK", ".java", "ll-lang Java backend", "public class")
              ("Platform.CSharp.SDK", ".cs", "ll-lang C# backend", "public static class")
              ("Platform.Python.SDK", ".py", "ll-lang Python backend", "def isNone")
              ("Platform.TypeScript.SDK", ".ts", "ll-lang TypeScript backend", "const isNone")
              ("Platform.LLVM.SDK", ".ll", "ll-lang LLVM backend", "define i1 @isNone") ]

        for (target, ext, markerA, markerB) in targets do
            let outPath = LLLang.Tests.TestCompat.changeExtensionOrInput compilerSrcPath ext
            let (exitCode, stdout, stderr) =
                runLllc tempRoot ["build"; "--target"; target; compilerSrcPath]

            Assert.True(
                (exitCode = 0),
                $"lllc build failed for {target}: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")

            Assert.Contains("Built " + Path.GetFileName(outPath), stdout)
            Assert.True(File.Exists(outPath), $"missing emitted file for {target}: {outPath}")

            let emitted = File.ReadAllText(outPath)
            Assert.Contains(markerA, emitted)
            Assert.Contains(markerB, emitted)

            match target with
            | "Platform.FSharp.SDK" ->
                let fsproj = LLLang.Tests.TestCompat.changeExtensionOrInput compilerSrcPath ".fsproj"
                Assert.True(File.Exists(fsproj), $"missing emitted fsproj for {target}: {fsproj}")
                let (code, so, se) = runProc tempRoot "dotnet" ["build"; "--nologo"; "--verbosity"; quietDotnetVerbosity; fsproj]
                Assert.True((code = 0), $"dotnet build failed for {target}\nstdout:\n{so}\nstderr:\n{se}")
            | "Platform.CSharp.SDK" ->
                let csproj = LLLang.Tests.TestCompat.changeExtensionOrInput compilerSrcPath ".csproj"
                Assert.True(File.Exists(csproj), $"missing emitted csproj for {target}: {csproj}")
                let (code, so, se) = runProc tempRoot "dotnet" ["build"; "--nologo"; "--verbosity"; quietDotnetVerbosity; csproj]
                Assert.True((code = 0), $"dotnet build failed for {target}\nstdout:\n{so}\nstderr:\n{se}")
            | "Platform.Java.SDK" ->
                if envFlagIsOne "LLLANG_BOOTSTRAP_JAVA_COMPILE" && toolExists "javac" then
                    let (code, so, se) = runProc tempRoot "javac" [outPath]
                    Assert.True((code = 0), $"javac failed for {target}\nstdout:\n{so}\nstderr:\n{se}")
            | "Platform.Python.SDK" ->
                if toolExists "python3" then
                    let (code, so, se) = runProc tempRoot "python3" ["-m"; "py_compile"; outPath]
                    Assert.True((code = 0), $"python3 py_compile failed for {target}\nstdout:\n{so}\nstderr:\n{se}")
            | "Platform.TypeScript.SDK" ->
                if toolExists "tsc" then
                    let outDir = LLLang.Tests.TestCompat.directoryNameOrCurrent outPath
                    let fileName = LLLang.Tests.TestCompat.fileNameOrEmpty outPath
                    let (code, so, se) = runProc outDir "tsc" [fileName; "--target"; "es2022"; "--module"; "esnext"; "--lib"; "es2022"; "--noEmit"]
                    Assert.True((code = 0), $"tsc --noEmit failed for {target}\nstdout:\n{so}\nstderr:\n{se}")
            | "Platform.LLVM.SDK" ->
                if toolExists "llvm-as" then
                    let bcPath = LLLang.Tests.TestCompat.changeExtensionOrInput outPath ".bc"
                    let (code, so, se) = runProc tempRoot "llvm-as" [outPath; "-o"; bcPath]
                    Assert.True((code = 0), $"llvm-as failed for {target}\nstdout:\n{so}\nstderr:\n{se}")
            | _ -> ()
    finally
        try Directory.Delete(tempRoot, true) with _ -> ()
