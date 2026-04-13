module LLLang.Compiler

open LLLang.AST
open LLLang.Elaborator
open LLLang.Lexer
open LLLang.Parser
open LLLang.HMInfer
open LLLang.Types
open LLLang.TypedAST
open LLLang.Codegen
open LLLang.Platform
open LLLang.ProjectLoader

// Backward-compatible target aliases for call sites that only open LLLang.Compiler.
let FSharp = Target.FSharp
let TypeScript = Target.TypeScript
let Python = Target.Python
let Java = Target.Java
let CSharp = Target.CSharp
let LLVM = Target.LLVM

let private extractLineCol (msg: string) : int * int =
    let m = Regex.Match(msg, @"at (\d+):(\d+)")
    if m.Success then
        (int m.Groups.[1].Value, int m.Groups.[2].Value)
    else
        (0, 0)

let private wrapErr (msg: string) : LLError list =
    [{ Code = E001; Line = 0; Col = 0; Message = msg }]

let private externalMappingError (target: Target) (pm: PosMap) (sigRecord: FnSig) : LLError =
    let pos = PosMap.tryFind pm (box sigRecord)
    {
        Code = E026
        Line = pos.Line
        Col = pos.Col
        Message =
            sprintf
                "E026 %d:%d UnknownExternalMapping target:%s name:%s"
                pos.Line
                pos.Col
                (targetPlatformName target)
                sigRecord.Name
    }

let private validateExternalMappingsForTarget (target: Target) (pm: PosMap) (m: LLModule) : LLError list =
    m.Decls
    |> List.choose (fun (decl, _isExported) ->
        match decl with
        | DExternal sigRecord when hasExternalTarget target sigRecord.Name = false ->
            Some (externalMappingError target pm sigRecord)
        | _ -> None)

/// Check a ll-lang source string for a specific target:
/// lex → parse → elaborate → infer, skip codegen.
/// Includes target-specific external mapping validation (E026).
let checkTarget (target: Target) (src: string) : Result<unit, LLError list> =
    match parseModuleWithPos src with
    | Error e -> Error (wrapErr e)
    | Ok toks ->
        match parseModuleWithPos toks with
        | Error e -> Error (wrapErr e)
        | Ok (m, pm) ->
            match elaborate pm m with
            | Error es -> Error es
            | Ok _ ->
                let externalErrors = validateExternalMappingsForTarget target pm m'
                if List.isEmpty externalErrors then Ok ()
                else Error externalErrors

/// Check a ll-lang source string: lex → parse → elaborate → infer, skip codegen.
let check (src: string) : Result<unit, LLError list> =
    checkTarget FSharp src

/// Run the pipeline through H-M inference and apply the given emitter.
let private compileSrcForTarget (target: Target) (emitter: TypedModule -> string) (src: string) : Result<string, LLError list> =
    match parseModuleWithPos src with
    | Error e -> Error (wrapErr e)
    | Ok toks ->
        match parseModuleWithPos toks with
        | Error e -> Error (wrapErr e)
        | Ok (m, pm) ->
            match elaborate pm m with
            | Error es -> Error es
            | Ok tm ->
                let externalErrors = validateExternalMappingsForTarget target pm m'
                if List.isEmpty externalErrors then
                    Ok (emitter tm)
                else
                    Error externalErrors

/// Full pipeline: ll-lang source string → F# source string.
/// Threads a PosMap side-table from the parser through the elaborator and
/// HMInfer so that error messages carry real line:col from the source
/// (instead of the old 0:0 placeholder).
let compile (src: string) : Result<string, LLError list> =
    compileSrcForTarget FSharp emit src

/// Compile to TypeScript source.
let compileToTS (src: string) : Result<string, LLError list> =
    compileSrcForTarget TypeScript LLLang.CodegenTS.emit src

/// Compile to Python source.
let compileToPy (src: string) : Result<string, LLError list> =
    compileSrcForTarget Python LLLang.CodegenPy.emit src

/// Compile to Java source.
let compileToJava (src: string) : Result<string, LLError list> =
    compileSrcForTarget Java LLLang.CodegenJava.emit src

/// Compile to C# source.
let compileToCSharp (src: string) : Result<string, LLError list> =
    compileSrcForTarget CSharp LLLang.CodegenCSharp.emit src

/// Compile to LLVM IR source.
let compileToLLVM (src: string) : Result<string, LLError list> =
    compileSrcForTarget LLVM LLLang.CodegenLLVM.emit src

