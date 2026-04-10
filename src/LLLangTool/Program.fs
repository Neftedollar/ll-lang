module LLLang.Tool

open System
open System.IO
open System.Diagnostics
open LLLang.Elaborator
open LLLang.Compiler
open LLLang.Manifest
open LLLang.ProjectLoader

let private printErrors (es: LLError list) =
    for e in es do
        eprintfn "%s" e.Message

/// Parse --target flag from args list. Returns (target, remaining args).
let private parseTarget (args: string list) : Target * string list =
    match args with
    | "--target" :: t :: rest ->
        let tgt =
            match t.ToLower() with
            | "ts" | "typescript" -> TypeScript
            | "py" | "python"     -> Python
            | "java" | "jvm"      -> Java
            | _                   -> FSharp
        tgt, rest
    | _ -> FSharp, args

/// Extension for each compilation target.
let private targetExt = function
    | FSharp     -> ".fs"
    | TypeScript -> ".ts"
    | Python     -> ".py"
    | Java       -> ".java"

/// Build: compile file.lll → file.<ext>. Returns exit code.
let private cmdBuild (path: string) (target: Target) : int =
    try
        let src = File.ReadAllText(path)
        match compileTarget target src with
        | Ok out ->
            let outPath = Path.ChangeExtension(path, targetExt target)
            File.WriteAllText(outPath, out)
            let stem = Path.GetFileName(outPath)
            printfn "Built %s" stem
            0
        | Error es ->
            printErrors es
            1
    with
    | ex ->
        eprintfn "lllc: %s" ex.Message
        1

/// Build project rooted at rootDir (has ll.toml). Returns exit code.
let private cmdBuildProject (rootDir: string) : int =
    try
        match loadProject rootDir with
        | Error es -> printErrors es; 1
        | Ok proj ->
            match LLLang.Compiler.compileProject proj with
            | Error es -> printErrors es; 1
            | Ok fs ->
                let binDir = Path.Combine(rootDir, "bin")
                Directory.CreateDirectory(binDir) |> ignore
                let outPath = Path.Combine(binDir, proj.Manifest.Name + ".fs")
                File.WriteAllText(outPath, fs)
                // Generate a minimal .fsproj
                let fsproj = $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>preview</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{proj.Manifest.Name}.fs" />
  </ItemGroup>
