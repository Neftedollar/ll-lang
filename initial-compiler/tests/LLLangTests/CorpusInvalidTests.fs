module LLLang.Tests.CorpusInvalidTests

open System.IO
open System.Diagnostics
open Xunit

/// G3.2 — All spec/examples/invalid/*.lll files must trigger the expected
/// error code declared on line 1 as "-- expect: EXXX".
///
/// Run with: dotnet test --filter "Category=Corpus"

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private lllcDll =
    Path.Combine(repoRoot, "src/LLLangTool/bin/Debug/net10.0/lllc.dll")

let private invalidDir =
    Path.Combine(repoRoot, "spec/examples/invalid")

let private parseExpectedCode (firstLine: string) : string option =
    let prefix = "-- expect: "
    if firstLine.StartsWith(prefix) then
        Some (firstLine.Substring(prefix.Length).Trim())
    else None

/// MemberData source: returns (filePath, expectedCode) pairs for every
/// .lll file in spec/examples/invalid/ that declares "-- expect: EXXX".
let invalidCorpusFiles () : obj[][] =
    Directory.GetFiles(invalidDir, "*.lll")
    |> Array.sort
    |> Array.choose (fun path ->
        let lines = File.ReadAllLines(path)
        let first = if lines.Length > 0 then lines.[0] else ""
        parseExpectedCode first
        |> Option.map (fun code -> [| (path :> obj); (code :> obj) |]))

[<Theory>]
[<Trait("Category", "Corpus")>]
[<MemberData("invalidCorpusFiles")>]
let ``invalid corpus file triggers expected error code`` (filePath: string) (expectedCode: string) =
    let psi = ProcessStartInfo("dotnet", $"\"{lllcDll}\" check \"{filePath}\"")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError  <- true
    psi.UseShellExecute        <- false
    use proc = LLLang.Tests.TestCompat.startProcess psi
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    let output = stdout + "\n" + stderr
    Assert.True(
        output.Contains(expectedCode),
        $"Expected error code '{expectedCode}' in output for {Path.GetFileName(filePath)}\nstdout:\n{stdout}\nstderr:\n{stderr}")
