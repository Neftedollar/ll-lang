module LLLang.Tool

open System
open System.IO
open System.Diagnostics
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open LLLang.AST
open LLLang.Elaborator
open LLLang.Compiler
open LLLang.Manifest
open LLLang.Platform
open LLLang.ProjectLoader
open LLLang.FParsecParser
open LLLang.ReverseTranspiler

let private printErrors (es: LLError list) =
    for e in es do
        eprintfn "%s" e.Message

let private startProcessOrFail (psi: ProcessStartInfo) : Process =
    match Process.Start(psi) with
    | null -> failwith "Failed to start process."
    | proc -> proc

let private tryGetEnv (name: string) : string option =
    match Environment.GetEnvironmentVariable(name) with
    | null -> None
    | value -> Some value

let private directoryNameOrCurrent (path: string) : string =
    match Path.GetDirectoryName(path) with
    | null
    | "" -> Directory.GetCurrentDirectory()
    | dir -> dir

let private tryParentDir (dir: string) : string option =
    match Directory.GetParent(dir) with
    | null -> None
    | parent when parent.FullName = dir -> None
    | parent -> Some parent.FullName

let private fileNameOrEmpty (path: string) : string =
    match Path.GetFileName(path) with
    | null -> ""
    | value -> value

let private fileNameWithoutExtensionOrEmpty (path: string) : string =
    match Path.GetFileNameWithoutExtension(path) with
    | null -> ""
    | value -> value

let private extensionOrEmpty (path: string) : string =
    match Path.GetExtension(path) with
    | null -> ""
    | value -> value

let private changeExtensionOrInput (path: string) (ext: string) : string =
    match Path.ChangeExtension(path, ext) with
    | null -> path
    | value -> value

/// Parse --target flag from args list. Returns (target override, remaining args).
let private parseTarget (args: string list) : Result<Target option * string list, string> =
    match args with
    | "--target" :: t :: rest ->
        match tryParseTarget t with
        | Some target -> Ok (Some target, rest)
        | None ->
            let known = knownTargetAliases () |> String.concat ", "
            Error ("lllc: unknown target '" + t + "'. known aliases: " + known)
    | _ -> Ok (None, args)

/// Extension for each compilation target.
let private targetExt (target: Target) = targetOutputExt target

let private targetOrDefault (targetOpt: Target option) : Target =
    targetOpt |> Option.defaultValue FSharp

let private moduleHasTopLevelMain (m: LLModule) : bool =
    m.Decls
    |> List.exists (fun (decl, _) ->
        match decl with
        | DFn(sig_, _) -> sig_.Name = "main"
        | DLet(name, _) -> name = "main"
        | _ -> false)

let private sourceHasTopLevelMain (src: string) : bool =
    match parseModuleWithPos src with
    | Ok (m, _) -> moduleHasTopLevelMain m
    | Error _ -> false

let private resolveProjectTargets (manifestPlatforms: string list) : Result<Target list, string list> =
    let platforms =
        if manifestPlatforms.IsEmpty then ["fsharp"]
        else normalizePlatforms manifestPlatforms
    let unknown =
        platforms
        |> List.filter (fun p -> tryParseTarget p |> Option.isNone)
        |> List.distinct
    if not (List.isEmpty unknown) then
        let known = knownTargetAliases () |> String.concat ", "
        let msgs =
            unknown
            |> List.map (fun p -> "lllc: unknown platform '" + p + "' in manifest [platform].use; known aliases: " + known)
        Error msgs
    else
        let targets =
            platforms
            |> List.choose tryParseTarget
            |> List.distinct
        Ok targets

type private EmittedArtifact = {
    Target : Target
    OutDir : string
    MainFilePath : string
    ProjectFilePath : string option
    JavaSourceFilePath : string option
    JavaClassName : string option
}

let private templateOutputFileName (projectName: string) (templatePath: string) : string =
    let fileName = fileNameWithoutExtensionOrEmpty templatePath // strip ".tmpl"
    let ext = extensionOrEmpty fileName |> fun x -> x.ToLowerInvariant()
    match ext with
    | ".fsproj"
    | ".csproj" -> projectName + ext
    | _ -> fileName

let private writeRuntimeTemplateIfAvailable
    (projectName: string)
    (target: Target)
    (outDir: string)
    (mainFileName: string)
    (isExecutable: bool)
    : string option =
    match tryResolveRuntimeTemplate target with
    | None -> None
    | Some templatePath ->
        let mainFileStem = fileNameWithoutExtensionOrEmpty mainFileName
        let outputType = if isExecutable then "Exe" else "Library"
        let rendered =
            File.ReadAllText(templatePath)
                .Replace("{project_name}", projectName)
                .Replace("{main_file}", mainFileName)
                .Replace("{main_file_stem}", mainFileStem)
                .Replace("{output_type}", outputType)
                .Replace("<OutputType>Exe</OutputType>", "<OutputType>" + outputType + "</OutputType>")
        let outFile = templateOutputFileName projectName templatePath
        let outPath = Path.Combine(outDir, outFile)
        File.WriteAllText(outPath, rendered)
        Some outPath

let private tryExtractJavaPublicClassName (javaCode: string) : string option =
    let m = Regex.Match(javaCode, @"public\s+class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)")
    if m.Success then Some m.Groups.["name"].Value else None

let private withJavaCommandMetadata (artifact: EmittedArtifact) : EmittedArtifact =
    match artifact.Target with
    | Java ->
        if not (File.Exists artifact.MainFilePath) then
            artifact
        else
            let javaCode = File.ReadAllText(artifact.MainFilePath)
            match tryExtractJavaPublicClassName javaCode with
            | None -> artifact
            | Some className ->
                let expectedFileName = className + ".java"
                let expectedPath = Path.Combine(artifact.OutDir, expectedFileName)
                let javaSourcePath =
                    if StringComparer.Ordinal.Equals(fileNameOrEmpty artifact.MainFilePath, expectedFileName) then
                        artifact.MainFilePath
                    else
                        let writeMirror =
                            if not (File.Exists(expectedPath)) then true
                            else
                                let existing = File.ReadAllText(expectedPath)
                                existing <> javaCode
                        if writeMirror then
                            File.WriteAllText(expectedPath, javaCode)
                        expectedPath
                {
                    artifact with
                        JavaSourceFilePath = Some javaSourcePath
                        JavaClassName = Some className
                }
    | _ -> artifact

let private renderSdkCommand (template: string) (artifact: EmittedArtifact) : string =
    let shQuote (s: string) =
        "'" + s.Replace("'", "'\"'\"'") + "'"
    let projectFile = artifact.ProjectFilePath |> Option.defaultValue artifact.MainFilePath
    let javaSourceFile = artifact.JavaSourceFilePath |> Option.defaultValue artifact.MainFilePath
    let mainFileStem = Path.Combine(artifact.OutDir, fileNameWithoutExtensionOrEmpty artifact.MainFilePath)
    let mainJsFile = mainFileStem + ".js"
    let mainBcFile = mainFileStem + ".bc"
    let javaClassName =
        artifact.JavaClassName
        |> Option.defaultValue (fileNameWithoutExtensionOrEmpty javaSourceFile)
    template
        .Replace("{project_file}", projectFile)
        .Replace("{main_file}", artifact.MainFilePath)
        .Replace("{main_file_stem}", mainFileStem)
        .Replace("{main_js_file}", mainJsFile)
        .Replace("{main_bc_file}", mainBcFile)
        .Replace("{java_source_file}", javaSourceFile)
        .Replace("{java_class_name}", javaClassName)
        .Replace("{out_dir}", artifact.OutDir)
        .Replace("{project_file_q}", shQuote projectFile)
        .Replace("{main_file_q}", shQuote artifact.MainFilePath)
        .Replace("{main_file_stem_q}", shQuote mainFileStem)
        .Replace("{main_js_file_q}", shQuote mainJsFile)
        .Replace("{main_bc_file_q}", shQuote mainBcFile)
        .Replace("{java_source_file_q}", shQuote javaSourceFile)
        .Replace("{java_class_name_q}", shQuote javaClassName)
        .Replace("{out_dir_q}", shQuote artifact.OutDir)

