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

[<Fact>]
let ``parse tagged literal: 5.0[m] (lowercase tag on float literal)`` () =
    Assert.Equal(ETagged(ELit (LFloat 5.0), "m"), parseExprStr "5.0[m]")

[<Fact>]
let ``parse tagged literal: 42[Years] (TypeId tag on int literal)`` () =
    Assert.Equal(ETagged(ELit (LInt 42L), "Years"), parseExprStr "42[Years]")

// --- Phase 7.2.2: atom[Tag] vs list-literal disambiguation ---
//
// `[X]` is consumed as a tag suffix only when the preceding atom is a literal
// (`ELit _`). For Var / Con / App results the `[X]` is left for the outer
// `parseApp` so it becomes a fresh list-literal argument. This unblocks
// idiomatic application like `cons TPlus [TMinus]` without parens.

[<Fact>]
let ``parse cons TPlus [TMinus] as application with list-literal arg`` () =
    // `cons TPlus [TMinus]` -> EApp(EApp(cons, TPlus), [TMinus])
    let expected =
        EApp(
            EApp(EVar "cons", ECon "TPlus"),
            EList [ECon "TMinus"])
    Assert.Equal(expected, parseExprStr "cons TPlus [TMinus]")

[<Fact>]
let ``parse f [TFoo] as application with single-element list arg`` () =
    let expected = EApp(EVar "f", EList [ECon "TFoo"])
    Assert.Equal(expected, parseExprStr "f [TFoo]")

[<Fact>]
let ``parse f x [1 2 3] as application with three-element list arg`` () =
    let expected =
        EApp(
            EApp(EVar "f", EVar "x"),
            EList [ELit (LInt 1L); ELit (LInt 2L); ELit (LInt 3L)])
    Assert.Equal(expected, parseExprStr "f x [1 2 3]")

[<Fact>]
let ``parse Some [TFoo] as constructor application with list arg`` () =
    // `Some [TFoo]` -> EApp(Some, [TFoo]) (Some is a Con; no tag suffix)
    let expected = EApp(ECon "Some", EList [ECon "TFoo"])
    Assert.Equal(expected, parseExprStr "Some [TFoo]")

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

// --- Phase 7.1.6: multi-line sum type declarations ---

[<Fact>]
let ``parse multi-line sum type with three indented arms`` () =
    let src =
        "module M\n" +
        "type Token =\n" +
        "  | TIdent Str\n" +
        "  | TNum Str\n" +
        "  | TLParen\n"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DType("Token", [], TBSum ctors) ->
        Assert.Equal(3, ctors.Length)
        Assert.Equal("TIdent", fst ctors[0])
        Assert.Equal("TNum",   fst ctors[1])
        Assert.Equal("TLParen", fst ctors[2])
        Assert.Equal(1, (snd ctors[0]).Length)  // TIdent has one Str arg
        Assert.Equal(0, (snd ctors[2]).Length)  // TLParen has no args
    | d -> failwith $"Expected DType Token with TBSum 3 arms, got {d}"

[<Fact>]
let ``parse multi-line sum type with type parameter`` () =
    let src =
        "module M\n" +
        "type Maybe A =\n" +
        "  | Some A\n" +
        "  | None\n"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DType("Maybe", [TPBare "A"], TBSum ctors) ->
        Assert.Equal(2, ctors.Length)
    | d -> failwith $"Expected DType Maybe A multi-line, got {d}"

[<Fact>]
let ``single-line sum type still works (regression)`` () =
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

[<Fact>]
let ``parse 10-multiline-sum.lll`` () =
    let src = readExample "10-multiline-sum.lll"
    let result = tokenize src |> Result.bind parseModule
    match result with
    | Ok _ -> ()
    | Error e -> failwith $"Failed to parse 10-multiline-sum.lll: {e}"

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

// --- Phase 6.7: indented let without 'in' ---

/// Extract fn body expr from a module with a single fn.
let private fnBody (m: LLModule) : Expr =
    match fst m.Decls[0] with
    | DFn(_, body) -> body
    | d -> failwith $"Expected DFn, got {d}"

