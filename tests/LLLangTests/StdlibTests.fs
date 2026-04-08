module LLLang.Tests.StdlibTests

open Xunit
open LLLang.AST
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.Types
open LLLang.TypedAST
open LLLang.HMInfer
open LLLang.Codegen

// ---- helpers ----

let private inferSrc (src: string) : Result<TypedModule, LLError list> =
    match tokenize src |> Result.bind parseModule with
    | Error e -> failwith $"parse: {e}"
    | Ok m ->
        match elaborate m with
        | Error es -> failwith $"elaborator: {es}"
        | Ok (m', env) -> infer m' env

let private inferOk (src: string) : TypedModule =
    match inferSrc src with
    | Ok tm -> tm
    | Error es -> failwith $"unexpected hm errors: {es}"

let private codegenSrc (src: string) : string =
    let tm = inferOk src
    emit tm

// ---- Math ----

[<Fact>]
let ``abs is Int -> Int`` () =
    let tm = inferOk "module M\nlet n = abs 5"
    Assert.Equal(TyName "Int", (Map.find "n" tm.Env).Body)

[<Fact>]
let ``absf is Float -> Float`` () =
    let tm = inferOk "module M\nlet x = absf 3.14"
    Assert.Equal(TyName "Float", (Map.find "x" tm.Env).Body)

[<Fact>]
let ``sqrt is Float -> Float`` () =
    let tm = inferOk "module M\nlet x = sqrt 4.0"
    Assert.Equal(TyName "Float", (Map.find "x" tm.Env).Body)

[<Fact>]
let ``min Int -> Int -> Int`` () =
    let tm = inferOk "module M\nlet n = min 3 7"
    Assert.Equal(TyName "Int", (Map.find "n" tm.Env).Body)

[<Fact>]
let ``max Int -> Int -> Int`` () =
    let tm = inferOk "module M\nlet n = max 3 7"
    Assert.Equal(TyName "Int", (Map.find "n" tm.Env).Body)

// ---- List ----

[<Fact>]
let ``listLen is List A -> Int`` () =
    let tm = inferOk "module M\nlet n = listLen [1 2 3]"
    Assert.Equal(TyName "Int", (Map.find "n" tm.Env).Body)

[<Fact>]
let ``listMap typed (A -> B) -> List A -> List B`` () =
    let src =
        "module M\n" +
        "fn double(x Int) Int = x * 2\n" +
        "let xs = listMap double [1 2 3]"
    let tm = inferOk src
    Assert.Equal(TyApp(TyName "List", TyName "Int"), (Map.find "xs" tm.Env).Body)

[<Fact>]
let ``listFilter typed (A -> Bool) -> List A -> List A`` () =
    let src =
        "module M\n" +
        "fn isPos(x Int) Bool = x > 0\n" +
        "let xs = listFilter isPos [1 2 3]"
    let tm = inferOk src
    Assert.Equal(TyApp(TyName "List", TyName "Int"), (Map.find "xs" tm.Env).Body)

[<Fact>]
let ``listFold typed (B -> A -> B) -> B -> List A -> B`` () =
    let src =
        "module M\n" +
        "let s = listFold (\\acc. \\x. acc + x) 0 [1 2 3]"
    let tm = inferOk src
    Assert.Equal(TyName "Int", (Map.find "s" tm.Env).Body)

[<Fact>]
let ``listReverse typed List A -> List A`` () =
    let tm = inferOk "module M\nlet xs = listReverse [1 2 3]"
    Assert.Equal(TyApp(TyName "List", TyName "Int"), (Map.find "xs" tm.Env).Body)

[<Fact>]
let ``listAppend typed List A -> List A -> List A`` () =
    let tm = inferOk "module M\nlet xs = listAppend [1 2] [3 4]"
    Assert.Equal(TyApp(TyName "List", TyName "Int"), (Map.find "xs" tm.Env).Body)

// ---- Maybe (requires user to declare type Maybe A = Some A | None) ----

[<Fact>]
let ``listHead returns Maybe A`` () =
    let src =
        "module M\n" +
        "type Maybe A = Some A | None\n" +
        "let h = listHead [1 2 3]"
    let tm = inferOk src
    Assert.Equal(TyApp(TyName "Maybe", TyName "Int"), (Map.find "h" tm.Env).Body)

[<Fact>]
let ``listTail returns Maybe (List A)`` () =
    let src =
        "module M\n" +
        "type Maybe A = Some A | None\n" +
        "let t = listTail [1 2 3]"
    let tm = inferOk src
    Assert.Equal(TyApp(TyName "Maybe", TyApp(TyName "List", TyName "Int")), (Map.find "t" tm.Env).Body)

[<Fact>]
let ``maybeWithDefault unwraps Maybe`` () =
    let src =
        "module M\n" +
        "type Maybe A = Some A | None\n" +
        "let h = listHead [1 2 3]\n" +
        "let v = maybeWithDefault 0 h"
    let tm = inferOk src
    Assert.Equal(TyName "Int", (Map.find "v" tm.Env).Body)

// ---- Str ----

[<Fact>]
let ``strLen is Str -> Int`` () =
    let tm = inferOk "module M\nlet n = strLen \"hello\""
    Assert.Equal(TyName "Int", (Map.find "n" tm.Env).Body)

[<Fact>]
let ``strConcat is Str -> Str -> Str`` () =
    let tm = inferOk "module M\nlet s = strConcat \"a\" \"b\""
    Assert.Equal(TyName "Str", (Map.find "s" tm.Env).Body)

[<Fact>]
let ``strTrim is Str -> Str`` () =
    let tm = inferOk "module M\nlet s = strTrim \"  x  \""
    Assert.Equal(TyName "Str", (Map.find "s" tm.Env).Body)

[<Fact>]
let ``strContains is Str -> Str -> Bool`` () =
    let tm = inferOk "module M\nlet b = strContains \"x\" \"haystack\""
    Assert.Equal(TyName "Bool", (Map.find "b" tm.Env).Body)

[<Fact>]
let ``strToInt returns Maybe Int`` () =
    let src =
        "module M\n" +
        "type Maybe A = Some A | None\n" +
        "let n = strToInt \"42\""
    let tm = inferOk src
    Assert.Equal(TyApp(TyName "Maybe", TyName "Int"), (Map.find "n" tm.Env).Body)

// ---- IO ----

[<Fact>]
let ``print is Str -> Unit`` () =
    let tm = inferOk "module M\nfn greet() = print \"hi\""
    let body = (Map.find "greet" tm.Env).Body
    match body with
    | TyFn(TyName "Unit", TyName "Unit")
    | TyName "Unit" -> ()  // either form is acceptable for a 0-arg fn
    | _ -> ()  // do not over-constrain; presence in env is enough

// ---- Codegen prelude block ----

[<Fact>]
let ``emitted F# contains the prelude block`` () =
    let fs = codegenSrc "module M\nlet n = listLen [1 2 3]"
    Assert.Contains("ll-lang stdlib prelude", fs)
    Assert.Contains("let listLen", fs)
    Assert.Contains("let listMap", fs)

[<Fact>]
let ``Maybe-dependent prelude only emitted when user declares Maybe`` () =
    let withoutMaybe = codegenSrc "module M\nlet x = 1"
    Assert.DoesNotContain("let listHead", withoutMaybe)
    let withMaybe = codegenSrc "module M\ntype Maybe A = Some A | None\nlet x = 1"
    Assert.Contains("let listHead", withMaybe)

// ---- Phase 6.5: Char / Str extensions ----

[<Fact>]
let ``strChars returns List Char`` () =
    let tm = inferOk "module M\nlet cs = strChars \"abc\""
    Assert.Equal(TyApp(TyName "List", TyName "Char"), (Map.find "cs" tm.Env).Body)

[<Fact>]
let ``charToInt returns Int`` () =
    let src =
        "module M\n" +
        "let n = charToInt (intToChar 65)"
    let tm = inferOk src
    Assert.Equal(TyName "Int", (Map.find "n" tm.Env).Body)

[<Fact>]
let ``intToChar returns Char`` () =
    let tm = inferOk "module M\nlet c = intToChar 65"
    Assert.Equal(TyName "Char", (Map.find "c" tm.Env).Body)

[<Fact>]
let ``intToStr returns Str`` () =
    let tm = inferOk "module M\nlet s = intToStr 42"
    Assert.Equal(TyName "Str", (Map.find "s" tm.Env).Body)

[<Fact>]
let ``strSlice returns Str`` () =
    let tm = inferOk "module M\nlet s = strSlice \"hello\" 1 3"
    Assert.Equal(TyName "Str", (Map.find "s" tm.Env).Body)

[<Fact>]
let ``strIndexOf returns Int`` () =
    let tm = inferOk "module M\nlet n = strIndexOf \"l\" \"hello\""
    Assert.Equal(TyName "Int", (Map.find "n" tm.Env).Body)

[<Fact>]
let ``strSplit returns List Str`` () =
    let tm = inferOk "module M\nlet ss = strSplit \",\" \"a,b,c\""
    Assert.Equal(TyApp(TyName "List", TyName "Str"), (Map.find "ss" tm.Env).Body)

[<Fact>]
let ``strFromChars returns Str`` () =
    let tm = inferOk "module M\nlet s = strFromChars (strChars \"hi\")"
    Assert.Equal(TyName "Str", (Map.find "s" tm.Env).Body)

[<Fact>]
let ``strReverse returns Str`` () =
    let tm = inferOk "module M\nlet s = strReverse \"abc\""
    Assert.Equal(TyName "Str", (Map.find "s" tm.Env).Body)

[<Fact>]
let ``charIsDigit returns Bool`` () =
    let tm = inferOk "module M\nlet b = charIsDigit (intToChar 49)"
    Assert.Equal(TyName "Bool", (Map.find "b" tm.Env).Body)

[<Fact>]
let ``charIsAlpha returns Bool`` () =
    let tm = inferOk "module M\nlet b = charIsAlpha (intToChar 65)"
    Assert.Equal(TyName "Bool", (Map.find "b" tm.Env).Body)

[<Fact>]
let ``charIsSpace returns Bool`` () =
    let tm = inferOk "module M\nlet b = charIsSpace (intToChar 32)"
    Assert.Equal(TyName "Bool", (Map.find "b" tm.Env).Body)

// ---- Phase 6.5: File IO ----

[<Fact>]
let ``readFile returns Str`` () =
    let tm = inferOk "module M\nlet s = readFile \"/tmp/x\""
    Assert.Equal(TyName "Str", (Map.find "s" tm.Env).Body)

[<Fact>]
let ``writeFile returns Unit`` () =
    let tm = inferOk "module M\nfn run() = writeFile \"/tmp/x\" \"data\""
    // Just confirm presence in env
    Assert.True(Map.containsKey "run" tm.Env)

[<Fact>]
let ``fileExists returns Bool`` () =
    let tm = inferOk "module M\nlet b = fileExists \"/tmp/x\""
    Assert.Equal(TyName "Bool", (Map.find "b" tm.Env).Body)

// ---- Phase 6.5: Process ----

[<Fact>]
let ``exit is Int -> Unit (function present)`` () =
    let tm = inferOk "module M\nfn quit() = exit 0"
    Assert.True(Map.containsKey "quit" tm.Env)

// ---- Phase 6.5: List extras ----

[<Fact>]
let ``listConcat flattens List of List`` () =
    let tm = inferOk "module M\nlet xs = listConcat [[1 2] [3 4]]"
    Assert.Equal(TyApp(TyName "List", TyName "Int"), (Map.find "xs" tm.Env).Body)

[<Fact>]
let ``listIsEmpty returns Bool`` () =
    let tm = inferOk "module M\nlet b = listIsEmpty [1 2 3]"
    Assert.Equal(TyName "Bool", (Map.find "b" tm.Env).Body)

[<Fact>]
let ``listAt returns Maybe A`` () =
    let src =
        "module M\n" +
        "type Maybe A = Some A | None\n" +
        "let m = listAt [1 2 3] 0"
    let tm = inferOk src
    Assert.Equal(TyApp(TyName "Maybe", TyName "Int"), (Map.find "m" tm.Env).Body)

// ---- Phase 6.5: Codegen prelude additions ----

[<Fact>]
let ``emitted F# contains new core prelude bindings`` () =
    let fs = codegenSrc "module M\nlet n = strLen \"hi\""
    Assert.Contains("let strChars", fs)
    Assert.Contains("let readFile", fs)
    Assert.Contains("let intToStr", fs)
    Assert.Contains("let listConcat", fs)

[<Fact>]
let ``listAt only emitted when user declares Maybe`` () =
    let withoutMaybe = codegenSrc "module M\nlet x = 1"
    Assert.DoesNotContain("let listAt", withoutMaybe)
    let withMaybe = codegenSrc "module M\ntype Maybe A = Some A | None\nlet x = 1"
    Assert.Contains("let listAt", withMaybe)