let private printSdkSuggestions (artifact: EmittedArtifact) : unit =
    let artifact = withJavaCommandMetadata artifact
    match tryGetBuildCompileCommand artifact.Target with
    | Some compileCmd ->
        Console.WriteLine("  suggested compile: " + renderSdkCommand compileCmd artifact)
    | None -> ()
    match tryGetBuildRunCommand artifact.Target with
    | Some runCmd ->
        Console.WriteLine("  suggested run: " + renderSdkCommand runCmd artifact)
    | None -> ()

/// Build: compile file.lll → file.<ext>. Returns exit code.
let private cmdBuild (path: string) (target: Target) : int =
    try
        let src = File.ReadAllText(path)
        let isExecutable = sourceHasTopLevelMain src
        match compileTarget target src with
        | Ok out ->
            let outPath = changeExtensionOrInput path (targetExt target)
            File.WriteAllText(outPath, out)
            let outDir = directoryNameOrCurrent outPath
            let stem = fileNameOrEmpty outPath
            let projectStem = fileNameWithoutExtensionOrEmpty outPath
            let templatePath = writeRuntimeTemplateIfAvailable projectStem target outDir stem isExecutable
            Console.WriteLine("Built " + stem)
            let artifact =
                {
                    Target = target
                    OutDir = outDir
                    MainFilePath = outPath
                    ProjectFilePath = templatePath
                    JavaSourceFilePath = None
                    JavaClassName = None
                }
            printSdkSuggestions artifact
            0
        | Error es ->
            printErrors es
            1
    with
    | ex ->
        eprintfn "lllc: %s" ex.Message
        1

/// Write output files for one target into bin/<platform>/.
let private writeTargetOutput
    (rootDir: string)
    (name: string)
    (target: Target)
    (code: string)
    (isExecutable: bool)
    : EmittedArtifact =
    let platform = targetPlatformName target
    let outDir = Path.Combine(rootDir, "bin", platform)
    Directory.CreateDirectory(outDir) |> ignore
    let writeMainAndTemplate (ext: string) =
        let outPath = Path.Combine(outDir, name + ext)
        File.WriteAllText(outPath, code)
        let templatePath = writeRuntimeTemplateIfAvailable name target outDir (fileNameOrEmpty outPath) isExecutable
        outPath, templatePath
    match target with
    | FSharp ->
        // Single-file fallback path (multi-file F# output uses writeTargetOutputMultiFile).
        let (outPath, templateProjectPath) = writeMainAndTemplate ".fs"
        let projectPath = Path.Combine(outDir, name + ".fsproj")
        if not (File.Exists(projectPath)) then
            let outputType = if isExecutable then "Exe" else "Library"
            let fsproj = $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>{outputType}</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>preview</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{name}.fs" />
  </ItemGroup>
