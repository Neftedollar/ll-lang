module LLLang.Compiler

open LLLang.Elaborator
open LLLang.Lexer
open LLLang.Parser
open LLLang.HMInfer
open LLLang.Codegen

let private wrapErr (msg: string) : LLError list =
    [{ Code = E001; Line = 0; Col = 0; Message = msg }]

/// Full pipeline: ll-lang source string → F# source string.
let compile (src: string) : Result<string, LLError list> =
    match tokenize src with
    | Error e -> Error (wrapErr e)
    | Ok toks ->
        match parseModule toks with
        | Error e -> Error (wrapErr e)
        | Ok m ->
            match elaborate m with
            | Error es -> Error es
            | Ok env ->
                match infer m env with
                | Error es -> Error es
                | Ok tm -> Ok (emit tm)
