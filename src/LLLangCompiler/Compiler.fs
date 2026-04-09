module LLLang.Compiler

open LLLang.AST
open LLLang.Elaborator
open LLLang.Lexer
open LLLang.Parser
open LLLang.HMInfer
open LLLang.TypedAST
open LLLang.Codegen
open LLLang.ProjectLoader

let private wrapErr (msg: string) : LLError list =
    [{ Code = E001; Line = 0; Col = 0; Message = msg }]

/// Full pipeline: ll-lang source string → F# source string.
/// Threads a PosMap side-table from the parser through the elaborator and
/// HMInfer so that error messages carry real line:col from the source
/// (instead of the old 0:0 placeholder).
let compile (src: string) : Result<string, LLError list> =
    match tokenize src with
    | Error e -> Error (wrapErr e)
    | Ok toks ->
        match parseModuleWithPos toks with
        | Error e -> Error (wrapErr e)
        | Ok (m, pm) ->
            match elaborate pm m with
            | Error es -> Error es
            | Ok (m', env) ->
                match infer pm m' env with
                | Error es -> Error es
                | Ok tm -> Ok (emit tm)

/// Compile a single LoadedFile. Each file is compiled independently;
/// F# handles cross-module type resolution in the concatenated output.
let private compileFile (lf: LoadedFile) : Result<TypedModule, LLError list> =
    match tokenize lf.Src with
    | Error e -> Error (wrapErr e)
    | Ok toks ->
        match parseModuleWithPos toks with
        | Error e -> Error (wrapErr e)
        | Ok (m, pm) ->
            // If no module header, assign path from file location
            let m' = if m.Path = [] then { m with Path = lf.ModulePath } else m
            match elaborate pm m' with
            | Error es -> Error es
            | Ok (m'', env) ->
                match infer pm m'' env with
                | Error es -> Error es
                | Ok tm -> Ok tm

/// Compile a multi-file project: compile each file in topo order,
/// then concatenate all modules into a single F# source string.
let compileProject (proj: LLProject) : Result<string, LLError list> =
    let results =
        proj.Files
        |> List.map compileFile
    let errors = results |> List.collect (fun r -> match r with Error es -> es | Ok _ -> [])
    if not errors.IsEmpty then Error errors
    else
        let tms = results |> List.choose (fun r -> match r with Ok tm -> Some tm | Error _ -> None)
        Ok (emitProjectModules tms)
