module LLLang.HMInfer

open LLLang.AST
open LLLang.Types
open LLLang.TypedAST
open LLLang.Elaborator

/// Main entry point: infer types for a module given the Phase-3 elaborator environment.
let infer (m: LLModule) (env: Elaborator.TypeEnv) : Result<TypedModule, LLError list> =
    Error [{ Code = E001; Line = 0; Col = 0; Message = "HMInfer: not implemented" }]
