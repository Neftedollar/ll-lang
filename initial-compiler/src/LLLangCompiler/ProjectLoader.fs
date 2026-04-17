module LLLang.ProjectLoader

open System
open System.IO
open LLLang.AST
open LLLang.Elaborator
open LLLang.Lexer
open LLLang.Parser
open LLLang.Manifest

// ---- Types -----------------------------------------------------------------

type LoadedFile = {
    ModulePath : string list   // e.g. ["Myapp"; "Foo"; "Bar"]
    FilePath   : string        // absolute path to .lll file
    Src        : string        // file contents
}

type LLProject = {
    Manifest : LLManifest
    RootDir  : string          // absolute path to project root (where lll.toml or ll.toml lives)
    Files    : LoadedFile list // topo-sorted (dependencies first)
}

// ---- Error helpers ---------------------------------------------------------

let private e020 (filePath: string) (expected: string list) (actual: string list) : LLError =
    let exp = String.concat "." expected
    let act = String.concat "." actual
    mkLLError E020 0 0 (sprintf "ModulePathMismatch file:%s expected:%s got:%s" filePath exp act)

let private e024 (cycle: string list) : LLError =
    let path = String.concat " -> " cycle
    mkLLError E024 0 0 (sprintf "ModuleCycle %s" path)

// ---- Helpers ---------------------------------------------------------------