/// Compile to any target.
let compileTarget (target: Target) (src: string) : Result<string, LLError list> =
    match target with
    | FSharp     -> compile src
    | TypeScript -> compileToTS src
    | Python     -> compileToPy src
    | Java       -> compileToJava src
    | CSharp     -> compileToCSharp src
    | LLVM       -> compileToLLVM src

/// Compile a single LoadedFile. Each file is compiled independently;
/// F# handles cross-module type resolution in the concatenated output.
let private compileFileForTarget (target: Target) (lf: LoadedFile) : Result<TypedModule, LLError list> =
    match parseModuleWithPos lf.Src with
    | Error e -> Error (wrapErr e)
    | Ok toks ->
        match parseModuleWithPos toks with
        | Error e -> Error (wrapErr e)
        | Ok (m, pm) ->
            // If no module header, assign path from file location
            let m' = if m.Path = [] then { m with Path = lf.ModulePath } else m
            match elaborate pm m' with
            | Error es -> Error es
            | Ok tm ->
                let externalErrors = validateExternalMappingsForTarget target pm m''
                if List.isEmpty externalErrors then
                    Ok tm
                else
                    Error externalErrors

let private compileFile (lf: LoadedFile) : Result<TypedModule, LLError list> =
    compileFileForTarget FSharp lf

/// Compile a single LoadedFile with an accumulated imported environment from
/// previously compiled files. Imported names are made visible to the elaborator
/// and HM inference engine so that cross-module references resolve correctly.
let private compileFileWithEnvForTarget
    (target: Target)
    (lf: LoadedFile)
    (importedEnv: Env)
    : Result<TypedModule, LLError list> =
    match parseModuleWithPos lf.Src with
    | Error e -> Error (wrapErr e)
    | Ok toks ->
        match parseModuleWithPos toks with
        | Error e -> Error (wrapErr e)
        | Ok (m, pm) ->
            let m' = if m.Path = [] then { m with Path = lf.ModulePath } else m
            // Convert the HM Env (name → TypeScheme) to a plain TypeEnv
            // (name → TypeExpr) for the elaborator by extracting each scheme's body.
            let importedTypeEnv : LLLang.Elaborator.TypeEnv =
                importedEnv |> Map.map (fun _ sch -> sch.Body)
            match elaborateWithImports pm m' importedTypeEnv with
            | Error es -> Error es
            | Ok tm ->
                let externalErrors = validateExternalMappingsForTarget target pm m''
                if List.isEmpty externalErrors then
                    Ok tm
                else
                    Error externalErrors

let private compileFileWithEnv (lf: LoadedFile) (importedEnv: Env) : Result<TypedModule, LLError list> =
    compileFileWithEnvForTarget FSharp lf importedEnv

/// Front-end only: lex → parse → elaborate → infer all project files in topo
/// order using the provided target.
/// External validation is done using the same per-target mapping used by emitters.
let compileProjectToModulesForTarget
    (target: Target)
    (proj: LLProject)
    : Result<TypedModule list, LLError list> =
    let rec compileAll (files: LoadedFile list) (accEnv: Env) (accModules: TypedModule list) =
        match files with
        | [] -> Ok (List.rev accModules)
        | lf :: rest ->
            match compileFileWithEnvForTarget target lf accEnv with
            | Error es -> Error es
            | Ok tm ->
                let newAccEnv = Map.fold (fun acc k v -> Map.add k v acc) accEnv tm.Env
                compileAll rest newAccEnv (tm :: accModules)
    compileAll proj.Files Map.empty []

/// Front-end only: lex → parse → elaborate → infer all project files in topo
/// order, returning the list of TypedModules without running any codegen.
/// The inferred exports of each file are accumulated and made available to
/// subsequent files so that cross-module name resolution works.
let compileProjectToModules (proj: LLProject) : Result<TypedModule list, LLError list> =
    compileProjectToModulesForTarget FSharp proj

/// Compile a multi-file project: compile each file in topo order,
/// then concatenate all modules into a single F# source string.
/// The inferred exports of each file are accumulated and made available
/// to subsequent files so that cross-module name resolution works.
let compileProject (proj: LLProject) : Result<string, LLError list> =
    match compileProjectToModules proj with
    | Error es -> Error es
    | Ok tms -> Ok (emitProjectModules tms)
