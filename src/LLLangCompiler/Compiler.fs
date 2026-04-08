module LLLang.Compiler

open LLLang.Elaborator

/// Full pipeline: ll-lang source string → F# source string.
let compile (src: string) : Result<string, LLError list> = Ok ""
