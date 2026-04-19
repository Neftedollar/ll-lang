module LLLangTests.ModuleSystemTests

open System
open System.IO
open System.Diagnostics
open Xunit
open LLLang.Elaborator
open LLLang.Manifest
open LLLang.ProjectLoader
open LLLang.Compiler

// ---- Manifest parsing tests ------------------------------------------------

[<Fact>]
let ``parseManifest: minimal manifest with name only`` () =
    let src = "[project]\nname = \"myapp\"\n"
    match parseManifest src with
    | Error e -> Assert.Fail(sprintf "Expected Ok but got Error: %s" e)
    | Ok m ->
        Assert.Equal("myapp", m.Name)
        Assert.Equal("0.0.0", m.Version)
        Assert.Equal("src/Main.lll", m.Entry)
        Assert.Empty(m.Deps)
        Assert.Empty(m.Platform)

[<Fact>]
let ``parseManifest: full manifest with all sections`` () =
    let src = "[project]\nname = \"myapp\"\nversion = \"1.2.3\"\nentry = \"src/App.lll\"\n\n[deps]\njson = \"https://github.com/alice/json#v1.0.0\"\n\n[platform]\nuse = [\"Platform.IO\", \"Platform.Math\"]\n"
    match parseManifest src with
    | Error e -> Assert.Fail(sprintf "Expected Ok but got Error: %s" e)
    | Ok m ->
        Assert.Equal("myapp", m.Name)
        Assert.Equal("1.2.3", m.Version)
        Assert.Equal("src/App.lll", m.Entry)
        Assert.Equal(1, m.Deps.Count)
        Assert.Equal(GitDep("https://github.com/alice/json", "v1.0.0"), m.Deps.["json"])
        Assert.Equal<string list>(["Platform.IO"; "Platform.Math"], m.Platform)

[<Fact>]
let ``parseManifest: error on missing project name`` () =
    let src = "[project]\nversion = \"1.0.0\"\n"
    match parseManifest src with
    | Ok _ -> Assert.Fail("Expected Error but got Ok")
    | Error e -> Assert.Contains("name", e)

[<Fact>]
let ``parseManifest: ignores unknown keys and tables`` () =
    let src = "[project]\nname = \"x\"\nunknown_key = \"ignored\"\n[unknown_table]\nfoo = \"bar\"\n"
    match parseManifest src with
    | Error e -> Assert.Fail(sprintf "Expected Ok but got Error: %s" e)
    | Ok m -> Assert.Equal("x", m.Name)

[<Fact>]
let ``parseManifest: comments are ignored`` () =
    let src = "# this is a comment\n[project]\n# another comment\nname = \"proj\"\n"
    match parseManifest src with
    | Error e -> Assert.Fail(sprintf "Expected Ok but got Error: %s" e)
    | Ok m -> Assert.Equal("proj", m.Name)

// ---- Multi-file project tests -----------------------------------------------

let private withTempDir (f: string -> 'a) : 'a =
    let dir = Path.Combine(Path.GetTempPath(), "lll-test-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    try f dir
    finally
        try Directory.Delete(dir, true) with _ -> ()

let private errMsg (es: LLLang.Elaborator.LLError list) =
    es |> List.map (fun e -> e.Message) |> String.concat "; "

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private lllcDllPath =
    Path.Combine(repoRoot, "src/LLLangTool/bin/Debug/net10.0/lllc.dll")

let private runLllc (cwd: string) (args: string list) : int * string * string =
    let psi = ProcessStartInfo("dotnet")
    psi.WorkingDirectory <- cwd
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.ArgumentList.Add(lllcDllPath)
    for a in args do
        psi.ArgumentList.Add(a)
    use proc = LLLang.Tests.TestCompat.startProcess psi
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    (proc.ExitCode, stdout, stderr)

let private runCmd (cwd: string) (exe: string) (args: string list) : int * string * string =
    let psi = ProcessStartInfo(exe)
    psi.WorkingDirectory <- cwd
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    for a in args do
        psi.ArgumentList.Add(a)
    use proc = LLLang.Tests.TestCompat.startProcess psi
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    (proc.ExitCode, stdout, stderr)

[<Fact>]
let ``lllc new: scaffolds modern template that passes project check`` () =
    withTempDir (fun root ->
        let (newCode, newOut, newErr) = runLllc root ["new"; "starter"]
        Assert.True((newCode = 0), sprintf "lllc new failed\nstdout:\n%s\nstderr:\n%s" newOut newErr)

        let projectDir = Path.Combine(root, "starter")
        let manifestPath = Path.Combine(projectDir, "lll.toml")
        let mainPath = Path.Combine(projectDir, "src", "Main.lll")
        Assert.True(File.Exists(manifestPath), "missing scaffolded lll.toml")
        Assert.True(File.Exists(mainPath), "missing scaffolded src/Main.lll")

        let manifest = File.ReadAllText(manifestPath)
        Assert.Contains("[project]", manifest)
        Assert.Contains("name = \"starter\"", manifest)
        Assert.Contains("version = \"0.1.0\"", manifest)
        Assert.Contains("entry = \"src/Main.lll\"", manifest)

        let mainSrc = File.ReadAllText(mainPath)
        Assert.DoesNotContain("fn main()", mainSrc)
        Assert.Contains("main() =", mainSrc)

        let (checkCode, checkOut, checkErr) = runLllc projectDir ["check"]
        Assert.True((checkCode = 0), sprintf "lllc check on scaffold failed\nstdout:\n%s\nstderr:\n%s\nmain:\n%s" checkOut checkErr mainSrc))

[<Fact>]
let ``loadProject: two-file project sorts in dependency order`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"),"[project]\nname = \"hello\"\n")
        File.WriteAllText(Path.Combine(root, "src", "Lib.lll"),
            "module Hello.Lib\n\nexport greet() Str = \"hi\"\n")
        File.WriteAllText(Path.Combine(root, "src", "Main.lll"),
            "module Hello.Main\nimport Hello.Lib\n\nmain() Str = \"main\"\n")
        match loadProject root with
        | Error es -> Assert.Fail(sprintf "loadProject failed: %s" (errMsg es))
        | Ok proj ->
            Assert.Equal(2, proj.Files.Length)
            // Lib must come before Main (dependency order)
            Assert.Equal<string list>(["Hello"; "Lib"], proj.Files.[0].ModulePath)
            Assert.Equal<string list>(["Hello"; "Main"], proj.Files.[1].ModulePath)
    )

[<Fact>]
let ``loadProject: cycle detection returns E024`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"),"[project]\nname = \"cycle\"\n")
        File.WriteAllText(Path.Combine(root, "src", "A.lll"),
            "module Cycle.A\nimport Cycle.B\n\nfa() Str = \"a\"\n")
        File.WriteAllText(Path.Combine(root, "src", "B.lll"),
            "module Cycle.B\nimport Cycle.A\n\nfb() Str = \"b\"\n")
        match loadProject root with
        | Ok _ -> Assert.Fail("Expected E024 cycle error but got Ok")
        | Error es ->
            let hasE024 = es |> List.exists (fun e -> e.Code = E024)
            Assert.True(hasE024, "Expected E024 ModuleCycle error")
    )

