module LLLangTests.ModuleSystemTests

open System
open System.IO
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
    let src = "[project]\nname = \"myapp\"\nversion = \"1.2.3\"\nentry = \"src/App.lll\"\n\n[deps]\n\"github.com/alice/json\" = \"v1.0.0\"\n\n[platform]\nuse = [\"Platform.IO\", \"Platform.Math\"]\n"
    match parseManifest src with
    | Error e -> Assert.Fail(sprintf "Expected Ok but got Error: %s" e)
    | Ok m ->
        Assert.Equal("myapp", m.Name)
        Assert.Equal("1.2.3", m.Version)
        Assert.Equal("src/App.lll", m.Entry)
        Assert.Equal(1, m.Deps.Count)
        Assert.Equal("v1.0.0", m.Deps.["github.com/alice/json"])
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

[<Fact>]
let ``loadProject: two-file project sorts in dependency order`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "ll.toml"), "[project]\nname = \"hello\"\n")
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
        File.WriteAllText(Path.Combine(root, "ll.toml"), "[project]\nname = \"cycle\"\n")
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
        File.WriteAllText(Path.Combine(root, "ll.toml"), "[project]\nname = \"proj\"\n")
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
        File.WriteAllText(Path.Combine(root, "ll.toml"), "[project]\nname = \"greet\"\n")
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
let ``loadProject: single-file project loads correctly`` () =
    withTempDir (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
        File.WriteAllText(Path.Combine(root, "ll.toml"), "[project]\nname = \"single\"\n")
        File.WriteAllText(Path.Combine(root, "src", "Main.lll"),
            "module Single.Main\n\nmain() Str = \"hello\"\n")
        match loadProject root with
        | Error es -> Assert.Fail(sprintf "loadProject failed: %s" (errMsg es))
        | Ok proj ->
            Assert.Equal("single", proj.Manifest.Name)
            Assert.Equal(1, proj.Files.Length)
            Assert.Equal<string list>(["Single"; "Main"], proj.Files.[0].ModulePath)
    )
