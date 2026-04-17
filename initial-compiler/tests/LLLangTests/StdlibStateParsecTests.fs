module LLLang.Tests.StdlibStateParsecTests

open System
open System.IO
open Xunit

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private llcDll =
    Path.Combine(repoRoot, "src/LLLangTool/bin/Debug/net10.0/lllc.dll")

let private runSource (moduleName: string) (src: string) =
    let tempDir = Path.Combine(Path.GetTempPath(), "lllang-stdlib-tests")
    Directory.CreateDirectory(tempDir) |> ignore
    let path = Path.Combine(tempDir, moduleName + "-" + Guid.NewGuid().ToString("N") + ".lll")
    File.WriteAllText(path, src)
    let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"\"{llcDll}\" run \"{path}\"")
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
    try File.Delete(path) with _ -> ()
    (proc.ExitCode, stdout, stderr)

[<Fact>]
let ``Std.State: stateRun/stateEval/stateExec/stateModify smoke`` () =
    let src = """
module Tmp.StateSmoke

import Std.State

main() =
  prog =
    stateBind (statePut 5) (\ignored1.
      stateBind (stateModify (\s. s + 3)) (\ignored2.
        stateGet 0
      )
    )
  v = stateEval prog 0
  s = stateExec prog 0
  _ = if v == 8
    printfn "OK state-eval"
  else
    printfn "FAIL state-eval"
  _ = if s == 8
    printfn "OK state-exec"
  else
    printfn "FAIL state-exec"
  0
"""
    let (exitCode, stdout, stderr) = runSource "state-smoke" src
    Assert.Equal(0, exitCode)
    Assert.Contains("OK state-eval", stdout)
    Assert.Contains("OK state-exec", stdout)
    Assert.DoesNotContain("FAIL ", stdout)

[<Fact>]
let ``Std.Parsec: combinators/backtracking/errors smoke`` () =
    let src = """
module Tmp.ParsecSmoke

import Std.Parsec

main() =
  pIntAfterAB =
    parseBind (parseString "ab") (\ignoredPrefix.
      parseBind parseInt (\n.
        parsePure n
      )
    )
  r1 = runParser pIntAfterAB "ab123"
  _ = match r1
    | Ok (n, _) ->
      if n == 123
        printfn "OK parse-int"
      else
        printfn "FAIL parse-int"
    | Err _ -> printfn "FAIL parse-int"

  pBacktrack = parseOrElse (parseTry (parseString "abz")) (parseString "ab")
  r2 = runParser pBacktrack "ab"
  _ = match r2
    | Ok (s, _) ->
      if s == "ab"
        printfn "OK backtrack"
      else
        printfn "FAIL backtrack"
    | Err _ -> printfn "FAIL backtrack"

  r3 = runParser parseQuotedString "\"a\\n\\u0041\""
  _ = match r3
    | Ok (s, _) ->
      if strLen s == 3
        printfn "OK quoted"
      else
        printfn "FAIL quoted"
    | Err _ -> printfn "FAIL quoted"

  r4 = runParser parseQuotedString "\"abc"
  _ = match r4
    | Ok _ -> printfn "FAIL err-pos"
    | Err (MkParseError _ (MkParsePos _ l c)) ->
      if l == 1
        if c >= 2
          printfn "OK err-pos"
        else
          printfn "FAIL err-pos"
      else
        printfn "FAIL err-pos"
  0
"""
    let (exitCode, stdout, stderr) = runSource "parsec-smoke" src
    Assert.Equal(0, exitCode)
    Assert.Contains("OK parse-int", stdout)
    Assert.Contains("OK backtrack", stdout)
    Assert.Contains("OK quoted", stdout)
    Assert.Contains("OK err-pos", stdout)
    Assert.DoesNotContain("FAIL ", stdout)

[<Fact>]
let ``Std.Lazy: delay/force memoized-node smoke`` () =
    let src = """
module Tmp.LazySmoke

import Std.Lazy

main() =
  node = lazyDelay (\ignored. 41 + 1)
  forced = lazyForce node
  _ = match forced
    | (v, cached) ->
      if v == 42
        match lazyForce cached
          | (v2, _) ->
            if v2 == 42
              printfn "OK lazy-force"
            else
              printfn "FAIL lazy-force"
      else
        printfn "FAIL lazy-force"
  0
"""
    let (exitCode, stdout, stderr) = runSource "lazy-smoke" src
    Assert.Equal(0, exitCode)
    Assert.Contains("OK lazy-force", stdout)
    Assert.DoesNotContain("FAIL ", stdout)