[<Fact>]
let ``loadProject: module path mismatch returns E020`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"),"[project]\nname = \"proj\"\n")
        File.WriteAllText(Path.Combine(root, "src", "Foo.lll"),
            "module Wrong.Name\n\nf() Str = \"x\"\n")
        match loadProject root with
        | Ok _ -> Assert.Fail("Expected E020 path mismatch error but got Ok")
        | Error es ->
            let hasE020 = es |> List.exists (fun e -> e.Code = E020)
            Assert.True(hasE020, "Expected E020 ModulePathMismatch error")
    )

[<Fact>]
let ``compileProject: two-file project emits concatenated F# with both modules`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"),"[project]\nname = \"greet\"\n")
        File.WriteAllText(Path.Combine(root, "src", "Lib.lll"),
            "module Greet.Lib\n\nexport greet() Str = \"hello\"\n")
        File.WriteAllText(Path.Combine(root, "src", "Main.lll"),
            "module Greet.Main\nimport Greet.Lib\n\nmain() Str = \"main\"\n")
        match loadProject root with
        | Error es -> Assert.Fail(sprintf "loadProject failed: %s" (errMsg es))
        | Ok proj ->
            match compileProject proj with
            | Error es -> Assert.Fail(sprintf "compileProject failed: %s" (errMsg es))
            | Ok fs ->
                Assert.Contains("module Greet.Lib", fs)
                Assert.Contains("module Greet.Main", fs)
                Assert.True(fs.Length > 50)
    )

[<Fact>]
let ``compileProject: imported polymorphic value can be reused at two concrete types`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"), "[project]\nname = \"poly\"\n")
        File.WriteAllText(
            Path.Combine(root, "src", "Lib.lll"),
            "module Poly.Lib\n\nexport id(x A) A = x\n")
        File.WriteAllText(
            Path.Combine(root, "src", "Main.lll"),
            "module Poly.Main\nimport Poly.Lib\n\nmain() Int =\n  n = id 42\n  s = id \"hi\"\n  n\n")

        match loadProject root with
        | Error es -> Assert.Fail(sprintf "loadProject failed: %s" (errMsg es))
        | Ok proj ->
            match compileProjectToModules proj with
            | Error es ->
                Assert.Fail(sprintf "compileProjectToModules failed (polymorphic import regression): %s" (errMsg es))
            | Ok tms ->
                Assert.Equal(2, tms.Length)
    )

[<Fact>]
let ``compileProject: export list gates imported visibility`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"), "[project]\nname = \"vis\"\n")
        File.WriteAllText(
            Path.Combine(root, "src", "Lib.lll"),
            "module Vis.Lib\n\nexport { pub }\npub() Int = 1\nhidden() Int = 2\n")
        File.WriteAllText(
            Path.Combine(root, "src", "Main.lll"),
            "module Vis.Main\nimport Vis.Lib\n\nmain() Int = hidden()\n")

        match loadProject root with
        | Error es -> Assert.Fail(sprintf "loadProject failed: %s" (errMsg es))
        | Ok proj ->
            match compileProjectToModules proj with
            | Ok _ -> Assert.Fail("Expected E002 on non-exported imported name but got Ok")
            | Error es ->
                let msg = errMsg es
                Assert.Contains("UnboundVar hidden", msg))

[<Fact>]
let ``compileProject: modules without explicit export remain import-visible`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"), "[project]\nname = \"vislegacy\"\n")
        File.WriteAllText(
            Path.Combine(root, "src", "Lib.lll"),
            "module Vislegacy.Lib\n\nhelper() Int = 41\n")
        File.WriteAllText(
            Path.Combine(root, "src", "Main.lll"),
            "module Vislegacy.Main\nimport Vislegacy.Lib\n\nmain() Int = helper() + 1\n")

        match loadProject root with
        | Error es -> Assert.Fail(sprintf "loadProject failed: %s" (errMsg es))
        | Ok proj ->
            match compileProjectToModules proj with
            | Error es ->
                Assert.Fail(sprintf "compileProjectToModules failed (legacy visibility regression): %s" (errMsg es))
            | Ok tms ->
                Assert.Equal(2, tms.Length))

[<Fact>]
let ``compileProject: legacy export decl does not gate visibility without export list`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"), "[project]\nname = \"visold\"\n")
        File.WriteAllText(
            Path.Combine(root, "src", "Lib.lll"),
            "module Visold.Lib\n\nexport pub() Int = 1\nhidden() Int = 2\n")
        File.WriteAllText(
            Path.Combine(root, "src", "Main.lll"),
            "module Visold.Main\nimport Visold.Lib\n\nmain() Int = hidden()\n")

        match loadProject root with
        | Error es -> Assert.Fail(sprintf "loadProject failed: %s" (errMsg es))
        | Ok proj ->
            match compileProjectToModules proj with
            | Error es ->
                Assert.Fail(sprintf "compileProjectToModules failed (legacy export compatibility regression): %s" (errMsg es))
            | Ok tms ->
                Assert.Equal(2, tms.Length))

[<Fact>]
let ``compileProject: non-imported sibling module does not leak names into scope`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"), "[project]\nname = \"leak\"\n")
        File.WriteAllText(
            Path.Combine(root, "src", "A.lll"),
            "module Leak.A\n\nexport v() Int = 1\n")
        File.WriteAllText(
            Path.Combine(root, "src", "ZZ.lll"),
            "module Leak.ZZ\nimport Leak.A\n\nexport v() Str = \"bad\"\n")
        File.WriteAllText(
            Path.Combine(root, "src", "Main.lll"),
            "module Leak.Main\nimport Leak.A\n\nmain() Int = v() + 1\n")

        match loadProject root with
        | Error es -> Assert.Fail(sprintf "loadProject failed: %s" (errMsg es))
        | Ok proj ->
            match compileProjectToModules proj with
            | Error es ->
                Assert.Fail(sprintf "compileProjectToModules failed (import leakage regression): %s" (errMsg es))
            | Ok tms ->
                Assert.Equal(3, tms.Length)
    )

[<Fact>]
let ``loadProject: single-file project loads correctly`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"),"[project]\nname = \"single\"\n")
        File.WriteAllText(Path.Combine(root, "src", "Main.lll"),
            "module Single.Main\n\nmain() Str = \"hello\"\n")
        match loadProject root with
        | Error es -> Assert.Fail(sprintf "loadProject failed: %s" (errMsg es))
        | Ok proj ->
            Assert.Equal("single", proj.Manifest.Name)
            Assert.Equal(1, proj.Files.Length)
            Assert.Equal<string list>(["Single"; "Main"], proj.Files.[0].ModulePath)
    )

