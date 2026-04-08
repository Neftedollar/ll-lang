module LLLang.Compiler

open LLLang.AST
open LLLang.Elaborator
open LLLang.Lexer
open LLLang.Parser
open LLLang.HMInfer
open LLLang.Codegen

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
