module LLLang.FParsecParser

open FParsec
open System.Globalization
open LLLang.AST
open LLLang.Token
module LegacyLexer = LLLang.Lexer
module LegacyParser = LLLang.Parser

type ParseState =
    { PosMap: PosMap }

type FParser<'a> = Parser<'a, ParseState>

let private withPos (p: FParser<'a>) : FParser<'a> =
    getPosition >>= fun startPos ->
        p >>= fun value ->
            getUserState >>= fun state ->
                preturn (PosMap.add state.PosMap (box value) { Line = int startPos.Line; Col = int startPos.Column }; value)

// ============================================================
// ll-lang parser using FParsec
// Step 2: Primitives (lexer-level combinators)
// ============================================================

// ---- Whitespace & comments ----

/// Whitespace (spaces, tabs, newlines)
let ws : FParser<unit> = spaces

/// Comment: "--" to end of line
let comment : FParser<unit> =
    skipString "--"
    >>. skipManySatisfy (fun c -> c <> '\n')
    >>. optional (skipChar '\n')
    >>% ()

/// Whitespace or comments (any combination, incl. newlines)
let wsOrComment : FParser<unit> =
    skipMany (spaces1 <|> comment)

/// Combined: skip at least one whitespace char
let ws1 : FParser<unit> = spaces1

// ---- Keywords ----

/// Match exact keyword
let kw (s: string) : FParser<unit> =
    skipString s .>> wsOrComment

// ---- Identifiers ----

/// Check if character is valid for identifier start
let isIdentStart (c: char) : bool = isLower c || c = '_'

/// Check if character is valid for identifier continuation
let isIdentCont (c: char) : bool = isAsciiLetter c || isDigit c || c = '_'

/// Parse identifier (lowercase start) — not a keyword
let pIdentRaw : FParser<string> =
    many1Satisfy2L isIdentStart isIdentCont "identifier" .>> wsOrComment

/// Parse type identifier (uppercase start)
let pTypeIdRaw : FParser<string> =
    many1Satisfy2L isUpper isIdentCont "type identifier" .>> wsOrComment

/// Set of keywords that cannot be used as identifiers
let keywords =
    set ["let"; "tag"; "unit"; "trait"; "impl"; "import"; "export"
         "module"; "match"; "if"; "else"; "true"; "false"]

/// Parse identifier (rejects keywords)
let pIdent : FParser<string> =
    pIdentRaw >>= fun s ->
        if keywords.Contains(s) then
            fail $"Keyword '{s}' cannot be used as identifier"
        else
            preturn s

/// Parse type identifier
let pTypeId : FParser<string> = pTypeIdRaw

// ---- Literals ----

/// Parse integer literal
let pInt : FParser<int64> =
    many1SatisfyL isDigit "integer" .>> wsOrComment
    |>> fun s -> System.Int64.Parse(s, CultureInfo.InvariantCulture)

/// Parse float literal
let pFloat : FParser<float> =
    pipe2
        (many1SatisfyL isDigit "float integer part")
        (skipChar '.' >>. many1SatisfyL isDigit "float fractional part")
        (fun i f -> System.Double.Parse(i + "." + f, CultureInfo.InvariantCulture))
    .>> wsOrComment

/// Parse string literal
let pStrLit : FParser<string> =
    let strChar : FParser<char> =
        skipChar '\\' >>. (
            (skipChar 'n' >>% '\n')
            <|> (skipChar 't' >>% '\t')
            <|> (skipChar 'r' >>% '\r')
            <|> (skipChar '\\' >>% '\\')
            <|> (skipChar '"' >>% '"')
            <|> anyChar)
        <|> satisfy (fun c -> c <> '"' && c <> '\\')
    between (skipChar '"') (skipChar '"')
        (many strChar |>> System.String.Concat)
    .>> wsOrComment
    <?> "string literal"

/// Parse char literal
let pCharLit : FParser<char> =
    let esc =
        skipChar '\\' >>. (
            (skipChar 'n' >>% '\n')
            <|> (skipChar 't' >>% '\t')
            <|> (skipChar 'r' >>% '\r')
            <|> (skipChar '\\' >>% '\\')
            <|> (skipChar '\'' >>% '\'')
            <|> (skipChar '"' >>% '"')
            <|> anyChar)
    between (skipChar '\'') (skipChar '\'')
        (esc <|> anyChar)
    .>> wsOrComment
    <?> "char literal"

/// Parse boolean literal
let pBool : FParser<bool> =
    (skipString "true" >>. wsOrComment >>% true)
    <|> (skipString "false" >>. wsOrComment >>% false)

// ---- Patterns (Step 6) ----

/// Pattern: Constructor arg* | variable | _
let pPattern : FParser<Pattern> =
    let conArg = pIdent |>> PVar
    let conPat =
        pipe2 pTypeId (many conArg)
            (fun name args -> PCon(name, args))
    (conPat <|> (pIdent |>> PVar) <|> (skipChar '_' >>. wsOrComment >>% PWild))
    |> withPos

// ---- Expressions ----
// Order: pListLit/pTupleLit → pAtom → pAppExpr → pMulExpr → pAddExpr → pConsExpr → pCmpExpr → pPipeExpr → pExpr
// pExpr uses createParserForwardedToRef to break circular dependency with pListLit/pTupleLit

let pExpr, pExprImpl = createParserForwardedToRef()
let pIfExpr, pIfExprImpl = createParserForwardedToRef()
let pMatchExpr, pMatchExprImpl = createParserForwardedToRef()
let pLetKwExpr, pLetKwExprImpl = createParserForwardedToRef()
let pLambdaExpr, pLambdaExprImpl = createParserForwardedToRef()

// Stubs for Step 5
// do pIfExprImpl := fail "pIfExpr not yet implemented (Step 5)"
// do pMatchExprImpl := fail "pMatchExpr not yet implemented (Step 5)"
// do pLetKwExprImpl := fail "pLetKwExpr not yet implemented (Step 5)"
// do pLambdaExprImpl := fail "pLambdaExpr not yet implemented (Step 5)"

// ---- Lambda: \x y. body ----
do pLambdaExprImpl :=
    pipe2
        (skipChar '\\' >>. wsOrComment >>. many1 pIdent)
        (skipChar '.' >>. wsOrComment >>. pExpr)
        (fun parms body -> ELam(parms, body))
    |> withPos

// ---- Let binding: let x = e1 in e2 ----
do pLetKwExprImpl :=
    pipe3
        (kw "let" >>. pIdent)
        (skipChar '=' >>. wsOrComment >>. pExpr)
        (kw "in" >>. pExpr)
        (fun name e1 body -> ELet(name, e1, Some body))
    |> withPos

/// List literal: [expr; expr; ...]
let pListLit : FParser<Expr> =
    between (skipChar '[') (skipChar ']' .>> wsOrComment)
        (sepBy pExpr (skipChar ';' >>. wsOrComment))
    |>> EList
    |> withPos

/// Tuple literal: expr, expr, ...
let pTupleLit : FParser<Expr> =
    pipe2
        (skipChar '(' >>. wsOrComment >>. pExpr)
        (many1 (skipChar ',' >>. wsOrComment >>. pExpr))
        (fun head tail -> ETuple (head :: tail))
    |> withPos

let pNegFloat : FParser<Expr> =
    attempt (skipChar '-' >>. wsOrComment >>. pFloat |>> fun f -> ELit (LFloat (-f)))

let pNegInt : FParser<Expr> =
    attempt (skipChar '-' >>. wsOrComment >>. pInt |>> fun n -> ELit (LInt (-n)))

/// Atom (pre-tag): literal, variable, constructor, list, tuple, or parenthesized expr
let pAtomBase : FParser<Expr> =
    pNegFloat
    <|> pNegInt
    <|> (pFloat |>> fun f -> ELit (LFloat f))
    <|> (pInt |>> fun n -> ELit (LInt n))
    <|> (pStrLit |>> fun s -> ELit (LStr s))
    <|> (pCharLit |>> fun c -> ELit (LChar c))
    <|> (pBool |>> fun b -> ELit (LBool b))
    <|> (pIdent |>> EVar)
    <|> (pTypeId |>> ECon)
    <|> pListLit
    <|> pTupleLit
    <|> between (skipChar '(' >>. wsOrComment) (skipChar ')' .>> wsOrComment) pExpr
    |> withPos

let pTagSuffix : FParser<string> =
    between (skipChar '[' >>. wsOrComment) (skipChar ']' .>> wsOrComment) (pTypeId <|> pIdent)

/// Atom: pre-tag atom plus literal-only [Tag] suffix
let pAtom : FParser<Expr> =
    pAtomBase >>= fun atom ->
        match atom with
        | ELit _ ->
            opt pTagSuffix
            |>> function
                | Some tag -> ETagged(atom, tag)
                | None -> atom
        | _ -> preturn atom
    |> withPos

/// Application: atom atom* (juxtaposition = curried call)
let pAppExpr : FParser<Expr> =
    pipe2
        pAtom
        (many pAtom)
        (fun head args ->
            List.fold (fun acc arg -> EApp(acc, arg)) head args)
    |> withPos

/// Multiplication: expr * expr, expr / expr
let pMulExpr : FParser<Expr> =
    let mulOp = (skipChar '*' >>. wsOrComment >>% "*") <|> (skipChar '/' >>. wsOrComment >>% "/")
    pipe2
        pAppExpr
        (many (pipe2 mulOp pAppExpr (fun op right -> (op, right))))
        (fun head tail ->
            List.fold (fun acc (op, right) -> EApp(EApp(EVar op, acc), right)) head tail)
    |> withPos

/// Addition: expr + expr, expr - expr
let pAddExpr : FParser<Expr> =
    let addOp = (skipChar '+' >>. wsOrComment >>% "+") <|> (skipChar '-' >>. wsOrComment >>% "-")
    pipe2
        pMulExpr
        (many (pipe2 addOp pMulExpr (fun op right -> (op, right))))
        (fun head tail ->
            List.fold (fun acc (op, right) -> EApp(EApp(EVar op, acc), right)) head tail)
    |> withPos

/// List cons: expr :: expr (right-associative)
let pConsExpr : FParser<Expr> =
    chainr1 pAddExpr (skipString "::" >>. wsOrComment >>% (fun l r -> ECons(l, r)))
    |> withPos

/// Comparison: expr op expr
let pCmpExpr : FParser<Expr> =
    let cmpOp =
        (skipString "==" >>. wsOrComment >>% "==")
        <|> (skipString "!=" >>. wsOrComment >>% "!=")
        <|> (skipString "<=" >>. wsOrComment >>% "<=")
        <|> (skipString ">=" >>. wsOrComment >>% ">=")
        <|> (skipChar '<' >>. wsOrComment >>% "<")
        <|> (skipChar '>' >>. wsOrComment >>% ">")
    pipe2
        pConsExpr
        (many (pipe2 cmpOp pConsExpr (fun op right -> (op, right))))
        (fun head tail ->
            List.fold (fun acc (op, right) -> EApp(EApp(EVar op, acc), right)) head tail)
    |> withPos

/// Pipe: expr -> expr -> ...
let pPipeExpr : FParser<Expr> =
    pipe2
        pCmpExpr
        (many (skipString "->" >>. wsOrComment >>. pCmpExpr))
        (fun head tail ->
            List.fold (fun acc arg -> EPipe(acc, arg)) head tail)
    |> withPos

// ---- If-then-else: if cond [then]? body else other ----
do pIfExprImpl :=
    pipe3
        (kw "if" >>. pPipeExpr)
        (opt (skipString "then" >>. wsOrComment) >>. pExpr)
        (kw "else" >>. pExpr)
        (fun cond thenE elseE -> EIf(cond, thenE, elseE))
    |> withPos

// ---- Match: match scrut [with] | pat -> body | pat -> body ----
do pMatchExprImpl :=
    let matchArm =
        pipe3
            (skipChar '|' >>. wsOrComment >>. pPattern)
            (skipString "->" >>. wsOrComment)
            (pExpr)
            (fun pat _ body -> (pat, body))
    pipe2
        (kw "match" >>. pPipeExpr .>> opt (skipString "with" >>. wsOrComment))
        (many1 matchArm)
        (fun scrutinee arms -> EMatchOf(scrutinee, arms))
    |> withPos

/// Main expression entry
do pExprImpl :=
    pIfExpr
    <|> pMatchExpr
    <|> pLetKwExpr
    <|> pLambdaExpr
    <|> pPipeExpr

// ---- Type expressions ----
// Use forward ref to break circular dependency

let pTypeExpr, pTypeExprImpl = createParserForwardedToRef()

let pTypeBase : FParser<TypeExpr> =
    (between (skipChar '(') (skipChar ')' .>> wsOrComment) pTypeExpr)
    <|> (pTypeId |>> TyName)
    <|> (pIdent |>> TyVar)

let pTypeApp : FParser<TypeExpr> =
    pipe2 pTypeBase (many (between (skipChar '[') (skipChar ']' .>> wsOrComment) pTypeExpr))
        (fun tb args ->
            List.fold (fun acc a -> TyApp(acc, a)) tb args)
    |> withPos

let pTypeArrow : FParser<TypeExpr> =
    pipe2 pTypeApp (skipString "->" >>. wsOrComment >>. pTypeExpr)
        (fun l r -> TyFn(l, r))
    <|> pTypeApp
    |> withPos

do pTypeExprImpl := pTypeArrow

// ---- Module parser ----

let pModulePath : FParser<string list> =
    sepBy1 (pTypeId <|> pIdent) (skipChar '.' >>. wsOrComment)

let pFnParam : FParser<Param> =
    pipe2
        (skipChar '(' >>. wsOrComment >>. pIdent)
        (wsOrComment >>.
            ((skipChar ')' >>. wsOrComment >>% TyVar "?")
             <|> (pTypeExpr .>> skipChar ')' .>> wsOrComment)))
        (fun name ty -> (name, ty))

let pFnConstraint : FParser<string * string> =
    pipe2
        (skipChar '[' >>. wsOrComment >>. pIdent)
        (skipChar ':' >>. wsOrComment >>. pTypeId .>> skipChar ']' .>> wsOrComment)
        (fun v t -> (v, t))

let pFnSig : FParser<FnSig> =
    pipe4
        (opt (kw "fn") >>. pIdent)
        (many pFnConstraint)
        (many pFnParam)
        (opt pTypeExpr)
        (fun name constraints parms retTy ->
            { Name = name
              Params = parms
              Constraints = constraints
              ReturnType = retTy })
    |> withPos

let pFnDecl : FParser<Decl> =
    pipe2 pFnSig (skipChar '=' >>. wsOrComment >>. pExpr)
        (fun sig' body -> DFn(sig', body))
    |> withPos

