module LLLang.Tests.ElaboratorTests

open System.IO
open Xunit
open LLLang.Lexer
open LLLang.Parser
open LLLang.AST
open LLLang.Elaborator

/// Lex + parse + elaborate. Returns error list (empty = clean).
let elab src =
    match tokenize src |> Result.bind parseModule with
    | Error e -> failwith $"parse: {e}"
    | Ok m ->
        match elaborate m with
        | Ok _ -> []
        | Error errs -> errs

/// Lex + parse + elaborate. Returns TypeEnv or fails.
let elabOk src =
    match tokenize src |> Result.bind parseModule with
    | Error e -> failwith $"parse: {e}"
    | Ok m ->
        match elaborate m with
        | Ok env -> env
        | Error errs -> failwith $"unexpected errors: {errs}"

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

let private readInvalid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/invalid", name))

// --- Pass 1: TypeEnv collection ---

[<Fact>]
let ``TypeEnv contains declared single-param fn`` () =
    let env = elabOk "module M\nfn f(x Int) Int = x"
    Assert.True(Map.containsKey "f" env)
    Assert.Equal(TyFn(TyName "Int", TyName "Int"), env["f"])

[<Fact>]
let ``TypeEnv contains declared multi-param fn`` () =
    let env = elabOk "module M\nfn add(x Int)(y Int) Int = x"
    Assert.Equal(TyFn(TyName "Int", TyFn(TyName "Int", TyName "Int")), env["add"])

[<Fact>]
let ``TypeEnv fn with no return type uses TyVar unknown`` () =
    let env = elabOk "module M\nfn double(x Int) = x"
    Assert.Equal(TyFn(TyName "Int", TyVar "?"), env["double"])

[<Fact>]
let ``TypeEnv contains let binding with int literal`` () =
    let env = elabOk "module M\nlet x = 42"
    Assert.Equal(TyName "Int", env["x"])

[<Fact>]
let ``TypeEnv contains let binding with float literal`` () =
    let env = elabOk "module M\nlet pi = 3.14"
    Assert.Equal(TyName "Float", env["pi"])

[<Fact>]
let ``TypeEnv contains let binding with str literal`` () =
    let env = elabOk "module M\nlet s = \"hello\""
    Assert.Equal(TyName "Str", env["s"])

[<Fact>]
let ``TypeEnv contains type constructors`` () =
    let env = elabOk "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    Assert.Equal(TyFn(TyName "Float", TyName "Shape"), env["Circle"])
    Assert.Equal(TyFn(TyName "Float", TyFn(TyName "Float", TyName "Shape")), env["Rect"])
    Assert.Equal(TyName "Shape", env["Empty"])

[<Fact>]
let ``TypeEnv tag and unit declarations produce no errors`` () =
    Assert.Empty(elab "module M\ntag UserId\nunit m")

// --- E002: Unbound variable / constructor ---

[<Fact>]
let ``E002 unbound var in fn body`` () =
    let errs = elab "module M\nfn f = undefinedVar"
    Assert.Contains(E002, errs |> List.map _.Code)

[<Fact>]
let ``E002 unbound constructor`` () =
    let errs = elab "module M\nfn f = UnknownCon"
    Assert.Contains(E002, errs |> List.map _.Code)

[<Fact>]
let ``no E002 for declared fn param in body`` () =
    Assert.Empty(elab "module M\nfn f(x Int) Int = x")

[<Fact>]
let ``no E002 for fn calling another declared fn`` () =
    Assert.Empty(elab "module M\nfn double(x Int) = x\nfn quad(x Int) = double x")

[<Fact>]
let ``valid module has no errors`` () =
    Assert.Empty(elab "module M\nfn f(x Int) Int = x")
