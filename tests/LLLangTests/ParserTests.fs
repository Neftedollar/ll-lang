module LLLang.Tests.ParserTests

open Xunit
open LLLang.Token
open LLLang.Lexer
open LLLang.AST
open LLLang.Parser

/// Helper: lex + parse a single expression string.
let parseExprStr src =
    match tokenize src with
    | Error e -> failwith $"Lex error: {e}"
    | Ok toks ->
        match parseExpr toks with
        | Ok (expr, _) -> expr
        | Error e -> failwith $"Parse error: {e}"

/// Helper: lex + parse a full module string.
let parseModuleStr src =
    match tokenize src with
    | Error e -> failwith $"Lex error: {e}"
    | Ok toks ->
        match parseModule toks with
        | Ok m -> m
        | Error e -> failwith $"Parse error: {e}"

// --- Literals ---

[<Fact>]
let ``parse int literal`` () =
    Assert.Equal(ELit (LInt 42L), parseExprStr "42")

[<Fact>]
let ``parse float literal`` () =
    Assert.Equal(ELit (LFloat 3.14), parseExprStr "3.14")

[<Fact>]
let ``parse string literal`` () =
    Assert.Equal(ELit (LStr "hello"), parseExprStr "\"hello\"")

[<Fact>]
let ``parse bool true`` () =
    Assert.Equal(ELit (LBool true), parseExprStr "true")

// --- Variables and Constructors ---

[<Fact>]
let ``parse variable`` () =
    Assert.Equal(EVar "foo", parseExprStr "foo")

[<Fact>]
let ``parse constructor`` () =
    Assert.Equal(ECon "Some", parseExprStr "Some")

// --- Function Application (juxtaposition) ---

[<Fact>]
let ``parse application: f x`` () =
    Assert.Equal(EApp(EVar "f", EVar "x"), parseExprStr "f x")

[<Fact>]
let ``parse application: f x y is left-assoc`` () =
    Assert.Equal(EApp(EApp(EVar "f", EVar "x"), EVar "y"), parseExprStr "f x y")

// --- Pipe ---

[<Fact>]
let ``parse pipe: x -> f`` () =
    Assert.Equal(EPipe(EVar "x", EVar "f"), parseExprStr "x -> f")

[<Fact>]
let ``parse pipe chain: x -> f -> g`` () =
    // left-associative: (x -> f) -> g
    Assert.Equal(EPipe(EPipe(EVar "x", EVar "f"), EVar "g"), parseExprStr "x -> f -> g")

// --- Lambda ---

[<Fact>]
let ``parse lambda: \x. x`` () =
    Assert.Equal(ELam(["x"], EVar "x"), parseExprStr "\\x. x")

[<Fact>]
let ``parse lambda two params: \x y. x`` () =
    Assert.Equal(ELam(["x"; "y"], EVar "x"), parseExprStr "\\x y. x")

// --- If/Then/Else ---

[<Fact>]
let ``parse if expression`` () =
    let expected = EIf(EVar "b", ELit (LInt 1L), ELit (LInt 2L))
    Assert.Equal(expected, parseExprStr "if b then 1 else 2")

// --- Let ---

[<Fact>]
let ``parse let without in`` () =
    Assert.Equal(ELet("x", ELit (LInt 5L), None), parseExprStr "let x = 5")

[<Fact>]
let ``parse let with in`` () =
    let expected = ELet("x", ELit (LInt 5L), Some (EVar "x"))
    Assert.Equal(expected, parseExprStr "let x = 5 in x")

// --- Tagged literal ---

[<Fact>]
let ``parse tagged literal: "id"[UserId]`` () =
    Assert.Equal(ETagged(ELit (LStr "user-42"), "UserId"), parseExprStr "\"user-42\"[UserId]")

// --- Arithmetic ---

[<Fact>]
let ``parse addition`` () =
    Assert.Equal(EApp(EApp(EVar "+", EVar "a"), EVar "b"), parseExprStr "a + b")
