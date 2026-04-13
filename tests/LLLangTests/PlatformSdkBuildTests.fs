module LLLang.Tests.PlatformSdkBuildTests

open System
open System.IO
open System.Diagnostics
open Xunit
open LLLang.Platform

let private withTempDir (f: string -> 'a) : 'a =
    let dir = Path.Combine(Path.GetTempPath(), "lll-sdk-build-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    try f dir
    finally
        try Directory.Delete(dir, true) with _ -> ()

let private lllcDll =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../src/LLLangTool/bin/Debug/net10.0/lllc.dll"))

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

let private writeSampleProject (root: string) (platforms: string list) =
    Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
    let platformsToml =
        if List.isEmpty platforms then
            ""
        else
            let quoted = platforms |> List.map (fun p -> "\"" + p + "\"") |> String.concat ", "
            "\n[platform]\nuse = [" + quoted + "]\n"
    let manifest =
        "[project]\nname = \"app\"\n" + platformsToml
    File.WriteAllText(Path.Combine(root, "lll.toml"), manifest)
    File.WriteAllText(
        Path.Combine(root, "src", "Main.lll"),
        "module App.Main\n\nmain() Int = 42\n")

let private writeFetchExternalProject (root: string) =
    Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
    File.WriteAllText(Path.Combine(root, "lll.toml"), "[project]\nname = \"app\"\n")
    File.WriteAllText(
        Path.Combine(root, "src", "Main.lll"),
        "module App.Main\n" +
        "external fetch(url Str) Promise[Response]\n" +
        "opaque Response\n" +
        "opaque Promise[A]\n" +
        "main() Int = 0\n")

[<Fact>]
let ``Platform SDK: built-in runtime templates resolve`` () =
    for target in [FSharp; TypeScript; CSharp] do
        match tryResolveRuntimeTemplate target with
        | None -> Assert.Fail($"expected runtime template for {target}")
        | Some path -> Assert.True(File.Exists(path), $"template path does not exist: {path}")

[<Fact>]
let ``lllc build project emits all platform outputs with sdk runtime files`` () =
    withTempDir (fun root ->
        Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
        writeSampleProject root ["fsharp"; "typescript"; "python"; "java"; "csharp"; "llvm"]
        let (exitCode, stdout, stderr) = runLllc root ["build"]
        Assert.True((exitCode = 0), $"lllc build failed: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
        Assert.Contains("suggested compile:", stdout)
        Assert.Contains("suggested run:", stdout)
        Assert.Contains("suggested compile: javac ", stdout)
        Assert.Contains("Main.java", stdout)
        Assert.Contains("suggested run: javac ", stdout)
        Assert.Contains("&& java ", stdout)
        Assert.Contains("Main", stdout)

        let fsproj = Path.Combine(root, "bin", "fsharp", "app.fsproj")
        let fsMain = Path.Combine(root, "bin", "fsharp", "Main.fs")
        let tsMain = Path.Combine(root, "bin", "typescript", "app.ts")
        let tsPkg = Path.Combine(root, "bin", "typescript", "package.json")
        let pyMain = Path.Combine(root, "bin", "python", "app.py")
        let javaMain = Path.Combine(root, "bin", "java", "app.java")
        let javaClassMain = Path.Combine(root, "bin", "java", "Main.java")
        let csMain = Path.Combine(root, "bin", "csharp", "app.cs")
        let csProj = Path.Combine(root, "bin", "csharp", "app.csproj")
        let llvmMain = Path.Combine(root, "bin", "llvm", "app.ll")

        for file in [fsproj; fsMain; tsMain; tsPkg; pyMain; javaMain; javaClassMain; csMain; csProj; llvmMain] do
            Assert.True(File.Exists(file), $"missing expected build output: {file}")

        let csProjText = File.ReadAllText(csProj)
        Assert.Contains("<Compile Include=\"app.cs\" />", csProjText)

        let tsPkgText = File.ReadAllText(tsPkg)
        Assert.Contains("\"build\": \"tsc \\\"app.ts\\\" --target es2022 --module esnext\"", tsPkgText)
        Assert.Contains("\"run\": \"tsc \\\"app.ts\\\" --target es2022 --module esnext && node \\\"app.js\\\"\"", tsPkgText)

        let (fsCode, fsOut, fsErr) = runProc root "dotnet" ["build"; "--nologo"; "--verbosity"; "quiet"; fsproj]
        Assert.True((fsCode = 0), $"fsharp build failed\nstdout:\n{fsOut}\nstderr:\n{fsErr}")

        let (csCode, csOut, csErr) = runProc root "dotnet" ["build"; "--nologo"; "--verbosity"; "quiet"; csProj]
        Assert.True((csCode = 0), $"csharp build failed\nstdout:\n{csOut}\nstderr:\n{csErr}")

        if toolExists "tsc" then
            let (tsCode, tsOut, tsErr) = runProc root "tsc" [tsMain; "--target"; "es2022"; "--module"; "esnext"; "--noEmit"]
            Assert.True((tsCode = 0), $"typescript check failed\nstdout:\n{tsOut}\nstderr:\n{tsErr}")

        if toolExists "python3" then
            let (pyCode, pyOut, pyErr) = runProc root "python3" ["-m"; "py_compile"; pyMain]
            Assert.True((pyCode = 0), $"python check failed\nstdout:\n{pyOut}\nstderr:\n{pyErr}")

        if toolExists "javac" then
            let (javaCode, javaOut, javaErr) = runProc root "javac" [javaClassMain]
            Assert.True((javaCode = 0), $"java check failed\nstdout:\n{javaOut}\nstderr:\n{javaErr}")

        if toolExists "llvm-as" then
            let bcPath = Path.Combine(root, "bin", "llvm", "app.bc")
            let (llCode, llOut, llErr) = runProc root "llvm-as" [llvmMain; "-o"; bcPath]
            Assert.True((llCode = 0), $"llvm-as check failed\nstdout:\n{llOut}\nstderr:\n{llErr}")
    )

[<Fact>]
let ``lllc build validates externals per target`` () =
    withTempDir (fun root ->
        Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
        writeFetchExternalProject root

        let (tsExitCode, tsOut, tsErr) = runLllc root ["build"; "--target"; "ts"; root]
        Assert.True((tsExitCode = 0), $"ts build failed: exit={tsExitCode}\nstdout:\n{tsOut}\nstderr:\n{tsErr}")
        let tsMain = Path.Combine(root, "bin", "typescript", "app.ts")
        Assert.True(File.Exists(tsMain), $"missing expected TS output: {tsMain}")

        let (fsExitCode, fsOut, fsErr) = runLllc root ["build"; "--target"; "fs"; root]
        Assert.True((fsExitCode <> 0), $"fsharp build should fail for fetch on fs target; exit={fsExitCode}\nstdout:\n{fsOut}\nstderr:\n{fsErr}")
        Assert.Contains("E026", fsErr)
        Assert.Contains("target:fsharp", fsErr)
    )

[<Fact>]
let ``lllc check single-file validates without emitting target files`` () =
    withTempDir (fun root ->
        Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
        let srcPath = Path.Combine(root, "CheckMe.lll")
        File.WriteAllText(srcPath, "module Demo.CheckMe\n\nmain() Int = 42\n")

        let (exitCode, stdout, stderr) = runLllc root ["check"; srcPath]
        Assert.True((exitCode = 0), $"lllc check failed: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
        Assert.Contains("Checked CheckMe.lll [fsharp]", stdout)

        let fsPath = Path.ChangeExtension(srcPath, ".fs")
        let tsPath = Path.ChangeExtension(srcPath, ".ts")
        Assert.False(File.Exists(fsPath), $"check should not emit codegen artifacts: {fsPath}")
        Assert.False(File.Exists(tsPath), $"check should not emit codegen artifacts: {tsPath}")
    )

[<Fact>]
let ``lllc check validates externals for selected target`` () =
    withTempDir (fun root ->
        Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
        let srcPath = Path.Combine(root, "CheckExternal.lll")
        File.WriteAllText(
            srcPath,
            "module Demo.CheckExternal\n" +
            "external fetch(url Str) Promise[Response]\n" +
            "opaque Response\n" +
            "opaque Promise[A]\n" +
            "main() Int = 0\n")

        let (tsCode, tsOut, tsErr) = runLllc root ["check"; "--target"; "ts"; srcPath]
        Assert.True((tsCode = 0), $"check --target ts should pass\nstdout:\n{tsOut}\nstderr:\n{tsErr}")

        let (fsCode, fsOut, fsErr) = runLllc root ["check"; "--target"; "fs"; srcPath]
        Assert.True((fsCode <> 0), $"check --target fs should fail on unknown external mapping\nstdout:\n{fsOut}\nstderr:\n{fsErr}")
        Assert.Contains("E026", fsErr)
        Assert.Contains("target:fsharp", fsErr)
    )

[<Fact>]
let ``lllc build fails hard on unknown manifest platform (no skip)`` () =
    withTempDir (fun root ->
        Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
        writeSampleProject root ["fsharp"; "wat"]

        let (exitCode, stdout, stderr) = runLllc root ["build"]
        Assert.True((exitCode <> 0), $"build should fail on unknown platform\nstdout:\n{stdout}\nstderr:\n{stderr}")
        Assert.Contains("unknown platform 'wat'", stderr)
        Assert.DoesNotContain("skipping", stderr)

        let fsproj = Path.Combine(root, "bin", "fsharp", "app.fsproj")
        Assert.False(File.Exists(fsproj), "build should fail before emitting artifacts when manifest contains unknown targets")
    )

[<Fact>]
let ``lllc build single-file csharp emits sibling csproj scaffold`` () =
    withTempDir (fun root ->
        Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
        let srcPath = Path.Combine(root, "Mini.lll")
        File.WriteAllText(srcPath, "module Mini\n\nmain() Int = 1\n")
        let (exitCode, stdout, stderr) = runLllc root ["build"; "--target"; "cs"; srcPath]
        Assert.True((exitCode = 0), $"single-file build failed: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
        Assert.Contains("suggested compile:", stdout)
        Assert.Contains("suggested run:", stdout)

        let csPath = Path.Combine(root, "Mini.cs")
        let csProjPath = Path.Combine(root, "Mini.csproj")
        Assert.True(File.Exists(csPath), $"missing output file: {csPath}")
        Assert.True(File.Exists(csProjPath), $"missing sdk scaffold file: {csProjPath}")
        Assert.Contains("<Compile Include=\"Mini.cs\" />", File.ReadAllText(csProjPath))

        let (buildCode, buildOut, buildErr) =
            runProc root "dotnet" ["build"; "--nologo"; "--verbosity"; "quiet"; csProjPath]
        Assert.True((buildCode = 0), $"single-file csharp build failed\nstdout:\n{buildOut}\nstderr:\n{buildErr}")
    )

[<Fact>]
let ``lllc build single-file java emits class-aligned source and sdk commands`` () =
    withTempDir (fun root ->
        Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
        let srcPath = Path.Combine(root, "Mini.lll")
        File.WriteAllText(srcPath, "module Demo.Entry\n\nmain() Int = 7\n")

        let (exitCode, stdout, stderr) = runLllc root ["build"; "--target"; "java"; srcPath]
        Assert.True((exitCode = 0), $"single-file java build failed: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
        Assert.Contains("Built Mini.java", stdout)
        Assert.Contains("suggested compile: javac ", stdout)
        Assert.Contains("Entry.java", stdout)
        Assert.Contains("suggested run: javac ", stdout)
        Assert.Contains("&& java ", stdout)
        Assert.Contains("Entry", stdout)

        let javaPath = Path.Combine(root, "Mini.java")
        let entryPath = Path.Combine(root, "Entry.java")
        Assert.True(File.Exists(javaPath), $"missing output file: {javaPath}")
        Assert.True(File.Exists(entryPath), $"missing class-aligned java file: {entryPath}")

        if toolExists "javac" then
            let (buildCode, buildOut, buildErr) = runProc root "javac" [entryPath]
            Assert.True((buildCode = 0), $"single-file java compile failed\nstdout:\n{buildOut}\nstderr:\n{buildErr}")

            if toolExists "java" then
                let (runCode, runOut, runErr) = runProc root "java" ["Entry"]
                Assert.True((runCode = 0), $"single-file java run failed\nstdout:\n{runOut}\nstderr:\n{runErr}")
    )

[<Fact>]
let ``lllc run --target java compiles and executes class-aligned source`` () =
    withTempDir (fun root ->
        if toolExists "javac" && toolExists "java" then
            Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
            let srcPath = Path.Combine(root, "RunMe.lll")
            File.WriteAllText(srcPath, "module Demo.RunMe\n\nmain() Int = 11\n")

            let (exitCode, stdout, stderr) = runLllc root ["run"; "--target"; "java"; srcPath]
            Assert.True((exitCode = 0), $"lllc run --target java failed: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
            Assert.Contains("Running: javac ", stdout)
            Assert.Contains("&& java ", stdout)
            Assert.Contains("RunMe", stdout)
            Assert.True(File.Exists(Path.Combine(root, "RunMe.java")), "expected class-aligned java source to be emitted")
    )

[<Fact>]
let ``lllc run --target ts compiles and executes emitted javascript`` () =
    withTempDir (fun root ->
        if toolExists "npx" && toolExists "node" then
            Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
            let srcPath = Path.Combine(root, "RunTs.lll")
            File.WriteAllText(srcPath, "module Demo.RunTs\n\nmain() Int = 3\n")

            let (exitCode, stdout, stderr) = runLllc root ["run"; "--target"; "ts"; srcPath]
            Assert.True((exitCode = 0), $"lllc run --target ts failed: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
            Assert.Contains("Running: npx tsc ", stdout)
            Assert.Contains("&& node ", stdout)
            Assert.Contains("RunTs.js", stdout)
            Assert.True(File.Exists(Path.Combine(root, "RunTs.js")), "expected emitted javascript file")
    )

[<Fact>]
let ``lllc run --target py executes emitted python module`` () =
    withTempDir (fun root ->
        if toolExists "python3" then
            Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
            let srcPath = Path.Combine(root, "RunPy.lll")
            File.WriteAllText(srcPath, "module Demo.RunPy\n\nmain() Int = 0\n")

            let (exitCode, stdout, stderr) = runLllc root ["run"; "--target"; "py"; srcPath]
            Assert.True((exitCode = 0), $"lllc run --target py failed: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
            Assert.Contains("Running: if command -v python", stdout)
            Assert.Contains("RunPy.py", stdout)
            Assert.True(File.Exists(Path.Combine(root, "RunPy.py")), "expected emitted python file")
    )

[<Fact>]
let ``lllc run --target cs executes emitted csharp project`` () =
    withTempDir (fun root ->
        Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
        let srcPath = Path.Combine(root, "RunCs.lll")
        File.WriteAllText(srcPath, "module Demo.RunCs\n\nmain() Int = 0\n")

        let (exitCode, stdout, stderr) = runLllc root ["run"; "--target"; "cs"; srcPath]
        Assert.True((exitCode = 0), $"lllc run --target cs failed: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
        Assert.Contains("Running: dotnet run --project ", stdout)
        Assert.Contains("RunCs.csproj", stdout)
        Assert.True(File.Exists(Path.Combine(root, "RunCs.cs")), "expected emitted csharp file")
        Assert.True(File.Exists(Path.Combine(root, "RunCs.csproj")), "expected emitted csharp project")
    )

[<Fact>]
let ``lllc run --target llvm executes emitted llvm module when lli is available`` () =
    withTempDir (fun root ->
        if toolExists "lli" then
            Assert.True(File.Exists(lllcDll), $"missing lllc tool at {lllcDll}")
            let srcPath = Path.Combine(root, "RunLlvm.lll")
            File.WriteAllText(srcPath, "module Demo.RunLlvm\n\nmain() Int = 0\n")

            let (exitCode, stdout, stderr) = runLllc root ["run"; "--target"; "llvm"; srcPath]
            Assert.True((exitCode = 0), $"lllc run --target llvm failed: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")
            Assert.Contains("Running: lli ", stdout)
            Assert.Contains("RunLlvm.ll", stdout)
            Assert.True(File.Exists(Path.Combine(root, "RunLlvm.ll")), "expected emitted llvm file")
    )