[<Fact>]
let ``indented let without in: single-line with 'in' baseline`` () =
    let src = "module M\nfn f() = let x = 1 in let y = 2 in x + y"
    let m = parseModuleStr src
    match fnBody m with
    | ELet("x", ELit(LInt 1L), Some(ELet("y", ELit(LInt 2L), Some(_)))) -> ()
    | e -> failwith $"Expected nested ELets, got {e}"

[<Fact>]
let ``indented let without in: multi-line no 'in' keyword`` () =
    // fn f() =
    //   let x = 1
    //   let y = 2
    //   x + y
    let src = "module M\nfn f() =\n  let x = 1\n  let y = 2\n  x + y"
    let m = parseModuleStr src
    match fnBody m with
    | ELet("x", ELit(LInt 1L), Some(ELet("y", ELit(LInt 2L), Some(_)))) -> ()
    | e -> failwith $"Expected nested ELets, got {e}"

[<Fact>]
let ``indented let inside else branch`` () =
    // fn f() =
    //   if true then 1
    //   else
    //     let x = 2
    //     let y = 3
    //     x + y
    let src = "module M\nfn f() =\n  if true then 1\n  else\n    let x = 2\n    let y = 3\n    x + y"
    let m = parseModuleStr src
    match fnBody m with
    | EIf(_, _, ELet("x", ELit(LInt 2L), Some(ELet("y", ELit(LInt 3L), Some _)))) -> ()
    | e -> failwith $"Expected EIf with ELet-chain else, got {e}"

[<Fact>]
let ``module-level multiple lets still parse as siblings`` () =
    // Regression: two top-level DLet decls, NOT a nested let
    let src = "module M\nlet a = 1\nlet b = 2"
    let m = parseModuleStr src
    Assert.Equal(2, m.Decls.Length)
    match fst m.Decls[0] with
    | DLet("a", ELit(LInt 1L)) -> ()
    | d -> failwith $"Expected DLet a = 1, got {d}"
    match fst m.Decls[1] with
    | DLet("b", ELit(LInt 2L)) -> ()
    | d -> failwith $"Expected DLet b = 2, got {d}"

// --- Phase 6.8: tuple patterns ---

[<Fact>]
let ``parse tuple pattern: (a, b)`` () =
    // match p with | (a, b) -> a
    let src = "module M\nfn fst(p) =\n  | (a, b) -> a"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DFn(_, EMatch [(PTuple [PVar "a"; PVar "b"], EVar "a")]) -> ()
    | d -> failwith $"Expected DFn with match on PTuple [a; b], got {d}"

[<Fact>]
let ``parse tuple pattern: (a, _)`` () =
    let src = "module M\nfn fst(p) =\n  | (a, _) -> a"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DFn(_, EMatch [(PTuple [PVar "a"; PWild], EVar "a")]) -> ()
    | d -> failwith $"Expected DFn with match on PTuple [a; _], got {d}"

[<Fact>]
let ``parse tuple pattern: (a, b, c) three elements`` () =
    let src = "module M\nfn f(p) =\n  | (a, b, c) -> a"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DFn(_, EMatch [(PTuple [PVar "a"; PVar "b"; PVar "c"], EVar "a")]) -> ()
    | d -> failwith $"Expected DFn with match on PTuple with 3 vars, got {d}"

[<Fact>]
let ``parse single-parenthesised pattern stays as pattern (no PTuple)`` () =
    // (a) is NOT a tuple, it's just a in parens
    let src = "module M\nfn id2(p) =\n  | (a) -> a"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DFn(_, EMatch [(PVar "a", EVar "a")]) -> ()
    | d -> failwith $"Expected DFn with match on PVar a, got {d}"

// --- Phase 7.1.5: cons patterns ---

[<Fact>]
let ``parse cons pattern: h :: t`` () =
    let src = "module M\nfn first(xs) =\n  | h :: t -> h"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DFn(_, EMatch [(PCons(PVar "h", PVar "t"), EVar "h")]) -> ()
    | d -> failwith $"Expected DFn with match on PCons(h, t), got {d}"

[<Fact>]
let ``parse cons pattern: a :: b :: rest is right-associative`` () =
    let src = "module M\nfn f(xs) =\n  | a :: b :: rest -> a"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DFn(_, EMatch [(PCons(PVar "a", PCons(PVar "b", PVar "rest")), EVar "a")]) -> ()
    | d -> failwith $"Expected nested PCons, got {d}"

