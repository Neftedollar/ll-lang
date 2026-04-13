module LLLang.Mcp

open System
open System.IO
open System.Text.Json
open System.Diagnostics
open System.Threading.Tasks
open FsMcp.Core
open FsMcp.Core.Validation
open FsMcp.Server
open LLLang.Elaborator
open LLLang.Compiler
open LLLang.Platform
open LLLang.ProjectLoader

// ─── Arg types ───────────────────────────────────────────────────────────────

type CompileFileArgs   = { path: string; include_output: bool option; target: string option }
type CompileSourceArgs = { source: string; target: string option }
type CheckFileArgs     = { path: string }
type CheckSourceArgs   = { source: string }
type RunFileArgs       = { path: string }
type LookupErrorArgs    = { code: string }
type StdlibSearchArgs   = { query: string }
type GrammarLookupArgs  = { rule: string }
type ProjectInfoArgs    = { path: string }

// ─── Helpers ─────────────────────────────────────────────────────────────────

let private ok text : Task<Result<Content list, McpError>> =
    Task.FromResult(Ok [ Content.text text ])

let private js (s: string) = JsonSerializer.Serialize(s)

let private errorsToJson (es: LLError list) =
    let items =
        es |> List.map (fun e ->
            sprintf "{\"code\":\"%A\",\"line\":%d,\"col\":%d,\"message\":%s}"
                e.Code e.Line e.Col (js e.Message))
    "[" + String.concat "," items + "]"

// ─── helpers ─────────────────────────────────────────────────────────────────

let private parseTargetStr (t: string option) : Target =
    parseTargetOrDefault t FSharp

let private targetFieldName = function
    | FSharp -> "fsharp"
    | TypeScript -> "typescript"
    | Python -> "python"
    | Java -> "java"
    | CSharp -> "csharp"
    | LLVM -> "llvm"

// ─── compile_file ────────────────────────────────────────────────────────────

let compileFileTool (args: CompileFileArgs) : Task<Result<Content list, McpError>> =
    task {
        try
            if not (args.path.EndsWith(".lll")) then
                return! ok """{"ok":false,"errors":[{"code":"E000","message":"path must end with .lll"}]}"""
            else
                let src = File.ReadAllText(args.path)
                let target = parseTargetStr args.target
                match compileTarget target src with
                | Ok out ->
                    let outputField =
                        if args.include_output |> Option.defaultValue false
                        then sprintf ",\"%s\":%s" (targetFieldName target) (js out)
                        else ""
                    return! ok (sprintf "{\"ok\":true,\"errors\":[],\"target\":%s%s}" (js (string target)) outputField)
                | Error es ->
                    return! ok (sprintf "{\"ok\":false,\"errors\":%s}" (errorsToJson es))
        with ex ->
            return! ok (sprintf "{\"ok\":false,\"errors\":[{\"code\":\"E000\",\"message\":%s}]}" (js ex.Message))
    }

// ─── compile_source ──────────────────────────────────────────────────────────

let compileSourceTool (args: CompileSourceArgs) : Task<Result<Content list, McpError>> =
    task {
        try
            let target = parseTargetStr args.target
            match compileTarget target args.source with
            | Ok out ->
                return! ok (sprintf "{\"ok\":true,\"errors\":[],\"target\":%s,\"%s\":%s}"
                                (js (string target)) (targetFieldName target) (js out))
            | Error es ->
                return! ok (sprintf "{\"ok\":false,\"errors\":%s}" (errorsToJson es))
        with ex ->
            return! ok (sprintf "{\"ok\":false,\"errors\":[{\"code\":\"E000\",\"message\":%s}]}" (js ex.Message))
    }

// ─── check_source ────────────────────────────────────────────────────────────

let checkSourceTool (args: CheckSourceArgs) : Task<Result<Content list, McpError>> =
    task {
        try
            match check args.source with
            | Ok ()  -> return! ok """{"ok":true,"errors":[]}"""
            | Error es ->
                return! ok (sprintf "{\"ok\":false,\"errors\":%s}" (errorsToJson es))
        with ex ->
            return! ok (sprintf "{\"ok\":false,\"errors\":[{\"code\":\"E000\",\"message\":%s}]}" (js ex.Message))
    }

// ─── check_file ──────────────────────────────────────────────────────────────

