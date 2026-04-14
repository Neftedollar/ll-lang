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

let private wrapErr (msg: string) : LLError list =
    [{ Code = E001; Line = 0; Col = 0; Message = msg }]

let private parseModuleWithPosFromSrc (src: string) : Result<LLModule * PosMap, LLError list> =
    match tokenize src with
    | Error e -> Error (wrapErr e)
    | Ok toks ->
        parseModuleWithPos toks
        |> Result.mapError wrapErr

/// Apply a substitution over all TyVar occurrences (rigid + flexible).
/// Used only when materializing imported TypeSchemes for elaboration.
let private applyTypeVarSubstAll (subst: Map<Ident, TypeExpr>) (ty: TypeExpr) : TypeExpr =
    let rec go t =
        match t with
        | TyVar v ->
            match Map.tryFind v subst with
            | Some t' -> t'
            | None -> t
        | TyName _ -> t
        | TyApp(a, b) -> TyApp(go a, go b)
        | TyFn(a, b) -> TyFn(go a, go b)
        | TyTagged(a, u) -> TyTagged(go a, u)
    go ty

/// Convert HM schemes to an elaborator TypeEnv while preserving imported
/// polymorphism across the elaboration -> HM boundary.
///
/// We rewrite quantified vars to stable rigid placeholders (`__imp_*`) so
/// `fromElaboratorEnv` can re-quantify them later. Monomorphic imported
/// schemes (Vars = []) remain monomorphic.
let private importedSchemesToElaboratorEnv (importedEnv: Env) : Elaborator.TypeEnv =
    importedEnv
    |> Map.toList
    |> List.mapi (fun i (name, sch) ->
        let quantSubst =
            sch.Vars
            |> List.mapi (fun j v -> v, TyVar (sprintf "__imp_%d_%d" i j))
            |> Map.ofList
        let body =
            if Map.isEmpty quantSubst then sch.Body
            else applyTypeVarSubstAll quantSubst sch.Body
        name, body)
    |> Map.ofList

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

let private inferModuleForTarget
    (target: Target)
    (pm: PosMap)
    (m: LLModule)
    (env0: Elaborator.TypeEnv)
    : Result<TypedModule, LLError list> =
    match infer pm m env0 with
    | Error es -> Error es
    | Ok tm ->
        let externalErrors = validateExternalMappingsForTarget target pm m
        if List.isEmpty externalErrors then Ok tm
        else Error externalErrors