let pTraitFnSig : FParser<FnSig> =
    pipe4
        pIdent
        (many pFnConstraint)
        (many pFnParam)
        (opt pTypeExpr)
        (fun name constraints parms retTy ->
            { Name = name
              Params = parms
              Constraints = constraints
              ReturnType = retTy })
    |> withPos

let pTypeDecl : FParser<Decl> =
    let typeParam = pTypeId |>> TPBare
    let ctor =
        pipe2 pTypeId (many pTypeExpr)
            (fun name args -> (name, args))
    let sumBody =
        many1 (skipChar '|' >>. wsOrComment >>. ctor)
        |>> TBSum
        <|> (ctor |>> fun c -> TBSum [c])
    pipe3
        pTypeId
        (many typeParam)
        (skipChar '=' >>. wsOrComment >>. sumBody)
        (fun name typeParams body -> DType(name, typeParams, body))
    |> withPos

let pTagDecl : FParser<Decl> =
    let tagId : FParser<TypeIdent> = pTypeId <|> pIdent
    kw "tag" >>. (tagId |>> DTag)
    |> withPos

let pUnitDecl : FParser<Decl> =
    let unitId : FParser<TypeIdent> = pTypeId <|> pIdent
    kw "unit" >>. (unitId |>> DUnit)
    |> withPos