let checkFileTool (args: CheckFileArgs) : Task<Result<Content list, McpError>> =
    task {
        try
            if not (args.path.EndsWith(".lll")) then
                return! ok """{"ok":false,"errors":[{"code":"E000","message":"path must end with .lll"}]}"""
            else
                let src = File.ReadAllText(args.path)
                match check src with
                | Ok ()  -> return! ok """{"ok":true,"errors":[]}"""
                | Error es ->
                    return! ok (sprintf "{\"ok\":false,\"errors\":%s}" (errorsToJson es))
        with ex ->
            return! ok (sprintf "{\"ok\":false,\"errors\":[{\"code\":\"E000\",\"message\":%s}]}" (js ex.Message))
    }

// ─── run_file ────────────────────────────────────────────────────────────────

let runFileTool (args: RunFileArgs) : Task<Result<Content list, McpError>> =
    task {
        try
            if not (args.path.EndsWith(".lll")) then
                return! ok """{"exit_code":-1,"stdout":"","stderr":"path must end with .lll","errors":[]}"""
            else
                let src = File.ReadAllText(args.path)
                match compile src with
                | Error es ->
                    return! ok (sprintf "{\"exit_code\":-1,\"stdout\":\"\",\"stderr\":\"\",\"errors\":%s}" (errorsToJson es))
                | Ok fs ->
                    let tmp = Path.GetTempFileName() + ".fsx"
                    try
                        let stripped =
                            fs.Split('\n')
                            |> Array.filter (fun l ->
                                let t = l.TrimStart()
                                not (t.StartsWith("module ")) && not (t.StartsWith("[<EntryPoint>]")))
                            |> String.concat "\n"
                        File.WriteAllText(tmp, stripped + "\nmain [||] |> int64 |> exit\n")
                        let psi = ProcessStartInfo("dotnet", sprintf "fsi \"%s\"" tmp)
                        psi.RedirectStandardOutput <- true
                        psi.RedirectStandardError  <- true
                        psi.UseShellExecute        <- false
                        use proc = Process.Start(psi)
                        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
                        let stderrTask = proc.StandardError.ReadToEndAsync()
                        do! Task.WhenAll(stdoutTask, stderrTask) :> Task
                        proc.WaitForExit()
                        let result =
                            sprintf "{\"exit_code\":%d,\"stdout\":%s,\"stderr\":%s,\"errors\":[]}"
                                proc.ExitCode (js stdoutTask.Result) (js stderrTask.Result)
                        return! ok result
                    finally
                        try File.Delete(tmp) with _ -> ()
        with ex ->
            return! ok (sprintf "{\"exit_code\":-1,\"stdout\":\"\",\"stderr\":%s,\"errors\":[]}" (js ex.Message))
    }

// ─── list_errors ─────────────────────────────────────────────────────────────

let private knownErrors = [
    "E001", "TypeMismatch",           "Expected type A, got type B at a usage site."
    "E002", "UnboundVar",             "Identifier not found in scope."
    "E003", "NonExhaustiveMatch",     "Pattern match does not cover all constructors of a sum type."
    "E004", "UnitMismatch",           "Incompatible units in arithmetic or argument position."
    "E005", "TagViolation",           "Untagged value passed where tagged value expected."
    "E006", "MissingImpl",            "No impl TraitName TypeName found for a constrained type variable."
    "E007", "PlatformMismatch",       "Platform-specific module imported but compile target doesn't support it."
    "E008", "InfiniteType",           "Type unification would produce an infinite type (occurs-check failure)."
    "E026", "UnknownExternalMapping", "External declaration has no matching target mapping."
    "E020", "ModulePathMismatch",     "module header does not match the file's location in src/."
    "E024", "ModuleCycle",            "Import graph contains a cycle."
    "E025", "NoProjectForImport",     "Non-Std.* import used in single-file mode (no lll.toml)."
]

let listErrorsTool (_args: {| dummy: string option |}) : Task<Result<Content list, McpError>> =
    task {
        let items =
            knownErrors |> List.map (fun (code, name, desc) ->
                sprintf "{\"code\":%s,\"name\":%s,\"description\":%s}" (js code) (js name) (js desc))
        return! ok ("[" + String.concat "," items + "]")
    }

// ─── lookup_error ────────────────────────────────────────────────────────────