// ---- DepSource parsing tests ------------------------------------------------

[<Fact>]
let ``parseManifest: git dep with ref parses to GitDep`` () =
    let src = "[project]\nname = \"app\"\n\n[deps]\nstd = \"https://github.com/user/repo#v0.8.0\"\n"
    match parseManifest src with
    | Error e -> Assert.Fail(sprintf "Expected Ok but got Error: %s" e)
    | Ok m ->
        Assert.Equal(1, m.Deps.Count)
        match m.Deps.["std"] with
        | GitDep(url, ref) ->
            Assert.Equal("https://github.com/user/repo", url)
            Assert.Equal("v0.8.0", ref)
        | PathDep _ -> Assert.Fail("Expected GitDep but got PathDep")

[<Fact>]
let ``parseManifest: git dep without ref defaults to main`` () =
    let src = "[project]\nname = \"app\"\n\n[deps]\nstd = \"https://github.com/user/repo\"\n"
    match parseManifest src with
    | Error e -> Assert.Fail(sprintf "Expected Ok but got Error: %s" e)
    | Ok m ->
        match m.Deps.["std"] with
        | GitDep(url, ref) ->
            Assert.Equal("https://github.com/user/repo", url)
            Assert.Equal("main", ref)
        | PathDep _ -> Assert.Fail("Expected GitDep but got PathDep")

[<Fact>]
let ``parseManifest: path dep parses to PathDep`` () =
    let src = "[project]\nname = \"app\"\n\n[deps]\njson = { path = \"../ll-json\" }\n"
    match parseManifest src with
    | Error e -> Assert.Fail(sprintf "Expected Ok but got Error: %s" e)
    | Ok m ->
        match m.Deps.["json"] with
        | PathDep p -> Assert.Equal("../ll-json", p)
        | GitDep _ -> Assert.Fail("Expected PathDep but got GitDep")

// ---- Path dep loading tests -------------------------------------------------

[<Fact>]
let ``loadProject: path dep files included in sorted results`` () =
    withTempDir (fun root ->
        // Set up main project
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\nmylib = { path = \"../mylib\" }\n")
        File.WriteAllText(Path.Combine(root, "src", "Main.lll"),
            "module App.Main\nimport Mylib.Util\n\nmain() Str = \"hello\"\n")

        // Set up dep project in a sibling directory
        withTempDir (fun libRoot ->
            Directory.CreateDirectory(Path.Combine(libRoot, "src")) |> ignore
            File.WriteAllText(Path.Combine(libRoot, "lll.toml"), "[project]\nname = \"mylib\"\n")
            File.WriteAllText(Path.Combine(libRoot, "src", "Util.lll"),
                "module Mylib.Util\n\nexport greet() Str = \"hi\"\n")

            // Simulate vendored dep layout used by ProjectLoader.
            let depsDir = Path.Combine(root, "vendor")
            Directory.CreateDirectory(depsDir) |> ignore
            // Copy the dep directory contents instead of symlinking (portable test)
            let depTarget = Path.Combine(depsDir, "mylib")
            Directory.CreateDirectory(Path.Combine(depTarget, "src")) |> ignore
            File.WriteAllText(Path.Combine(depTarget, "lll.toml"), "[project]\nname = \"mylib\"\n")
            File.WriteAllText(Path.Combine(depTarget, "src", "Util.lll"),
                "module Mylib.Util\n\nexport greet() Str = \"hi\"\n")

            match loadProject root with
            | Error es -> Assert.Fail(sprintf "loadProject failed: %s" (errMsg es))
            | Ok proj ->
                // Should have both Mylib.Util and App.Main
                Assert.Equal(2, proj.Files.Length)
                // Mylib.Util (dep) must come before App.Main (depends on it)
                Assert.Equal<string list>(["Mylib"; "Util"], proj.Files.[0].ModulePath)
                Assert.Equal<string list>(["App"; "Main"], proj.Files.[1].ModulePath)
        )
    )