let pImportDecl : FParser<string list> =
    kw "import" >>. pModulePath

let pTraitDecl : FParser<Decl> =
    pipe3
        (kw "trait" >>. pTypeId)
        (many (pIdent <|> pTypeId))
        (skipChar '=' >>. wsOrComment >>. many1 (attempt pTraitFnSig))
        (fun name tvars sigs -> DTrait(name, List.ofSeq tvars, sigs))
    |> withPos

let pImplDecl : FParser<Decl> =
    let implFn =
        pipe2
            pTraitFnSig
            (skipChar '=' >>. wsOrComment >>. pExpr)
            (fun sig' body -> (sig', body))
    pipe4
        (kw "impl" >>. pTypeId)
        (pTypeId <|> pIdent)
        (skipChar '=' >>. wsOrComment)
        (many1 (attempt implFn))
        (fun traitName typeName _ impls ->
            DImpl(traitName, typeName, impls))
    |> withPos

let pTopLevelLet : FParser<Decl> =
    pipe2
        (kw "let" >>. pIdent)
        (skipChar '=' >>. wsOrComment >>. pExpr)
        (fun name body -> DLet(name, body))
    |> withPos

let pDecl : FParser<Decl> =
    pFnDecl
    <|> pTypeDecl
    <|> pTraitDecl
    <|> pImplDecl
    <|> pTagDecl
    <|> pUnitDecl
    <|> pTopLevelLet

