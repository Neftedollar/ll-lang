module LLLang.Platform

open System
open System.IO
open System.Text
open System.Text.RegularExpressions

/// Compilation target.
type Target =
    | FSharp
    | TypeScript
    | Python
    | Java
    | CSharp
    | LLVM

type PlatformSdk = {
    PackageName : string
    ModuleName : string
    Target : Target
    PlatformName : string
    Aliases : string list
    OutputExt : string
    ProjectExt : string option
    RuntimeTemplate : string option
    BuildCompile : string option
    BuildRun : string option
}

let private normalize (s: string) =
    s.Trim().ToLowerInvariant()

let private moduleNameFromPackage (packageName: string) =
    if packageName.EndsWith(".SDK", StringComparison.OrdinalIgnoreCase) then
        packageName.Substring(0, packageName.Length - 4)
    else
        packageName

let private canonicalPlatformName (target: Target) =
    match target with
    | FSharp -> "fsharp"
    | TypeScript -> "typescript"
    | Python -> "python"
    | Java -> "java"
    | CSharp -> "csharp"
    | LLVM -> "llvm"

let private tryParseCanonicalTarget (raw: string) : Target option =
    match normalize raw with
    | "fsharp" | "fs" | "f#" | "platform.fsharp.sdk" -> Some FSharp
    | "typescript" | "ts" | "platform.typescript.sdk" -> Some TypeScript
    | "python" | "py" | "platform.python.sdk" -> Some Python
    | "java" | "jvm" | "platform.java.sdk" -> Some Java
    | "csharp" | "cs" | "c#" | "platform.csharp.sdk" -> Some CSharp
    | "llvm" | "ll" | "platform.llvm.sdk" -> Some LLVM
    | _ -> None

let private fallbackFSharpExternalTargetMap : Map<string, string> =
    [
        "console_log", "System.Console.WriteLine"
        "JSON_parse", "System.Text.Json.JsonSerializer.Deserialize<obj>"
        "fileReadAll", "System.IO.File.ReadAllText"
        "fileWriteAll", "System.IO.File.WriteAllText"
        "fileExists", "System.IO.File.Exists"
        "dirList", "ll_dirList"
        "dirExists", "System.IO.Directory.Exists"
        "processRun", "ll_processRun"
    ]
    |> Map.ofList

let private fallbackTypeScriptExternalTargetMap : Map<string, string> =
    [
        "console_log", "console.log"
        "JSON_parse", "JSON.parse"
        "fetch", "(globalThis as any).fetch"
    ]
    |> Map.ofList

let private fallbackPythonExternalTargetMap : Map<string, string> =
    [
        "console_log", "print"
        "JSON_parse", "json.loads"
    ]
    |> Map.ofList

let private fallbackJavaExternalTargetMap : Map<string, string> =
    [
        "console_log", "System.out.println"
    ]
    |> Map.ofList

let private fallbackCSharpExternalTargetMap : Map<string, string> =
    [
        "console_log", "Console.WriteLine"
        "JSON_parse", "System.Text.Json.JsonSerializer.Deserialize<object>"
    ]
    |> Map.ofList

let private fallbackLlvmExternalTargetMap : Map<string, string> =
    [
        // LLVM backend currently uses direct symbol declarations.
        "console_log", "console_log"
    ]
    |> Map.ofList

let private fallbackExternalTargetMap : Map<Target, Map<string, string>> =
    [
        (FSharp, fallbackFSharpExternalTargetMap)
        (TypeScript, fallbackTypeScriptExternalTargetMap)
        (Python, fallbackPythonExternalTargetMap)
        (Java, fallbackJavaExternalTargetMap)
        (CSharp, fallbackCSharpExternalTargetMap)
        (LLVM, fallbackLlvmExternalTargetMap)
    ]
    |> Map.ofList