[<Fact>]
let ``lllc install resolves transitive path deps into vendor and keeps ll.sum deterministic`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depARoot = Path.Combine(root, "dep-a")
        let depBRoot = Path.Combine(root, "dep-b")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depARoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(appRoot, "vendor", "stale")) |> ignore

        File.WriteAllText(Path.Combine(depBRoot, "lll.toml"), "[project]\nname = \"depb\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(depARoot, "lll.toml"),
            "[project]\nname = \"depa\"\n\n[deps]\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(depARoot, "src", "Main.lll"), "module Depa.Main\n\nexport v() Int = 2\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndepa = { path = \"../dep-a\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code1, out1, err1) = runLllc appRoot ["install"]
        Assert.True((code1 = 0), $"first install failed\nstdout:\n{out1}\nstderr:\n{err1}")

        let depAVendor = Path.Combine(appRoot, "vendor", "depa")
        let depBVendor = Path.Combine(appRoot, "vendor", "depb")
        let staleVendor = Path.Combine(appRoot, "vendor", "stale")
        Assert.True(Directory.Exists(depAVendor), "vendor/depa should exist after install")
        Assert.True(Directory.Exists(depBVendor), "vendor/depb should exist after install")
        Assert.False(Directory.Exists(staleVendor), "stale vendor dir should be removed during sync")

        let llSumPath = Path.Combine(appRoot, "ll.sum")
        Assert.True(File.Exists(llSumPath), "ll.sum should be produced by install")
        let sum1 = File.ReadAllText(llSumPath)
        Assert.Contains("depa path:../dep-a sha256:", sum1)
        Assert.Contains("depb path:../dep-b sha256:", sum1)

        let (code2, out2, err2) = runLllc appRoot ["install"]
        Assert.True((code2 = 0), $"second install failed\nstdout:\n{out2}\nstderr:\n{err2}")
        let sum2 = File.ReadAllText(llSumPath)
        Assert.Equal(sum1, sum2)
    )

[<Fact>]
let ``lllc install converges deterministically when transitive path deps resolve same name from different sources`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depARoot = Path.Combine(root, "dep-a")
        let depBRoot = Path.Combine(root, "dep-b")
        let commonV1Root = Path.Combine(root, "common-v1")
        let commonV2Root = Path.Combine(root, "common-v2")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depARoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonV1Root, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonV2Root, "src")) |> ignore

        File.WriteAllText(Path.Combine(commonV1Root, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonV1Root, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 1\n")
        File.WriteAllText(Path.Combine(commonV2Root, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonV2Root, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 2\n")

        File.WriteAllText(
            Path.Combine(depARoot, "lll.toml"),
            "[project]\nname = \"depa\"\n\n[deps]\ncommon = { path = \"../common-v1\" }\n")
        File.WriteAllText(Path.Combine(depARoot, "src", "Main.lll"), "module Depa.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(depBRoot, "lll.toml"),
            "[project]\nname = \"depb\"\n\n[deps]\ncommon = { path = \"../common-v2\" }\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndepa = { path = \"../dep-a\" }\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code, outText, errText) = runLllc appRoot ["install"]
        Assert.True((code = 0), $"install should converge to deterministic path winner\nstdout:\n{outText}\nstderr:\n{errText}")
        let resolvedCommonMain = Path.Combine(appRoot, "vendor", "common", "src", "Main.lll")
        Assert.True(File.Exists(resolvedCommonMain), "vendor/common/src/Main.lll should exist after install")
        let commonMain = File.ReadAllText(resolvedCommonMain)
        // Canonical path lexical winner: .../common-v2 > .../common-v1.
        Assert.Contains("export v() Int = 2", commonMain)
    )

[<Fact>]
let ``lllc install prefers highest semver when transitive git deps resolve same name`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depARoot = Path.Combine(root, "dep-a")
        let depBRoot = Path.Combine(root, "dep-b")
        let commonRepo = Path.Combine(root, "common-repo")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depARoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonRepo, "src")) |> ignore

        let ensureGitOk (code: int, so: string, se: string) (ctx: string) =
            Assert.True((code = 0), $"git command failed ({ctx})\nstdout:\n{so}\nstderr:\n{se}")

        // Build local git repo with two commits and two tags.
        File.WriteAllText(Path.Combine(commonRepo, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 1\n")
        runCmd commonRepo "git" ["init"] |> fun r -> ensureGitOk r "init"
        runCmd commonRepo "git" ["config"; "user.email"; "tests@example.com"] |> fun r -> ensureGitOk r "config email"
        runCmd commonRepo "git" ["config"; "user.name"; "LLLang Tests"] |> fun r -> ensureGitOk r "config name"
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add v1"
        runCmd commonRepo "git" ["commit"; "-m"; "v1"] |> fun r -> ensureGitOk r "commit v1"
        runCmd commonRepo "git" ["tag"; "v1"] |> fun r -> ensureGitOk r "tag v1"

        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 2\n")
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add v2"
        runCmd commonRepo "git" ["commit"; "-m"; "v2"] |> fun r -> ensureGitOk r "commit v2"
        runCmd commonRepo "git" ["tag"; "v2"] |> fun r -> ensureGitOk r "tag v2"

        let repoUrl = commonRepo.Replace("\\", "/")

        File.WriteAllText(
            Path.Combine(depARoot, "lll.toml"),
            "[project]\nname = \"depa\"\n\n[deps]\ncommon = \"" + repoUrl + "#v1\"\n")
        File.WriteAllText(Path.Combine(depARoot, "src", "Main.lll"), "module Depa.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(depBRoot, "lll.toml"),
            "[project]\nname = \"depb\"\n\n[deps]\ncommon = \"" + repoUrl + "#v2\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndepa = { path = \"../dep-a\" }\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code, outText, errText) = runLllc appRoot ["install"]
        Assert.True((code = 0), $"install should pick highest semver ref\nstdout:\n{outText}\nstderr:\n{errText}")
        let resolvedCommonMain = Path.Combine(appRoot, "vendor", "common", "src", "Main.lll")
        Assert.True(File.Exists(resolvedCommonMain), "vendor/common/src/Main.lll should exist after install")
        let commonMain = File.ReadAllText(resolvedCommonMain)
        Assert.Contains("export v() Int = 2", commonMain)
    )

[<Fact>]
let ``lllc install prefers higher transitive semver over lower direct git dep`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depBRoot = Path.Combine(root, "dep-b")
        let commonRepo = Path.Combine(root, "common-repo")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonRepo, "src")) |> ignore

        let ensureGitOk (code: int, so: string, se: string) (ctx: string) =
            Assert.True((code = 0), $"git command failed ({ctx})\nstdout:\n{so}\nstderr:\n{se}")

        File.WriteAllText(Path.Combine(commonRepo, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 1\n")
        runCmd commonRepo "git" ["init"] |> fun r -> ensureGitOk r "init"
        runCmd commonRepo "git" ["config"; "user.email"; "tests@example.com"] |> fun r -> ensureGitOk r "config email"
        runCmd commonRepo "git" ["config"; "user.name"; "LLLang Tests"] |> fun r -> ensureGitOk r "config name"
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add v1"
        runCmd commonRepo "git" ["commit"; "-m"; "v1"] |> fun r -> ensureGitOk r "commit v1"
        runCmd commonRepo "git" ["tag"; "v1"] |> fun r -> ensureGitOk r "tag v1"

        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 2\n")
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add v2"
        runCmd commonRepo "git" ["commit"; "-m"; "v2"] |> fun r -> ensureGitOk r "commit v2"
        runCmd commonRepo "git" ["tag"; "v2"] |> fun r -> ensureGitOk r "tag v2"

        let repoUrl = commonRepo.Replace("\\", "/")

        File.WriteAllText(
            Path.Combine(depBRoot, "lll.toml"),
            "[project]\nname = \"depb\"\n\n[deps]\ncommon = \"" + repoUrl + "#v2\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ncommon = \"" + repoUrl + "#v1\"\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code, outText, errText) = runLllc appRoot ["install"]
        Assert.True((code = 0), $"install should converge to higher transitive semver\nstdout:\n{outText}\nstderr:\n{errText}")
        let resolvedCommonMain = Path.Combine(appRoot, "vendor", "common", "src", "Main.lll")
        Assert.True(File.Exists(resolvedCommonMain), "vendor/common/src/Main.lll should exist after install")
        let commonMain = File.ReadAllText(resolvedCommonMain)
        Assert.Contains("export v() Int = 2", commonMain)
    )

[<Fact>]
let ``lllc install semver compare handles multi-digit components deterministically`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depARoot = Path.Combine(root, "dep-a")
        let depBRoot = Path.Combine(root, "dep-b")
        let commonRepo = Path.Combine(root, "common-repo")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depARoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonRepo, "src")) |> ignore

        let ensureGitOk (code: int, so: string, se: string) (ctx: string) =
            Assert.True((code = 0), $"git command failed ({ctx})\nstdout:\n{so}\nstderr:\n{se}")

        File.WriteAllText(Path.Combine(commonRepo, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 12\n")
        runCmd commonRepo "git" ["init"] |> fun r -> ensureGitOk r "init"
        runCmd commonRepo "git" ["config"; "user.email"; "tests@example.com"] |> fun r -> ensureGitOk r "config email"
        runCmd commonRepo "git" ["config"; "user.name"; "LLLang Tests"] |> fun r -> ensureGitOk r "config name"
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add v1.2.0"
        runCmd commonRepo "git" ["commit"; "-m"; "v1.2.0"] |> fun r -> ensureGitOk r "commit v1.2.0"
        runCmd commonRepo "git" ["tag"; "v1.2.0"] |> fun r -> ensureGitOk r "tag v1.2.0"

        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 110\n")
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add v1.10.0"
        runCmd commonRepo "git" ["commit"; "-m"; "v1.10.0"] |> fun r -> ensureGitOk r "commit v1.10.0"
        runCmd commonRepo "git" ["tag"; "v1.10.0"] |> fun r -> ensureGitOk r "tag v1.10.0"

        let repoUrl = commonRepo.Replace("\\", "/")

        File.WriteAllText(
            Path.Combine(depARoot, "lll.toml"),
            "[project]\nname = \"depa\"\n\n[deps]\ncommon = \"" + repoUrl + "#v1.2.0\"\n")
        File.WriteAllText(Path.Combine(depARoot, "src", "Main.lll"), "module Depa.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(depBRoot, "lll.toml"),
            "[project]\nname = \"depb\"\n\n[deps]\ncommon = \"" + repoUrl + "#v1.10.0\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndepa = { path = \"../dep-a\" }\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code, outText, errText) = runLllc appRoot ["install"]
        Assert.True((code = 0), $"install should prefer v1.10.0 over v1.2.0\nstdout:\n{outText}\nstderr:\n{errText}")
        let resolvedCommonMain = Path.Combine(appRoot, "vendor", "common", "src", "Main.lll")
        Assert.True(File.Exists(resolvedCommonMain), "vendor/common/src/Main.lll should exist after install")
        let commonMain = File.ReadAllText(resolvedCommonMain)
        Assert.Contains("export v() Int = 110", commonMain)
    )

[<Fact>]
let ``lllc install semver convergence is idempotent across repeated runs`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depBRoot = Path.Combine(root, "dep-b")
        let commonRepo = Path.Combine(root, "common-repo")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonRepo, "src")) |> ignore

        let ensureGitOk (code: int, so: string, se: string) (ctx: string) =
            Assert.True((code = 0), $"git command failed ({ctx})\nstdout:\n{so}\nstderr:\n{se}")

        File.WriteAllText(Path.Combine(commonRepo, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 1\n")
        runCmd commonRepo "git" ["init"] |> fun r -> ensureGitOk r "init"
        runCmd commonRepo "git" ["config"; "user.email"; "tests@example.com"] |> fun r -> ensureGitOk r "config email"
        runCmd commonRepo "git" ["config"; "user.name"; "LLLang Tests"] |> fun r -> ensureGitOk r "config name"
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add v1"
        runCmd commonRepo "git" ["commit"; "-m"; "v1"] |> fun r -> ensureGitOk r "commit v1"
        runCmd commonRepo "git" ["tag"; "v1"] |> fun r -> ensureGitOk r "tag v1"

        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 2\n")
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add v2"
        runCmd commonRepo "git" ["commit"; "-m"; "v2"] |> fun r -> ensureGitOk r "commit v2"
        runCmd commonRepo "git" ["tag"; "v2"] |> fun r -> ensureGitOk r "tag v2"

        let repoUrl = commonRepo.Replace("\\", "/")
        File.WriteAllText(
            Path.Combine(depBRoot, "lll.toml"),
            "[project]\nname = \"depb\"\n\n[deps]\ncommon = \"" + repoUrl + "#v2\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ncommon = \"" + repoUrl + "#v1\"\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code1, out1, err1) = runLllc appRoot ["install"]
        Assert.True((code1 = 0), $"first install should converge\nstdout:\n{out1}\nstderr:\n{err1}")
        let llSumPath = Path.Combine(appRoot, "ll.sum")
        Assert.True(File.Exists(llSumPath), "ll.sum should exist after first install")
        let sum1 = File.ReadAllText(llSumPath)
        let commonMain1 = File.ReadAllText(Path.Combine(appRoot, "vendor", "common", "src", "Main.lll"))
        Assert.Contains("export v() Int = 2", commonMain1)

        let (code2, out2, err2) = runLllc appRoot ["install"]
        Assert.True((code2 = 0), $"second install should stay stable\nstdout:\n{out2}\nstderr:\n{err2}")
        let sum2 = File.ReadAllText(llSumPath)
        Assert.Equal(sum1, sum2)
        let commonMain2 = File.ReadAllText(Path.Combine(appRoot, "vendor", "common", "src", "Main.lll"))
        Assert.Contains("export v() Int = 2", commonMain2)
    )

[<Fact>]
let ``lllc install prefers deterministic winner when transitive git deps resolve same name from non-semver refs`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depARoot = Path.Combine(root, "dep-a")
        let depBRoot = Path.Combine(root, "dep-b")
        let commonRepo = Path.Combine(root, "common-repo")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depARoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonRepo, "src")) |> ignore

        let ensureGitOk (code: int, so: string, se: string) (ctx: string) =
            Assert.True((code = 0), $"git command failed ({ctx})\nstdout:\n{so}\nstderr:\n{se}")

        File.WriteAllText(Path.Combine(commonRepo, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 10\n")
        runCmd commonRepo "git" ["init"] |> fun r -> ensureGitOk r "init"
        runCmd commonRepo "git" ["config"; "user.email"; "tests@example.com"] |> fun r -> ensureGitOk r "config email"
        runCmd commonRepo "git" ["config"; "user.name"; "LLLang Tests"] |> fun r -> ensureGitOk r "config name"
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add alpha"
        runCmd commonRepo "git" ["commit"; "-m"; "alpha"] |> fun r -> ensureGitOk r "commit alpha"
        runCmd commonRepo "git" ["tag"; "alpha"] |> fun r -> ensureGitOk r "tag alpha"

        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 20\n")
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add beta"
        runCmd commonRepo "git" ["commit"; "-m"; "beta"] |> fun r -> ensureGitOk r "commit beta"
        runCmd commonRepo "git" ["tag"; "beta"] |> fun r -> ensureGitOk r "tag beta"

        let repoUrl = commonRepo.Replace("\\", "/")

        File.WriteAllText(
            Path.Combine(depARoot, "lll.toml"),
            "[project]\nname = \"depa\"\n\n[deps]\ncommon = \"" + repoUrl + "#alpha\"\n")
        File.WriteAllText(Path.Combine(depARoot, "src", "Main.lll"), "module Depa.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(depBRoot, "lll.toml"),
            "[project]\nname = \"depb\"\n\n[deps]\ncommon = \"" + repoUrl + "#beta\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndepa = { path = \"../dep-a\" }\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code, outText, errText) = runLllc appRoot ["install"]
        Assert.True((code = 0), $"install should pick deterministic non-semver winner\nstdout:\n{outText}\nstderr:\n{errText}")
        let resolvedCommonMain = Path.Combine(appRoot, "vendor", "common", "src", "Main.lll")
        Assert.True(File.Exists(resolvedCommonMain), "vendor/common/src/Main.lll should exist after install")
        let commonMain = File.ReadAllText(resolvedCommonMain)
        // beta > alpha lexically, so resolver should converge to beta.
        Assert.Contains("export v() Int = 20", commonMain)
    )

[<Fact>]
let ``lllc install prefers semver tag over non-semver ref for same-repo git conflicts`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depARoot = Path.Combine(root, "dep-a")
        let depBRoot = Path.Combine(root, "dep-b")
        let commonRepo = Path.Combine(root, "common-repo")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depARoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonRepo, "src")) |> ignore

        let ensureGitOk (code: int, so: string, se: string) (ctx: string) =
            Assert.True((code = 0), $"git command failed ({ctx})\nstdout:\n{so}\nstderr:\n{se}")

        File.WriteAllText(Path.Combine(commonRepo, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 10\n")
        runCmd commonRepo "git" ["init"] |> fun r -> ensureGitOk r "init"
        runCmd commonRepo "git" ["config"; "user.email"; "tests@example.com"] |> fun r -> ensureGitOk r "config email"
        runCmd commonRepo "git" ["config"; "user.name"; "LLLang Tests"] |> fun r -> ensureGitOk r "config name"
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add v1.0.0"
        runCmd commonRepo "git" ["commit"; "-m"; "v1.0.0"] |> fun r -> ensureGitOk r "commit v1.0.0"
        runCmd commonRepo "git" ["tag"; "v1.0.0"] |> fun r -> ensureGitOk r "tag v1.0.0"

        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 20\n")
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add alpha"
        runCmd commonRepo "git" ["commit"; "-m"; "alpha"] |> fun r -> ensureGitOk r "commit alpha"
        runCmd commonRepo "git" ["tag"; "alpha"] |> fun r -> ensureGitOk r "tag alpha"

        let repoUrl = commonRepo.Replace("\\", "/")

        File.WriteAllText(
            Path.Combine(depARoot, "lll.toml"),
            "[project]\nname = \"depa\"\n\n[deps]\ncommon = \"" + repoUrl + "#alpha\"\n")
        File.WriteAllText(Path.Combine(depARoot, "src", "Main.lll"), "module Depa.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(depBRoot, "lll.toml"),
            "[project]\nname = \"depb\"\n\n[deps]\ncommon = \"" + repoUrl + "#v1.0.0\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndepa = { path = \"../dep-a\" }\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code, outText, errText) = runLllc appRoot ["install"]
        Assert.True((code = 0), $"install should prefer semver over non-semver\nstdout:\n{outText}\nstderr:\n{errText}")
        let resolvedCommonMain = Path.Combine(appRoot, "vendor", "common", "src", "Main.lll")
        Assert.True(File.Exists(resolvedCommonMain), "vendor/common/src/Main.lll should exist after install")
        let commonMain = File.ReadAllText(resolvedCommonMain)
        Assert.Contains("export v() Int = 10", commonMain)
    )

[<Fact>]
let ``lllc install uses ll.sum pinned source for non-semver same-repo conflicts`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depARoot = Path.Combine(root, "dep-a")
        let depBRoot = Path.Combine(root, "dep-b")
        let commonRepo = Path.Combine(root, "common-repo")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depARoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonRepo, "src")) |> ignore

        let ensureGitOk (code: int, so: string, se: string) (ctx: string) =
            Assert.True((code = 0), $"git command failed ({ctx})\nstdout:\n{so}\nstderr:\n{se}")

        File.WriteAllText(Path.Combine(commonRepo, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 10\n")
        runCmd commonRepo "git" ["init"] |> fun r -> ensureGitOk r "init"
        runCmd commonRepo "git" ["config"; "user.email"; "tests@example.com"] |> fun r -> ensureGitOk r "config email"
        runCmd commonRepo "git" ["config"; "user.name"; "LLLang Tests"] |> fun r -> ensureGitOk r "config name"
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add alpha"
        runCmd commonRepo "git" ["commit"; "-m"; "alpha"] |> fun r -> ensureGitOk r "commit alpha"
        runCmd commonRepo "git" ["tag"; "alpha"] |> fun r -> ensureGitOk r "tag alpha"

        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 20\n")
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "add beta"
        runCmd commonRepo "git" ["commit"; "-m"; "beta"] |> fun r -> ensureGitOk r "commit beta"
        runCmd commonRepo "git" ["tag"; "beta"] |> fun r -> ensureGitOk r "tag beta"

        let repoUrl = commonRepo.Replace("\\", "/")

        File.WriteAllText(
            Path.Combine(depARoot, "lll.toml"),
            "[project]\nname = \"depa\"\n\n[deps]\ncommon = \"" + repoUrl + "#alpha\"\n")
        File.WriteAllText(Path.Combine(depARoot, "src", "Main.lll"), "module Depa.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(depBRoot, "lll.toml"),
            "[project]\nname = \"depb\"\n\n[deps]\ncommon = \"" + repoUrl + "#beta\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndepa = { path = \"../dep-a\" }\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        // Pin winner in lock file: resolver should follow this over deterministic lexical winner.
        File.WriteAllText(
            Path.Combine(appRoot, "ll.sum"),
            "common git:" + repoUrl + "#alpha sha256:lockpin\n")

        let (code, outText, errText) = runLllc appRoot ["install"]
        Assert.True((code = 0), $"install should respect ll.sum non-semver pin\nstdout:\n{outText}\nstderr:\n{errText}")
        let resolvedCommonMain = Path.Combine(appRoot, "vendor", "common", "src", "Main.lll")
        Assert.True(File.Exists(resolvedCommonMain), "vendor/common/src/Main.lll should exist after install")
        let commonMain = File.ReadAllText(resolvedCommonMain)
        Assert.Contains("export v() Int = 10", commonMain)
    )

[<Fact>]
let ``lllc install resolves same-name deps from different git repos by semver winner`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depARoot = Path.Combine(root, "dep-a")
        let depBRoot = Path.Combine(root, "dep-b")
        let commonRepoA = Path.Combine(root, "common-repo-a")
        let commonRepoB = Path.Combine(root, "common-repo-b")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depARoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonRepoA, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonRepoB, "src")) |> ignore

        let ensureGitOk (code: int, so: string, se: string) (ctx: string) =
            Assert.True((code = 0), $"git command failed ({ctx})\nstdout:\n{so}\nstderr:\n{se}")

        File.WriteAllText(Path.Combine(commonRepoA, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonRepoA, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 12\n")
        runCmd commonRepoA "git" ["init"] |> fun r -> ensureGitOk r "repoA init"
        runCmd commonRepoA "git" ["config"; "user.email"; "tests@example.com"] |> fun r -> ensureGitOk r "repoA config email"
        runCmd commonRepoA "git" ["config"; "user.name"; "LLLang Tests"] |> fun r -> ensureGitOk r "repoA config name"
        runCmd commonRepoA "git" ["add"; "."] |> fun r -> ensureGitOk r "repoA add"
        runCmd commonRepoA "git" ["commit"; "-m"; "repoA v1.2.0"] |> fun r -> ensureGitOk r "repoA commit"
        runCmd commonRepoA "git" ["tag"; "v1.2.0"] |> fun r -> ensureGitOk r "repoA tag"

        File.WriteAllText(Path.Combine(commonRepoB, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonRepoB, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 110\n")
        runCmd commonRepoB "git" ["init"] |> fun r -> ensureGitOk r "repoB init"
        runCmd commonRepoB "git" ["config"; "user.email"; "tests@example.com"] |> fun r -> ensureGitOk r "repoB config email"
        runCmd commonRepoB "git" ["config"; "user.name"; "LLLang Tests"] |> fun r -> ensureGitOk r "repoB config name"
        runCmd commonRepoB "git" ["add"; "."] |> fun r -> ensureGitOk r "repoB add"
        runCmd commonRepoB "git" ["commit"; "-m"; "repoB v1.10.0"] |> fun r -> ensureGitOk r "repoB commit"
        runCmd commonRepoB "git" ["tag"; "v1.10.0"] |> fun r -> ensureGitOk r "repoB tag"

        let repoUrlA = commonRepoA.Replace("\\", "/")
        let repoUrlB = commonRepoB.Replace("\\", "/")

        File.WriteAllText(
            Path.Combine(depARoot, "lll.toml"),
            "[project]\nname = \"depa\"\n\n[deps]\ncommon = \"" + repoUrlA + "#v1.2.0\"\n")
        File.WriteAllText(Path.Combine(depARoot, "src", "Main.lll"), "module Depa.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(depBRoot, "lll.toml"),
            "[project]\nname = \"depb\"\n\n[deps]\ncommon = \"" + repoUrlB + "#v1.10.0\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndepa = { path = \"../dep-a\" }\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code, outText, errText) = runLllc appRoot ["install"]
        Assert.True((code = 0), $"install should converge across cross-repo semver conflict\nstdout:\n{outText}\nstderr:\n{errText}")
        let resolvedCommonMain = Path.Combine(appRoot, "vendor", "common", "src", "Main.lll")
        Assert.True(File.Exists(resolvedCommonMain), "vendor/common/src/Main.lll should exist after install")
        let commonMain = File.ReadAllText(resolvedCommonMain)
        Assert.Contains("export v() Int = 110", commonMain)
    )

[<Fact>]
let ``lllc install prefers direct path dep over transitive git dep with same name`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depBRoot = Path.Combine(root, "dep-b")
        let commonLocal = Path.Combine(root, "common-local")
        let commonRepo = Path.Combine(root, "common-repo")

        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonLocal, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(commonRepo, "src")) |> ignore

        let ensureGitOk (code: int, so: string, se: string) (ctx: string) =
            Assert.True((code = 0), $"git command failed ({ctx})\nstdout:\n{so}\nstderr:\n{se}")

        File.WriteAllText(Path.Combine(commonLocal, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonLocal, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 77\n")

        File.WriteAllText(Path.Combine(commonRepo, "lll.toml"), "[project]\nname = \"common\"\n")
        File.WriteAllText(Path.Combine(commonRepo, "src", "Main.lll"), "module Common.Main\n\nexport v() Int = 9\n")
        runCmd commonRepo "git" ["init"] |> fun r -> ensureGitOk r "repo init"
        runCmd commonRepo "git" ["config"; "user.email"; "tests@example.com"] |> fun r -> ensureGitOk r "repo config email"
        runCmd commonRepo "git" ["config"; "user.name"; "LLLang Tests"] |> fun r -> ensureGitOk r "repo config name"
        runCmd commonRepo "git" ["add"; "."] |> fun r -> ensureGitOk r "repo add"
        runCmd commonRepo "git" ["commit"; "-m"; "repo v9"] |> fun r -> ensureGitOk r "repo commit"
        runCmd commonRepo "git" ["tag"; "v9.0.0"] |> fun r -> ensureGitOk r "repo tag"
        let repoUrl = commonRepo.Replace("\\", "/")

        File.WriteAllText(
            Path.Combine(depBRoot, "lll.toml"),
            "[project]\nname = \"depb\"\n\n[deps]\ncommon = \"" + repoUrl + "#v9.0.0\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ncommon = { path = \"../common-local\" }\ndepb = { path = \"../dep-b\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code, outText, errText) = runLllc appRoot ["install"]
        Assert.True((code = 0), $"install should prefer direct path dep as winner\nstdout:\n{outText}\nstderr:\n{errText}")
        let resolvedCommonMain = Path.Combine(appRoot, "vendor", "common", "src", "Main.lll")
        Assert.True(File.Exists(resolvedCommonMain), "vendor/common/src/Main.lll should exist after install")
        let commonMain = File.ReadAllText(resolvedCommonMain)
        Assert.Contains("export v() Int = 77", commonMain)
    )

[<Fact>]
let ``lllc mod add with path source updates manifest and installs dependency`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depRoot = Path.Combine(root, "dep")
        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depRoot, "src")) |> ignore

        File.WriteAllText(Path.Combine(appRoot, "lll.toml"), "[project]\nname = \"app\"\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")
        File.WriteAllText(Path.Combine(depRoot, "lll.toml"), "[project]\nname = \"dep\"\n")
        File.WriteAllText(Path.Combine(depRoot, "src", "Util.lll"), "module Dep.Util\n\nexport value() Int = 1\n")

        let rel = Path.GetRelativePath(appRoot, depRoot).Replace('\\', '/')
        let (code, outText, errText) = runLllc appRoot ["mod"; "add"; "dep=" + "path:" + rel]
        Assert.True((code = 0), $"mod add should succeed\nstdout:\n{outText}\nstderr:\n{errText}")

        let manifest = File.ReadAllText(Path.Combine(appRoot, "lll.toml"))
        Assert.Contains("dep = { path = \"" + rel + "\" }", manifest)
        Assert.True(Directory.Exists(Path.Combine(appRoot, "vendor", "dep")), "vendor/dep should exist after mod add")
        Assert.True(File.Exists(Path.Combine(appRoot, "ll.sum")), "ll.sum should exist after mod add")
    )

[<Fact>]
let ``lllc mod tidy removes stale vendor entries`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depRoot = Path.Combine(root, "dep")
        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(appRoot, "vendor", "stale")) |> ignore

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndep = { path = \"../dep\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")
        File.WriteAllText(Path.Combine(depRoot, "lll.toml"), "[project]\nname = \"dep\"\n")
        File.WriteAllText(Path.Combine(depRoot, "src", "Util.lll"), "module Dep.Util\n\nexport value() Int = 1\n")

        let (code, outText, errText) = runLllc appRoot ["mod"; "tidy"]
        Assert.True((code = 0), $"mod tidy should succeed\nstdout:\n{outText}\nstderr:\n{errText}")
        Assert.False(Directory.Exists(Path.Combine(appRoot, "vendor", "stale")), "stale vendor dir should be removed by mod tidy")
        Assert.True(Directory.Exists(Path.Combine(appRoot, "vendor", "dep")), "declared dependency should remain in vendor after mod tidy")
    )

[<Fact>]
let ``lllc mod why reports local importers for declared dependency`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depRoot = Path.Combine(root, "dep")
        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depRoot, "src")) |> ignore

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndep = { path = \"../dep\" }\n")
        File.WriteAllText(
            Path.Combine(appRoot, "src", "Main.lll"),
            "module App.Main\nimport Dep.Util\n\nmain() Int = value()\n")
        File.WriteAllText(Path.Combine(depRoot, "lll.toml"), "[project]\nname = \"dep\"\n")
        File.WriteAllText(Path.Combine(depRoot, "src", "Util.lll"), "module Dep.Util\n\nexport value() Int = 1\n")

        let (installCode, installOut, installErr) = runLllc appRoot ["install"]
        Assert.True((installCode = 0), $"install should succeed before mod why\nstdout:\n{installOut}\nstderr:\n{installErr}")

        let (code, outText, errText) = runLllc appRoot ["mod"; "why"; "dep"]
        Assert.True((code = 0), $"mod why should succeed for declared dependency\nstdout:\n{outText}\nstderr:\n{errText}")
        Assert.Contains("dep is imported by:", outText)
        Assert.Contains("App.Main", outText)
        Assert.Contains("dependency chain: app -> dep", outText)
    )

