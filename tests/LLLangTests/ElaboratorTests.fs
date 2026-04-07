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

// --- E001/E004/E005: Application type checking ---

[<Fact>]
let ``E001 type mismatch Int where Str expected`` () =
    let src = "module M\nfn f(x Str) Str = x\nlet bad = f 42"
    let errs = elab src
    Assert.Contains(E001, errs |> List.map _.Code)

[<Fact>]
let ``E004 unit mismatch Float[kg] where Float[m] expected`` () =
    // mk_kg returns Float[kg]; f expects Float[m] — unit mismatch → E004
    let src = "module M\nunit m\nunit kg\nfn mk_kg(x Float) Float[kg] = x\nfn f(x Float[m]) Float = x\nlet bad = f (mk_kg 1.0)"
    let errs = elab src
    Assert.Contains(E004, errs |> List.map _.Code)

[<Fact>]
let ``E005 tag violation untagged Str where Str[UserId] expected`` () =
    let src = "module M\ntag UserId\nfn f(x Str[UserId]) Str = x\nlet bad = f \"raw\""
    let errs = elab src
    Assert.Contains(E005, errs |> List.map _.Code)

[<Fact>]
let ``no error when correct tag applied`` () =
    let src = "module M\ntag UserId\nfn f(x Str[UserId]) Str = x\nlet ok = f \"id\"[UserId]"
    Assert.Empty(elab src)

[<Fact>]
let ``no error when TyVar param accepts anything`` () =
    let src = "module M\nfn double(x Int) = x\nlet y = double 5"
    Assert.Empty(elab src)
