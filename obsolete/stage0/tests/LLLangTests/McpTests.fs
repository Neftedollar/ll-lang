module LLLangTests.McpTests

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open Xunit

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

let private lllcToolProject =
    Path.Combine(repoRoot, "src", "LLLangTool", "LLLangTool.fsproj")

let private jsonString (value: string) =
    JsonSerializer.Serialize(value)

let private rpcRequest (id: int) (methodName: string) (paramsJson: string) =
    sprintf """{"jsonrpc":"2.0","id":%d,"method":"%s","params":%s}""" id methodName paramsJson

let private toolCallRequest (id: int) (toolName: string) (argumentsJson: string) =
    rpcRequest id "tools/call" (sprintf """{"name":"%s","arguments":%s}""" toolName argumentsJson)

let private parseJsonLines (stdout: string) =
    stdout.Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun line -> line.Trim())
    |> Array.filter (fun line -> line <> "")
    |> Array.map (fun line ->
        use doc = JsonDocument.Parse(line)
        doc.RootElement.Clone())
    |> Array.toList

let private prop (name: string) (element: JsonElement) =
    let mutable value = Unchecked.defaultof<JsonElement>
    if element.TryGetProperty(name, &value) then
        value
    else
        failwithf "Missing property '%s' in JSON: %s" name (element.ToString())

let private strProp (name: string) (element: JsonElement) =
    match (prop name element).GetString() with
    | null -> ""
    | value -> value

let private runMcp (requests: string list) =
    let psi = ProcessStartInfo()
    psi.FileName <- "dotnet"
    psi.Arguments <- sprintf "run --project \"%s\" -- mcp" lllcToolProject
    psi.WorkingDirectory <- repoRoot
    psi.UseShellExecute <- false
    psi.RedirectStandardInput <- true
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true

    use proc = new Process()
    proc.StartInfo <- psi

    let started = proc.Start()
    Assert.True(started, "Failed to start MCP process")

    for req in requests do
        proc.StandardInput.WriteLine(req)
    proc.StandardInput.Close()

    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    let exited = proc.WaitForExit(120_000)
    Assert.True(exited, "MCP process timed out")
    Assert.True(proc.ExitCode = 0, $"MCP process exited with {proc.ExitCode}. stderr: {stderr}")

    let responses = parseJsonLines stdout
    Assert.Equal(requests.Length, responses.Length)
    responses