let lookupErrorTool (args: LookupErrorArgs) : Task<Result<Content list, McpError>> =
    task {
        let code = args.code.ToUpper()
        match knownErrors |> List.tryFind (fun (c, _, _) -> c = code) with
        | None ->
            return! ok (sprintf "{\"found\":false,\"code\":%s}" (js code))
        | Some (c, name, desc) ->
            let exampleDir =
                let b = AppContext.BaseDirectory
                // Walk up to find spec/examples/invalid from the binary dir
                let candidates = [1..6] |> List.map (fun n ->
                    let rel = String.replicate n "../" |> fun r -> r.TrimEnd('/')
                    Path.Combine(b, rel, "spec", "examples", "invalid") |> Path.GetFullPath)
                candidates |> List.tryFind Directory.Exists |> Option.defaultValue ""
            let exampleContent =
                if Directory.Exists(exampleDir) then
                    Directory.GetFiles(exampleDir, "*.lll")
                    |> Array.tryFind (fun f ->
                        try
                            File.ReadLines(f)
                            |> Seq.tryHead
                            |> Option.map (fun l -> l.Contains("expect: " + c))
                            |> Option.defaultValue false
                        with _ -> false)
                    |> Option.map (fun f -> try File.ReadAllText(f) with _ -> "")
                    |> Option.defaultValue ""
                else ""
            return! ok (sprintf "{\"found\":true,\"code\":%s,\"name\":%s,\"description\":%s,\"example\":%s}"
                            (js c) (js name) (js desc) (js exampleContent))
    }

// ─── stdlib_search ───────────────────────────────────────────────────────────

let private stdlibEntries = [
    "printfn",      "Str -> Unit",                          "Std.IO"
    "print",        "Str -> Unit",                          "Std.IO"
    "readFile",     "Str -> Str",                           "Std.IO"
    "writeFile",    "Str -> Str -> Unit",                   "Std.IO"
    "exit",         "Int -> Unit",                          "Std.IO"
    "getArgs",      "Unit -> List[Str]",                    "Std.IO"
    "getEnv",       "Str -> Maybe[Str]",                    "Std.IO"
    "abs",          "Int -> Int",                           "Std.Math"
    "absf",         "Float -> Float",                       "Std.Math"
    "sqrt",         "Float -> Float",                       "Std.Math"
    "min",          "Int -> Int -> Int",                    "Std.Math"
    "max",          "Int -> Int -> Int",                    "Std.Math"
    "intToFloat",   "Int -> Float",                         "Std.Math"
    "floatToInt",   "Float -> Int",                         "Std.Math"
    "listLen",      "List[A] -> Int",                       "Std.List"
    "listMap",      "(A -> B) -> List[A] -> List[B]",       "Std.List"
    "listFilter",   "(A -> Bool) -> List[A] -> List[A]",    "Std.List"
    "listFold",     "(B -> A -> B) -> B -> List[A] -> B",   "Std.List"
    "listHead",     "List[A] -> Maybe[A]",                  "Std.List"
    "listTail",     "List[A] -> Maybe[List[A]]",            "Std.List"
    "listReverse",  "List[A] -> List[A]",                   "Std.List"
    "listAppend",   "List[A] -> List[A] -> List[A]",        "Std.List"
    "listIsEmpty",  "List[A] -> Bool",                      "Std.List"
    "listContains", "List[A] -> A -> Bool",                 "Std.List"
    "listRange",    "Int -> Int -> List[Int]",              "Std.List"
    "listZip",      "List[A] -> List[B] -> List[(A,B)]",    "Std.List"
    "maybeMap",     "(A -> B) -> Maybe[A] -> Maybe[B]",     "Std.Maybe"
    "maybeDefault", "A -> Maybe[A] -> A",                   "Std.Maybe"
    "maybeBind",    "(A -> Maybe[B]) -> Maybe[A] -> Maybe[B]", "Std.Maybe"
    "maybeIsNone",  "Maybe[A] -> Bool",                     "Std.Maybe"
    "resultMap",    "(A -> B) -> Result[A,E] -> Result[B,E]", "Std.Result"
    "resultBind",   "(A -> Result[B,E]) -> Result[A,E] -> Result[B,E]", "Std.Result"
    "resultIsOk",   "Result[A,E] -> Bool",                  "Std.Result"
    "strLen",       "Str -> Int",                           "Std.Str"
    "strConcat",    "Str -> Str -> Str",                    "Std.Str"
    "strSplit",     "Str -> Str -> List[Str]",              "Std.Str"
    "strTrim",      "Str -> Str",                           "Std.Str"
    "strContains",  "Str -> Str -> Bool",                   "Std.Str"
    "strToInt",     "Str -> Maybe[Int]",                    "Std.Str"
    "strToFloat",   "Str -> Maybe[Float]",                  "Std.Str"
    "strFromChars", "List[Char] -> Str",                    "Std.Str"
    "strChars",     "Str -> List[Char]",                    "Std.Str"
    "intToStr",     "Int -> Str",                           "Std.Str"
    "floatToStr",   "Float -> Str",                         "Std.Str"
    "charIsDigit",  "Char -> Bool",                         "Std.Char"
    "charIsSpace",  "Char -> Bool",                         "Std.Char"
    "charIsAlpha",  "Char -> Bool",                         "Std.Char"
    "charToInt",    "Char -> Int",                          "Std.Char"
    "intToChar",    "Int -> Char",                          "Std.Char"
]