</Project>
"""
                let fsprojPath = Path.Combine(binDir, proj.Manifest.Name + ".fsproj")
                File.WriteAllText(fsprojPath, fsproj)
                printfn "Built project '%s' → %s" proj.Manifest.Name outPath
                0
    with ex ->
        eprintfn "lllc: %s" ex.Message
        1

/// Walk up from startDir looking for ll.toml. Returns the directory containing it, or None.
let private findProjectRoot (startDir: string) : string option =
    let mutable dir = startDir
    let mutable found = false
    let mutable result: string option = None
    while not found do
        if File.Exists(Path.Combine(dir, "ll.toml")) then
            found <- true
            result <- Some dir
        else
            let parent = Directory.GetParent(dir)
            if parent = null || parent.FullName = dir then
                found <- true // no more parents
            else
                dir <- parent.FullName
    result

/// Run: compile file.lll → temp .fsx → dotnet fsi. Returns exit code.
let private cmdRun (path: string) : int =
    try
        let src = File.ReadAllText(path)
        match LLLang.Compiler.compile src with
        | Ok fs ->
            let tmp = Path.GetTempFileName() + ".fsx"
            let stripped =
                fs.Split('\n')
                |> Array.filter (fun l ->
                    let t = l.TrimStart()
                    not (t.StartsWith("module ")) && not (t.StartsWith("[<EntryPoint>]")))
                |> String.concat "\n"
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
        eprintfn "lllc: %s" ex.Message
        1

/// Install dependencies listed in ll.toml into .ll-deps/. Returns exit code.
let private cmdInstall (rootDir: string) : int =
    try
        let tomlPath = Path.Combine(rootDir, "ll.toml")
        let manifest =
            match parseManifest (File.ReadAllText tomlPath) with
            | Ok m -> m
            | Error e ->
                eprintfn "lllc: manifest error: %s" e
                { Name = ""; Version = ""; Entry = ""; Deps = Map.empty; Platform = [] }
        let depDir = Path.Combine(rootDir, ".ll-deps")
        Directory.CreateDirectory(depDir) |> ignore
        for KeyValue(name, source) in manifest.Deps do
            let targetDir = Path.Combine(depDir, name)
            if Directory.Exists(targetDir) then
                printfn "  skip %s (already installed)" name
            else
                match source with
                | GitDep(url, ref) ->
                    printfn "  fetch %s from %s#%s" name url ref
                    let psi = ProcessStartInfo("git", sprintf "clone --depth 1 --branch %s %s \"%s\"" ref url targetDir)
                    psi.UseShellExecute <- false
                    let proc = Process.Start(psi)
                    proc.WaitForExit()
                    if proc.ExitCode <> 0 then
                        eprintfn "  error: git clone failed for %s" name
                | PathDep(path) ->
                    let resolved = Path.GetFullPath(Path.Combine(rootDir, path))
                    printfn "  link %s -> %s" name resolved
                    let psi = ProcessStartInfo("ln", sprintf "-s \"%s\" \"%s\"" resolved targetDir)
                    psi.UseShellExecute <- false
                    Process.Start(psi).WaitForExit() |> ignore
        printfn "Installed %d dependencies." manifest.Deps.Count
        0
    with ex ->
        eprintfn "lllc: %s" ex.Message
        1

/// Scaffold a new project. Returns exit code.
let private cmdNew (name: string) : int =
    try
        let dir = Path.Combine(Directory.GetCurrentDirectory(), name)
        if Directory.Exists(dir) then
            eprintfn "lllc: directory '%s' already exists" dir
            1
        else
            Directory.CreateDirectory(Path.Combine(dir, "src")) |> ignore
            let capName = if name.Length = 0 then name else string (Char.ToUpper name.[0]) + name.[1..]
            let tomlContent = "[project]\nname = \"" + name + "\"\n"
            let mainContent = "module " + capName + ".Main\n\nfn main() Str = \"Hello from " + name + "!\"\n"
            File.WriteAllText(Path.Combine(dir, "ll.toml"), tomlContent)
            File.WriteAllText(Path.Combine(dir, "src", "Main.lll"), mainContent)
            printfn "Created project '%s' in ./%s/" name name
            0
    with ex ->
        eprintfn "lllc: %s" ex.Message
        1

[<EntryPoint>]
let main (argv: string[]) : int =
    let args = List.ofArray argv
    match args with
    | "build" :: rest ->
        let (target, rest2) = parseTarget rest
        match rest2 with
        | [path] when path.EndsWith(".lll") -> cmdBuild path target
        | [dir] -> cmdBuildProject (Path.GetFullPath dir)
        | [] ->
            match findProjectRoot (Directory.GetCurrentDirectory()) with
            | Some root -> cmdBuildProject root
            | None ->
                eprintfn "lllc: no ll.toml found. Use 'lllc new <name>' to create a project."
                1
        | _ ->
            eprintfn "lllc: unrecognized build arguments"
            1
    | ["run"; path] -> cmdRun path
    | ["new"; name] -> cmdNew name
    | "install" :: _ ->
        let root =
            match findProjectRoot (Directory.GetCurrentDirectory()) with
            | Some r -> r
            | None -> Directory.GetCurrentDirectory()
        cmdInstall root
    | ["mcp"] -> Mcp.runServer (); 0
    | _ ->
        eprintfn "Usage:"
        eprintfn "  lllc build [--target fs|ts|py] <file.lll>  compile single file"
        eprintfn "  lllc build [--target fs|ts|py] [dir]       compile project (reads ll.toml)"
        eprintfn "  lllc run   <file.lll>                      compile and run single file"
        eprintfn "  lllc new   <name>                          scaffold new project"
        eprintfn "  lllc install                               install dependencies from ll.toml"
        eprintfn "  lllc mcp                                   run MCP server (stdio transport)"
        eprintfn ""
        eprintfn "  --target fs   emit F# (default)"
        eprintfn "  --target ts   emit TypeScript"
        eprintfn "  --target py   emit Python"
        eprintfn "  --target java emit Java"
        1