let private mkTempDir () =
    let path = Path.Combine(Path.GetTempPath(), "lllang-mcp-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(path) |> ignore
    path

[<Fact>]
let ``mcp initialize returns self-hosted metadata`` () =
    let responses =
        runMcp [ rpcRequest 1 "initialize" "{}" ]

    let root = List.head responses
    let result = prop "result" root
    Assert.Equal("2024-11-05", strProp "protocolVersion" result)
    Assert.Equal("lllcself", strProp "name" (prop "serverInfo" result))
    Assert.Equal("1.0.0", strProp "version" (prop "serverInfo" result))

[<Fact>]
let ``mcp tools list matches current self-hosted contract`` () =
    let responses =
        runMcp [ rpcRequest 1 "tools/list" "{}" ]

    let tools =
        (prop "tools" (prop "result" (List.head responses))).EnumerateArray()
        |> Seq.map (fun tool -> strProp "name" tool)
        |> Seq.toList

    let expected =
        [
            "compile_source"
            "check_source"
            "compile_file"
            "check_file"
            "diagnose_source"
            "diagnose_file"
            "format_source"
            "format_file"
            "parse_source"
            "typed_ast"
            "project_graph"
            "symbols"
            "definition"
            "references"
            "explain_error"
            "fix_suggest"
            "apply_fix_preview"
            "check_project"
            "build_project"
            "test_list"
            "test_run"
            "mod_add"
            "mod_tidy"
            "mod_why"
            "stdlib_search"
            "list_errors"
            "lookup_error"
            "list_targets"
        ]

    Assert.Equal<string list>(expected, tools)

[<Fact>]
let ``mcp tools call smoke check_source compile_source lookup_error`` () =
    let validSource = "module M\nmain = 1"

    let responses =
        runMcp
            [
                toolCallRequest 1 "check_source" (sprintf """{"source":%s}""" (jsonString validSource))
                toolCallRequest 2 "compile_source" (sprintf """{"source":%s}""" (jsonString validSource))
                toolCallRequest 3 "lookup_error" """{"code":"E003"}"""
            ]

    let checkResult = prop "result" responses[0]
    let compileResult = prop "result" responses[1]
    let lookupResult = prop "result" responses[2]

    Assert.True((prop "ok" checkResult).GetBoolean())
    Assert.Contains("\"ok\":true", strProp "result" checkResult)

    Assert.True((prop "ok" compileResult).GetBoolean())
    Assert.Equal("fsharp", strProp "target" compileResult)
    Assert.Contains("module M", strProp "fs" compileResult)

    Assert.True((prop "found" lookupResult).GetBoolean())
    Assert.Equal("E003", strProp "code" lookupResult)

[<Fact>]
let ``mcp new tools smoke for issues 159 to 168`` () =
    let srcWithError = "module M\nmain() Int = undefined"
    let validSrc = "module M\nfoo(x Int) Int = x\nbar = foo 1"
    let projectRoot = Path.Combine(repoRoot, "lllcself")
    let testsProject = Path.Combine(repoRoot, "tests", "LLLangTests", "LLLangTests.fsproj")
    let testsFilter = "FullyQualifiedName~LLLangTests.McpTests.mcp initialize returns self-hosted metadata"

    let responses =
        runMcp
            [
                toolCallRequest 1 "diagnose_source" (sprintf """{"source":%s}""" (jsonString srcWithError))
                toolCallRequest 2 "format_source" (sprintf """{"source":%s,"check_only":true}""" (jsonString "module M\nmain = 1   \n"))
                toolCallRequest 3 "parse_source" (sprintf """{"source":%s}""" (jsonString validSrc))
                toolCallRequest 4 "typed_ast" (sprintf """{"source":%s}""" (jsonString validSrc))
                toolCallRequest 5 "project_graph" (sprintf """{"root":%s}""" (jsonString projectRoot))
                toolCallRequest 6 "symbols" (sprintf """{"source":%s}""" (jsonString validSrc))
                toolCallRequest 7 "definition" (sprintf """{"symbol":"foo","source":%s}""" (jsonString validSrc))
                toolCallRequest 8 "references" (sprintf """{"symbol":"foo","source":%s}""" (jsonString validSrc))
                toolCallRequest 9 "explain_error" (sprintf """{"source":%s}""" (jsonString srcWithError))
                toolCallRequest 10 "fix_suggest" (sprintf """{"source":%s}""" (jsonString srcWithError))
                toolCallRequest 11 "apply_fix_preview" (sprintf """{"fix_id":"insert_stub_binding","source":%s}""" (jsonString srcWithError))
                toolCallRequest 12 "check_project" (sprintf """{"root":%s}""" (jsonString projectRoot))
                toolCallRequest 13 "build_project" (sprintf """{"root":%s}""" (jsonString projectRoot))
                toolCallRequest 14 "test_list" (sprintf """{"project":%s,"filter":%s}""" (jsonString testsProject) (jsonString testsFilter))
                toolCallRequest 15 "test_run" (sprintf """{"project":%s,"filter":%s,"timeout_sec":300}""" (jsonString testsProject) (jsonString testsFilter))
                toolCallRequest 16 "mod_tidy" (sprintf """{"root":%s}""" (jsonString projectRoot))
                toolCallRequest 17 "mod_why" (sprintf """{"root":%s,"dep":"Std.List"}""" (jsonString projectRoot))
            ]

    let diagnose = prop "result" responses[0]
    let format = prop "result" responses[1]
    let parse = prop "result" responses[2]
    let typed = prop "result" responses[3]
    let graph = prop "result" responses[4]
    let symbols = prop "result" responses[5]
    let definition = prop "result" responses[6]
    let references = prop "result" responses[7]
    let explain = prop "result" responses[8]
    let suggest = prop "result" responses[9]
    let preview = prop "result" responses[10]
    let checkProject = prop "result" responses[11]
    let buildProject = prop "result" responses[12]
    let testList = prop "result" responses[13]
    let testRun = prop "result" responses[14]
    let modTidy = prop "result" responses[15]
    let modWhy = prop "result" responses[16]

    Assert.False((prop "ok" diagnose).GetBoolean())
    Assert.True((prop "changed" format).GetBoolean())
    Assert.Equal("M", strProp "module" parse)
    Assert.True((prop "ok" typed).GetBoolean())
    Assert.True((prop "ok" graph).GetBoolean())
    Assert.True((prop "ok" symbols).GetBoolean())
    Assert.True((prop "found" definition).GetBoolean())
    Assert.True((prop "ok" references).GetBoolean())
    Assert.Equal("E002", strProp "code" explain)
    Assert.True((prop "ok" suggest).GetBoolean())
    Assert.True((prop "applied" preview).GetBoolean())
    Assert.True((prop "ok" checkProject).GetBoolean())
    Assert.True((prop "ok" buildProject).GetBoolean())
    Assert.True((prop "supported" testList).GetBoolean())
    Assert.True((prop "ok" testList).GetBoolean())
    Assert.True((prop "total" testList).GetInt32() >= 1)
    Assert.True((prop "supported" testRun).GetBoolean())
    Assert.True((prop "total" testRun).GetInt32() >= 1)
    Assert.True((prop "ok" modTidy).GetBoolean())
    Assert.True((prop "ok" modWhy).GetBoolean())

[<Fact>]
let ``mcp diagnose_source and diagnose_file return structured diagnostics`` () =
    let srcWithError = "module M\nmain() Int = undefined"
    let tmpDir = mkTempDir ()
    let tmpFile = Path.Combine(tmpDir, "Broken.lll")
    File.WriteAllText(tmpFile, srcWithError)

    try
        let responses =
            runMcp
                [
                    toolCallRequest 1 "diagnose_source" (sprintf """{"source":%s}""" (jsonString srcWithError))
                    toolCallRequest 2 "diagnose_file" (sprintf """{"path":%s}""" (jsonString tmpFile))
                ]

        let diagnoseSource = prop "result" responses[0]
        let diagnoseFile = prop "result" responses[1]
        let firstDiagSource = (prop "diagnostics" diagnoseSource).EnumerateArray() |> Seq.head
        let firstDiagFile = (prop "diagnostics" diagnoseFile).EnumerateArray() |> Seq.head

        Assert.False((prop "ok" diagnoseSource).GetBoolean())
        Assert.False((prop "ok" diagnoseFile).GetBoolean())
        Assert.NotEqual<string>("", strProp "stage" diagnoseSource)
        Assert.NotEqual<string>("", strProp "stage" diagnoseFile)

        let assertDiagShape (diag: JsonElement) =
            Assert.NotEqual<string>("", strProp "code" diag)
            Assert.NotEqual<string>("", strProp "message" diag)
            Assert.True((prop "line" diag).GetInt32() >= 0)
            Assert.True((prop "col" diag).GetInt32() >= 0)
            Assert.True((prop "endLine" diag).GetInt32() >= 0)
            Assert.True((prop "endCol" diag).GetInt32() >= 0)
            Assert.Equal("error", strProp "severity" diag)

        assertDiagShape firstDiagSource
        assertDiagShape firstDiagFile
    finally
        if Directory.Exists(tmpDir) then
            Directory.Delete(tmpDir, true)

[<Fact>]
let ``mcp format tools are idempotent and check_only is non writing`` () =
    let srcMessy = "module M\nmain = 1   \n"
    let tmpDir = mkTempDir ()
    let tmpFile = Path.Combine(tmpDir, "Fmt.lll")
    File.WriteAllText(tmpFile, srcMessy)

    try
        let responses =
            runMcp
                [
                    toolCallRequest 1 "format_source" (sprintf """{"source":%s}""" (jsonString srcMessy))
                    toolCallRequest 2 "format_source" (sprintf """{"source":%s,"check_only":true}""" (jsonString "module M\nmain = 1\n"))
                    toolCallRequest 3 "format_file" (sprintf """{"path":%s,"check_only":true}""" (jsonString tmpFile))
                    toolCallRequest 4 "format_file" (sprintf """{"path":%s}""" (jsonString tmpFile))
                    toolCallRequest 5 "format_file" (sprintf """{"path":%s,"check_only":true}""" (jsonString tmpFile))
                ]

        let formatSource = prop "result" responses[0]
        let formatSourceIdem = prop "result" responses[1]
        let formatFileCheckOnly = prop "result" responses[2]
        let formatFileWrite = prop "result" responses[3]
        let formatFileIdem = prop "result" responses[4]

        Assert.True((prop "changed" formatSource).GetBoolean())
        Assert.False((prop "changed" formatSourceIdem).GetBoolean())
        Assert.True((prop "changed" formatFileCheckOnly).GetBoolean())
        Assert.False((prop "wrote" formatFileCheckOnly).GetBoolean())
        Assert.True((prop "changed" formatFileWrite).GetBoolean())
        Assert.True((prop "wrote" formatFileWrite).GetBoolean())
        Assert.False((prop "changed" formatFileIdem).GetBoolean())

        let formattedDisk = File.ReadAllText(tmpFile)
        Assert.Equal("module M\nmain = 1\n", formattedDisk)
    finally
        if Directory.Exists(tmpDir) then
            Directory.Delete(tmpDir, true)

[<Fact>]
let ``mcp typed_ast reports failure diagnostics for invalid source`` () =
    let invalidSrc = "module M\nmain() Int = undefined"

    let responses =
        runMcp [ toolCallRequest 1 "typed_ast" (sprintf """{"source":%s}""" (jsonString invalidSrc)) ]

    let typed = prop "result" (List.head responses)
    let diagnostics = prop "diagnostics" typed

    Assert.False((prop "ok" typed).GetBoolean())
    Assert.NotEqual<string>("", strProp "stage" typed)
    Assert.True((diagnostics.GetArrayLength()) > 0)

[<Fact>]
let ``mcp project_graph reports cycles for cyclic imports`` () =
    let tmpRoot = mkTempDir ()
    let srcDir = Path.Combine(tmpRoot, "src")
    Directory.CreateDirectory(srcDir) |> ignore
    File.WriteAllText(Path.Combine(srcDir, "A.lll"), "module A\nimport B\na = b\n")
    File.WriteAllText(Path.Combine(srcDir, "B.lll"), "module B\nimport A\nb = a\n")

    try
        let responses =
            runMcp [ toolCallRequest 1 "project_graph" (sprintf """{"root":%s}""" (jsonString tmpRoot)) ]

        let graph = prop "result" (List.head responses)
        let errors = prop "errors" graph
        let topo = prop "topo_order" graph
        let renderedErrors =
            errors.EnumerateArray()
            |> Seq.map (fun e -> e.GetString())
            |> Seq.filter (fun s -> not (isNull s))
            |> String.concat "\n"

        Assert.False((prop "ok" graph).GetBoolean())
        Assert.True(errors.GetArrayLength() > 0)
        Assert.Equal(0, topo.GetArrayLength())
        Assert.True(renderedErrors.Contains("E024") || renderedErrors.Contains("cycle"))
    finally
        if Directory.Exists(tmpRoot) then
            Directory.Delete(tmpRoot, true)

[<Fact>]
let ``mcp definition and references support project scope lookup`` () =
    let tmpRoot = mkTempDir ()
    let srcDir = Path.Combine(tmpRoot, "src")
    Directory.CreateDirectory(srcDir) |> ignore
    File.WriteAllText(Path.Combine(srcDir, "Util.lll"), "module Util\nhelper(x Int) Int = x\n")
    File.WriteAllText(Path.Combine(srcDir, "Main.lll"), "module Main\nimport Util\nmain = helper 1\n")

    try
        let responses =
            runMcp
                [
                    toolCallRequest 1 "definition" (sprintf """{"root":%s,"symbol":"helper"}""" (jsonString tmpRoot))
                    toolCallRequest 2 "references" (sprintf """{"root":%s,"symbol":"helper"}""" (jsonString tmpRoot))
                ]

        let definition = prop "result" responses[0]
        let references = prop "result" responses[1]
        let defObj = prop "definition" definition
        let refs = prop "references" references
        let refPaths = refs.EnumerateArray() |> Seq.map (fun r -> strProp "path" r) |> Set.ofSeq

        Assert.True((prop "found" definition).GetBoolean())
        Assert.True((strProp "path" defObj).EndsWith("Util.lll"))
        Assert.True((prop "ok" references).GetBoolean())
        Assert.True(refs.GetArrayLength() > 0)
        Assert.True(refPaths |> Seq.exists (fun p -> p.EndsWith("Main.lll")))
    finally
        if Directory.Exists(tmpRoot) then
            Directory.Delete(tmpRoot, true)

[<Fact>]
let ``mcp mod_add mod_why mod_tidy roundtrip works in temp project`` () =
    let tmpRoot = mkTempDir ()
    let srcDir = Path.Combine(tmpRoot, "src")
    Directory.CreateDirectory(srcDir) |> ignore
    File.WriteAllText(Path.Combine(tmpRoot, "lll.toml"), "[project]\nname = \"tmp\"\nversion = \"0.1.0\"\n")
    File.WriteAllText(Path.Combine(srcDir, "Main.lll"), "module Main\nmain = 1\n")

    try
        let responses =
            runMcp
                [
                    toolCallRequest 1 "mod_add" (sprintf """{"root":%s,"name":"Std.List","source":"builtin://Std.List"}""" (jsonString tmpRoot))
                    toolCallRequest 2 "mod_why" (sprintf """{"root":%s,"dep":"Std.List"}""" (jsonString tmpRoot))
                    toolCallRequest 3 "mod_tidy" (sprintf """{"root":%s}""" (jsonString tmpRoot))
                ]

        let modAdd = prop "result" responses[0]
        let modWhy = prop "result" responses[1]
        let modTidy = prop "result" responses[2]
        let manifest = File.ReadAllText(Path.Combine(tmpRoot, "lll.toml"))

        Assert.True((prop "ok" modAdd).GetBoolean())
        Assert.True(manifest.Contains("Std.List = \"builtin://Std.List\""))
        Assert.True((prop "ok" modWhy).GetBoolean())
        Assert.True((prop "in_manifest" modWhy).GetBoolean())
        Assert.True((prop "ok" modTidy).GetBoolean())
    finally
        if Directory.Exists(tmpRoot) then
            Directory.Delete(tmpRoot, true)

[<Fact>]
let ``mcp test_run reports timeout and explicit project failure`` () =
    let testsProject = Path.Combine(repoRoot, "tests", "LLLangTests", "LLLangTests.fsproj")
    let testsFilter = "FullyQualifiedName~LLLangTests.McpTests.mcp initialize returns self-hosted metadata"
    let missingProject = Path.Combine(Path.GetTempPath(), "lllang-missing", "Missing.fsproj")

    let responses =
        runMcp
            [
                toolCallRequest 1 "test_run" (sprintf """{"project":%s,"filter":%s,"timeout_sec":1}""" (jsonString testsProject) (jsonString testsFilter))
                toolCallRequest 2 "test_run" (sprintf """{"project":%s,"timeout_sec":30}""" (jsonString missingProject))
            ]

    let timeoutRun = prop "result" responses[0]
    let missingProjectRun = prop "result" responses[1]

    Assert.False((prop "ok" timeoutRun).GetBoolean())
    Assert.True((prop "timed_out" timeoutRun).GetBoolean())
    Assert.Equal(-1, (prop "exit_code" timeoutRun).GetInt32())

    Assert.False((prop "ok" missingProjectRun).GetBoolean())
    Assert.False((prop "timed_out" missingProjectRun).GetBoolean())
    Assert.NotEqual<int>(0, (prop "exit_code" missingProjectRun).GetInt32())

[<Fact>]
let ``mcp file tools work with absolute paths`` () =
    let absPath = Path.Combine(repoRoot, "spec", "examples", "valid", "01-basics.lll")
    Assert.True(File.Exists(absPath), $"Expected test fixture file: {absPath}")

    let responses =
        runMcp
            [
                toolCallRequest 1 "compile_file" (sprintf """{"path":%s}""" (jsonString absPath))
                toolCallRequest 2 "check_file" (sprintf """{"path":%s}""" (jsonString absPath))
            ]

    let compileResult = prop "result" responses[0]
    let checkResult = prop "result" responses[1]

    Assert.True((prop "ok" compileResult).GetBoolean())
    Assert.Equal("fsharp", strProp "target" compileResult)
    Assert.Contains("module Examples.Basics", strProp "fs" compileResult)

    Assert.True((prop "ok" checkResult).GetBoolean())
    Assert.Contains("\"ok\":true", strProp "result" checkResult)

[<Fact>]
let ``mcp list_targets exposes stable and experimental targets`` () =
    let responses =
        runMcp [ toolCallRequest 1 "list_targets" "{}" ]

    let targets =
        (prop "result" (List.head responses)).EnumerateArray()
        |> Seq.map (fun t -> (strProp "id" t, strProp "status" t))
        |> Map.ofSeq

    Assert.Equal("stable", targets["fs"])
    Assert.Equal("stable", targets["ts"])
    Assert.Equal("stable", targets["py"])
    Assert.Equal("stable", targets["java"])
    Assert.Equal("stable", targets["cs"])
    Assert.Equal("experimental", targets["llvm"])