[<Fact>]
let ``parse cons pattern with wildcard tail`` () =
    let src = "module M\nfn first(xs) =\n  | x :: _ -> x"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DFn(_, EMatch [(PCons(PVar "x", PWild), EVar "x")]) -> ()
    | d -> failwith $"Expected DFn with PCons(x, _), got {d}"

// --- Phase 7.1.5: cons expressions ---

[<Fact>]
let ``parse cons expression: 1 :: rest`` () =
    match parseExprStr "1 :: rest" with
    | ECons(ELit (LInt 1L), EVar "rest") -> ()
    | e -> failwith $"Expected ECons(1, rest), got {e}"

[<Fact>]
let ``parse cons expression: 1 :: 2 :: xs is right-associative`` () =
    match parseExprStr "1 :: 2 :: xs" with
    | ECons(ELit (LInt 1L), ECons(ELit (LInt 2L), EVar "xs")) -> ()
    | e -> failwith $"Expected nested ECons, got {e}"

[<Fact>]
let ``parse cons expression with let: let xs = 1 :: 2 :: rest`` () =
    match parseExprStr "let xs = 1 :: 2 :: rest" with
    | ELet("xs", ECons(ELit (LInt 1L), ECons(ELit (LInt 2L), EVar "rest")), None) -> ()
    | e -> failwith $"Expected ELet xs = ECons-chain, got {e}"

[<Fact>]
let ``parse cons precedence: a + 1 :: rest is (a + 1) :: rest`` () =
    // + binds tighter than ::
    match parseExprStr "a + 1 :: rest" with
    | ECons(EApp(EApp(EVar "+", EVar "a"), ELit (LInt 1L)), EVar "rest") -> ()
    | e -> failwith $"Expected ECons((a+1), rest), got {e}"

// --- Phase 7.1.5: match as expression ---

[<Fact>]
let ``parse match expression: match x with`` () =
    match parseExprStr "match x with | 0 -> \"zero\" | _ -> \"other\"" with
    | EMatchOf(EVar "x",
               [(PLit (LInt 0L), ELit (LStr "zero"));
                (PWild, ELit (LStr "other"))]) -> ()
    | e -> failwith $"Expected EMatchOf, got {e}"

[<Fact>]
let ``parse match expression in let binding`` () =
    let src = "let v = match x with | 0 -> 1 | _ -> 2"
    match parseExprStr src with
    | ELet("v",
           EMatchOf(EVar "x",
                    [(PLit (LInt 0L), ELit (LInt 1L));
                     (PWild, ELit (LInt 2L))]),
           None) -> ()
    | e -> failwith $"Expected ELet v = EMatchOf, got {e}"

[<Fact>]
let ``parse match expression with cons pattern`` () =
    let src = "match xs with | h :: t -> h | _ -> 0"
    match parseExprStr src with
    | EMatchOf(EVar "xs",
               [(PCons(PVar "h", PVar "t"), EVar "h");
                (PWild, ELit (LInt 0L))]) -> ()
    | e -> failwith $"Expected EMatchOf with PCons branch, got {e}"

// --- Phase 7.1.6: let pattern destructuring ---

[<Fact>]
let ``parse let with tuple pattern in expression`` () =
    // let (a, b) = pair in a + b
    match parseExprStr "let (a, b) = pair in a + b" with
    | ELetPat(PTuple [PVar "a"; PVar "b"], EVar "pair", Some _) -> ()
    | e -> failwith $"Expected ELetPat (a, b) = pair in ..., got {e}"

[<Fact>]
let ``parse let with tuple pattern, no in`` () =
    match parseExprStr "let (a, b) = pair" with
    | ELetPat(PTuple [PVar "a"; PVar "b"], EVar "pair", None) -> ()
    | e -> failwith $"Expected ELetPat (a, b) = pair, got {e}"

[<Fact>]
let ``parse let with wildcard pattern`` () =
    match parseExprStr "let _ = e" with
    | ELetPat(PWild, EVar "e", None) -> ()
    | e -> failwith $"Expected ELetPat PWild = e, got {e}"

