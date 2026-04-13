module LLLang.Tests.StdlibJsonTests

open System.IO
open Xunit

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private runJsonLll () =
    let lllPath =
        Path.Combine(repoRoot, "stdlib/src/Json.lll")
    let llcDll =
        Path.Combine(repoRoot, "src/LLLangTool/bin/Debug/net10.0/lllc.dll")
    let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"\"{llcDll}\" run \"{lllPath}\"")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.WorkingDirectory <- repoRoot
    use proc = LLLang.Tests.TestCompat.startProcess psi
    let stdoutTask = System.Threading.Tasks.Task.Run(fun () -> proc.StandardOutput.ReadToEnd())
    let stderrTask = System.Threading.Tasks.Task.Run(fun () -> proc.StandardError.ReadToEnd())
    proc.WaitForExit()
    let stdout = stdoutTask.Result
    let stderr = stderrTask.Result
    (proc.ExitCode, stdout, stderr)

[<Fact>]
let ``Std.Json: parser, stringify, and roundtrip smoke tests pass (lllc run)`` () =
    let (exitCode, stdout, stderr) = runJsonLll ()
    let expected =
        [ "OK pos-null"
          "OK pos-bool"
          "OK pos-int"
          "OK pos-exp"
          "OK pos-str-esc"
          "OK pos-u-basic"
          "OK pos-u-surrogate"
          "OK pos-array"
          "OK pos-object"
          "OK rt-num"
          "OK rt-str-esc"
          "OK rt-u-surrogate"
          "OK rt-array"
          "OK rt-object"
          "OK util-float-to-str"
          "OK neg-leading-zero"
          "OK neg-bad-exp"
          "OK neg-bad-frac"
          "OK neg-bad-escape"
          "OK neg-lone-high-surrogate"
          "OK neg-lone-low-surrogate"
          "OK neg-missing-comma"
          "OK neg-trailing-garbage" ]
    Assert.Equal(0, exitCode)
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing line: {line}\nstdout: {stdout}\nstderr: {stderr}")
    Assert.DoesNotContain("FAIL ", stdout)
