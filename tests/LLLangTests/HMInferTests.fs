module LLLang.Tests.HMInferTests

open System.IO
open Xunit
open LLLang.AST
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.Types
open LLLang.TypedAST
open LLLang.HMInfer

// ---------- helpers (reused across all tests) ----------

let private inferSrc (src: string) : Result<TypedModule, LLError list> =
    match tokenize src |> Result.bind parseModule with
    | Error e -> failwith $"parse: {e}"
    | Ok m ->
        match elaborate m with
        | Error es -> failwith $"elaborator: {es}"
        | Ok env -> infer m env

let private inferOk (src: string) : TypedModule =
    match inferSrc src with
    | Ok tm -> tm
    | Error es -> failwith $"unexpected hm errors: {es}"

let private inferErrs (src: string) : LLError list =
    match inferSrc src with
    | Ok _ -> []
    | Error es -> es

let private schemeOf (tm: TypedModule) (name: string) : TypeScheme =
    Map.find name tm.Env

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

let private readInvalid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/invalid", name))

// ---------- Task 1: module scaffolding test ----------

[<Fact>]
let ``Types and TypedAST modules compile and Env type exists`` () =
    // Trivial sanity check: the new types must be referenceable.
    let empty : Env = Map.empty
    let m : TypeScheme = { Vars = []; Body = TyName "Int" }
    Assert.Equal(0, Map.count empty)
    Assert.Equal<Ident list>([], m.Vars)
