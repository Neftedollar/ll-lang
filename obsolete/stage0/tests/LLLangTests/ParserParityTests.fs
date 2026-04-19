module LLLang.Tests.ParserParityTests

open System.IO
open System.Text.RegularExpressions
open Xunit
open LLLang.AST
open LLLang.FParsecParser
open LLLang.Lexer
open LLLang.Parser
open LLLang.Token

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private readSpecExample (group: string) (name: string) : string =
    File.ReadAllText(Path.Combine(repoRoot, "spec/examples", group, name))

let private parseLegacyExpr (src: string) : Result<Expr, string> =
    match tokenize src with
    | Error e -> Error (sprintf "Parse error: %s" e)
    | Ok toks ->
        match LLLang.Parser.parseExpr toks with
        | Error e -> Error (sprintf "Parse error: %s" e)
        | Ok (expr, rest) ->
            let hasTrailingTokens =
                rest
                |> List.exists (fun t ->
                    match t.Token with
                    | Newline
                    | Eof -> false
                    | _ -> true)
            if hasTrailingTokens then
                Error "Parse error: trailing tokens after expression"
            else
                Ok expr

let private parseLegacyModule (src: string) : Result<LLModule, string> =
    match tokenize src with
    | Error e -> Error (sprintf "Parse error: %s" e)
    | Ok toks ->
        match LLLang.Parser.parseModuleWithPos toks with
        | Error e -> Error (sprintf "Parse error: %s" e)
        | Ok (m, _) -> Ok m

let private firstLineCol (message: string) : Option<int * int> =
    let m = Regex.Match(message, @"(\d+):(\d+)")
    if m.Success then
        Some(int m.Groups[1].Value, int m.Groups[2].Value)
    else
        None

let private assertFailurePosParity (fparsecResult: string) (legacyResult: string) =
    let fpPos = firstLineCol fparsecResult
    let legacyPos = firstLineCol legacyResult

    match fpPos, legacyPos with
    | Some fp, Some legacy ->
        Assert.NotEqual((0, 0), fp)
        Assert.NotEqual((0, 0), legacy)
        Assert.Equal(fp, legacy)
    | _ ->
        failwithf
            "Expected both parse errors to include line:col, but got\nFParsec: %s\nLegacy: %s"
            fparsecResult
            legacyResult

let private assertExprParity (src: string) =
    let fparsec = LLLang.FParsecParser.parseExpr src
    let legacy = parseLegacyExpr src

    match fparsec, legacy with
    | Ok fExpr, Ok legacyExpr -> Assert.Equal(legacyExpr, fExpr)
    | Ok _, Error legacyErr ->
        failwithf "Expected both parsers to fail, FParsec succeeded but legacy failed: %s" legacyErr
    | Error fErr, Ok _ ->
        failwithf "Expected both parsers to succeed, FParsec failed: %s" fErr
    | Error fErr, Error legacyErr ->
        assertFailurePosParity fErr legacyErr

let private assertModuleParity (src: string) =
    let fparsec = LLLang.FParsecParser.parseModuleWithPos src
    let legacy = parseLegacyModule src

    match fparsec, legacy with
    | Ok (fModule, _), Ok legacyModule -> Assert.Equal(legacyModule, fModule)
    | Ok _, Error legacyErr ->
        failwithf
            "Expected both parsers to fail, FParsec succeeded but legacy failed: %s"
            legacyErr
    | Error fErr, Ok _ ->
        failwithf "Expected both parsers to succeed, FParsec failed: %s" fErr
    | Error fErr, Error legacyErr ->
        assertFailurePosParity fErr legacyErr

[<Theory>]
[<InlineData("42")>]
[<InlineData("let x = 1")>]
[<InlineData("x = 1")>]
[<InlineData("\\x y. x + y")>]
[<InlineData("f()")>]
[<InlineData("f() x")>]
[<InlineData("x |> f")>]
[<InlineData("m >>= f")>]
[<InlineData("p1 <|> p2")>]
[<InlineData("a >> b")>]
[<InlineData("x |> f >>= g")>]
[<InlineData("a >>= b <|> c")>]
[<InlineData("a <|> b <|> c")>]
[<InlineData("a >> b >>= c")>]
[<InlineData("f x \\y. y")>]
[<InlineData("stateBind x \\y.\n  stateBind y \\z. z")>]
[<InlineData("match xs | [] -> 0 | h :: t -> 1")>]
[<InlineData("match xs | [] -> 0 | [a, b] -> a")>]
[<InlineData("if x\n  1\nelse 2")>]
let ``expression parser parity: success cases`` (src: string) =
    assertExprParity src

[<Theory>]
[<InlineData("(1,")>]
[<InlineData("let x =")>]
[<InlineData("if x then else")>]
let ``expression parser parity: failure cases`` (src: string) =
    assertExprParity src

[<Theory>]
[<InlineData("valid", "01-basics.lll")>]
[<InlineData("valid", "14-exprparser-real.lll")>]
[<InlineData("valid", "15-moduleparser-real.lll")>]
[<InlineData("valid", "20a-bootstrap-input.lll")>]
[<InlineData("invalid", "E001-type-mismatch.lll")>]
[<InlineData("invalid", "E005-tag-violation.lll")>]
let ``module parser parity: selected corpus files`` (group: string) (fileName: string) =
    let src = readSpecExample group fileName
    assertModuleParity src

[<Theory>]
[<InlineData("module\nlet x = 1")>]
[<InlineData("module M\nlet x = (")>]
[<InlineData("module M\nfn f x Int = 1")>]
let ``module parser parity: failure cases`` (src: string) =
    assertModuleParity src