[<Fact>]
let ``lllc mod why reports transitive dependency chain`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        let depARoot = Path.Combine(root, "depa")
        let depBRoot = Path.Combine(root, "depb")
        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depARoot, "src")) |> ignore
        Directory.CreateDirectory(Path.Combine(depBRoot, "src")) |> ignore

        File.WriteAllText(Path.Combine(depBRoot, "lll.toml"), "[project]\nname = \"depb\"\n")
        File.WriteAllText(Path.Combine(depBRoot, "src", "Main.lll"), "module Depb.Main\n\nexport v() Int = 1\n")

        File.WriteAllText(
            Path.Combine(depARoot, "lll.toml"),
            "[project]\nname = \"depa\"\n\n[deps]\ndepb = { path = \"../depb\" }\n")
        File.WriteAllText(Path.Combine(depARoot, "src", "Main.lll"), "module Depa.Main\n\nexport v() Int = 2\n")

        File.WriteAllText(
            Path.Combine(appRoot, "lll.toml"),
            "[project]\nname = \"app\"\n\n[deps]\ndepa = { path = \"../depa\" }\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\nimport Depa.Main\n\nmain() Int = v()\n")

        let (installCode, installOut, installErr) = runLllc appRoot ["install"]
        Assert.True((installCode = 0), $"install should succeed before mod why depb\nstdout:\n{installOut}\nstderr:\n{installErr}")

        let (code, outText, errText) = runLllc appRoot ["mod"; "why"; "depb"]
        Assert.True((code = 0), $"mod why should resolve transitive dependency chain\nstdout:\n{outText}\nstderr:\n{errText}")
        Assert.Contains("no local modules directly import depb", outText)
        Assert.Contains("dependency chain: app -> depa -> depb", outText)
    )

[<Fact>]
let ``lllc mod why fails for undeclared dependency`` () =
    withTempDir (fun root ->
        let appRoot = Path.Combine(root, "app")
        Directory.CreateDirectory(Path.Combine(appRoot, "src")) |> ignore
        File.WriteAllText(Path.Combine(appRoot, "lll.toml"), "[project]\nname = \"app\"\n")
        File.WriteAllText(Path.Combine(appRoot, "src", "Main.lll"), "module App.Main\n\nmain() Int = 0\n")

        let (code, outText, errText) = runLllc appRoot ["mod"; "why"; "missing"]
        Assert.True((code <> 0), $"mod why should fail for undeclared dependency\nstdout:\n{outText}\nstderr:\n{errText}")
        Assert.Contains("dependency 'missing' is not present in resolved dependency graph", errText)
    )