let stdlibSearchTool (args: StdlibSearchArgs) : Task<Result<Content list, McpError>> =
    task {
        let q = args.query.ToLower()
        let matches =
            stdlibEntries
            |> List.filter (fun (name, sig_, _) ->
                name.ToLower().Contains(q) || sig_.ToLower().Contains(q))
            |> List.map (fun (name, sig_, modul) ->
                sprintf "{\"name\":%s,\"signature\":%s,\"module\":%s,\"scope\":\"stdlib\"}"
                    (js name) (js sig_) (js modul))
        return! ok ("[" + String.concat "," matches + "]")
    }

// ─── grammar_lookup ──────────────────────────────────────────────────────────

let private findGrammarFile () =
    let b = AppContext.BaseDirectory
    [1..6]
    |> List.map (fun n ->
        let up = String.concat "" (List.replicate n "../")
        Path.Combine(b, up, "spec", "grammar.ebnf") |> Path.GetFullPath)
    |> List.tryFind File.Exists

let grammarLookupTool (args: GrammarLookupArgs) : Task<Result<Content list, McpError>> =
    task {
        match findGrammarFile () with
        | None ->
            return! ok (sprintf "{\"found\":false,\"rule\":%s,\"error\":\"grammar.ebnf not found\"}" (js args.rule))
        | Some gpath ->
            let lines = File.ReadAllLines(gpath)
            let rule = args.rule.Trim()
            let startIdx =
                lines |> Array.tryFindIndex (fun l ->
                    let t = l.TrimStart()
                    t.StartsWith(rule + " ") || t.StartsWith(rule + "\t") || t = rule)
            match startIdx with
            | None ->
                return! ok (sprintf "{\"found\":false,\"rule\":%s}" (js rule))
            | Some i ->
                let sb = System.Text.StringBuilder()
                let mutable j = i
                let mutable cont = true
                while cont && j < lines.Length do
                    let line = lines.[j]
                    if j > i && (line.Trim() = "" || (line.Length > 0 && Char.IsLetter(line.[0]))) then
                        cont <- false
                    else
                        sb.AppendLine(line) |> ignore
                        j <- j + 1
                return! ok (sprintf "{\"found\":true,\"rule\":%s,\"production\":%s}"
                                (js rule) (js (sb.ToString().TrimEnd())))
    }

// ─── project_info ────────────────────────────────────────────────────────────

let private findProjectRoot (startPath: string) =
    let startDir =
        if File.Exists(startPath) then Path.GetDirectoryName(startPath)
        elif Directory.Exists(startPath) then startPath
        else startPath
    let mutable dir = startDir
    let mutable found = false
    let mutable result: string option = None
    while not found do
        // Prefer lll.toml; fall back to ll.toml for backwards compat
        let lll = Path.Combine(dir, "lll.toml")
        let ll  = Path.Combine(dir, "ll.toml")
        if File.Exists(lll) || File.Exists(ll) then
            found <- true
            result <- Some dir
        else
            let parent = Directory.GetParent(dir)
            if parent = null || parent.FullName = dir then found <- true
            else dir <- parent.FullName
    result

