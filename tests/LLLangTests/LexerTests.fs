module LLLang.Tests.LexerTests

open Xunit
open LLLang.Token
open LLLang.Lexer

/// Helper: tokenize and return token types, filtering out Newline and Eof.
let toks src =
    match tokenize src with
    | Ok ts -> ts |> List.map _.Token |> List.filter (fun t -> t <> Newline && t <> Eof)
    | Error e -> failwith e

[<Fact>]
let ``keyword fn`` () =
    Assert.Equal<Token list>([KwFn], toks "fn")

[<Fact>]
let ``keyword let`` () =
    Assert.Equal<Token list>([KwLet], toks "let")

[<Fact>]
let ``all keywords recognized`` () =
    let kws = "fn let in type tag unit trait impl import export module if then else"
    let expected = [
        KwFn; KwLet; KwIn; KwType; KwTag; KwUnit
        KwTrait; KwImpl; KwImport; KwExport; KwModule
        KwIf; KwThen; KwElse
    ]
    Assert.Equal<Token list>(expected, toks kws)

[<Fact>]
let ``lowercase identifier`` () =
    Assert.Equal<Token list>([Ident "foo"], toks "foo")

[<Fact>]
let ``uppercase TypeId`` () =
    Assert.Equal<Token list>([TypeId "Maybe"], toks "Maybe")

[<Fact>]
let ``identifier with underscores and digits`` () =
    Assert.Equal<Token list>([Ident "my_var2"], toks "my_var2")

[<Fact>]
let ``int literal`` () =
    Assert.Equal<Token list>([IntLit 42L], toks "42")

[<Fact>]
let ``float literal`` () =
    Assert.Equal<Token list>([FloatLit 3.14], toks "3.14")

[<Fact>]
let ``string literal`` () =
    Assert.Equal<Token list>([StrLit "hello"], toks "\"hello\"")

[<Fact>]
let ``string with escape`` () =
    Assert.Equal<Token list>([StrLit "a\nb"], toks "\"a\\nb\"")

[<Fact>]
let ``bool true and false`` () =
    Assert.Equal<Token list>([KwTrue; KwFalse], toks "true false")

[<Fact>]
let ``arrow operator`` () =
    Assert.Equal<Token list>([Arrow], toks "->")

[<Fact>]
let ``two-char operators`` () =
    Assert.Equal<Token list>([Le; Ge; EqEq; Neq], toks "<= >= == !=")

[<Fact>]
let ``single-char operators`` () =
    Assert.Equal<Token list>([Plus; Minus; Star; Slash; Caret; Lt; Gt; Eq], toks "+ - * / ^ < > =")

[<Fact>]
let ``punctuation`` () =
    Assert.Equal<Token list>([LParen; RParen; LBrack; RBrack; Comma; Dot; Colon; Bar], toks "( ) [ ] , . : |")

[<Fact>]
let ``backslash and underscore`` () =
    Assert.Equal<Token list>([Backslash; Underscore], toks "\\ _")

[<Fact>]
let ``comment stripped`` () =
    Assert.Equal<Token list>([KwFn; Ident "f"; Eq; IntLit 1L], toks "fn f = 1 -- comment")

[<Fact>]
let ``comment-only line is empty`` () =
    Assert.Equal<Token list>([], toks "-- just a comment")

[<Fact>]
let ``function declaration tokenized`` () =
    let src = "fn add(a Int)(b Int) Int = a + b"
    let expected = [
        KwFn; Ident "add"
        LParen; Ident "a"; TypeId "Int"; RParen
        LParen; Ident "b"; TypeId "Int"; RParen
        TypeId "Int"; Eq
        Ident "a"; Plus; Ident "b"
    ]
    Assert.Equal<Token list>(expected, toks src)
