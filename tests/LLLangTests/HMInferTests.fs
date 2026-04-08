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

// --- Task 2: Substitution + fresh vars + generalize/instantiate ---

[<Fact>]
let ``freshVar increments and produces $N names`` () =
    let s = newFreshState ()
    let a = freshVar s
    let b = freshVar s
    Assert.Equal(TyVar "$0", a)
    Assert.Equal(TyVar "$1", b)

[<Fact>]
let ``applyType replaces flexible var`` () =
    let s : Subst = Map.ofList ["$0", TyName "Int"]
    let t = TyFn(TyVar "$0", TyName "Bool")
    Assert.Equal(TyFn(TyName "Int", TyName "Bool"), applyType s t)

[<Fact>]
let ``applyType does not replace rigid var`` () =
    let s : Subst = Map.ofList ["a", TyName "Int"]
    let t = TyVar "a"
    Assert.Equal(TyVar "a", applyType s t)

[<Fact>]
let ``applyType is recursive through TyFn and TyApp`` () =
    let s : Subst = Map.ofList ["$0", TyName "Int"; "$1", TyName "Str"]
    let t = TyApp(TyVar "$0", TyFn(TyVar "$1", TyVar "$0"))
    Assert.Equal(TyApp(TyName "Int", TyFn(TyName "Str", TyName "Int")), applyType s t)

[<Fact>]
let ``compose applies s2 first then s1`` () =
    let s1 : Subst = Map.ofList ["$1", TyName "Int"]
    let s2 : Subst = Map.ofList ["$0", TyVar "$1"]
    let composed = compose s1 s2
    Assert.Equal(TyName "Int", applyType composed (TyVar "$0"))

[<Fact>]
let ``ftvType collects only flexible vars`` () =
    let t = TyFn(TyVar "$0", TyFn(TyVar "a", TyVar "$1"))
    let ftvs = ftvType t
    Assert.True(Set.contains "$0" ftvs)
    Assert.True(Set.contains "$1" ftvs)
    Assert.False(Set.contains "a" ftvs)

[<Fact>]
let ``ftvScheme removes quantified vars from body free vars`` () =
    let sch = { Vars = ["$0"]; Body = TyFn(TyVar "$0", TyVar "$1") }
    let ftvs = ftvScheme sch
    Assert.False(Set.contains "$0" ftvs)
    Assert.True(Set.contains "$1" ftvs)

[<Fact>]
let ``generalize quantifies free vars not in env`` () =
    let envScheme = { Vars = []; Body = TyVar "$5" }
    let env : Env = Map.ofList ["fixed", envScheme]
    let ty = TyFn(TyVar "$0", TyVar "$5")
    let sch = generalize env ty
    Assert.Equal<Ident list>(["$0"], sch.Vars)

[<Fact>]
let ``instantiate replaces each quantified var with a fresh flexible var`` () =
    let fs = newFreshState ()
    let sch = { Vars = ["a"]; Body = TyFn(TyVar "a", TyVar "a") }
    let t = instantiate fs sch
    match t with
    | TyFn(TyVar v1, TyVar v2) ->
        Assert.Equal(v1, v2)
        Assert.StartsWith("$", v1)
    | _ -> failwith $"expected TyFn of same fresh var, got {t}"

[<Fact>]
let ``instantiate uses different fresh vars for different quantifiers`` () =
    let fs = newFreshState ()
    let sch = { Vars = ["a"; "b"]; Body = TyFn(TyVar "a", TyVar "b") }
    match instantiate fs sch with
    | TyFn(TyVar v1, TyVar v2) ->
        Assert.NotEqual<string>(v1, v2)
    | t -> failwith $"expected TyFn, got {t}"

[<Fact>]
let ``fromElaboratorEnv turns declared type vars into quantifiers`` () =
    let e3 : LLLang.Elaborator.TypeEnv = Map.ofList ["id", TyFn(TyVar "A", TyVar "A")]
    let env = fromElaboratorEnv e3
    let sch = Map.find "id" env
    Assert.Contains("A", sch.Vars)
