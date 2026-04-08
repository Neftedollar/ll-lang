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

// --- Char literals (Phase 6.7) ---

[<Fact>]
let ``char literal: plain a`` () =
    Assert.Equal<Token list>([CharLit 'a'], toks "'a'")

[<Fact>]
let ``char literal: uppercase A`` () =
    Assert.Equal<Token list>([CharLit 'A'], toks "'A'")

[<Fact>]
let ``char literal: digit`` () =
    Assert.Equal<Token list>([CharLit '0'], toks "'0'")

[<Fact>]
let ``char literal: paren`` () =
    Assert.Equal<Token list>([CharLit '('], toks "'('")

[<Fact>]
let ``char literal: space`` () =
    Assert.Equal<Token list>([CharLit ' '], toks "' '")

[<Fact>]
let ``char literal: escape newline`` () =
    Assert.Equal<Token list>([CharLit '\n'], toks "'\\n'")

[<Fact>]
let ``char literal: escape tab`` () =
    Assert.Equal<Token list>([CharLit '\t'], toks "'\\t'")

[<Fact>]
let ``char literal: escape backslash`` () =
    Assert.Equal<Token list>([CharLit '\\'], toks "'\\\\'")

[<Fact>]
let ``char literal: escape single quote`` () =
    Assert.Equal<Token list>([CharLit '\''], toks "'\\''")

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

// --- INDENT/DEDENT tests ---

/// Helper: return ALL token types including Newline/Indent/Dedent, filter only Eof.
let allToks src =
    match tokenize src with
    | Ok ts -> ts |> List.map _.Token |> List.filter ((<>) Eof)
    | Error e -> failwith e

[<Fact>]
let ``indented block: INDENT after = newline`` () =
    let src = "fn f =\n  42"
    let ts = allToks src
    Assert.Contains(Indent, ts)
    Assert.Contains(Dedent, ts)

[<Fact>]
let ``INDENT appears before first indented token`` () =
    let src = "fn f =\n  42"
    let ts = allToks src
    let indentIdx = ts |> List.findIndex ((=) Indent)
    let intIdx = ts |> List.findIndex ((=) (IntLit 42L))
    Assert.True(indentIdx < intIdx, "Indent must precede the indented token")

[<Fact>]
let ``DEDENT appears after indented block ends`` () =
    let src = "fn f =\n  42\nfn g = 1"
    let ts = allToks src
    Assert.Contains(Dedent, ts)

[<Fact>]
let ``nested indent: two levels`` () =
    let src = "fn f =\n  fn g =\n    42"
    let ts = allToks src
    let indentCount = ts |> List.filter ((=) Indent) |> List.length
    let dedentCount = ts |> List.filter ((=) Dedent) |> List.length
    Assert.Equal(2, indentCount)
    Assert.Equal(2, dedentCount)

[<Fact>]
let ``blank lines do not affect indentation`` () =
    let src = "fn f =\n\n  42"
    let ts = allToks src
    Assert.Contains(Indent, ts)

[<Fact>]
let ``comment-only line does not affect indentation`` () =
    let src = "fn f =\n  -- comment\n  42"
    let ts = allToks src
    let indentCount = ts |> List.filter ((=) Indent) |> List.length
    Assert.Equal(1, indentCount)

[<Fact>]
let ``match branches generate INDENT and DEDENT`` () =
    let src = "fn area s =\n  | Circle r -> r\n  | Empty -> 0"
    let ts = allToks src
    Assert.Contains(Indent, ts)
    Assert.Contains(Dedent, ts)