</Project>
        """
            File.WriteAllText(projectPath, fsproj)
        Console.WriteLine("Built project '" + name + "' [fsharp] -> " + outPath)
        {
            Target = target
            OutDir = outDir
            MainFilePath = outPath
            ProjectFilePath = Some (templateProjectPath |> Option.defaultValue projectPath)
            JavaSourceFilePath = None
            JavaClassName = None
        }
    | _ ->
        let (outPath, templateProjectPath) = writeMainAndTemplate (targetOutputExt target)
        Console.WriteLine("Built project '" + name + "' [" + platform + "] -> " + outPath)
        {
            Target = target
            OutDir = outDir
            MainFilePath = outPath
            ProjectFilePath = templateProjectPath
            JavaSourceFilePath = None
            JavaClassName = None
        }

/// Write multi-file F# output into bin/fsharp/ — one .fs per module plus Prelude.fs.
/// Generates a .fsproj that lists every file in the correct compilation order.
let private writeTargetOutputMultiFile
    (rootDir: string)
    (name: string)
    (files: (string * string) list)
    (isExecutable: bool)
    : EmittedArtifact =
    let outDir = Path.Combine(rootDir, "bin", "fsharp")
    Directory.CreateDirectory(outDir) |> ignore
    for (fileName, content) in files do
        File.WriteAllText(Path.Combine(outDir, fileName), content)
    let compileItems =
        files
        |> List.map (fun (fn, _) -> sprintf "    <Compile Include=\"%s\" />" fn)
        |> String.concat "\n"
    let outputType = if isExecutable then "Exe" else "Library"
    let fsproj =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
        + "  <PropertyGroup>\n"
        + "    <OutputType>" + outputType + "</OutputType>\n"
        + "    <TargetFramework>net10.0</TargetFramework>\n"
        + "    <LangVersion>preview</LangVersion>\n"
        + "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n"
        + "  </PropertyGroup>\n"
        + "  <ItemGroup>\n"
        + compileItems + "\n"
        + "  </ItemGroup>\n"
        + "</Project>\n"
    File.WriteAllText(Path.Combine(outDir, name + ".fsproj"), fsproj)
    let outPath = Path.Combine(outDir, name + ".fsproj")
    Console.WriteLine("Built project '" + name + "' [fsharp] -> " + outPath + " (" + string (List.length files) + " files)")
    let mainFilePath =
        match files |> List.tryFind (fun (fn, _) -> fn = "Main.fs") with
        | Some (fn, _) -> Path.Combine(outDir, fn)
        | None ->
            match files with
            | (fn, _) :: _ -> Path.Combine(outDir, fn)
            | [] -> outPath
    {
        Target = FSharp
        OutDir = outDir
        MainFilePath = mainFilePath
        ProjectFilePath = Some outPath
        JavaSourceFilePath = None
        JavaClassName = None
    }

let private writeSiblingFSharpFilesAndProject
    (outDir: string)
    (name: string)
    (files: (string * string) list)
    (isExecutable: bool)
    : EmittedArtifact =
    Directory.CreateDirectory(outDir) |> ignore
    for (fileName, content) in files do
        File.WriteAllText(Path.Combine(outDir, fileName), content)
    let compileItems =
        files
        |> List.map (fun (fn, _) -> "    <Compile Include=\"" + fn + "\" />")
        |> String.concat "\n"
    let outputType = if isExecutable then "Exe" else "Library"
    let fsproj =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
        + "  <PropertyGroup>\n"
        + "    <OutputType>" + outputType + "</OutputType>\n"
        + "    <TargetFramework>net10.0</TargetFramework>\n"
        + "    <LangVersion>preview</LangVersion>\n"
        + "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n"
        + "  </PropertyGroup>\n"
        + "  <ItemGroup>\n"
        + compileItems + "\n"
        + "  </ItemGroup>\n"
        + "</Project>\n"
    let fsprojPath = Path.Combine(outDir, name + ".fsproj")
    File.WriteAllText(fsprojPath, fsproj)
    let mainFilePath =
        match files |> List.tryFind (fun (fn, _) -> fn = "Main.fs") with
        | Some (fn, _) -> Path.Combine(outDir, fn)
        | None ->
            match files with
            | (fn, _) :: _ -> Path.Combine(outDir, fn)
            | [] -> fsprojPath
    {
        Target = FSharp
        OutDir = outDir
        MainFilePath = mainFilePath
        ProjectFilePath = Some fsprojPath
        JavaSourceFilePath = None
        JavaClassName = None
    }

/// Build project rooted at rootDir (has lll.toml or ll.toml). Returns exit code.
/// targetOverride, when present, compiles only that target and ignores manifest platforms.
let private cmdBuildProject (rootDir: string) (targetOverride: Target option) : int =
    try
        match loadProject rootDir with
        | Error es -> printErrors es; 1
        | Ok proj ->
            let isExecutableProject = not (String.IsNullOrWhiteSpace proj.Manifest.Entry)
            let compileForTarget (target: Target) =
                match LLLang.Compiler.compileProjectToModulesForTarget target proj with
                | Error es ->
                    printErrors es
                    false
                | Ok tms ->
                    let artifact =
                        match target with
                        | FSharp ->
                            // Multi-file: Prelude.fs + one .fs per module, valid .fsproj.
                            let files = LLLang.Codegen.emitProjectFiles tms
                            writeTargetOutputMultiFile rootDir proj.Manifest.Name files isExecutableProject
                        | TypeScript ->
                            let code = LLLang.CodegenTS.emitProjectModules tms
                            writeTargetOutput rootDir proj.Manifest.Name TypeScript code isExecutableProject
                        | Python ->
                            let code = LLLang.CodegenPy.emitProjectModules tms
                            writeTargetOutput rootDir proj.Manifest.Name Python code isExecutableProject
                        | Java ->
                            let code = LLLang.CodegenJava.emitProjectModules tms
                            writeTargetOutput rootDir proj.Manifest.Name Java code isExecutableProject
                        | CSharp ->
                            let code = LLLang.CodegenCSharp.emitProjectModules tms
                            writeTargetOutput rootDir proj.Manifest.Name CSharp code isExecutableProject
                        | LLVM ->
                            let code = LLLang.CodegenLLVM.emitProjectModules tms
                            writeTargetOutput rootDir proj.Manifest.Name LLVM code isExecutableProject
                    printSdkSuggestions artifact
                    true
            let targetsResult =
                match targetOverride with
                | Some target -> Ok [target]
                | None -> resolveProjectTargets proj.Manifest.Platform
            match targetsResult with
            | Error msgs ->
                for m in msgs do Console.Error.WriteLine(m)
                1
            | Ok targets ->
                let mutable exitCode = 0
                for target in targets do
                    if not (compileForTarget target) then exitCode <- 1
                exitCode
    with ex ->
        Console.Error.WriteLine("lllc: " + ex.Message)
        1

/// Check file: lex/parse/elaborate/infer and target-specific external mapping checks.
let private cmdCheckFile (path: string) (target: Target) : int =
    try
        let src = File.ReadAllText(path)
        match checkTarget target src with
        | Ok () ->
            Console.WriteLine("Checked " + fileNameOrEmpty path + " [" + targetPlatformName target + "]")
            0
        | Error es ->
            printErrors es
            1
    with ex ->
        Console.Error.WriteLine("lllc: " + ex.Message)
        1

/// Check project rooted at rootDir (has lll.toml or ll.toml). Returns exit code.
/// targetOverride, when present, checks only that target and ignores manifest platforms.
let private cmdCheckProject (rootDir: string) (targetOverride: Target option) : int =
    try
        match loadProject rootDir with
        | Error es ->
            printErrors es
            1
        | Ok proj ->
            let targetsResult =
                match targetOverride with
                | Some target -> Ok [target]
                | None -> resolveProjectTargets proj.Manifest.Platform
            match targetsResult with
            | Error msgs ->
                for m in msgs do Console.Error.WriteLine(m)
                1
            | Ok targets ->
                let mutable exitCode = 0
                for target in targets do
                    match LLLang.Compiler.compileProjectToModulesForTarget target proj with
                    | Ok _ ->
                        Console.WriteLine("Checked project '" + proj.Manifest.Name + "' [" + targetPlatformName target + "]")
                    | Error es ->
                        printErrors es
                        exitCode <- 1
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
            match tryParentDir dir with
            | None -> found <- true // no more parents
            | Some parentDir -> dir <- parentDir
    result

// ---- Auto-resolve imports for `lllc run` ------------------------------------

/// Find the stdlib directory by trying several strategies.
let private findStdlibDir (mainFilePath: string) : string option =
    // Strategy 1: LL_STDLIB_PATH environment variable
    let envPath = tryGetEnv "LL_STDLIB_PATH"
    match envPath with
    | Some p when Directory.Exists(p) -> Some p
    | _ ->
        // Strategy 2: Walk up from app base directory looking for stdlib/src/.
        // Assembly.Location can be empty in single-file/AOT deployments.
        let compilerBin = AppContext.BaseDirectory
        let fromMainAncestors =
            let startDir = directoryNameOrCurrent mainFilePath
            let rec loop (dir: string) =
                let cand = Path.Combine(dir, "stdlib", "src")
                if Directory.Exists(cand) then Some cand
                else
                    match tryParentDir dir with
                    | None -> None
                    | Some parentDir -> loop parentDir
            loop startDir

        let candidates =
            [ // binary is typically at src/LLLangTool/bin/Debug/net10.0/ → 5 levels up is repo root
              Path.GetFullPath(Path.Combine(compilerBin, "..", "..", "..", "..", "..", "stdlib", "src"))
              Path.GetFullPath(Path.Combine(compilerBin, "..", "..", "..", "..", "stdlib", "src"))
              Path.GetFullPath(Path.Combine(compilerBin, "..", "..", "..", "stdlib", "src"))
              // Strategy 3: relative to main file's directory
              Path.GetFullPath(Path.Combine(directoryNameOrCurrent mainFilePath, "stdlib", "src"))
              // Strategy 4: relative to current working directory
              Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "stdlib", "src"))
            ]
        match fromMainAncestors with
        | Some p -> Some p
        | None -> candidates |> List.tryFind Directory.Exists

/// Parse imports out of a .lll source string without failing if there are errors.
let private extractImports (src: string) : string list list =
    match parseModuleWithPos src with
    | Error _ -> []
    | Ok (m, _) -> m.Imports

/// Given an import path like ["Std"; "Maybe"], find the file on disk.
/// Searches stdlib/src/, vendor/ relative to mainFileDir, and mainFileDir itself.
let private resolveImport (mainFilePath: string) (importPath: string list) : string option =
    let mainFileDir = directoryNameOrCurrent mainFilePath
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
    // For vendor/: depName matches first segment (lowercased)
    let depCandidates =
        let depName = (List.head importPath).ToLower()
        let depSrc = Path.Combine(mainFileDir, "vendor", depName, "src")
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
                                        match parseModuleWithPos depSrc with
                                        | Ok (m, _) when m.Path <> [] -> m.Path
                                        | _ ->
                                            let stem = fileNameWithoutExtensionOrEmpty depPath
                                            [stem]
                                    if not (Map.containsKey absDepPath loadedMap) then
                                        loadedMap <- Map.add absDepPath { ModulePath = modulePath; FilePath = absDepPath; Src = depSrc } loadedMap
            match err with
            | Some e -> Error e
            | None ->
                depGraph <- Map.add absPath depPaths depGraph
                // Register main file if not already
                let modulePath =
                    match parseModuleWithPos src with
                    | Ok (m, _) when m.Path <> [] -> m.Path
                    | _ ->
                        let stem = fileNameWithoutExtensionOrEmpty filePath
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

/// Build one .lll file (with transitive imports) and emit next to the source.
let private cmdBuildFile (path: string) (targetOverride: Target option) : int =
    try
        let absPath = Path.GetFullPath(path)
        let src = File.ReadAllText(absPath)
        let isExecutable = sourceHasTopLevelMain src
        let target = targetOrDefault targetOverride
        match resolveRunImports absPath src with
        | Error msg ->
            Console.Error.WriteLine("lllc: import resolution error: " + msg)
            1
        | Ok files ->
            let rootDir = directoryNameOrCurrent absPath
            let stem = fileNameWithoutExtensionOrEmpty absPath
            let fakeManifest : LLManifest =
                { Name = fileNameWithoutExtensionOrEmpty absPath
                  Version = "0.0.0"
                  Entry = ""
                  Deps = Map.empty
                  Platform = [] }
            let proj : LLProject = { Manifest = fakeManifest; RootDir = rootDir; Files = files }
            match LLLang.Compiler.compileProjectToModulesForTarget target proj with
            | Error es ->
                printErrors es
                1
            | Ok tms ->
                let artifact =
                    match target with
                    | FSharp when List.length tms > 1 ->
                        // Multi-module single-file builds must stay multi-file for valid F#.
                        let filesOut = LLLang.Codegen.emitProjectFiles tms
                        let built = writeSiblingFSharpFilesAndProject rootDir stem filesOut isExecutable
                        let outPath = Path.Combine(rootDir, stem + ".fs")
                        Console.WriteLine("Built " + fileNameOrEmpty outPath)
                        built
                    | _ ->
                        let out =
                            match target with
                            | FSharp -> LLLang.Codegen.emitProjectModules tms
                            | TypeScript -> LLLang.CodegenTS.emitProjectModules tms
                            | Python -> LLLang.CodegenPy.emitProjectModules tms
                            | Java -> LLLang.CodegenJava.emitProjectModules tms
                            | CSharp -> LLLang.CodegenCSharp.emitProjectModules tms
                            | LLVM -> LLLang.CodegenLLVM.emitProjectModules tms
                        let outPath = changeExtensionOrInput absPath (targetExt target)
                        File.WriteAllText(outPath, out)
                        let outDir = directoryNameOrCurrent outPath
                        let stemOut = fileNameWithoutExtensionOrEmpty outPath
                        let templatePath = writeRuntimeTemplateIfAvailable stemOut target outDir (fileNameOrEmpty outPath) isExecutable
                        Console.WriteLine("Built " + fileNameOrEmpty outPath)
                        {
                            Target = target
                            OutDir = outDir
                            MainFilePath = outPath
                            ProjectFilePath = templatePath
                            JavaSourceFilePath = None
                            JavaClassName = None
                        }
                printSdkSuggestions artifact
                0
    with
    | ex ->
        Console.Error.WriteLine("lllc: " + ex.Message)
        1

/// Reverse: recover a minimal ll-lang module from generated target code.
let private cmdReverse (args: string list) : int =
    match args with
    | ["--from"; rawTarget; path] ->
        try
            match tryParseTarget rawTarget with
            | None ->
                let known = knownTargetAliases () |> String.concat ", "
                Console.Error.WriteLine("lllc: unknown reverse source target '" + rawTarget + "'. known aliases: " + known)
                1
            | Some target ->
                let absPath = Path.GetFullPath(path)
                let src = File.ReadAllText(absPath)
                match reverseToLll target src with
                | Error msg ->
                    Console.Error.WriteLine("lllc: " + msg)
                    1
                | Ok lll ->
                    let outPath =
                        let dir = directoryNameOrCurrent absPath
                        let stem = fileNameWithoutExtensionOrEmpty absPath
                        let baseDir =
                            if String.IsNullOrWhiteSpace(dir) then Directory.GetCurrentDirectory() else dir
                        Path.Combine(baseDir, stem + ".reversed.lll")
                    File.WriteAllText(outPath, lll)
                    Console.WriteLine("Reversed " + fileNameOrEmpty absPath + " -> " + fileNameOrEmpty outPath)
                    0
        with
        | ex ->
            Console.Error.WriteLine("lllc: " + ex.Message)
            1
    | _ ->
        Console.Error.WriteLine("lllc: usage: lllc reverse --from fs|ts|py|java|cs|llvm|Platform.*.SDK <file>  [experimental]")
        1

let private runShellCommand (workingDir: string) (command: string) : int =
    let psi = ProcessStartInfo("sh")
    psi.WorkingDirectory <- workingDir
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- false
    psi.RedirectStandardError <- false
    psi.ArgumentList.Add("-lc")
    psi.ArgumentList.Add(command)
    use proc = startProcessOrFail psi
    proc.WaitForExit()
    proc.ExitCode

let private inferredProjectFilePath (target: Target) (mainFilePath: string) : string option =
    let outDir = directoryNameOrCurrent mainFilePath
    let stem = fileNameWithoutExtensionOrEmpty mainFilePath
    let fromTemplate =
        match tryResolveRuntimeTemplate target with
        | None -> None
        | Some templatePath ->
            Some (Path.Combine(outDir, templateOutputFileName stem templatePath))
    match target, fromTemplate with
    | FSharp, None ->
        let fsproj = Path.Combine(outDir, stem + ".fsproj")
        if File.Exists(fsproj) then Some fsproj else None
    | _, _ -> fromTemplate

let private cmdRunViaSdk (path: string) (target: Target) : int =
    let buildExit = cmdBuildFile path (Some target)
    if buildExit <> 0 then
        buildExit
    else
        let absPath = Path.GetFullPath(path)
        let outPath = changeExtensionOrInput absPath (targetExt target)
        let outDir = directoryNameOrCurrent outPath
        let artifact =
            {
                Target = target
                OutDir = outDir
                MainFilePath = outPath
                ProjectFilePath = inferredProjectFilePath target outPath
                JavaSourceFilePath = None
                JavaClassName = None
            }
            |> withJavaCommandMetadata
        match tryGetBuildRunCommand target with
        | None ->
            Console.Error.WriteLine("lllc: SDK run command is not configured for target " + targetPlatformName target)
            1
        | Some cmd ->
            let resolved = renderSdkCommand cmd artifact
            Console.WriteLine("Running: " + resolved)
            runShellCommand artifact.OutDir resolved

let private runDotnetProject (projectPath: string) (workingDir: string option) (toolArgs: string list) : int =
    let buildPsi = ProcessStartInfo("dotnet")
    buildPsi.ArgumentList.Add("build")
    buildPsi.ArgumentList.Add("-c")
    buildPsi.ArgumentList.Add("Release")
    buildPsi.ArgumentList.Add("-v")
    buildPsi.ArgumentList.Add("q")
    buildPsi.ArgumentList.Add("--nologo")
    buildPsi.ArgumentList.Add(projectPath)
    buildPsi.RedirectStandardOutput <- true
    buildPsi.RedirectStandardError <- true
    buildPsi.UseShellExecute <- false
    match workingDir with
    | Some wd -> buildPsi.WorkingDirectory <- wd
    | None -> ()
    use buildProc = startProcessOrFail buildPsi
    let buildStdOut = buildProc.StandardOutput.ReadToEnd()
    let buildStdErr = buildProc.StandardError.ReadToEnd()
    buildProc.WaitForExit()
    if buildProc.ExitCode <> 0 then
        if not (String.IsNullOrWhiteSpace buildStdOut) then Console.Error.WriteLine(buildStdOut)
        if not (String.IsNullOrWhiteSpace buildStdErr) then Console.Error.WriteLine(buildStdErr)
        buildProc.ExitCode
    else
        let runPsi = ProcessStartInfo("dotnet")
        runPsi.ArgumentList.Add("run")
        runPsi.ArgumentList.Add("--no-build")
        runPsi.ArgumentList.Add("-c")
        runPsi.ArgumentList.Add("Release")
        runPsi.ArgumentList.Add("-v")
        runPsi.ArgumentList.Add("q")
        runPsi.ArgumentList.Add("--project")
        runPsi.ArgumentList.Add(projectPath)
        if not (List.isEmpty toolArgs) then
            runPsi.ArgumentList.Add("--")
            for a in toolArgs do
                runPsi.ArgumentList.Add(a)
        runPsi.RedirectStandardOutput <- false
        runPsi.RedirectStandardError <- false
        runPsi.UseShellExecute <- false
        match workingDir with
        | Some wd -> runPsi.WorkingDirectory <- wd
        | None -> ()
        use runProc = startProcessOrFail runPsi
        runProc.WaitForExit()
        runProc.ExitCode

/// Run: compile file.lll -> temp multi-file F# project -> dotnet run. Returns exit code.
let private cmdRun (path: string) (targetOverride: Target option) : int =
    try
        let target = targetOrDefault targetOverride
        if target <> FSharp then
            cmdRunViaSdk path target
        else
            let absPath = Path.GetFullPath(path)
            let src = File.ReadAllText(absPath)
            // Canonical run path: resolve imports -> compile project modules ->
            // materialize temporary F# project -> dotnet run.
            match resolveRunImports absPath src with
            | Error msg ->
                Console.Error.WriteLine("lllc: import resolution error: " + msg)
                1
            | Ok files ->
                let fakeManifest : LLManifest =
                    { Name = "run"
                      Version = "0.0.0"
                      Entry = ""
                      Deps = Map.empty
                      Platform = ["fsharp"] }
                let proj : LLProject =
                    { Manifest = fakeManifest
                      RootDir = directoryNameOrCurrent absPath
                      Files = files }
                match LLLang.Compiler.compileProjectToModules proj with
                | Error es ->
                    printErrors es
                    1
                | Ok tms ->
                    let filesOut = LLLang.Codegen.emitProjectFiles tms
                    let tempDir =
                        Path.Combine(Path.GetTempPath(), "lllc-run-" + Guid.NewGuid().ToString("N"))
                    Directory.CreateDirectory(tempDir) |> ignore
                    try
                        let artifact =
                            writeSiblingFSharpFilesAndProject tempDir "lllc_run" filesOut true
                        let projectPath =
                            artifact.ProjectFilePath |> Option.defaultValue (Path.Combine(tempDir, "lllc_run.fsproj"))
                        runDotnetProject projectPath None []
                    finally
                        try
                            Directory.Delete(tempDir, true)
                        with _ ->
                            ()
    with
    | ex ->
        Console.Error.WriteLine("lllc: " + ex.Message)
        1

let private fsStringLiteral (s: string) =
    let escaped =
        s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t")
    "\"" + escaped + "\""

let private findSelfMainPath () : string option =
    let cwd = Directory.GetCurrentDirectory()
    let fromEnv =
        match tryGetEnv "LLLC_SELF_MAIN" with
        | Some p when not (String.IsNullOrWhiteSpace p) ->
            let abs = Path.GetFullPath(p)
            if File.Exists(abs) then Some abs else None
        | _ -> None

    let fromCwdAncestors =
        let rec loop (dir: string) =
            let candidate = Path.Combine(dir, "lllcself", "src", "Main.lll")
            if File.Exists(candidate) then Some candidate
            else
                match tryParentDir dir with
                | None -> None
                | Some parentDir -> loop parentDir
        loop cwd

    let fromBaseDirCandidates =
        [
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "lllcself", "src", "Main.lll"))
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "lllcself", "src", "Main.lll"))
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "lllcself", "src", "Main.lll"))
        ]
        |> List.tryFind File.Exists

    match fromEnv with
    | Some p -> Some p
    | None ->
        match fromCwdAncestors with
        | Some p -> Some p
        | None -> fromBaseDirCandidates

let private cmdRunSelf (toolArgs: string list) : int =
    try
        match findSelfMainPath () with
        | None ->
            Console.Error.WriteLine("lllc: cannot find lllcself/src/Main.lll")
            1
        | Some selfPath ->
            let absPath = Path.GetFullPath(selfPath)
            let src = File.ReadAllText(absPath)
            match resolveRunImports absPath src with
            | Error msg ->
                Console.Error.WriteLine("lllc: import resolution error: " + msg)
                1
            | Ok files ->
                let fakeManifest : LLManifest = { Name = "lllcself"; Version = "0.0.0"; Entry = ""; Deps = Map.empty; Platform = ["fsharp"] }
                let proj : LLProject = { Manifest = fakeManifest; RootDir = directoryNameOrCurrent absPath; Files = files }
                match LLLang.Compiler.compileProjectToModules proj with
                | Error es ->
                    printErrors es
                    1
                | Ok tms ->
                    let tempDir = Path.Combine(Path.GetTempPath(), "lllcself-" + Guid.NewGuid().ToString("N"))
                    Directory.CreateDirectory(tempDir) |> ignore
                    try
                        let filesOut = LLLang.Codegen.emitProjectFiles tms
                        for (fn, content) in filesOut do
                            File.WriteAllText(Path.Combine(tempDir, fn), content)
                        let compileItems =
                            filesOut
                            |> List.map (fun (fn, _) -> "    <Compile Include=\"" + fn + "\" />")
                            |> String.concat "\n"
                        let fsproj =
                            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                            + "  <PropertyGroup>\n"
                            + "    <OutputType>Exe</OutputType>\n"
                            + "    <TargetFramework>net10.0</TargetFramework>\n"
                            + "    <LangVersion>preview</LangVersion>\n"
                            + "    <OtherFlags>--strict-indentation-</OtherFlags>\n"
                            + "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n"
                            + "  </PropertyGroup>\n"
                            + "  <ItemGroup>\n"
                            + compileItems + "\n"
                            + "  </ItemGroup>\n"
                            + "</Project>\n"
                        let fsprojPath = Path.Combine(tempDir, "lllcself.fsproj")
                        File.WriteAllText(fsprojPath, fsproj)
                        runDotnetProject fsprojPath (Some tempDir) toolArgs
                    finally
                        let keepTemp =
                            match tryGetEnv "LL_KEEP_SELF_TEMP" with
                            | Some v -> (v = "1" || v.ToLowerInvariant() = "true")
                            | None -> false
                        if not keepTemp then
                            try Directory.Delete(tempDir, true) with _ -> ()
    with ex ->
        Console.Error.WriteLine("lllc: " + ex.Message)
        1

let rec private copyDirectoryRecursive (srcDir: string) (dstDir: string) : unit =
    Directory.CreateDirectory(dstDir) |> ignore
    for filePath in Directory.GetFiles(srcDir) do
        let name =
            match Path.GetFileName(filePath) with
            | null -> ""
            | n -> n
        let dstPath = Path.Combine(dstDir, name)
        File.Copy(filePath, dstPath, true)
    for childDir in Directory.GetDirectories(srcDir) do
        let name =
            match Path.GetFileName(childDir) with
            | null -> ""
            | n -> n
        let dstChild = Path.Combine(dstDir, name)
        copyDirectoryRecursive childDir dstChild

let private depSourceToText (source: DepSource) : string =
    match source with
    | GitDep(url, ref) -> "git:" + url + "#" + ref
    | PathDep(path) -> "path:" + path

let private tryParseSemverTag (value: string) : (int * int * int) option =
    let raw = value.Trim()
    let trimmed =
        if raw.StartsWith("v", StringComparison.OrdinalIgnoreCase) then
            raw.Substring(1)
        else
            raw
    let parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries)
    if parts.Length = 0 || parts.Length > 3 then
        None
    else
        let tryPart (idx: int) =
            if idx < parts.Length then
                match Int32.TryParse(parts.[idx]) with
                | true, parsed -> Some parsed
                | _ -> None
            else
                Some 0
        match tryPart 0, tryPart 1, tryPart 2 with
        | Some major, Some minor, Some patch -> Some(major, minor, patch)
        | _ -> None

let private sourcePriorityKey (source: DepSource) : (int * string * int * int * int * string) =
    match source with
    | GitDep(url, ref) ->
        match tryParseSemverTag ref with
        // Negative values to sort descending by semver.
        | Some (major, minor, patch) -> (0, url, -major, -minor, -patch, ref)
        | None -> (0, url, Int32.MaxValue, Int32.MaxValue, Int32.MaxValue, ref)
    | PathDep(path) -> (1, path, 0, 0, 0, "")

let private pendingPriorityKey (name: string, source: DepSource, ownerRoot: string, chain: string list) =
    // Prefer shallower nodes first so all direct deps materialize before
    // deeper transitive alternatives of the same logical dep name.
    let depth = List.length chain
    depth, name, sourcePriorityKey source, ownerRoot, String.concat "->" chain

let private compareSameRepoGitRef (leftSource: DepSource) (rightSource: DepSource) : int option =
    // > 0 => left preferred, < 0 => right preferred, = 0 => equal.
    // Policy for same-repo git refs:
    //  1) semver tags outrank non-semver refs;
    //  2) semver-vs-semver uses numeric compare;
    //  3) non-semver-vs-non-semver uses case-insensitive lexical compare.
    match leftSource, rightSource with
    | GitDep(leftUrl, leftRef), GitDep(rightUrl, rightRef) when leftUrl = rightUrl ->
        match tryParseSemverTag leftRef, tryParseSemverTag rightRef with
        | Some leftSemver, Some rightSemver -> Some (compare leftSemver rightSemver)
        | Some _, None -> Some 1
        | None, Some _ -> Some -1
        | None, None ->
            Some (StringComparer.OrdinalIgnoreCase.Compare(leftRef.Trim(), rightRef.Trim()))
    | _ -> None

let private canPreferExistingOverIncoming (existingSource: DepSource) (incomingSource: DepSource) : bool =
    match compareSameRepoGitRef existingSource incomingSource with
    | Some cmp when cmp >= 0 -> true
    | _ -> false

let private shouldSkipByPreferred (preferredSource: DepSource) (candidateSource: DepSource) : bool =
    if depSourceToText preferredSource = depSourceToText candidateSource then
        false
    else
        match compareSameRepoGitRef preferredSource candidateSource with
        | Some cmp when cmp > 0 -> true
        | _ -> false

let private sha256Hex (bytes: byte array) : string =
    let sb = StringBuilder(bytes.Length * 2)
    for b in bytes do
        sb.Append(b.ToString("x2")) |> ignore
    sb.ToString()

let private hashDirectorySha256 (dir: string) : string =
    if not (Directory.Exists(dir)) then
        ""
    else
        let isIgnoredForHash (relPath: string) =
            relPath = ".git"
            || relPath.StartsWith(".git/", StringComparison.Ordinal)
        use sha = SHA256.Create()
        let filesWithRel =
            Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            |> Array.map (fun p -> p, Path.GetRelativePath(dir, p).Replace('\\', '/'))
            |> Array.filter (fun (_, rel) -> not (isIgnoredForHash rel))
            |> Array.sortBy snd
        for (p, rel) in filesWithRel do
            let relBytes = Encoding.UTF8.GetBytes(rel + "\n")
            sha.TransformBlock(relBytes, 0, relBytes.Length, null, 0) |> ignore
            let data = File.ReadAllBytes(p)
            sha.TransformBlock(data, 0, data.Length, null, 0) |> ignore
            let nl = Encoding.UTF8.GetBytes("\n")
            sha.TransformBlock(nl, 0, nl.Length, null, 0) |> ignore
        sha.TransformFinalBlock(Array.empty<byte>, 0, 0) |> ignore
        let hash =
            match sha.Hash with
            | null -> Array.empty<byte>
            | h -> h
        sha256Hex hash

let private renderManifestToml (m: LLManifest) : string =
    let depsLines =
        m.Deps
        |> Map.toList
        |> List.sortBy fst
        |> List.map (fun (name, source) ->
            match source with
            | GitDep(url, ref) -> name + " = \"" + url + "#" + ref + "\""
            | PathDep(path) -> name + " = { path = \"" + path + "\" }")
    let platformLine =
        if List.isEmpty m.Platform then
            []
        else
            let items = m.Platform |> List.map (fun p -> "\"" + p + "\"") |> String.concat ", "
            ["[platform]"; "use = [" + items + "]"; ""]
    String.concat "\n" (
        ["[project]"
         "name = \"" + m.Name + "\""
         "version = \"" + m.Version + "\""
         "entry = \"" + m.Entry + "\""
         ""]
        @ (if List.isEmpty depsLines then [] else "[deps]" :: depsLines @ [""])
        @ platformLine)

let private parseAddSource (raw: string) : DepSource option =
    if raw.StartsWith("path:", StringComparison.Ordinal) then
        Some (PathDep(raw.Substring("path:".Length)))
    else
        let idx = raw.LastIndexOf('#')
        if idx > 0 then
            Some (GitDep(raw.Substring(0, idx), raw.Substring(idx + 1)))
        elif raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then
            Some (GitDep(raw, "main"))
        else
            None

let private parseDepSourceText (raw: string) : DepSource option =
    if raw.StartsWith("git:", StringComparison.Ordinal) then
        let payload = raw.Substring("git:".Length)
        let idx = payload.LastIndexOf('#')
        if idx > 0 then
            Some (GitDep(payload.Substring(0, idx), payload.Substring(idx + 1)))
        else
            Some (GitDep(payload, "main"))
    elif raw.StartsWith("path:", StringComparison.Ordinal) then
        Some (PathDep(raw.Substring("path:".Length)))
    else
        None

type private ResolvedDep = {
    DisplaySource: DepSource
    CanonicalSource: string
    DeclaredByRoot: string
    DeclaredByChain: string list
}

let private canonicalizeDepSource (ownerRoot: string) (source: DepSource) : string =
    match source with
    | GitDep(url, ref) -> "git:" + url + "#" + ref
    | PathDep(path) ->
        let abs = Path.GetFullPath(Path.Combine(ownerRoot, path))
        "path:" + abs

let private writeLlSum (rootDir: string) (resolvedDeps: Map<string, ResolvedDep>) : unit =
    let vendorDir = Path.Combine(rootDir, "vendor")
    let lines =
        resolvedDeps
        |> Map.toList
        |> List.sortBy fst
        |> List.map (fun (name, resolved) ->
            let depDir = Path.Combine(vendorDir, name)
            let hash = hashDirectorySha256 depDir
            name + " " + depSourceToText resolved.DisplaySource + " sha256:" + hash)
    File.WriteAllText(Path.Combine(rootDir, "ll.sum"), String.concat "\n" lines + (if List.isEmpty lines then "" else "\n"))

let private readLlSumSources (rootDir: string) : Map<string, DepSource> =
    let llSumPath = Path.Combine(rootDir, "ll.sum")
    if not (File.Exists(llSumPath)) then
        Map.empty
    else
        File.ReadAllLines(llSumPath)
        |> Array.choose (fun line ->
            let trimmed = line.Trim()
            if String.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal) then
                None
            else
                let parts = trimmed.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries)
                if parts.Length < 2 then
                    None
                else
                    parseDepSourceText parts.[1] |> Option.map (fun source -> parts.[0], source))
        |> Map.ofArray

let private readManifestOrFail (depRoot: string) : LLManifest option =
    match LLLang.ProjectLoader.findManifest depRoot with
    | None -> None
    | Some manifestPath ->
        match parseManifest (File.ReadAllText manifestPath) with
        | Ok manifest -> Some manifest
        | Error e ->
            failwith ("manifest parse failed for " + depRoot + ": " + e)

let private tryReadVendorDepManifest (rootDir: string) (depName: string) : LLManifest option =
    let manifestPath = Path.Combine(rootDir, "vendor", depName, "lll.toml")
    if not (File.Exists(manifestPath)) then
        None
    else
        match parseManifest (File.ReadAllText manifestPath) with
        | Ok m -> Some m
        | Error _ -> None

let private findDependencyChain (rootManifest: LLManifest) (rootDir: string) (targetDep: string) : string list option =
    let target = targetDep.ToLowerInvariant()
    let projectName =
        if String.IsNullOrWhiteSpace rootManifest.Name then
            "root"
        else
            rootManifest.Name
    let direct =
        rootManifest.Deps
        |> Map.toList
        |> List.map fst
        |> List.map (fun d -> d.ToLowerInvariant())
        |> List.distinct
    let parent = System.Collections.Generic.Dictionary<string, string>()
    let q = System.Collections.Generic.Queue<string>()
    for d in direct do
        parent.[d] <- projectName
        q.Enqueue(d)
    while q.Count > 0 do
        let cur = q.Dequeue()
        match tryReadVendorDepManifest rootDir cur with
        | None -> ()
        | Some manifest ->
            for (childName, _) in manifest.Deps |> Map.toList do
                let child = childName.ToLowerInvariant()
                if not (parent.ContainsKey(child)) then
                    parent.[child] <- cur
                    q.Enqueue(child)
    if not (parent.ContainsKey(target)) then
        None
    else
        let rec rebuild acc cur =
            let acc' = cur :: acc
            let p = parent.[cur]
            if p = projectName then
                projectName :: acc'
            else
                rebuild acc' p
        Some (rebuild [] target)

let private materializeDepOrFail (rootDir: string) (ownerRoot: string) (name: string) (source: DepSource) : string =
    let targetDir = Path.Combine(rootDir, "vendor", name)
    if Directory.Exists(targetDir) then
        Directory.Delete(targetDir, true)
    match source with
    | GitDep(url, ref) ->
        printfn "  fetch %s from %s#%s" name url ref
        let clonePsi = ProcessStartInfo("git", sprintf "clone --depth 1 %s \"%s\"" url targetDir)
        clonePsi.UseShellExecute <- false
        let cloneProc = startProcessOrFail clonePsi
        cloneProc.WaitForExit()
        if cloneProc.ExitCode <> 0 then
            failwith ("git clone failed for " + name)
        let coPsi = ProcessStartInfo("git", sprintf "-C \"%s\" checkout %s" targetDir ref)
        coPsi.UseShellExecute <- false
        let coProc = startProcessOrFail coPsi
        coProc.WaitForExit()
        if coProc.ExitCode <> 0 then
            failwith ("git checkout " + ref + " failed for " + name)
        targetDir
    | PathDep(path) ->
        let resolved = Path.GetFullPath(Path.Combine(ownerRoot, path))
        if not (Directory.Exists(resolved)) then
            failwith ("path dep not found for " + name + ": " + resolved)
        printfn "  copy %s from %s" name resolved
        copyDirectoryRecursive resolved targetDir
        // Keep source-root as original directory so nested path deps resolve
        // relative to the declaring dependency's own repository layout.
        resolved

type private ResolveOutcome =
    | ResolveDone of Map<string, ResolvedDep>
    | ResolveRestart of Map<string, DepSource>

let private clearVendorDirectory (vendorDir: string) : unit =
    if Directory.Exists(vendorDir) then
        for existing in Directory.GetDirectories(vendorDir) do
            Directory.Delete(existing, true)

let private upsertPreferredSemver (preferred: Map<string, DepSource>) (name: string) (incoming: DepSource) : Map<string, DepSource> =
    match Map.tryFind name preferred with
    | Some current when canPreferExistingOverIncoming current incoming -> preferred
    | _ -> preferred |> Map.add name incoming

let private isHigherRefConflict (existingSource: DepSource) (incomingSource: DepSource) : bool =
    match compareSameRepoGitRef existingSource incomingSource with
    | Some cmp when cmp < 0 -> true
    | _ -> false

let private hasPreferredWinner (preferred: Map<string, DepSource>) (name: string) (source: DepSource) : bool =
    match Map.tryFind name preferred with
    | Some winner when shouldSkipByPreferred winner source -> true
    | _ -> false

let private resolveWithPreferred
    (rootDir: string)
    (vendorDir: string)
    (manifest: LLManifest)
    (rootProjectName: string)
    (lockedSources: Map<string, DepSource>)
    : Map<string, ResolvedDep> =
    let initialPending =
        manifest.Deps
        |> Map.toList
        |> List.map (fun (name, source) -> name, source, rootDir, [rootProjectName; name])

    let rec resolvePass
        (pending: (string * DepSource * string * string list) list)
        (resolved: Map<string, ResolvedDep>)
        (preferred: Map<string, DepSource>)
        : ResolveOutcome =
        match pending |> List.sortBy pendingPriorityKey with
        | [] -> ResolveDone resolved
        | (name, source, ownerRoot, chain) :: rest ->
            if hasPreferredWinner preferred name source then
                resolvePass rest resolved preferred
            else
                let canonical = canonicalizeDepSource ownerRoot source
                match Map.tryFind name resolved with
                | Some existing when existing.CanonicalSource = canonical ->
                    resolvePass rest resolved preferred
                | Some existing ->
                    let lockedWinner =
                        match Map.tryFind name lockedSources with
                        | Some locked when depSourceToText locked = depSourceToText existing.DisplaySource ->
                            Some existing.DisplaySource
                        | Some locked when depSourceToText locked = depSourceToText source ->
                            Some source
                        | _ -> None
                    match lockedWinner with
                    | Some winner when depSourceToText winner = depSourceToText existing.DisplaySource ->
                        resolvePass rest resolved preferred
                    | Some winner ->
                        // Lock pins an alternative source for this dep name.
                        ResolveRestart (preferred |> Map.add name winner)
                    | None when canPreferExistingOverIncoming existing.DisplaySource source ->
                        resolvePass rest resolved preferred
                    | None when isHigherRefConflict existing.DisplaySource source ->
                        // Defer and restart from root with upgraded winner to avoid
                        // stale transitive tails from the lower version.
                        ResolveRestart (upsertPreferredSemver preferred name source)
                    | None ->
                        let existingChain =
                            if List.isEmpty existing.DeclaredByChain then
                                existing.DeclaredByRoot
                            else
                                String.concat " -> " existing.DeclaredByChain
                        let incomingChain =
                            if List.isEmpty chain then
                                ownerRoot
                            else
                                String.concat " -> " chain
                        failwith ("dependency conflict for " + name + ": "
                                  + depSourceToText existing.DisplaySource
                                  + " (declared in " + existing.DeclaredByRoot + " via " + existingChain + ") vs "
                                  + depSourceToText source
                                  + " (declared in " + ownerRoot + " via " + incomingChain + ")")
                | None ->
                    let sourceRoot = materializeDepOrFail rootDir ownerRoot name source
                    let nestedDeps =
                        match readManifestOrFail sourceRoot with
                        | None -> []
                        | Some depManifest ->
                            depManifest.Deps
                            |> Map.toList
                            |> List.map (fun (depName, depSource) -> depName, depSource, sourceRoot, chain @ [depName])
                    let resolved' =
                        Map.add name {
                            DisplaySource = source
                            CanonicalSource = canonical
                            DeclaredByRoot = ownerRoot
                            DeclaredByChain = chain
                        } resolved
                    resolvePass (rest @ nestedDeps) resolved' preferred

    let rec loop (attempt: int) (preferred: Map<string, DepSource>) =
        if attempt > 32 then
            failwith "dependency resolution exceeded restart limit (semver winner convergence)"
        match resolvePass initialPending Map.empty preferred with
        | ResolveDone resolved -> resolved
        | ResolveRestart preferred' ->
            clearVendorDirectory vendorDir
            loop (attempt + 1) preferred'
    loop 0 Map.empty

/// Install dependencies listed in lll.toml (or ll.toml) into vendor/. Returns exit code.
// TODO(selfhost:resolver):
// blocker: no MVS/version-conflict solver yet; resolver requires one canonical
//   source per dep name across full transitive graph.
// works-now: deterministic transitive resolution + vendor materialization +
//   ll.sum hashing for all resolved deps; repeated installs are idempotent;
//   conflict diagnostics include declaring roots and transitive "via" chains;
//   same-repo semver tag conflicts deterministically converge to the highest
//   tag via restartable preferred-source selection; same-repo non-semver git
//   refs also converge deterministically by canonical lexical ref ordering
//   (with ll.sum still able to pin an explicit winner).
// next-step: add full MVS/version selection policy for broader multi-source
//   conflicts beyond same-repo git ref selection.
// coverage-note: transitive git-ref conflict regression is covered in
//   ModuleSystemTests (`...same name from different refs`).
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
                failwith "manifest parse failed"
        let rootProjectName =
            if String.IsNullOrWhiteSpace manifest.Name then
                "root"
            else
                manifest.Name
        let lockedSources = readLlSumSources rootDir
        let vendorDir = Path.Combine(rootDir, "vendor")
        Directory.CreateDirectory(vendorDir) |> ignore
        let resolvedDeps = resolveWithPreferred rootDir vendorDir manifest rootProjectName lockedSources
        let resolvedNames = resolvedDeps |> Map.toList |> List.map fst |> Set.ofList
        for existing in Directory.GetDirectories(vendorDir) do
            let depName = Path.GetFileName(existing)
            if not (Set.contains depName resolvedNames) then
                Directory.Delete(existing, true)
        writeLlSum rootDir resolvedDeps
        Console.WriteLine("Installed " + string resolvedDeps.Count + " dependencies into vendor/.")
        0
    with ex ->
        eprintfn "lllc: %s" ex.Message
        1

let private cmdMod (rootDir: string) (args: string list) : int =
    let manifestPath =
        match LLLang.ProjectLoader.findManifest rootDir with
        | Some p -> p
        | None -> Path.Combine(rootDir, "lll.toml")
    match args with
    | ["tidy"] ->
        cmdInstall rootDir
    | ["add"; spec] ->
        match spec.IndexOf('=') with
        | -1 ->
            eprintfn "lllc: usage: lllc mod add <name>=<https://...#ref|path:../dir>"
            1
        | eqIdx ->
            let depName = spec.Substring(0, eqIdx).Trim()
            let rawSource = spec.Substring(eqIdx + 1).Trim()
            match parseAddSource rawSource with
            | None ->
                eprintfn "lllc: unsupported dep source '%s'" rawSource
                1
            | Some depSource ->
                let manifest =
                    match parseManifest (File.ReadAllText manifestPath) with
                    | Ok m -> m
                    | Error e ->
                        eprintfn "lllc: manifest error: %s" e
                        failwith "manifest parse failed"
                let updated = { manifest with Deps = manifest.Deps |> Map.add depName depSource }
                File.WriteAllText(manifestPath, renderManifestToml updated)
                cmdInstall rootDir
    | ["why"; depName] ->
        let manifestResult =
            match parseManifest (File.ReadAllText manifestPath) with
            | Ok m -> Ok m
            | Error e -> Error e
        match manifestResult with
        | Error e ->
            eprintfn "lllc: manifest error: %s" e
            1
        | Ok manifest ->
            let depKey = depName.ToLowerInvariant()
            match findDependencyChain manifest rootDir depName with
            | None ->
                let known =
                    manifest.Deps
                    |> Map.toList
                    |> List.map fst
                    |> List.sort
                    |> String.concat ", "
                eprintfn
                    "lllc: dependency '%s' is not present in resolved dependency graph (run 'lllc install' first)%s"
                    depName
                    (if String.IsNullOrWhiteSpace known then "" else " (direct deps: " + known + ")")
                1
            | Some chain ->
                match loadProject rootDir with
                | Error es ->
                    printErrors es
                    1
                | Ok proj ->
                    let rootSrc = Path.Combine(rootDir, "src")
                    let consumers =
                        proj.Files
                        |> List.filter (fun lf ->
                            let abs = Path.GetFullPath(lf.FilePath)
                            abs.StartsWith(rootSrc, StringComparison.Ordinal))
                        |> List.choose (fun lf ->
                            match parseModuleWithPos lf.Src with
                            | Error _ -> None
                            | Ok (m, _) ->
                                let hasDepImport =
                                    m.Imports
                                    |> List.exists (fun imp ->
                                        match imp with
                                        | h :: _ -> h.ToLowerInvariant() = depKey
                                        | [] -> false)
                                if hasDepImport then Some (String.concat "." lf.ModulePath) else None)
                    if List.isEmpty consumers then
                        printfn "no local modules directly import %s" depName
                    else
                        printfn "%s is imported by:" depName
                        for c in consumers do printfn "  - %s" c
                    printfn "dependency chain: %s" (String.concat " -> " chain)
                    0
    | _ ->
        eprintfn "lllc: usage: lllc mod tidy | lllc mod add <name>=<source> | lllc mod why <dep>"
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
    for e in sdkRegistryErrors () do
        Console.Error.WriteLine("lllc: sdk metadata warning: " + e)
    let args = List.ofArray argv
    match args with
    | "build" :: rest ->
        match parseTarget rest with
        | Error msg ->
            Console.Error.WriteLine(msg)
            1
        | Ok (targetOverride, rest2) ->
            match rest2 with
            | [path] when path.EndsWith(".lll") -> cmdBuildFile path targetOverride
            | [dir] -> cmdBuildProject (Path.GetFullPath dir) targetOverride
            | [] ->
                match findProjectRoot (Directory.GetCurrentDirectory()) with
                | Some root -> cmdBuildProject root targetOverride
                | None ->
                    Console.Error.WriteLine("lllc: no lll.toml found. Use 'lllc new <name>' to create a project.")
                    1
            | _ ->
                Console.Error.WriteLine("lllc: unrecognized build arguments")
                1
    | "check" :: rest ->
        match parseTarget rest with
        | Error msg ->
            Console.Error.WriteLine(msg)
            1
        | Ok (targetOverride, rest2) ->
            let target = targetOrDefault targetOverride
            match rest2 with
            | [path] when path.EndsWith(".lll") ->
                cmdCheckFile path target
            | [dir] ->
                cmdCheckProject (Path.GetFullPath dir) targetOverride
            | [] ->
                match findProjectRoot (Directory.GetCurrentDirectory()) with
                | Some root -> cmdCheckProject root targetOverride
                | None ->
                    Console.Error.WriteLine("lllc: no lll.toml found. Use 'lllc new <name>' to create a project.")
                    1
            | _ ->
                Console.Error.WriteLine("lllc: usage: lllc check [--target fs|ts|py|java|cs|llvm] <file.lll>|[dir]")
                1
    | "run" :: rest ->
        match parseTarget rest with
        | Error msg ->
            Console.Error.WriteLine(msg)
            1
        | Ok (targetOverride, rest2) ->
            match rest2 with
            | [path] -> cmdRun path targetOverride
            | _ ->
                Console.Error.WriteLine("lllc: usage: lllc run [--target fs|ts|py|java|cs|llvm] <file.lll>")
                1
    | "self" :: rest -> cmdRunSelf rest
    | "reverse" :: rest -> cmdReverse rest
    | ["new"; name] -> cmdNew name
    | "mod" :: rest ->
        let root =
            match findProjectRoot (Directory.GetCurrentDirectory()) with
            | Some r -> r
            | None -> Directory.GetCurrentDirectory()
        cmdMod root rest
    | "install" :: _ ->
        let root =
            match findProjectRoot (Directory.GetCurrentDirectory()) with
            | Some r -> r
            | None -> Directory.GetCurrentDirectory()
        cmdInstall root
    | ["mcp"] -> Mcp.runServer (); 0
    | _ ->
        Console.Error.WriteLine("Usage:")
        Console.Error.WriteLine("  lllc build [--target fs|ts|py|java|cs|llvm] <file.lll>  compile single file")
        Console.Error.WriteLine("  lllc build [--target fs|ts|py|java|cs|llvm] [dir]       compile project (reads lll.toml)")
        Console.Error.WriteLine("  lllc check [--target fs|ts|py|java|cs|llvm] <file.lll>  type-check single file (no codegen)")
        Console.Error.WriteLine("  lllc check [--target fs|ts|py|java|cs|llvm] [dir]       type-check project (no codegen)")
        Console.Error.WriteLine("  lllc run   [--target fs|ts|py|java|cs|llvm] <file.lll>  compile and run single file")
        Console.Error.WriteLine("  lllc reverse --from <target> <file>        [experimental] recover minimal ll-lang from generated target code")
        Console.Error.WriteLine("  lllc self  <cmd> <file> [arg]              run self-hosted lllc tools (lll layer)")
        Console.Error.WriteLine("  lllc new   <name>                          scaffold new project")
        Console.Error.WriteLine("  lllc install                               install dependencies into vendor/ and rewrite ll.sum")
        Console.Error.WriteLine("  lllc mod tidy                              sync vendor/ with lll.toml and rewrite ll.sum")
        Console.Error.WriteLine("  lllc mod add <name>=<source>              add dep and install (source: https://..#ref or path:../dir)")
        Console.Error.WriteLine("  lllc mod why <dep>                         explain dependency chain + local direct importers")
        Console.Error.WriteLine("  lllc mcp                                   run MCP server (stdio transport)")
        Console.Error.WriteLine("")
        Console.Error.WriteLine("  --target fs   emit F# (default)")
        Console.Error.WriteLine("  --target ts   emit TypeScript")
        Console.Error.WriteLine("  --target py   emit Python")
        Console.Error.WriteLine("  --target java emit Java")
        Console.Error.WriteLine("  --target cs   emit C#")
        Console.Error.WriteLine("  --target llvm emit LLVM IR (experimental subset)")
        1