[<Fact>]
let ``parse let with simple var still produces ELet (not ELetPat)`` () =
    // Regression: a single PVar should fall back to the existing ELet form.
    match parseExprStr "let x = 5" with
    | ELet("x", ELit (LInt 5L), None) -> ()
    | e -> failwith $"Expected ELet x = 5, got {e}"

[<Fact>]
let ``parse top-level let with tuple pattern produces DLetPat`` () =
    let src = "module M\nlet (a, b) = pair"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DLetPat(PTuple [PVar "a"; PVar "b"], EVar "pair") -> ()
    | d -> failwith $"Expected DLetPat (a, b) = pair, got {d}"

[<Fact>]
let ``parse top-level let with simple var still produces DLet`` () =
    let src = "module M\nlet pi = 3.14"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DLet("pi", ELit (LFloat _)) -> ()
    | d -> failwith $"Expected DLet pi (regression), got {d}"

// --- Phase 7.2.1: surface tuple literal expressions ---

[<Fact>]
let ``parse tuple literal: (1, 2) produces ETuple of two ints`` () =
    match parseExprStr "(1, 2)" with
    | ETuple [ELit (LInt 1L); ELit (LInt 2L)] -> ()
    | e -> failwith $"Expected ETuple [1; 2], got {e}"

[<Fact>]
let ``parse single-parenthesised expression stays as inner expr (no ETuple)`` () =
    // (e) is just grouping, NOT a 1-tuple.
    match parseExprStr "(42)" with
    | ELit (LInt 42L) -> ()
    | e -> failwith $"Expected ELit 42, got {e}"

[<Fact>]
let ``parse tuple literal: (1, 2, 3) three elements`` () =
    match parseExprStr "(1, 2, 3)" with
    | ETuple [ELit (LInt 1L); ELit (LInt 2L); ELit (LInt 3L)] -> ()
    | e -> failwith $"Expected ETuple [1; 2; 3], got {e}"

[<Fact>]
let ``parse tuple literal: heterogeneous (1, "x")`` () =
    match parseExprStr "(1, \"x\")" with
    | ETuple [ELit (LInt 1L); ELit (LStr "x")] -> ()
    | e -> failwith $"Expected ETuple [1; \"x\"], got {e}"

[<Fact>]
let ``parse let with tuple literal RHS`` () =
    match parseExprStr "let p = (1, 2)" with
    | ELet("p", ETuple [ELit (LInt 1L); ELit (LInt 2L)], None) -> ()
    | e -> failwith $"Expected ELet p = ETuple [1; 2], got {e}"

[<Fact>]
let ``parse let-pat with tuple literal RHS round-trip`` () =
    // The classic "construct + destructure" pattern.
    match parseExprStr "let (a, b) = (1, 2) in a" with
    | ELetPat(PTuple [PVar "a"; PVar "b"],
              ETuple [ELit (LInt 1L); ELit (LInt 2L)],
              Some (EVar "a")) -> ()
    | e -> failwith $"Expected ELetPat (a, b) = (1, 2) in a, got {e}"

[<Fact>]
let ``parse trailing comma in tuple literal is rejected`` () =
    // (a,) — trailing comma not accepted (we go strict, not Python-style).
    match tokenize "let p = (1,)" with
    | Error e -> failwith $"Lex error: {e}"
    | Ok toks ->
        match parseExpr toks with
        | Ok (e, _) -> failwith $"Expected parse error for (1,), got {e}"
        | Error _ -> ()

[<Fact>]
let ``parse empty parens () still works for fn main`` () =
    // Regression: () remains valid for fn main() decl form.
    let src = "module M\nfn main() = 0"
    let m = parseModuleStr src
    match fst m.Decls[0] with
    | DFn(s, ELit (LInt 0L)) when s.Name = "main" && List.isEmpty s.Params -> ()
    | d -> failwith $"Expected DFn main() = 0, got {d}"

[<Fact>]
let ``parse fn call with tuple argument: f (1, 2)`` () =
    // Previously a parse error, now parses as f applied to tuple (1, 2).
    match parseExprStr "f (1, 2)" with
    | EApp(EVar "f", ETuple [ELit (LInt 1L); ELit (LInt 2L)]) -> ()
    | e -> failwith $"Expected EApp(f, ETuple [1; 2]), got {e}"