let private fallbackSdks : PlatformSdk list =
    [
        {
            PackageName = "Platform.FSharp.SDK"
            ModuleName = moduleNameFromPackage "Platform.FSharp.SDK"
            Target = FSharp
            PlatformName = "fsharp"
            Aliases = ["fsharp"; "fs"; "f#"]
            OutputExt = ".fs"
            ProjectExt = Some ".fsproj"
            RuntimeTemplate = Some "runtime/project.fsproj.tmpl"
            BuildCompile = Some "dotnet build {project_file_q} -c Release"
            BuildRun = Some "dotnet run --project {project_file_q}"
        }
        {
            PackageName = "Platform.TypeScript.SDK"
            ModuleName = moduleNameFromPackage "Platform.TypeScript.SDK"
            Target = TypeScript
            PlatformName = "typescript"
            Aliases = ["typescript"; "ts"]
            OutputExt = ".ts"
            ProjectExt = None
            RuntimeTemplate = Some "runtime/package.json.tmpl"
            BuildCompile = Some "npx tsc {main_file_q} --target es2022 --module esnext"
            BuildRun = Some "npx tsc {main_file_q} --target es2022 --module esnext && node {main_js_file_q}"
        }
        {
            PackageName = "Platform.Python.SDK"
            ModuleName = moduleNameFromPackage "Platform.Python.SDK"
            Target = Python
            PlatformName = "python"
            Aliases = ["python"; "py"]
            OutputExt = ".py"
            ProjectExt = None
            RuntimeTemplate = None
            BuildCompile = Some "if command -v python >/dev/null 2>&1; then python -m py_compile {main_file_q}; else python3 -m py_compile {main_file_q}; fi"
            BuildRun = Some "if command -v python >/dev/null 2>&1; then python {main_file_q}; else python3 {main_file_q}; fi"
        }
        {
            PackageName = "Platform.Java.SDK"
            ModuleName = moduleNameFromPackage "Platform.Java.SDK"
            Target = Java
            PlatformName = "java"
            Aliases = ["java"; "jvm"]
            OutputExt = ".java"
            ProjectExt = None
            RuntimeTemplate = None
            BuildCompile = Some "javac {java_source_file_q}"
            BuildRun = Some "javac {java_source_file_q} && java {java_class_name_q}"
        }
        {
            PackageName = "Platform.CSharp.SDK"
            ModuleName = moduleNameFromPackage "Platform.CSharp.SDK"
            Target = CSharp
            PlatformName = "csharp"
            Aliases = ["csharp"; "cs"; "c#"]
            OutputExt = ".cs"
            ProjectExt = Some ".csproj"
            RuntimeTemplate = Some "runtime/project.csproj.tmpl"
            BuildCompile = Some "dotnet build {project_file_q} -c Release"
            BuildRun = Some "dotnet run --project {project_file_q}"
        }
        {
            PackageName = "Platform.LLVM.SDK"
            ModuleName = moduleNameFromPackage "Platform.LLVM.SDK"
            Target = LLVM
            PlatformName = "llvm"
            Aliases = ["llvm"; "ll"]
            OutputExt = ".ll"
            ProjectExt = None
            RuntimeTemplate = None
            BuildCompile = Some "llvm-as {main_file_q} -o {main_bc_file_q}"
            BuildRun = Some "lli {main_file_q}"
        }
    ]

type private TomlValue =
    | TString of string
    | TStringArray of string list

type private RegistryState = {
    Sdks : PlatformSdk list
    ExternalMappings : Map<Target, Map<string, string>>
    Errors : string list
}

let mutable private registryCache : RegistryState option = None

let private isWhitespace (c: char) =
    c = ' ' || c = '\t' || c = '\r'

