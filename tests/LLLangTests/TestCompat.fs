module LLLang.Tests.TestCompat

open System
open System.Diagnostics
open System.IO

let startProcess (psi: ProcessStartInfo) : Process =
    match Process.Start(psi) with
    | null -> failwith "Failed to start process."
    | proc -> proc

let tryGetEnv (name: string) : string option =
    match Environment.GetEnvironmentVariable(name) with
    | null -> None
    | value -> Some value

let envEquals (name: string) (expected: string) : bool =
    tryGetEnv name
    |> Option.exists (fun value -> String.Equals(value, expected, StringComparison.Ordinal))

let directoryNameOrCurrent (path: string) : string =
    match Path.GetDirectoryName(path) with
    | null
    | "" -> Directory.GetCurrentDirectory()
    | dir -> dir

let fileNameOrEmpty (path: string) : string =
    match Path.GetFileName(path) with
    | null -> ""
    | value -> value

let fileNameWithoutExtensionOrEmpty (path: string) : string =
    match Path.GetFileNameWithoutExtension(path) with
    | null -> ""
    | value -> value

let changeExtensionOrInput (path: string) (ext: string) : string =
    match Path.ChangeExtension(path, ext) with
    | null -> path
    | value -> value
