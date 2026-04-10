module LLLang.Tests.StdlibMapTests

open System.IO
open Xunit

/// Run `lllc run <path>` from the repo root, return (exitCode, stdout, stderr).
let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private runMapLll () =
    let lllPath =
        Path.Combine(repoRoot, "stdlib/src/Map.lll")
    let llcDll =
        Path.Combine(repoRoot, "src/LLLangTool/bin/Debug/net10.0/lllc.dll")
    let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"\"{llcDll}\" run \"{lllPath}\"")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError  <- true
    psi.UseShellExecute        <- false
    psi.WorkingDirectory       <- repoRoot
    use proc = System.Diagnostics.Process.Start(psi)
    let stdoutTask = System.Threading.Tasks.Task.Run(fun () -> proc.StandardOutput.ReadToEnd())
    let stderrTask = System.Threading.Tasks.Task.Run(fun () -> proc.StandardError.ReadToEnd())
    proc.WaitForExit()
    let stdout = stdoutTask.Result
    let stderr = stderrTask.Result
    (proc.ExitCode, stdout, stderr)

[<Fact>]
let ``Std.Map: all tests pass (lllc run)`` () =
    let (exitCode, stdout, stderr) = runMapLll ()
    let expected =
        [ "OK 1 mapEmpty size=0"
          "OK 2 mapInsert size=5"
          "OK 3 mapLookup found"
          "OK 4 mapLookup missing"
          "OK 5 mapContains present"
          "OK 6 mapContains absent"
          "OK 7 mapFold sum keys"
          "OK 8 mapKeys length"
          "OK 9 strCmp lookup"
          "OK 10 mapEmpty size=0"
          "OK 11 duplicate key insert"
          "OK 12 sorted order" ]
    Assert.Equal(0, exitCode)
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing line: {line}\nstdout: {stdout}\nstderr: {stderr}")