/// Convert a file path relative to rootDir/src/ into the expected module path.
/// e.g. rootDir=~/myapp, file=~/myapp/src/Foo/Bar.lll, name=myapp
///   → ["Myapp"; "Foo"; "Bar"]
let private fileToExpectedModulePath (rootDir: string) (projectName: string) (filePath: string) : string list =
    // Normalise the project name: capitalise first letter to match F# convention
    let capName =
        if projectName.Length = 0 then projectName
        else string (Char.ToUpper projectName.[0]) + projectName.[1..]
    let srcDir = Path.Combine(rootDir, "src")
    // Compute relative path from src/
    let rel = Path.GetRelativePath(srcDir, filePath)
    // Strip .lll extension and split by directory separator
    let noExt =
        match Path.ChangeExtension(rel, null) with
        | null -> rel
        | value -> value
    let parts =
        noExt.Split([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
    capName :: parts

/// Glob all .lll files under a directory recursively.
let private globLllFiles (dir: string) : string list =
    if not (Directory.Exists dir) then []
    else
        Directory.GetFiles(dir, "*.lll", SearchOption.AllDirectories)
        |> Array.toList
        |> List.map Path.GetFullPath

// ---- Kahn's topological sort -----------------------------------------------

/// Perform Kahn's algorithm on an adjacency list (node → dependencies).
/// Returns Ok (sorted list, leaves first) or Error (cycle members).
let private topoSort (nodes: 'a list) (deps: Map<'a, 'a list>) : Result<'a list, 'a list> =
    // Build in-degree map and adjacency (dependee → list of dependents)
    let mutable inDegree : Map<'a, int> = nodes |> List.map (fun n -> n, 0) |> Map.ofList
    let mutable revEdges : Map<'a, 'a list> = nodes |> List.map (fun n -> n, []) |> Map.ofList

    for n in nodes do
        let ds = deps |> Map.tryFind n |> Option.defaultValue []
        for d in ds do
            // n depends on d → d must come before n → d has an outgoing edge to n
            inDegree <- Map.add n (inDegree[n] + 1) inDegree
            revEdges <- Map.add d (n :: revEdges[d]) revEdges

    let mutable queue = nodes |> List.filter (fun n -> inDegree[n] = 0)
    let mutable result: 'a list = []

    while not queue.IsEmpty do
        let n = List.head queue
        queue <- List.tail queue
        result <- result @ [n]
        for m in revEdges[n] do
            let newDeg = inDegree[m] - 1
            inDegree <- Map.add m newDeg inDegree
            if newDeg = 0 then
                queue <- queue @ [m]

    if result.Length = nodes.Length then
        Ok result
    else
        // Remaining nodes with in-degree > 0 are in a cycle
        let cycleNodes = nodes |> List.filter (fun n -> inDegree[n] > 0)
        Error cycleNodes

// ---- Manifest discovery ----------------------------------------------------

/// Find the manifest file in dir, preferring lll.toml with fallback to ll.toml.
let findManifest (dir: string) : string option =
    let lll = Path.Combine(dir, "lll.toml")
    let ll  = Path.Combine(dir, "ll.toml")
    if   File.Exists(lll) then Some lll
    elif File.Exists(ll)  then Some ll   // backwards compat
    else None

// ---- Dep loading -----------------------------------------------------------

/// Load .lll files from vendor/{depName}/src/ for all installed deps.
let private loadDepFiles (rootDir: string) (manifest: LLManifest) : LoadedFile list =
    let depBaseDir = Path.Combine(rootDir, "vendor")
    [ for KeyValue(depName, _source) in manifest.Deps do
        let depDir = Path.Combine(depBaseDir, depName)
        if Directory.Exists(depDir) then
            // Read dep's own lll.toml (or ll.toml) to get its project name
            let depProjectName =
                match findManifest depDir with
                | Some depManifestPath ->
                    match parseManifest (File.ReadAllText depManifestPath) with
                    | Ok m -> m.Name
                    | Error _ -> depName
                | None -> depName
            let depSrcDir = Path.Combine(depDir, "src")
            for filePath in globLllFiles depSrcDir do
                let src =
                    try Some (File.ReadAllText filePath)
                    with _ -> None
                match src with
                | None -> ()
                | Some srcText ->
                    // Derive module path using dep's project name as prefix
                    let modulePath = fileToExpectedModulePath depDir depProjectName filePath
                    yield { ModulePath = modulePath; FilePath = filePath; Src = srcText }
    ]

// ---- Main entry point ------------------------------------------------------

/// Load and topo-sort all .lll files in a project rooted at rootDir.
/// Returns the sorted LLProject or a list of errors.
let loadProject (rootDir: string) : Result<LLProject, LLError list> =
    // 1. Parse lll.toml (falling back to ll.toml for backwards compat)
    let manifestPath =
        match findManifest rootDir with
        | Some p -> p
        | None   -> Path.Combine(rootDir, "lll.toml")  // will fail with a clear message below
    let manifestSrc =
        try Ok (File.ReadAllText manifestPath)
        with ex -> Error [mkLLError E001 0 0 (sprintf "CannotReadManifest %s: %s" manifestPath ex.Message)]
    match manifestSrc with
    | Error es -> Error es
    | Ok manifestText ->
    match parseManifest manifestText with
    | Error msg -> Error [mkLLError E001 0 0 (sprintf "ManifestError %s" msg)]
    | Ok manifest ->
    // 2. Glob all .lll files under rootDir/src/
    let srcDir = Path.Combine(rootDir, "src")
    let allFiles = globLllFiles srcDir

    // 3. Parse each file to extract module path and imports
    let mutable errors: LLError list = []
    let mutable loadedFiles: LoadedFile list = []
    let mutable moduleMap: Map<string list, string> = Map.empty  // path → filePath

    for filePath in allFiles do
        let src =
            try Some (File.ReadAllText filePath)
            with ex ->
                errors <- errors @ [mkLLError E001 0 0 (sprintf "CannotReadFile %s: %s" filePath ex.Message)]
                None
        match src with
        | None -> ()
        | Some srcText ->
            match tokenize srcText with
            | Error e ->
                errors <- errors @ [mkLLError E001 0 0 (sprintf "LexError %s: %s" filePath e)]
            | Ok toks ->
                match parseModuleWithPos toks with
                | Error e ->
                    errors <- errors @ [mkLLError E001 0 0 (sprintf "ParseError %s: %s" filePath e)]
                | Ok (m, _) ->
                    // 4. Validate module path matches file path
                    let expected = fileToExpectedModulePath rootDir manifest.Name filePath
                    if m.Path <> [] && m.Path <> expected then
                        errors <- errors @ [e020 filePath expected m.Path]
                    else
                        let effectivePath = if m.Path = [] then expected else m.Path
                        loadedFiles <- loadedFiles @ [{ ModulePath = effectivePath; FilePath = filePath; Src = srcText }]
                        moduleMap <- Map.add effectivePath filePath moduleMap

    if not errors.IsEmpty then Error errors
    else
    // 4b. Merge dep files (from vendor/) with main project files
    let depFiles = loadDepFiles rootDir manifest
    let allLoadedFiles = depFiles @ loadedFiles

    // 5. Build import graph for topo-sort
    // Map each module path to its imports
    let modulePathOf (lf: LoadedFile) = lf.ModulePath
    let allPaths = allLoadedFiles |> List.map modulePathOf

    // We need to re-parse to get imports for topo-sort
    // Build map: modulePath → import paths
    let importMap : Map<string list, string list list> =
        allLoadedFiles
        |> List.map (fun lf ->
            match tokenize lf.Src with
            | Ok toks ->
                match parseModuleWithPos toks with
                | Ok (m, _) -> lf.ModulePath, m.Imports
                | Error _ -> lf.ModulePath, []
            | Error _ -> lf.ModulePath, [])
        |> Map.ofList

    // Build dependency map: path → list of paths it depends on
    let depsMap : Map<string list, string list list> =
        allLoadedFiles
        |> List.map (fun lf ->
            let imports = importMap |> Map.tryFind lf.ModulePath |> Option.defaultValue []
            // Filter to only imports that are part of this project (incl. deps); dedup to avoid false cycles
            let projectImports = imports |> List.distinct |> List.filter (fun imp -> List.contains imp allPaths)
            lf.ModulePath, projectImports)
        |> Map.ofList

    // 6. Topo-sort
    match topoSort allPaths depsMap with
    | Error cyclePaths ->
        let cycleNames = cyclePaths |> List.map (String.concat ".")
        Error [e024 cycleNames]
    | Ok sortedPaths ->
        let filesByPath = allLoadedFiles |> List.map (fun lf -> lf.ModulePath, lf) |> Map.ofList
        let sortedFiles =
            sortedPaths
            |> List.choose (fun p -> Map.tryFind p filesByPath)
        Ok {
            Manifest = manifest
            RootDir  = rootDir
            Files    = sortedFiles
        }