let private trimLine (s: string) =
    let mutable i = 0
    let mutable inQuote = false
    let mutable commentStart = -1
    while i < s.Length && commentStart < 0 do
        match s.[i] with
        | '"' ->
            inQuote <- not inQuote
            i <- i + 1
        | '\\' when inQuote && i + 1 < s.Length ->
            i <- i + 2
        | '#' when not inQuote ->
            commentStart <- i
        | _ ->
            i <- i + 1
    let noComment =
        if commentStart >= 0 then s.[..commentStart - 1]
        else s
    noComment.Trim()

let private parseQuotedString (s: string) (i: int) : Result<string * int, string> =
    if i >= s.Length || s.[i] <> '"' then
        Error $"expected quoted string at {i}"
    else
        let sb = StringBuilder()
        let mutable j = i + 1
        let mutable done' = false
        while not done' && j < s.Length do
            match s.[j] with
            | '"' ->
                done' <- true
                j <- j + 1
            | '\\' when j + 1 < s.Length ->
                match s.[j + 1] with
                | '"' -> sb.Append('"') |> ignore; j <- j + 2
                | '\\' -> sb.Append('\\') |> ignore; j <- j + 2
                | 'n' -> sb.Append('\n') |> ignore; j <- j + 2
                | 'r' -> sb.Append('\r') |> ignore; j <- j + 2
                | 't' -> sb.Append('\t') |> ignore; j <- j + 2
                | other -> sb.Append(other) |> ignore; j <- j + 2
            | c ->
                sb.Append(c) |> ignore
                j <- j + 1
        if done' then Ok (sb.ToString(), j)
        else Error "unterminated quoted string"