/// Check a ll-lang source string for a specific target:
/// lex → parse → elaborate → infer, skip codegen.
/// Includes target-specific external mapping validation (E026).
let checkTarget (target: Target) (src: string) : Result<unit, LLError list> =
    match parseModuleWithPosFromSrc src with
    | Error es -> Error es
    | Ok (m, pm) ->
        match elaborate pm m with
        | Error es -> Error es
        | Ok (m', env0) ->
            match inferModuleForTarget target pm m' env0 with
            | Error es -> Error es
            | Ok _ -> Ok ()

/// Check a ll-lang source string: lex → parse → elaborate → infer, skip codegen.
let check (src: string) : Result<unit, LLError list> =
    checkTarget FSharp src

/// Run the pipeline through H-M inference and apply the given emitter.
let private compileSrcForTarget (target: Target) (emitter: TypedModule -> string) (src: string) : Result<string, LLError list> =
    match parseModuleWithPosFromSrc src with
    | Error es -> Error es
    | Ok (m, pm) ->
        match elaborate pm m with
        | Error es -> Error es
        | Ok (m', env0) ->
            match inferModuleForTarget target pm m' env0 with
            | Error es -> Error es
            | Ok tm -> Ok (emitter tm)

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
    match parseModuleWithPosFromSrc lf.Src with
    | Error es -> Error es
    | Ok (m0, pm) ->
        let m = if m0.Path = [] then { m0 with Path = lf.ModulePath } else m0
        match elaborate pm m with
        | Error es -> Error es
        | Ok (m', env0) ->
            inferModuleForTarget target pm m' env0

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
    match parseModuleWithPosFromSrc lf.Src with
    | Error es -> Error es
    | Ok (m0, pm) ->
        let m = if m0.Path = [] then { m0 with Path = lf.ModulePath } else m0
        // Preserve quantification info from imported TypeSchemes while passing
        // through elaboration's TypeExpr-only environment.
        let importedTypeEnv : LLLang.Elaborator.TypeEnv =
            importedSchemesToElaboratorEnv importedEnv
        match elaborateWithImports pm m importedTypeEnv with
        | Error es -> Error es
        | Ok (m', env0) ->
            inferModuleForTarget target pm m' env0

let private compileFileWithEnv (lf: LoadedFile) (importedEnv: Env) : Result<TypedModule, LLError list> =
    compileFileWithEnvForTarget FSharp lf importedEnv

type private ModuleMeta = {
    Imports: string list list
    Exports: Set<string> option
}

let private moduleMetaForProjectFiles (files: LoadedFile list) : Result<Map<string list, ModuleMeta>, LLError list> =
    let folder (acc: Result<Map<string list, ModuleMeta>, LLError list>) (lf: LoadedFile) =
        match acc with
        | Error es -> Error es
        | Ok st ->
            match parseModuleWithPosFromSrc lf.Src with
            | Error es ->
                Error (
                    es
                    |> List.map (fun e ->
                        { e with Message = e.Message + " file:" + lf.FilePath })
                )
            | Ok (m, _) ->
                let meta =
                    {
                        Imports = m.Imports
                        Exports = m.Exports |> Option.map Set.ofList
                    }
                Ok (Map.add lf.ModulePath meta st)
    List.fold folder (Ok Map.empty) files

let private applyImportVisibility (_meta: ModuleMeta) (env: Env) : Env =
    match _meta.Exports with
    | Some exports ->
        env |> Map.filter (fun name _ -> Set.contains name exports)
    | None ->
        // Compatibility fallback: modules without explicit export list
        // remain all-visible to imports.
        env

/// Front-end only: lex → parse → elaborate → infer all project files in topo
/// order using the provided target.
/// External validation is done using the same per-target mapping used by emitters.
let compileProjectToModulesForTarget
    (target: Target)
    (proj: LLProject)
    : Result<TypedModule list, LLError list> =
    let allPaths = proj.Files |> List.map (fun lf -> lf.ModulePath) |> Set.ofList
    match moduleMetaForProjectFiles proj.Files with
    | Error es -> Error es
    | Ok moduleMetaMap ->
        let rec compileAll
            (files: LoadedFile list)
            (moduleEnvs: Map<string list, Env>)
            (accModules: TypedModule list)
            =
            match files with
            | [] -> Ok (List.rev accModules)
            | lf :: rest ->
                let imports =
                    moduleMetaMap
                    |> Map.tryFind lf.ModulePath
                    |> Option.map (fun m -> m.Imports)
                    |> Option.defaultValue []
                    |> List.filter (fun imp -> Set.contains imp allPaths)

                let importedEnv =
                    imports
                    |> List.fold (fun acc imp ->
                        match Map.tryFind imp moduleEnvs with
                        | Some env -> Map.fold (fun st k v -> Map.add k v st) acc env
                        | None -> acc
                    ) Map.empty

                match compileFileWithEnvForTarget target lf importedEnv with
                | Error es -> Error es
                | Ok tm ->
                    let exportedEnv =
                        match Map.tryFind lf.ModulePath moduleMetaMap with
                        | Some meta -> applyImportVisibility meta tm.Env
                        | None -> tm.Env
                    let newModuleEnvs = Map.add lf.ModulePath exportedEnv moduleEnvs
                    compileAll rest newModuleEnvs (tm :: accModules)

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
