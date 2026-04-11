module LLLang.Tool

open System
open System.IO
open System.Diagnostics
open LLLang.Elaborator
open LLLang.Compiler
open LLLang.Manifest
open LLLang.ProjectLoader
open LLLang.Lexer
open LLLang.Parser

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

/// Write output files for one target into bin/<platform>/.
let private writeTargetOutput (rootDir: string) (name: string) (platform: string) (code: string) : unit =
    let outDir = Path.Combine(rootDir, "bin", platform)
    Directory.CreateDirectory(outDir) |> ignore
    match platform with
    | "fsharp" ->
        let outPath = Path.Combine(outDir, name + ".fs")
        File.WriteAllText(outPath, code)
        let fsproj = $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>preview</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{name}.fs" />
  </ItemGroup>
</Project>
"""
        File.WriteAllText(Path.Combine(outDir, name + ".fsproj"), fsproj)
        printfn "Built project '%s' [fsharp] → %s" name outPath
    | "typescript" ->
        let outPath = Path.Combine(outDir, name + ".ts")
        File.WriteAllText(outPath, code)
        printfn "Built project '%s' [typescript] → %s" name outPath
    | "python" ->
        let outPath = Path.Combine(outDir, name + ".py")
        File.WriteAllText(outPath, code)
        printfn "Built project '%s' [python] → %s" name outPath
    | "java" ->
        let outPath = Path.Combine(outDir, name + ".java")
        File.WriteAllText(outPath, code)
        printfn "Built project '%s' [java] → %s" name outPath
    | other ->
        eprintfn "lllc: unknown platform '%s', skipping" other

/// Build project rooted at rootDir (has lll.toml or ll.toml). Returns exit code.
let private cmdBuildProject (rootDir: string) : int =
    try
        match loadProject rootDir with
        | Error es -> printErrors es; 1
        | Ok proj ->
            // Front-end runs ONCE; codegen fans out to each target.
            match LLLang.Compiler.compileProjectToModules proj with
            | Error es -> printErrors es; 1
            | Ok tms ->
                let platforms =
                    if proj.Manifest.Platform.IsEmpty then ["fsharp"]
                    else proj.Manifest.Platform
                let mutable exitCode = 0
                for platform in platforms do
                    let codeResult =
                        match platform with
                        | "fsharp"     -> Ok (LLLang.Codegen.emitProjectModules tms)
                        | "typescript" -> Ok (LLLang.CodegenTS.emitProjectModules tms)
                        | "python"     -> Ok (LLLang.CodegenPy.emitProjectModules tms)
                        | "java"       -> Ok (LLLang.CodegenJava.emitProjectModules tms)
                        | other        -> Error $"unknown platform '{other}'"
                    match codeResult with
                    | Error msg -> eprintfn "lllc: %s" msg; exitCode <- 1
                    | Ok code   -> writeTargetOutput rootDir proj.Manifest.Name platform code
                exitCode
    with ex ->
        eprintfn "lllc: %s" ex.Message
        1

/// Walk up from startDir looking for lll.toml or ll.toml (in that order).
/// Returns the directory containing the manifest, or None.
let private findProjectRoot (startDir: string) : string option =
    let mutable dir = startDir
    let mutable found = false
    let mutable result: string option = None
    while not found do
        let lll = Path.Combine(dir, "lll.toml")
        let ll  = Path.Combine(dir, "ll.toml")
        if File.Exists(lll) || File.Exists(ll) then
            found <- true
            result <- Some dir
        else
            let parent = Directory.GetParent(dir)
            if parent = null || parent.FullName = dir then
                found <- true // no more parents
            else
                dir <- parent.FullName
    result

// ---- Auto-resolve imports for `lllc run` ------------------------------------

/// Find the stdlib directory by trying several strategies.
let private findStdlibDir (mainFilePath: string) : string option =
    // Strategy 1: LL_STDLIB_PATH environment variable
    let envPath = Environment.GetEnvironmentVariable("LL_STDLIB_PATH")
    if not (String.IsNullOrEmpty envPath) && Directory.Exists(envPath) then
        Some envPath
    else
    // Strategy 2: Walk up from the compiler binary looking for stdlib/src/
    let compilerBin = Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
    let candidates =
        [ // binary is typically at src/LLLangTool/bin/Debug/net10.0/ → 5 levels up is repo root
          Path.GetFullPath(Path.Combine(compilerBin, "..", "..", "..", "..", "..", "stdlib", "src"))
          Path.GetFullPath(Path.Combine(compilerBin, "..", "..", "..", "..", "stdlib", "src"))
          Path.GetFullPath(Path.Combine(compilerBin, "..", "..", "..", "stdlib", "src"))
          // Strategy 3: relative to main file's directory
          Path.GetFullPath(Path.Combine(Path.GetDirectoryName(mainFilePath), "stdlib", "src"))
          // Strategy 4: relative to current working directory
          Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "stdlib", "src"))
        ]
    candidates |> List.tryFind Directory.Exists

/// Parse imports out of a .lll source string without failing if there are errors.
let private extractImports (src: string) : string list list =
    match tokenize src with
    | Error _ -> []
    | Ok toks ->
        match parseModuleWithPos toks with
        | Error _ -> []
        | Ok (m, _) -> m.Imports

/// Given an import path like ["Std"; "Maybe"], find the file on disk.
/// Searches stdlib/src/, .ll-deps/ relative to mainFileDir, and mainFileDir itself.
let private resolveImport (mainFilePath: string) (importPath: string list) : string option =
    let mainFileDir = Path.GetDirectoryName(mainFilePath)
    // Derive filename: last segment + .lll
    let fileName = (List.last importPath) + ".lll"
    // For stdlib: prefix is "Std" → project name is "std" → look in stdlib/src/
    let stdlibCandidates =
        match findStdlibDir mainFilePath with
        | None -> []
        | Some stdlibSrc ->
            // importPath like ["Std"; "Map"] → file is stdlibSrc/Map.lll
            // or nested: ["Std"; "Foo"; "Bar"] → stdlibSrc/Foo/Bar.lll
            let relParts = List.tail importPath  // drop "Std" prefix
            let relPath = Path.Combine(relParts |> Array.ofList) + ".lll"
            [ Path.Combine(stdlibSrc, relPath) ]
    // For .ll-deps/: depName matches first segment (lowercased)
    let depCandidates =
        let depName = (List.head importPath).ToLower()
        let depSrc = Path.Combine(mainFileDir, ".ll-deps", depName, "src")
        let relParts = List.tail importPath
        if relParts.IsEmpty then []
        else
            let relPath = Path.Combine(relParts |> Array.ofList) + ".lll"
            [ Path.Combine(depSrc, relPath) ]
    // Sibling: just fileName in same directory
    let siblingCandidates = [ Path.Combine(mainFileDir, fileName) ]
    let allCandidates = stdlibCandidates @ depCandidates @ siblingCandidates
    allCandidates |> List.tryFind File.Exists

/// Recursively collect all files needed to compile mainFilePath, including
/// transitive deps. Returns topo-sorted LoadedFile list (deps first).
/// visited prevents infinite loops on circular imports.
let private resolveRunImports (mainFilePath: string) (mainSrc: string) : Result<LoadedFile list, string> =
    let mutable visited: Set<string> = Set.empty  // absolute file paths seen
    let mutable loadedMap: Map<string, LoadedFile> = Map.empty
    let mutable depGraph: Map<string, string list> = Map.empty  // filePath → dep filePaths

    let rec collect (filePath: string) (src: string) =
        let absPath = Path.GetFullPath(filePath)
        if Set.contains absPath visited then
            Ok ()
        else
            visited <- Set.add absPath visited
            let imports = extractImports src
            let mutable depPaths: string list = []
            let mutable err: string option = None
            for imp in imports do
                if err.IsNone then
                    match resolveImport filePath imp with
                    | None ->
                        // Import not found locally — skip (may be a future dep or stdlib not installed)
                        // We don't fail hard; the compiler will error if it truly can't resolve it.
                        ()
                    | Some depPath ->
                        let absDepPath = Path.GetFullPath(depPath)
                        depPaths <- absDepPath :: depPaths
                        if not (Set.contains absDepPath visited) then
                            match
                                try Ok (File.ReadAllText depPath)
                                with ex -> Error (sprintf "Cannot read %s: %s" depPath ex.Message)
                            with
                            | Error e -> err <- Some e
                            | Ok depSrc ->
                                match collect depPath depSrc with
                                | Error e -> err <- Some e
                                | Ok () ->
                                    // Register the dep file
                                    let modulePath =
                                        match tokenize depSrc with
                                        | Ok toks ->
                                            match parseModuleWithPos toks with
                                            | Ok (m, _) when m.Path <> [] -> m.Path
                                            | _ ->
                                                let stem = Path.GetFileNameWithoutExtension(depPath)
                                                [stem]
                                        | Error _ ->
                                            let stem = Path.GetFileNameWithoutExtension(depPath)
                                            [stem]
                                    if not (Map.containsKey absDepPath loadedMap) then
                                        loadedMap <- Map.add absDepPath { ModulePath = modulePath; FilePath = absDepPath; Src = depSrc } loadedMap
            match err with
            | Some e -> Error e
            | None ->
                depGraph <- Map.add absPath depPaths depGraph
                // Register main file if not already
                let modulePath =
                    match tokenize src with
                    | Ok toks ->
                        match parseModuleWithPos toks with
                        | Ok (m, _) when m.Path <> [] -> m.Path
                        | _ ->
                            let stem = Path.GetFileNameWithoutExtension(filePath)
                            [stem]
                    | Error _ ->
                        let stem = Path.GetFileNameWithoutExtension(filePath)
                        [stem]
                if not (Map.containsKey absPath loadedMap) then
                    loadedMap <- Map.add absPath { ModulePath = modulePath; FilePath = absPath; Src = src } loadedMap
                Ok ()

    match collect mainFilePath mainSrc with
    | Error e -> Error e
    | Ok () ->
        // Topo-sort by file path
        let allPaths = loadedMap |> Map.toList |> List.map fst
        // Build dep map for topo-sort: path → dep paths (filtered to known files)
        let depsMap =
            allPaths
            |> List.map (fun p ->
                let deps = depGraph |> Map.tryFind p |> Option.defaultValue []
                let filteredDeps = deps |> List.filter (fun d -> List.contains d allPaths) |> List.distinct
                p, filteredDeps)
            |> Map.ofList
        // Simple topo sort (Kahn's)
        let mutable inDegree = allPaths |> List.map (fun p -> p, 0) |> Map.ofList
        let mutable revEdges = allPaths |> List.map (fun p -> p, []) |> Map.ofList
        for p in allPaths do
            let ds = depsMap |> Map.tryFind p |> Option.defaultValue []
            for d in ds do
                inDegree <- Map.add p (inDegree[p] + 1) inDegree
                revEdges <- Map.add d (p :: revEdges[d]) revEdges
        let mutable queue = allPaths |> List.filter (fun p -> inDegree[p] = 0)
        let mutable sorted: string list = []
        while not queue.IsEmpty do
            let n = List.head queue
            queue <- List.tail queue
            sorted <- sorted @ [n]
            for m in revEdges[n] do
                let newDeg = inDegree[m] - 1
                inDegree <- Map.add m newDeg inDegree
                if newDeg = 0 then queue <- queue @ [m]
        if sorted.Length <> allPaths.Length then
            Error "Circular import detected among resolved files"
        else
            let files = sorted |> List.choose (fun p -> Map.tryFind p loadedMap)
            Ok files

/// Run: compile file.lll → temp .fsx → dotnet fsi. Returns exit code.
let private cmdRun (path: string) : int =
    try
        let absPath = Path.GetFullPath(path)
        let src = File.ReadAllText(absPath)
        let imports = extractImports src
        if imports.IsEmpty then
            // Fast path: no imports, compile single file as before
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
        else
            // Resolve imports and compile as mini-project
            match resolveRunImports absPath src with
            | Error msg ->
                eprintfn "lllc: import resolution error: %s" msg
                1
            | Ok files ->
                let fakeManifest : LLManifest = { Name = "run"; Version = "0.0.0"; Entry = ""; Deps = Map.empty; Platform = ["fsharp"] }
                let proj : LLProject = { Manifest = fakeManifest; RootDir = Path.GetDirectoryName(absPath); Files = files }
                match LLLang.Compiler.compileProjectToModules proj with
                | Error es ->
                    printErrors es
                    1
                | Ok tms ->
                    let fs = LLLang.Codegen.emitProjectModules tms
                    let tmp = Path.GetTempFileName() + ".fsx"
                    // Strip module declarations and [<EntryPoint>] attributes.
                    // For multi-module output, rename all `let main` except the last
                    // occurrence so that fsi sees only one `main` binding.
                    let lines = fs.Split('\n')
                    // Find all line indices where `let main (argv` appears
                    let mainLineIndices =
                        lines
                        |> Array.mapi (fun i l -> i, l)
                        |> Array.filter (fun (_, l) -> l.TrimStart().StartsWith("let main (argv"))
                        |> Array.map fst
                    let lastMainIdx = if mainLineIndices.Length > 0 then mainLineIndices[mainLineIndices.Length - 1] else -1
                    let mutable mainCounter = 0
                    let processed =
                        lines
                        |> Array.mapi (fun i l ->
                            let t = l.TrimStart()
                            if t.StartsWith("module ") || t.StartsWith("[<EntryPoint>]") then ""
                            elif t.StartsWith("let main (argv") && i <> lastMainIdx then
                                // Rename intermediate main to avoid duplicate definition
                                let renamed = sprintf "_dep_main_%d" mainCounter
                                mainCounter <- mainCounter + 1
                                l.Replace("let main (", sprintf "let %s (" renamed)
                            else l)
                        |> String.concat "\n"
                    let withInvoke = processed + "\nmain [||] |> int64 |> exit\n"
                    File.WriteAllText(tmp, withInvoke)
                    let psi = ProcessStartInfo("dotnet", $"fsi \"{tmp}\"")
                    psi.RedirectStandardOutput <- false
                    psi.RedirectStandardError  <- false
                    psi.UseShellExecute        <- false
                    use proc = Process.Start(psi)
                    proc.WaitForExit()
                    try File.Delete(tmp) with _ -> ()
                    proc.ExitCode
    with
    | ex ->
        eprintfn "lllc: %s" ex.Message
        1

/// Install dependencies listed in lll.toml (or ll.toml) into .ll-deps/. Returns exit code.
let private cmdInstall (rootDir: string) : int =
    try
        let tomlPath =
            match LLLang.ProjectLoader.findManifest rootDir with
            | Some p -> p
            | None   -> Path.Combine(rootDir, "lll.toml")
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
            File.WriteAllText(Path.Combine(dir, "lll.toml"), tomlContent)
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
                eprintfn "lllc: no lll.toml found. Use 'lllc new <name>' to create a project."
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
        eprintfn "  lllc build [--target fs|ts|py] [dir]       compile project (reads lll.toml)"
        eprintfn "  lllc run   <file.lll>                      compile and run single file"
        eprintfn "  lllc new   <name>                          scaffold new project"
        eprintfn "  lllc install                               install dependencies from lll.toml"
        eprintfn "  lllc mcp                                   run MCP server (stdio transport)"
        eprintfn ""
        eprintfn "  --target fs   emit F# (default)"
        eprintfn "  --target ts   emit TypeScript"
        eprintfn "  --target py   emit Python"
        eprintfn "  --target java emit Java"
        1