let private parseStringArray (s: string) (i: int) : Result<string list * int, string> =
    if i >= s.Length || s.[i] <> '[' then
        Error $"expected '[' at {i}"
    else
        let items = ResizeArray<string>()
        let mutable j = i + 1
        let mutable done' = false
        let mutable err: string option = None
        while not done' && err.IsNone && j < s.Length do
            while j < s.Length && (isWhitespace s.[j] || s.[j] = ',') do
                j <- j + 1
            if j >= s.Length then
                err <- Some "unterminated array"
            elif s.[j] = ']' then
                done' <- true
                j <- j + 1
            elif s.[j] = '"' then
                match parseQuotedString s j with
                | Ok (v, j') ->
                    items.Add(v)
                    j <- j'
                | Error e -> err <- Some e
            else
                err <- Some $"unexpected char '{s.[j]}' in array"
        match err with
        | Some e -> Error e
        | None -> Ok (List.ofSeq items, j)

let private parseTomlSubset (path: string) (src: string) : Result<Map<string, Map<string, TomlValue>>, string list> =
    let mutable section = ""
    let mutable table = Map.empty<string, Map<string, TomlValue>>
    let errors = ResizeArray<string>()
    let lines = src.Split('\n')
    for i = 0 to lines.Length - 1 do
        let lineNo = i + 1
        let line = trimLine lines.[i]
        if line = "" then
            ()
        elif line.StartsWith("[") then
            let closing = line.IndexOf(']')
            if closing < 0 then
                errors.Add($"{path}:{lineNo}: unclosed section header")
            else
                section <- line.Substring(1, closing - 1).Trim()
        elif line.Contains("=") then
            let eq = line.IndexOf('=')
            let key = line.Substring(0, eq).Trim()
            let valueRaw = line.Substring(eq + 1).Trim()
            let parsedValue =
                if valueRaw.StartsWith("[") then
                    match parseStringArray valueRaw 0 with
                    | Ok (items, _) -> Ok (TStringArray items)
                    | Error e -> Error e
                elif valueRaw.StartsWith("\"") then
                    match parseQuotedString valueRaw 0 with
                    | Ok (v, _) -> Ok (TString v)
                    | Error e -> Error e
                else
                    Ok (TString valueRaw)
            match parsedValue with
            | Error e ->
                errors.Add($"{path}:{lineNo}: {e}")
            | Ok value ->
                let secMap = table |> Map.tryFind section |> Option.defaultValue Map.empty
                table <- table |> Map.add section (secMap |> Map.add key value)
        else
            ()
    if errors.Count = 0 then Ok table
    else Error (List.ofSeq errors)

let private tryGetTomlString (section: string) (key: string) (table: Map<string, Map<string, TomlValue>>) : string option =
    match table |> Map.tryFind section with
    | None -> None
    | Some sec ->
        match sec |> Map.tryFind key with
        | Some (TString s) -> Some s
        | _ -> None

let private tryGetTomlStringArray (section: string) (key: string) (table: Map<string, Map<string, TomlValue>>) : string list option =
    match table |> Map.tryFind section with
    | None -> None
    | Some sec ->
        match sec |> Map.tryFind key with
        | Some (TStringArray xs) -> Some xs
        | _ -> None

let private validateAliasCollisions (sdks: PlatformSdk list) : string list =
    let mutable aliasMap = Map.empty<string, Target * string>
    let errors = ResizeArray<string>()
    for sdk in sdks do
        let aliases =
            [ yield sdk.PlatformName
              yield sdk.PackageName
              yield sdk.ModuleName
              yield! sdk.Aliases ]
            |> List.map normalize
            |> List.distinct
        for alias in aliases do
            match aliasMap |> Map.tryFind alias with
            | None ->
                aliasMap <- aliasMap |> Map.add alias (sdk.Target, sdk.PackageName)
            | Some (existingTarget, existingPackage) ->
                if existingTarget <> sdk.Target then
                    errors.Add($"SDK alias collision: '{alias}' maps to both {existingPackage} and {sdk.PackageName}")
    List.ofSeq errors

let private loadSdkFromDir (packageName: string) (sdkDir: string) : Result<PlatformSdk, string list> =
    let lllTomlPath = Path.Combine(sdkDir, "lll.toml")
    let llTomlPath = Path.Combine(sdkDir, "ll.toml")
    let manifestPathOpt =
        if File.Exists(lllTomlPath) then Some lllTomlPath
        elif File.Exists(llTomlPath) then Some llTomlPath
        else None
    match manifestPathOpt with
    | None ->
        Error [$"missing sdk manifest: {lllTomlPath} (legacy fallback: {llTomlPath})"]
    | Some manifestPath ->
        let llText = File.ReadAllText(manifestPath)
        match parseTomlSubset manifestPath llText with
        | Error es -> Error es
        | Ok llToml ->
            let metaPath = Path.Combine(sdkDir, "meta.toml")
            let metaTomlResult =
                if File.Exists(metaPath) then
                    parseTomlSubset metaPath (File.ReadAllText(metaPath))
                else
                    Ok Map.empty
            match metaTomlResult with
            | Error es -> Error es
            | Ok metaToml ->
                let packageNameFromToml =
                    tryGetTomlString "project" "name" llToml
                    |> Option.defaultValue packageName
                let targetRaw = tryGetTomlString "sdk" "target" llToml
                let ext = tryGetTomlString "sdk" "ext" llToml
                match targetRaw, ext with
                | None, _ ->
                    Error [$"invalid sdk metadata in {manifestPath}: missing [sdk].target"]
                | _, None ->
                    Error [$"invalid sdk metadata in {manifestPath}: missing [sdk].ext"]
                | Some rawTarget, Some outExt ->
                    match tryParseCanonicalTarget rawTarget with
                    | None ->
                        Error [$"invalid sdk metadata in {manifestPath}: unknown [sdk].target = '{rawTarget}'"]
                    | Some target ->
                        let aliases = tryGetTomlStringArray "sdk" "aliases" llToml |> Option.defaultValue []
                        let hostExt = tryGetTomlString "sdk" "host-ext" llToml
                        let projectTemplate = tryGetTomlString "build" "project-template" metaToml
                        let compileCommand = tryGetTomlString "build" "compile" metaToml
                        let runCommand = tryGetTomlString "build" "run" metaToml
                        Ok {
                            PackageName = packageNameFromToml
                            ModuleName = moduleNameFromPackage packageNameFromToml
                            Target = target
                            PlatformName = canonicalPlatformName target
                            Aliases = aliases
                            OutputExt = outExt
                            ProjectExt = hostExt
                            RuntimeTemplate = projectTemplate
                            BuildCompile = compileCommand
                            BuildRun = runCommand
                        }

let private ancestorDirs (startDir: string) : string list =
    let fullStart = Path.GetFullPath(startDir)
    let dirs = ResizeArray<string>()
    let mutable dir = fullStart
    let mutable keepGoing = true
    while keepGoing do
        dirs.Add(dir)
        match Directory.GetParent(dir) with
        | null -> keepGoing <- false
        | parent when parent.FullName = dir -> keepGoing <- false
        | parent -> dir <- parent.FullName
    List.ofSeq dirs

let private distinctPreservingOrder (items: string list) : string list =
    let rec loop seen acc rest =
        match rest with
        | [] -> List.rev acc
        | x :: xs ->
            if Set.contains x seen then
                loop seen acc xs
            else
                loop (Set.add x seen) (x :: acc) xs
    loop Set.empty [] items

let private sdkSearchRoots () : string list =
    [
        yield! ancestorDirs (Directory.GetCurrentDirectory())
        yield! ancestorDirs AppContext.BaseDirectory
    ]
    |> distinctPreservingOrder

type private FfiMapBlock = {
    Target : Target
    Entries : (string * string) list
    Source : string
}

let private ffiMapBlockRegex =
    Regex(@"(?ms)^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<body>.*?)(?=^\s*[A-Za-z_][A-Za-z0-9_]*(?:\([^\n=]*\))?\s*=|\z)")

let private ffiPairRegex =
    Regex(@"\(\s*""(?<name>(?:\\.|[^""\\])*)""\s*,\s*""(?<target>(?:\\.|[^""\\])*)""\s*\)\s*::")

let private allTargets : Target list =
    [ FSharp; TypeScript; Python; Java; CSharp; LLVM ]

let private tryGetPackageNameFromFfiPath (ffiPath: string) : string option =
    match Directory.GetParent(ffiPath) with
    | null -> None
    | srcDir ->
        match srcDir.Parent with
        | null -> None
        | pkgDir -> Some pkgDir.Name

let private ffiSidecarFiles () : string list =
    let candidates = ResizeArray<string>()
    let addIfFile (path: string) =
        if File.Exists(path) then
            candidates.Add(Path.GetFullPath(path))

    for root in sdkSearchRoots () do
        let sdkRoot = Path.Combine(root, "sdks")
        if Directory.Exists(sdkRoot) then
            for dir in Directory.EnumerateDirectories(sdkRoot) do
                addIfFile (Path.Combine(dir, "src", "FFI.lll"))

        let vendorRoot = Path.Combine(root, "vendor")
        if Directory.Exists(vendorRoot) then
            for dir in Directory.EnumerateDirectories(vendorRoot) do
                addIfFile (Path.Combine(dir, "src", "FFI.lll"))

        let projectManifest = Path.Combine(root, "lll.toml")
        let legacyManifest = Path.Combine(root, "ll.toml")
        if File.Exists(projectManifest) || File.Exists(legacyManifest) then
            addIfFile (Path.Combine(root, "src", "FFI.lll"))

    candidates
    |> List.ofSeq
    |> distinctPreservingOrder

let private parseFfiSidecar (ffiPath: string) : Result<FfiMapBlock list, string list> =
    try
        let src = File.ReadAllText(ffiPath)
        let packageTarget =
            tryGetPackageNameFromFfiPath ffiPath
            |> Option.bind tryParseCanonicalTarget

        let blocks =
            [ for m in (ffiMapBlockRegex.Matches(src) |> Seq.cast<Match>) do
                let mapName = m.Groups.["name"].Value
                if mapName.EndsWith("ExternalMap", StringComparison.Ordinal) then
                    let prefix = mapName.Substring(0, mapName.Length - "ExternalMap".Length)
                    let targetOpt =
                        match tryParseCanonicalTarget prefix with
                        | Some t -> Some t
                        | None -> packageTarget
                    match targetOpt with
                    | None -> ()
                    | Some target ->
                        let body = m.Groups.["body"].Value
                        let entries =
                            [ for pair in (ffiPairRegex.Matches(body) |> Seq.cast<Match>) do
                                let name = Regex.Unescape(pair.Groups.["name"].Value)
                                let symbol = Regex.Unescape(pair.Groups.["target"].Value)
                                yield (name, symbol) ]
                        yield { Target = target; Entries = entries; Source = ffiPath } ]
        Ok blocks
    with ex ->
        Error [$"{ffiPath}: failed to read/parse FFI sidecar: {ex.Message}"]

let private mergeExternalEntry
    (target: Target)
    (externalName: string)
    (symbol: string)
    (source: string)
    (current: Map<Target, Map<string, string>>)
    (errors: ResizeArray<string>)
    : Map<Target, Map<string, string>> =
    let mapForTarget = current |> Map.tryFind target |> Option.defaultValue Map.empty
    match mapForTarget |> Map.tryFind externalName with
    | None ->
        current |> Map.add target (mapForTarget |> Map.add externalName symbol)
    | Some existing when StringComparer.Ordinal.Equals(existing, symbol) ->
        current
    | Some existing ->
        errors.Add(
            $"FFI mapping collision target:{canonicalPlatformName target} name:{externalName} existing:'{existing}' incoming:'{symbol}' source:{source}")
        current

let private loadExternalMappings () : Map<Target, Map<string, string>> * string list =
    let errors = ResizeArray<string>()
    let mutable merged =
        allTargets
        |> List.map (fun target -> target, Map.empty<string, string>)
        |> Map.ofList

    let mutable seenTargets = Set.empty<Target>
    for ffiPath in ffiSidecarFiles () do
        match parseFfiSidecar ffiPath with
        | Error es ->
            for e in es do
                errors.Add(e)
        | Ok blocks ->
            for block in blocks do
                seenTargets <- Set.add block.Target seenTargets
                for (externalName, symbol) in block.Entries do
                    merged <- mergeExternalEntry block.Target externalName symbol block.Source merged errors

    // TODO(selfhost:bootstrap): remove bootstrap fallback once sidecar discovery
    // is mandatory in all supported workflows.
    // Bootstrapping fallback for environments where sidecars are not discoverable.
    for target in allTargets do
        if not (Set.contains target seenTargets) then
            match fallbackExternalTargetMap |> Map.tryFind target with
            | None -> ()
            | Some fallback ->
                for KeyValue(externalName, symbol) in fallback do
                    merged <- mergeExternalEntry target externalName symbol "bootstrap-fallback" merged errors

    (merged, List.ofSeq errors)

let private loadRegistry () : RegistryState =
    let errors = ResizeArray<string>()
    let sdks =
        fallbackSdks
        |> List.map (fun fallback ->
            if not (fallback.PackageName.StartsWith("Platform.", StringComparison.Ordinal)) then
                fallback
            else
                let sdkDirOpt =
                    sdkSearchRoots ()
                    |> List.tryPick (fun root ->
                        let dir = Path.Combine(root, "sdks", fallback.PackageName)
                        if Directory.Exists(dir) then Some dir else None)
                match sdkDirOpt with
                | None -> fallback
                | Some sdkDir ->
                    match loadSdkFromDir fallback.PackageName sdkDir with
                    | Ok loaded -> loaded
                    | Error es ->
                        for e in es do
                            errors.Add(e)
                        fallback)
    for e in validateAliasCollisions sdks do
        errors.Add(e)
    let (externalMappings, externalErrors) = loadExternalMappings ()
    for e in externalErrors do
        errors.Add(e)
    {
        Sdks = sdks
        ExternalMappings = externalMappings
        Errors = List.ofSeq errors
    }

let private registry () : RegistryState =
    match registryCache with
    | Some cached -> cached
    | None ->
        let loaded = loadRegistry ()
        registryCache <- Some loaded
        loaded

let builtInSdks : PlatformSdk list = registry().Sdks

let sdkRegistryErrors () : string list =
    registry().Errors

let externalRegistryErrors () : string list =
    registry().Errors

let tryGetExternalTarget (target: Target) (externalName: string) : string option =
    registry().ExternalMappings
    |> Map.tryFind target
    |> Option.bind (Map.tryFind externalName)

let hasExternalTarget (target: Target) (externalName: string) : bool =
    registry().ExternalMappings
    |> Map.tryFind target
    |> Option.exists (fun m -> Map.containsKey externalName m)

let sdkForTarget (target: Target) : PlatformSdk =
    match builtInSdks |> List.tryFind (fun s -> s.Target = target) with
    | Some sdk -> sdk
    | None ->
        // Conservative hard fallback.
        fallbackSdks |> List.find (fun s -> s.Target = target)

let targetPlatformName (target: Target) : string =
    (sdkForTarget target).PlatformName

let targetOutputExt (target: Target) : string =
    (sdkForTarget target).OutputExt

/// Resolve a runtime file path for built-in SDK packages.
/// Tries cwd/app-base ancestors until it finds `sdks/<PackageName>/<relativePath>`.
let tryResolveSdkRuntimeFile (sdk: PlatformSdk) (relativePath: string) : string option =
    sdkSearchRoots ()
    |> List.tryPick (fun root ->
        let candidate = Path.Combine(root, "sdks", sdk.PackageName, relativePath)
        if File.Exists(candidate) then Some candidate else None)

/// Resolve the runtime template path for the given target, if one is declared and present.
let tryResolveRuntimeTemplate (target: Target) : string option =
    let sdk = sdkForTarget target
    match sdk.RuntimeTemplate with
    | None -> None
    | Some rel -> tryResolveSdkRuntimeFile sdk rel

let tryGetBuildCompileCommand (target: Target) : string option =
    (sdkForTarget target).BuildCompile

let tryGetBuildRunCommand (target: Target) : string option =
    (sdkForTarget target).BuildRun

let knownTargetAliases () : string list =
    builtInSdks
    |> List.collect (fun s ->
        [ s.PlatformName
          s.PackageName
          s.ModuleName
          yield! s.Aliases ])
    |> List.map normalize
    |> List.distinct
    |> List.sort

let tryParseTarget (raw: string) : Target option =
    let alias = normalize raw
    builtInSdks
    |> List.tryFind (fun s ->
        alias = normalize s.PlatformName
        || alias = normalize s.PackageName
        || alias = normalize s.ModuleName
        || (s.Aliases |> List.exists (fun a -> normalize a = alias)))
    |> Option.map (fun s -> s.Target)

let parseTargetOrDefault (rawOpt: string option) (fallback: Target) : Target =
    match rawOpt with
    | None -> fallback
    | Some raw ->
        match tryParseTarget raw with
        | Some t -> t
        | None -> fallback

/// Normalize a manifest [platform] list to canonical platform names.
/// Unknown entries are preserved (lowercased) so callers can report them.
let normalizePlatforms (platforms: string list) : string list =
    let rec loop seen acc rest =
        match rest with
        | [] -> List.rev acc
        | x :: xs ->
            let canonical =
                match tryParseTarget x with
                | Some t -> targetPlatformName t
                | None -> normalize x
            if Set.contains canonical seen then
                loop seen acc xs
            else
                loop (Set.add canonical seen) (canonical :: acc) xs
    loop Set.empty [] platforms
