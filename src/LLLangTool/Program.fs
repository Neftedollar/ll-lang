module LLLang.Tool

open System
open System.IO
open System.Diagnostics
open LLLang.Elaborator

let private printErrors (es: LLError list) =
    for e in es do
        eprintfn "%s" e.Message

/// Build: compile file.lll → file.fs. Returns exit code.
let private cmdBuild (path: string) : int =
    try
        let src = File.ReadAllText(path)
        match LLLang.Compiler.compile src with
        | Ok fs ->
            let outPath = Path.ChangeExtension(path, ".fs")
            File.WriteAllText(outPath, fs)
            let stem = Path.GetFileName(outPath)
            printfn "Built %s" stem
            0
        | Error es ->
            printErrors es
            1
    with
    | ex ->
        eprintfn "llc: %s" ex.Message
        1

/// Run: compile file.lll → temp .fsx → dotnet fsi. Returns exit code.
/// The emitted F# contains a [<EntryPoint>] main, which fsi does NOT auto-invoke;
/// we strip the attribute and append an explicit `main [||] |> exit` call.
let private cmdRun (path: string) : int =
    try
        let src = File.ReadAllText(path)
        match LLLang.Compiler.compile src with
        | Ok fs ->
            let tmp = Path.GetTempFileName() + ".fsx"
            // For fsi scripts: strip module header and [<EntryPoint>], append explicit invocation.
            let stripped =
                fs.Split('\n')
                |> Array.filter (fun l ->
                    let t = l.TrimStart()
                    not (t.StartsWith("module ")) && not (t.StartsWith("[<EntryPoint>]")))
                |> String.concat "\n"
            // Phase 6.5: stdlib prelude defines `let exit : int64 -> unit`, which
            // shadows F# core `exit : int -> 'a`. So we feed `main [||]` (int) into
            // it via int64 conversion. The prelude exit terminates the fsi process.
            let withInvoke = stripped + "\nmain [||] |> int64 |> exit\n"
            File.WriteAllText(tmp, withInvoke)
            let psi = ProcessStartInfo("dotnet", $"fsi \"{tmp}\"")
            psi.RedirectStandardOutput <- false
            psi.RedirectStandardError  <- false
            psi.UseShellExecute        <- false
            use proc = Process.Start(psi)
            proc.WaitForExit()
            try File.Delete(tmp) with _ -> ()
            proc.ExitCode
        | Error es ->
            printErrors es
            1
    with
    | ex ->
        eprintfn "llc: %s" ex.Message
        1

[<EntryPoint>]
let main (argv: string[]) : int =
    match argv with
    | [| "build"; path |] -> cmdBuild path
    | [| "run"; path |] -> cmdRun path
    | _ ->
        eprintfn "Usage: llc <build|run> <file.lll>"
        1
