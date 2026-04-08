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

[<Fact>]
let ``parse char literal`` () =
    Assert.Equal(ELit (LChar 'a'), parseExprStr "'a'")

[<Fact>]
let ``parse char escape newline`` () =
    Assert.Equal(ELit (LChar '\n'), parseExprStr "'\\n'")

[<Fact>]
let ``parse top-level let with char literal`` () =
    let src = "module M\nlet c = 'a'"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DLet("c", ELit (LChar 'a')) -> ()
    | d -> failwith $"Expected DLet c = 'a', got {d}"

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

// --- Declaration tests ---

[<Fact>]
let ``parse simple fn declaration`` () =
    let src = "module M\nfn double(x Int) = x"
    let m = parseModuleStr src
    Assert.Equal(1, m.Decls.Length)
    match fst m.Decls[0] with
    | DFn(sig', _) -> Assert.Equal("double", sig'.Name)
    | d -> failwith $"Expected DFn, got {d}"

[<Fact>]
let ``parse fn with two params`` () =
    let src = "module M\nfn add(a Int)(b Int) = a"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DFn(sig', _) -> Assert.Equal(2, sig'.Params.Length)
    | d -> failwith $"Expected DFn, got {d}"

[<Fact>]
let ``parse top-level let`` () =
    let src = "module M\nlet pi = 3.14"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DLet("pi", ELit (LFloat _)) -> ()
    | d -> failwith $"Expected DLet pi, got {d}"

[<Fact>]
let ``parse sum type`` () =
    let src = "module M\ntype Shape = Circle Float | Empty"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DType("Shape", [], TBSum ctors) -> Assert.Equal(2, ctors.Length)
    | d -> failwith $"Expected DType Shape, got {d}"

[<Fact>]
let ``parse tag declaration`` () =
    let src = "module M\ntag UserId"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DTag "UserId" -> ()
    | d -> failwith $"Expected DTag UserId, got {d}"

[<Fact>]
let ``parse import`` () =
    let src = "module M\nimport Std.List"
    let m = parseModuleStr src
    Assert.Equal<string list list>([["Std"; "List"]], m.Imports)

[<Fact>]
let ``parse module path`` () =
    let src = "module Examples.Basics"
    let m = parseModuleStr src
    Assert.Equal<string list>(["Examples"; "Basics"], m.Path)

// --- Integration: valid example corpus ---

let private readExample name =
    let path = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name)
    System.IO.File.ReadAllText(path)

[<Fact>]
let ``parse 01-basics.lll`` () =
    let src = readExample "01-basics.lll"
    let result = tokenize src |> Result.bind parseModule
    match result with
    | Ok _ -> ()
    | Error e -> failwith $"Failed to parse 01-basics.lll: {e}"

[<Fact>]
let ``parse 02-adts.lll`` () =
    let src = readExample "02-adts.lll"
    let result = tokenize src |> Result.bind parseModule
    match result with
    | Ok _ -> ()
    | Error e -> failwith $"Failed to parse 02-adts.lll: {e}"

[<Fact>]
let ``parse 03-tags.lll`` () =
    let src = readExample "03-tags.lll"
    let result = tokenize src |> Result.bind parseModule
    match result with
    | Ok _ -> ()
    | Error e -> failwith $"Failed to parse 03-tags.lll: {e}"

[<Fact>]
let ``parse 04-traits.lll`` () =
    let src = readExample "04-traits.lll"
    let result = tokenize src |> Result.bind parseModule
    match result with
    | Ok _ -> ()
    | Error e -> failwith $"Failed to parse 04-traits.lll: {e}"

[<Fact>]
let ``parse 05-modules.lll`` () =
    let src = readExample "05-modules.lll"
    let result = tokenize src |> Result.bind parseModule
    match result with
    | Ok _ -> ()
    | Error e -> failwith $"Failed to parse 05-modules.lll: {e}"

// --- Integration: invalid examples must PARSE but type-check fails ---
// (At this stage we only have a parser, not a type checker.
//  Invalid examples are syntactically valid, so they should parse without error.)

let private readInvalidExample name =
    let path = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/invalid", name)
    System.IO.File.ReadAllText(path)

[<Fact>]
let ``E001 parses (type error detected later)`` () =
    let src = readInvalidExample "E001-type-mismatch.lll"
    let result = tokenize src |> Result.bind parseModule
    match result with
    | Ok _ -> ()
    | Error e -> failwith $"E001 should parse cleanly (type error, not syntax): {e}"

[<Fact>]
let ``E002 parses (unbound var detected later)`` () =
    let src = readInvalidExample "E002-unbound-var.lll"
    let result = tokenize src |> Result.bind parseModule
    match result with
    | Ok _ -> ()
    | Error e -> failwith $"E002 should parse cleanly: {e}"

// --- Regression tests for parser bug fixes ---

[<Fact>]
let ``list literal: three elements are separate atoms`` () =
    // [1 2 3] should be EList [ELit 1; ELit 2; ELit 3], not a one-element list with EApp
    match parseExprStr "[1 2 3]" with
    | EList elems -> Assert.Equal(3, elems.Length)
    | e -> failwith $"Expected EList with 3 elems, got {e}"

[<Fact>]
let ``arithmetic precedence: mul binds tighter than add`` () =
    // a + b * c should be a + (b * c), i.e. EApp(EApp("+", a), EApp(EApp("*", b), c))
    match parseExprStr "a + b * c" with
    | EApp(EApp(EVar "+", EVar "a"), EApp(EApp(EVar "*", EVar "b"), EVar "c")) -> ()
    | e -> failwith $"Wrong precedence: {e}"