let pDeclWithExport : FParser<Decl * bool> =
    pipe2
        (opt (kw "export"))
        pDecl
        (fun exportKw decl -> (decl, exportKw.IsSome))
    |> withPos

/// Full module parser
let pModule : FParser<LLModule> =
    pipe3
        (kw "module" >>. pModulePath)
        (many pImportDecl)
        (many pDeclWithExport)
        (fun path imports decls ->
            { Path = path
              Imports = imports
              Decls = decls })
    |> withPos

// ---- Public API ----

let private runToResult (p: FParser<'a>) (src: string) : Result<'a * ParseState, string> =
    let state0 = { PosMap = PosMap.empty () }
    match runParserOnString (wsOrComment >>. p .>> wsOrComment .>> eof) state0 "" src with
    | Success (value, state, _) ->
        Result.Ok (value, state)
    | Failure (msg, _, _) ->
        Result.Error (sprintf "Parse error: %s" msg)

/// Parse expression from source string.
let parseExpr (src: string) : Result<Expr, string> =
    match runToResult pExpr src |> Result.map fst with
    | Result.Ok expr -> Result.Ok expr
    | Result.Error _ ->
        match LegacyLexer.tokenize src with
        | Result.Error e ->
            Result.Error (sprintf "Parse error: %s" e)
        | Result.Ok toks ->
            match LegacyParser.parseExpr toks with
            | Result.Error e ->
                Result.Error (sprintf "Parse error: %s" e)
            | Result.Ok (expr, rest) ->
                let hasTrailing =
                    rest
                    |> List.exists (fun t ->
                        match t.Token with
                        | Newline | Eof -> false
                        | _ -> true)
                if hasTrailing then
                    Result.Error "Parse error: trailing tokens after expression"
                else
                    Result.Ok expr

/// Parse source string into LLModule with position map.
let parseModuleWithPos (src: string) : Result<LLModule * LLLang.AST.PosMap, string> =
    match runToResult pModule src with
    | Result.Ok (m, state) ->
        Result.Ok (m, state.PosMap)
    | Result.Error _ ->
        match LegacyLexer.tokenize src with
        | Result.Error e ->
            Result.Error (sprintf "Parse error: %s" e)
        | Result.Ok toks ->
            match LegacyParser.parseModuleWithPos toks with
            | Result.Ok parsed -> Result.Ok parsed
            | Result.Error e -> Result.Error (sprintf "Parse error: %s" e)

/// Parse source string
let parseModule (src: string) : Result<LLModule, string> =
    parseModuleWithPos src |> Result.map fst