let projectInfoTool (args: ProjectInfoArgs) : Task<Result<Content list, McpError>> =
    task {
        try
            match findProjectRoot args.path with
            | None ->
                let modName =
                    if args.path.EndsWith(".lll") && File.Exists(args.path) then
                        try
                            File.ReadLines(args.path)
                            |> Seq.tryFind (fun l -> l.TrimStart().StartsWith("module "))
                            |> Option.map (fun l -> l.Trim().[7..].Trim())
                            |> Option.defaultValue ""
                        with _ -> ""
                    else ""
                return! ok (sprintf
                    "{\"root\":null,\"manifest\":null,\"modules\":[{\"path\":%s,\"module\":%s}],\"deps\":[],\"platform_use\":[]}"
                    (js args.path) (js modName))
            | Some root ->
                match loadProject root with
                | Error es ->
                    return! ok (sprintf "{\"root\":%s,\"errors\":%s}" (js root) (errorsToJson es))
                | Ok proj ->
                    let modules =
                        proj.Files |> List.map (fun lf ->
                            sprintf "{\"path\":%s,\"module\":%s}"
                                (js lf.FilePath) (js (String.concat "." lf.ModulePath)))
                    let deps =
                        proj.Manifest.Deps |> Map.toList |> List.map (fun (k, src) ->
                            let srcStr =
                                match src with
                                | LLLang.Manifest.GitDep(url, ref) -> sprintf "%s#%s" url ref
                                | LLLang.Manifest.PathDep(path) -> sprintf "path:%s" path
                            sprintf "{\"name\":%s,\"source\":%s}" (js k) (js srcStr))
                    let platforms = proj.Manifest.Platform |> List.map js
                    return! ok (sprintf
                        "{\"root\":%s,\"manifest\":{\"name\":%s,\"version\":%s},\"modules\":[%s],\"deps\":[%s],\"platform_use\":[%s],\"errors\":[]}"
                        (js root)
                        (js proj.Manifest.Name)
                        (js proj.Manifest.Version)
                        (String.concat "," modules)
                        (String.concat "," deps)
                        (String.concat "," platforms))
        with ex ->
            return! ok (sprintf "{\"root\":null,\"error\":%s}" (js ex.Message))
    }

// ─── Server ──────────────────────────────────────────────────────────────────

let runServer () =
    let server = mcpServer {
        name "ll-lang"
        version "0.1.0"

        tool (TypedTool.define<CompileFileArgs>
            "compile_file"
            "Compile a .lll file. target: 'fs'|'ts'|'py'|'java'|'cs'|'llvm' (default 'fs'). include_output=true returns generated source. Returns {ok, errors[], target, <target>?}."
            compileFileTool |> unwrapResult)

        tool (TypedTool.define<CompileSourceArgs>
            "compile_source"
            "Compile ll-lang source string directly (no file needed). target: 'fs'|'ts'|'py'|'java'|'cs'|'llvm' (default 'fs'). Returns {ok, errors[], target, <target>}. Fastest way for an LLM to check generated code."
            compileSourceTool |> unwrapResult)

        tool (TypedTool.define<CheckFileArgs>
            "check_file"
            "Type-check a .lll file (lex→parse→elaborate→infer, no codegen). Returns {ok, errors[]}. Faster than compile_file."
            checkFileTool |> unwrapResult)

        tool (TypedTool.define<CheckSourceArgs>
            "check_source"
            "Type-check ll-lang source string directly (no file needed). Returns {ok, errors[]}. Fastest way for an LLM to validate syntax and types."
            checkSourceTool |> unwrapResult)

        tool (TypedTool.define<RunFileArgs>
            "run_file"
            "Compile and run a .lll file via dotnet fsi. Returns {exit_code, stdout, stderr, errors[]}. WARNING: executes arbitrary user code."
            runFileTool |> unwrapResult)

        tool (TypedTool.define<{| dummy: string option |}>
            "list_errors"
            "List all ll-lang error codes with names and short descriptions."
            listErrorsTool |> unwrapResult)

        tool (TypedTool.define<LookupErrorArgs>
            "lookup_error"
            "Get detailed explanation and minimal repro example for an error code like 'E003'."
            lookupErrorTool |> unwrapResult)

        tool (TypedTool.define<StdlibSearchArgs>
            "stdlib_search"
            "Search the ll-lang standard library by name or type signature substring. Returns [{name, signature, module, scope}]."
            stdlibSearchTool |> unwrapResult)

        tool (TypedTool.define<GrammarLookupArgs>
            "grammar_lookup"
            "Get the EBNF grammar production for a rule like 'Expr', 'Pattern', or 'TypeExpr'."
            grammarLookupTool |> unwrapResult)

        tool (TypedTool.define<ProjectInfoArgs>
            "project_info"
            "Get project metadata (manifest, modules, deps) by walking up from path to find lll.toml (or ll.toml). Works in single-file mode too."
            projectInfoTool |> unwrapResult)

        useStdio
    }
    Server.run server |> fun t -> t.GetAwaiter().GetResult()
