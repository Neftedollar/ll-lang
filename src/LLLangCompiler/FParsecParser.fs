module LLLang.FParsecParser

open FParsec
open System.Globalization
open LLLang.AST
open LLLang.Token
module LegacyLexer = LLLang.Lexer
module LegacyParser = LLLang.Parser

type ParseState =
    { PosMap: PosMap
      Source: string }

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

let private trailingWsHasNewline (src: string) (exclusiveEnd: int64) : bool =
    let mutable i = int exclusiveEnd - 1
    let mutable sawNewline = false
    while i >= 0 && System.Char.IsWhiteSpace(src[i]) do
        if src[i] = '\n' then
            sawNewline <- true
        i <- i - 1
    sawNewline

/// Combined: skip at least one whitespace char
let ws1 : FParser<unit> = spaces1

// ---- Keywords ----

/// Check if character is valid for identifier continuation
let private isIdentContChar (c: char) : bool = isAsciiLetter c || isDigit c || c = '_'

/// Match exact keyword with an identifier boundary.
/// Example: `fn` must not match the `fn` prefix in `fnName`.
let kw (s: string) : FParser<unit> =
    attempt (skipString s .>> notFollowedBy (satisfy isIdentContChar) .>> wsOrComment)

// ---- Identifiers ----

/// Check if character is valid for identifier start
let isIdentStart (c: char) : bool = isLower c || c = '_'

/// Check if character is valid for identifier continuation
let isIdentCont (c: char) : bool = isIdentContChar c

/// Parse identifier (lowercase start) — not a keyword
let pIdentRaw : FParser<string> =
    many1Satisfy2L isIdentStart isIdentCont "identifier" .>> wsOrComment

/// Parse type identifier (uppercase start)
let pTypeIdRaw : FParser<string> =
    many1Satisfy2L isUpper isIdentCont "type identifier" .>> wsOrComment

/// Set of keywords that cannot be used as identifiers
let keywords =
    set ["let"; "tag"; "unit"; "trait"; "impl"; "import"; "export"; "external"; "opaque"
         "module"; "match"; "if"; "then"; "else"; "in"; "with"; "fn"; "true"; "false"]

let private keywordTokenName (kw: string) : string =
    match kw with
    | "let" -> "KwLet"
    | "tag" -> "KwTag"
    | "unit" -> "KwUnit"
    | "trait" -> "KwTrait"
    | "impl" -> "KwImpl"
    | "import" -> "KwImport"
    | "export" -> "KwExport"
    | "module" -> "KwModule"
    | "external" -> "KwExternal"
    | "opaque" -> "KwOpaque"
    | "match" -> "KwMatch"
    | "if" -> "KwIf"
    | "else" -> "KwElse"
    | "true" -> "KwTrue"
    | "false" -> "KwFalse"
    | "then" -> "KwThen"
    | "in" -> "KwIn"
    | "with" -> "KwWith"
    | "fn" -> "KwFn"
    | _ -> $"Kw{kw}"

/// Parse identifier (rejects keywords)
let pIdent : FParser<string> =
    attempt (
        pIdentRaw >>= fun s ->
            if s = "_" then
                fail "Standalone '_' is wildcard, not an identifier"
            elif keywords.Contains(s) then
                fail $"Expected identifier, got {keywordTokenName s}"
            else
                preturn s)

/// Parse type identifier
let pTypeId : FParser<string> = pTypeIdRaw

// ---- Literals ----

/// Parse integer literal
let pInt : FParser<int64> =
    many1SatisfyL isDigit "integer" .>> wsOrComment
    |>> fun s -> System.Int64.Parse(s, CultureInfo.InvariantCulture)

/// Parse float literal
let pFloat : FParser<float> =
    attempt (
        pipe2
            (many1SatisfyL isDigit "float integer part")
            (skipChar '.' >>. many1SatisfyL isDigit "float fractional part")
            (fun i f -> System.Double.Parse(i + "." + f, CultureInfo.InvariantCulture)))
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
            <|> (skipChar '0' >>% '\000')
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
            <|> (skipChar '0' >>% '\000')
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

let pPattern, pPatternImpl = createParserForwardedToRef()
let pPatternCons, pPatternConsImpl = createParserForwardedToRef()
let pPatternAtom, pPatternAtomImpl = createParserForwardedToRef()

let pWildcardPattern : FParser<Pattern> =
    attempt (skipChar '_' >>. notFollowedBy (satisfy isIdentCont) >>. wsOrComment >>% PWild)

let pPatternLiteral : FParser<Pattern> =
    (pInt |>> (fun n -> PLit (LInt n)))
    <|> (pFloat |>> (fun f -> PLit (LFloat f)))
    <|> (pStrLit |>> (fun s -> PLit (LStr s)))
    <|> (pCharLit |>> (fun c -> PLit (LChar c)))
    <|> (pBool |>> (fun b -> PLit (LBool b)))

let pPatternParenOrTuple : FParser<Pattern> =
    between (skipChar '(' >>. wsOrComment) (skipChar ')' .>> wsOrComment)
        (pipe2
            pPatternCons
            (many (skipChar ',' >>. wsOrComment >>. pPatternCons))
            (fun head tail ->
                match tail with
                | [] -> head
                | _ -> PTuple (head :: tail)))

let pPatternList : FParser<Pattern> =
    between (skipChar '[' >>. wsOrComment) (skipChar ']' .>> wsOrComment)
        (sepBy pPatternCons (skipChar ',' >>. wsOrComment))
    |>> fun elems ->
        let nil = PCon("[]", [])
        Seq.foldBack (fun p acc -> PCons(p, acc)) elems nil

let pPatternCtorArg : FParser<Pattern> =
    pWildcardPattern
    <|> (pIdent |>> PVar)
    <|> pPatternLiteral
    <|> pPatternParenOrTuple

let pPatternCtor : FParser<Pattern> =
    pipe2 pTypeId (many pPatternCtorArg) (fun name args -> PCon(name, args))

do pPatternAtomImpl :=
    (pWildcardPattern
     <|> (pIdent |>> PVar)
     <|> pPatternLiteral
     <|> pPatternList
     <|> pPatternCtor
     <|> pPatternParenOrTuple)
    |> withPos

do pPatternConsImpl :=
    chainr1 pPatternAtom (skipString "::" >>. wsOrComment >>% (fun l r -> PCons(l, r)))
    |> withPos

do pPatternImpl := pPatternCons |> withPos

// ---- Expressions ----
// Order: pListLit/pTupleLit → pAtom → pAppExpr → pMulExpr → pAddExpr → pConsExpr → pCmpExpr → pPipeExpr → pExpr
// pExpr uses createParserForwardedToRef to break circular dependency with pListLit/pTupleLit

let pExpr, pExprImpl = createParserForwardedToRef()
let pIfExpr, pIfExprImpl = createParserForwardedToRef()
let pMatchExpr, pMatchExprImpl = createParserForwardedToRef()
let pLetKwExpr, pLetKwExprImpl = createParserForwardedToRef()
let pLetKeywordFreeExpr, pLetKeywordFreeExprImpl = createParserForwardedToRef()
let pLambdaExpr, pLambdaExprImpl = createParserForwardedToRef()
let pAtom, pAtomImpl = createParserForwardedToRef()
let pAppExpr, pAppExprImpl = createParserForwardedToRef()
let pUnaryExpr, pUnaryExprImpl = createParserForwardedToRef()

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
let pEqAssign : FParser<unit> =
    skipChar '=' .>> notFollowedBy (pchar '=') .>> wsOrComment

let pLetExprFromPattern (pat: Pattern) (e1: Expr) : Expr =
    match pat with
    | PVar name -> ELet(name, e1, None)
    | _ -> ELetPat(pat, e1, None)

let pKeywordFreeLetPattern : FParser<Pattern> =
    (pIdent |>> PVar) <|> pWildcardPattern

do pLetKwExprImpl :=
    attempt (
        getPosition >>= fun letPos ->
            pipe2
                (kw "let" >>. pPattern)
                (pEqAssign >>. pExpr)
                (fun pat rhs -> (pat, rhs, int letPos.Line, int letPos.Column))
            >>= fun (pat, rhs, letLine, letCol) ->
                ((attempt (kw "in" >>. pExpr |>> Some))
                 <|> (getPosition >>= fun nextPos ->
                        let nextLine = int nextPos.Line
                        let nextCol = int nextPos.Column
                        let hasIndentedBody =
                            nextLine > letLine && nextCol >= letCol
                        if hasIndentedBody then
                            pExpr |>> Some
                        else
                            preturn None))
                |>> fun bodyOpt ->
                    match pat with
                    | PVar name -> ELet(name, rhs, bodyOpt)
                    | _ -> ELetPat(pat, rhs, bodyOpt))
    |> withPos

do pLetKeywordFreeExprImpl :=
    attempt (
        getPosition >>= fun letPos ->
            pipe2
                pKeywordFreeLetPattern
                (pEqAssign >>. pExpr)
                (fun pat rhs -> (pat, rhs, int letPos.Line, int letPos.Column))
            >>= fun (pat, rhs, letLine, letCol) ->
                getPosition >>= fun nextPos ->
                    let nextLine = int nextPos.Line
                    let nextCol = int nextPos.Column
                    let hasIndentedBody =
                        nextLine > letLine && nextCol >= letCol
                    if hasIndentedBody then
                        pExpr
                        |>> fun body ->
                            match pat with
                            | PVar name -> ELet(name, rhs, Some body)
                            | _ -> ELetPat(pat, rhs, Some body)
                    else
                        preturn (pLetExprFromPattern pat rhs))
    |> withPos

/// List literal: [expr; expr; ...]
let pListLit : FParser<Expr> =
    between (skipChar '[') (skipChar ']' .>> wsOrComment)
        (manyTill
            (optional (skipChar ';' >>. wsOrComment)
             >>. (attempt (followedBy pTypeIdRaw >>. pAppExpr) <|> pAtom))
            (lookAhead (skipChar ']')))
    |>> List.ofSeq
    |>> EList
    |> withPos

/// Tuple literal: expr, expr, ...
let pTupleLit : FParser<Expr> =
    between (skipChar '(' >>. wsOrComment) (skipChar ')' .>> wsOrComment)
        (pipe2
            pExpr
            (many1 (skipChar ',' >>. wsOrComment >>. pExpr))
            (fun head tail -> ETuple (head :: tail)))
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
    <|> attempt pTupleLit
    <|> between (skipChar '(' >>. wsOrComment) (skipChar ')' .>> wsOrComment) pExpr
    |> withPos

let pTagSuffix : FParser<string> =
    between (skipChar '[' >>. wsOrComment) (skipChar ']' .>> wsOrComment) (pTypeId <|> pIdent)

let private pKeywordFreeBindingHead : FParser<unit> =
    let binder =
        (skipChar '_' .>> notFollowedBy (satisfy isIdentContChar))
        <|> (many1Satisfy2L isIdentStart isIdentCont "identifier" >>% ())
    attempt (lookAhead (binder .>> spaces .>> skipChar '=')) >>% ()

/// Atom: pre-tag atom plus literal-only [Tag] suffix
do pAtomImpl :=
    pAtomBase >>= fun atom ->
        match atom with
        | ELit _ ->
            opt (attempt pTagSuffix)
            |>> function
                | Some tag -> ETagged(atom, tag)
                | None -> atom
        | _ -> preturn atom
    |> withPos

/// Application: atom atom* (juxtaposition = curried call)
do pAppExprImpl :=
    getPosition >>= fun appStartPos ->
        pAtom >>= fun head ->
            let baseLine = int appStartPos.Line
            let baseCol = int appStartPos.Column
            let rec loop (acc: Expr) (continuationLine: int option) : FParser<Expr> =
                getPosition >>= fun pos ->
                    let line = int pos.Line
                    let col = int pos.Column
                    // Prevent declaration bleed-through: once we crossed to a
                    // later line, stop implicit application at same-or-less
                    // indentation than where this application started.
                    let hitHardBreak = line > baseLine && col <= baseCol
                    // Exception: allow continuation on the same physical line
                    // immediately following a multi-line argument (e.g.
                    // `f ( ... ) x y` where `x y` appear after the closing `)`).
                    let canContinueOnLine =
                        match continuationLine with
                        | Some ln when ln = line -> true
                        | _ -> false
                    // Never continue implicit application at column 1.
                    // This protects top-level declaration boundaries.
                    if hitHardBreak && (not canContinueOnLine || col = 1) then
                        preturn acc
                    elif hitHardBreak && canContinueOnLine then
                        // Also stop if this line starts a keyword-free
                        // binding (`name = ...` / `_ = ...`), otherwise an
                        // argument continuation can accidentally eat the next
                        // let-chain statement.
                        opt (attempt pKeywordFreeBindingHead)
                        >>= function
                            | Some _ -> preturn acc
                            | None ->
                                opt (
                                    attempt (
                                        getPosition >>= fun argStart ->
                                            opt (lookAhead anyChar) >>= function
                                                | None -> fail "end of input"
                                                | Some argHeadChar ->
                                                    // Keep `n - 1` as subtraction, not implicit app `n (-1)`.
                                                    if argHeadChar = '-' then
                                                        fail "stop implicit app before minus"
                                                    else
                                                        pAtom >>= fun arg ->
                                                            getPosition >>= fun argEnd ->
                                                                getUserState >>= fun state ->
                                                                    let hasTrailingNewline =
                                                                        trailingWsHasNewline state.Source argEnd.Index
                                                                    preturn (arg, argHeadChar, int argStart.Line, int argEnd.Line, int argEnd.Column, hasTrailingNewline)))
                                >>= function
                                    | Some (arg, argHeadChar, argStartLine, argEndLine, argEndCol, hasTrailingNewline) ->
                                        let startedWithGrouping =
                                            argHeadChar = '(' || argHeadChar = '[' || argHeadChar = '{'
                                        let spansMultipleLogicalLines =
                                            (argEndLine - argStartLine) > 1
                                        let nextContinuationLine =
                                            if startedWithGrouping && spansMultipleLogicalLines && argEndCol > 1 && not hasTrailingNewline then
                                                Some argEndLine
                                            elif continuationLine = Some argEndLine then
                                                continuationLine
                                            else
                                                None
                                        loop (EApp(acc, arg)) nextContinuationLine
                                    | None -> preturn acc
                    else
                        opt (
                            attempt (
                                getPosition >>= fun argStart ->
                                    opt (lookAhead anyChar) >>= function
                                        | None -> fail "end of input"
                                        | Some argHeadChar ->
                                            // Keep `n - 1` as subtraction, not implicit app `n (-1)`.
                                            if argHeadChar = '-' then
                                                fail "stop implicit app before minus"
                                            else
                                                pAtom >>= fun arg ->
                                                    getPosition >>= fun argEnd ->
                                                        getUserState >>= fun state ->
                                                            let hasTrailingNewline =
                                                                trailingWsHasNewline state.Source argEnd.Index
                                                            preturn (arg, argHeadChar, int argStart.Line, int argEnd.Line, int argEnd.Column, hasTrailingNewline)))
                        >>= function
                            | Some (arg, argHeadChar, argStartLine, argEndLine, argEndCol, hasTrailingNewline) ->
                                let startedWithGrouping =
                                    argHeadChar = '(' || argHeadChar = '[' || argHeadChar = '{'
                                let spansMultipleLogicalLines =
                                    (argEndLine - argStartLine) > 1
                                let nextContinuationLine =
                                    if startedWithGrouping && spansMultipleLogicalLines && argEndCol > 1 && not hasTrailingNewline then
                                        Some argEndLine
                                    elif continuationLine = Some argEndLine then
                                        continuationLine
                                    else
                                        None
                                loop (EApp(acc, arg)) nextContinuationLine
                            | None -> preturn acc
            loop head None
    |> withPos

let private mkBoolNot (e: Expr) : Expr =
    EApp(EApp(EVar "==", e), ELit (LBool false))

do pUnaryExprImpl :=
    attempt (skipChar '!' >>. wsOrComment >>. pUnaryExpr |>> mkBoolNot)
    <|> pAppExpr
    |> withPos

/// Multiplication: expr * expr, expr / expr
let pMulExpr : FParser<Expr> =
    let mulOp = (skipChar '*' >>. wsOrComment >>% "*") <|> (skipChar '/' >>. wsOrComment >>% "/")
    pipe2
        pUnaryExpr
        (many (pipe2 mulOp pUnaryExpr (fun op right -> (op, right))))
        (fun head tail ->
            List.fold (fun acc (op, right) -> EApp(EApp(EVar op, acc), right)) head tail)
    |> withPos

/// Addition: expr + expr, expr - expr
let pAddExpr : FParser<Expr> =
    let minusOp =
        attempt (skipChar '-' .>> notFollowedBy (skipChar '>') >>. wsOrComment >>% "-")
    let addOp = (skipChar '+' >>. wsOrComment >>% "+") <|> minusOp
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
let private pBarArms (bodyParser: FParser<Expr>) : FParser<(Pattern * Expr) list> =
    let armWithPos : FParser<(Pattern * Expr) * (int * int)> =
        getPosition >>= fun barPos ->
            pipe3
                (skipChar '|' >>. wsOrComment >>. pPattern)
                (skipString "->" >>. wsOrComment)
                bodyParser
                (fun pat _ body -> ((pat, body), (int barPos.Line, int barPos.Column)))

    armWithPos >>= fun (firstArm, (baseLine, baseCol)) ->
        let rec loop (acc: (Pattern * Expr) list) : FParser<(Pattern * Expr) list> =
            getPosition >>= fun pos ->
                let line = int pos.Line
                let col = int pos.Column
                // If we crossed to a less-indented line, this arm belongs to
                // an outer clause/match block. Stop without consuming it.
                let hitHardBreak = line > baseLine && col < baseCol
                if hitHardBreak then
                    preturn (List.rev acc)
                else
                    opt (attempt armWithPos)
                    >>= function
                        | Some (arm, _) -> loop (arm :: acc)
                        | None -> preturn (List.rev acc)
        loop [firstArm]

do pMatchExprImpl :=
    pipe2
        (kw "match" >>. pPipeExpr .>> opt (skipString "with" >>. wsOrComment))
        (pBarArms pExpr)
        (fun scrutinee arms -> EMatchOf(scrutinee, arms))
    |> withPos

/// Main expression entry
do pExprImpl :=
    pIfExpr
    <|> pMatchExpr
    <|> pLetKwExpr
    <|> pLetKeywordFreeExpr
    <|> pLambdaExpr
    <|> pPipeExpr

// ---- Type expressions ----
// Use forward ref to break circular dependency

let pTypeExpr, pTypeExprImpl = createParserForwardedToRef()

let pTypeBase : FParser<TypeExpr> =
    (between (skipChar '(') (skipChar ')' .>> wsOrComment) pTypeExpr)
    <|> (pTypeId |>> TyName)
    <|> (pIdent |>> TyVar)

let pTypeBracketArg : FParser<TypeExpr> =
    between (skipChar '[') (skipChar ']' .>> wsOrComment) pTypeExpr

let pTypeApp : FParser<TypeExpr> =
    getPosition >>= fun appStartPos ->
        pTypeBase >>= fun head ->
            let baseLine = int appStartPos.Line
            let baseCol = int appStartPos.Column
            let rec loop (acc: TypeExpr) : FParser<TypeExpr> =
                getPosition >>= fun pos ->
                    let line = int pos.Line
                    let col = int pos.Column
                    let hitHardBreak = line > baseLine && col <= baseCol
                    if hitHardBreak then
                        preturn acc
                    else
                        opt (attempt pTypeBracketArg <|> attempt pTypeBase)
                        >>= function
                            | Some arg -> loop (TyApp(acc, arg))
                            | None -> preturn acc
            loop head
    |> withPos

let pTypeArrow : FParser<TypeExpr> =
    attempt (
        pipe2 pTypeApp (skipString "->" >>. wsOrComment >>. pTypeExpr)
            (fun l r -> TyFn(l, r)))
    <|> pTypeApp
    |> withPos

do pTypeExprImpl := pTypeArrow

// ---- Module parser ----

let pModulePath : FParser<string list> =
    sepBy1 (pTypeId <|> pIdent) (skipChar '.' >>. wsOrComment)

let pFnParam : FParser<Param option> =
    skipChar '(' >>. wsOrComment >>.
        ((skipChar ')' .>> wsOrComment >>% None)
         <|> (pipe2
                pIdent
                (wsOrComment >>.
                    ((skipChar ')' >>. wsOrComment >>% TyVar "?")
                     <|> (pTypeExpr .>> skipChar ')' .>> wsOrComment)))
                (fun name ty -> Some (name, ty))))

let pFnConstraint : FParser<string * string> =
    pipe2
        (skipChar '[' >>. wsOrComment >>. (pIdent <|> pTypeId))
        (skipChar ':' >>. wsOrComment >>. pTypeId .>> skipChar ']' .>> wsOrComment)
        (fun v t -> (v, t))

let pFnSig : FParser<FnSig> =
    pipe3
        (opt (kw "fn") >>. (pIdent <|> pTypeId))
        (many pFnConstraint)
        (many pFnParam)
        (fun name constraints paramGroups -> (name, constraints, paramGroups))
    >>= fun (name, constraints, paramGroups) ->
        let canHaveReturnType = not (List.isEmpty paramGroups)
        (if canHaveReturnType then opt pTypeExpr else preturn None)
        |>> fun retTy ->
            { Name = name
              Params = paramGroups |> List.choose id
              Constraints = constraints
              ReturnType = retTy }
    |> withPos

let pFnBody : FParser<Expr> =
    (attempt (pBarArms pExpr |>> EMatch))
    <|> pExpr

let pFnDecl : FParser<Decl> =
    pipe2 pFnSig (skipChar '=' >>. wsOrComment >>. pFnBody)
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
              Params = parms |> List.choose id
              Constraints = constraints
              ReturnType = retTy })
    |> withPos

let private hasUntypedHole (t: TypeExpr) : bool =
    let rec go ty =
        match ty with
        | TyVar "?" -> true
        | TyApp(a, b) | TyFn(a, b) -> go a || go b
        | TyTagged(a, _) -> go a
        | _ -> false
    go t

let pTypeDecl : FParser<Decl> =
    let typeParam =
        (between
            (skipChar '[' >>. wsOrComment)
            (skipChar ']' .>> wsOrComment)
            (pIdent <|> pTypeId)
         |>> TPPhantom)
        <|> ((pIdent <|> pTypeId) |>> TPBare)
    let ctorArgType : FParser<TypeExpr> =
        pTypeBase >>= fun head ->
            many (attempt pTypeBracketArg)
            |>> fun bracketArgs ->
                List.fold (fun acc arg -> TyApp(acc, arg)) head bracketArgs
    let ctor =
        getPosition >>= fun ctorStartPos ->
            pTypeId >>= fun name ->
                let baseLine = int ctorStartPos.Line
                let baseCol = int ctorStartPos.Column
                let rec loop (acc: TypeExpr list) : FParser<TypeExpr list> =
                    getPosition >>= fun pos ->
                        let line = int pos.Line
                        let col = int pos.Column
                        // Do not eat the next top-level declaration as a ctor arg.
                        let hitHardBreak = line > baseLine && col <= baseCol
                        if hitHardBreak then
                            preturn (List.rev acc)
                        else
                            opt (attempt ctorArgType)
                            >>= function
                                | Some ty -> loop (ty :: acc)
                                | None -> preturn (List.rev acc)
                loop [] |>> fun args -> (name, args)
    let inlineSumBody =
        pipe2 ctor (many (skipChar '|' >>. wsOrComment >>. ctor))
            (fun first rest -> TBSum (first :: rest))
    let barSumBody =
        many1 (skipChar '|' >>. wsOrComment >>. ctor)
        |>> TBSum
    let recordBody =
        pipe2
            (pipe2 pIdent pTypeExpr (fun name ty -> (name, ty)))
            (many (skipChar ',' >>. wsOrComment >>. pipe2 pIdent pTypeExpr (fun name ty -> (name, ty))))
            (fun first rest -> TBRecord (first :: rest))
    let wrappedBody = pTypeExpr |>> TBWrapped
    let typeBody =
        attempt barSumBody
        <|> attempt inlineSumBody
        <|> attempt recordBody
        <|> wrappedBody
    pipe3
        pTypeId
        (many typeParam)
        (skipChar '=' >>. wsOrComment >>. typeBody)
        (fun name typeParams body -> DType(name, typeParams, body))
    |> withPos

let pExternalDecl : FParser<Decl> =
    kw "external" >>. pFnSig >>= fun sig' ->
        match sig'.ReturnType with
        | None -> fail "external declaration requires an explicit return type"
        | Some retTy when hasUntypedHole retTy ->
            fail "external declaration requires fully typed return type"
        | Some _ ->
            if sig'.Params |> List.exists (fun (_, ty) -> hasUntypedHole ty) then
                fail "external declaration requires fully typed parameters"
            else
                notFollowedBy (skipChar '=')
                >>% DExternal sig'
    |> withPos

let pOpaqueDecl : FParser<Decl> =
    let typeParam =
        (between
            (skipChar '[' >>. wsOrComment)
            (skipChar ']' .>> wsOrComment)
            (pIdent <|> pTypeId)
         |>> TPBare)
    pipe2
        (kw "opaque" >>. (pTypeId <|> pIdent))
        (many typeParam)
        (fun name typeParams -> DOpaque(name, typeParams))
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
            (skipChar '=' >>. wsOrComment >>. pFnBody)
            (fun sig' body -> (sig', body))
    getPosition >>= fun implPos ->
        pipe3
            (kw "impl" >>. pTypeId)
            (pTypeId <|> pIdent)
            (skipChar '=' >>. wsOrComment >>. implFn)
            (fun traitName typeName firstImpl -> (traitName, typeName, firstImpl))
        >>= fun (traitName, typeName, firstImpl) ->
            let baseLine = int implPos.Line
            let baseCol = int implPos.Column
            let rec loop (acc: (FnSig * Expr) list) : FParser<(FnSig * Expr) list> =
                getPosition >>= fun pos ->
                    let line = int pos.Line
                    let col = int pos.Column
                    // Stop once we return to top-level indentation.
                    let hitHardBreak = line > baseLine && col <= baseCol
                    if hitHardBreak then
                        preturn (List.rev acc)
                    else
                        opt (attempt implFn)
                        >>= function
                            | Some fn -> loop (fn :: acc)
                            | None -> preturn (List.rev acc)
            loop [firstImpl]
            |>> fun impls -> DImpl(traitName, typeName, impls)
    |> withPos

let pTopLevelLet : FParser<Decl> =
    pipe2
        (kw "let" >>. pPattern)
        (pEqAssign >>. pExpr)
        (fun pat body ->
            match pat with
            | PVar name -> DLet(name, body)
            | _ -> DLetPat(pat, body))
    |> withPos

let pDecl : FParser<Decl> =
    attempt pTagDecl
    <|> attempt pUnitDecl
    <|> attempt pExternalDecl
    <|> attempt pOpaqueDecl
    <|> attempt pTraitDecl
    <|> attempt pImplDecl
    <|> attempt pTopLevelLet
    <|> attempt pTypeDecl
    <|> attempt pFnDecl

let pDeclWithExport : FParser<Decl * bool> =
    pipe2
        (opt (kw "export"))
        pDecl
        (fun exportKw decl -> (decl, exportKw.IsSome))
    |> withPos

/// Full module parser
let pModule : FParser<LLModule> =
    pipe3
        (kw "module" >>. opt pModulePath)
        (many pImportDecl)
        (many pDeclWithExport)
        (fun path imports decls ->
            { Path = path
                      |> Option.defaultValue []
              Imports = imports
              Decls = decls })
    |> withPos

// ---- Public API ----

let private runToResult (p: FParser<'a>) (src: string) : Result<'a * ParseState, string> =
    let state0 = { PosMap = PosMap.empty (); Source = src }
    match runParserOnString (wsOrComment >>. p .>> wsOrComment .>> eof) state0 "" src with
    | Success (value, state, _) ->
        Result.Ok (value, state)
    | Failure (msg, _, _) ->
        Result.Error (sprintf "Parse error: %s" msg)

let private strictModeEnabled () : bool =
    match System.Environment.GetEnvironmentVariable("LLLANG_FPARSEC_STRICT") with
    | null -> false
    | raw ->
        raw = "1"
        || raw.Equals("true", System.StringComparison.OrdinalIgnoreCase)
        || raw.Equals("yes", System.StringComparison.OrdinalIgnoreCase)

/// Strict FParsec-only expression parse (no legacy fallback).
let parseExprStrict (src: string) : Result<Expr, string> =
    runToResult pExpr src |> Result.map fst

let private parseExprLegacy (src: string) : Result<Expr, string> =
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

/// Parse expression from source string.
let parseExpr (src: string) : Result<Expr, string> =
    if strictModeEnabled () then
        match parseExprStrict src with
        | Result.Ok expr -> Result.Ok expr
        | Result.Error strictErr ->
            // Keep strict FParsec AST on success, but normalize failure text
            // to legacy diagnostics when both parsers fail so parity checks
            // (line:col + token names like KwTag) stay stable.
            match parseExprLegacy src with
            | Result.Error legacyErr -> Result.Error legacyErr
            | Result.Ok _ -> Result.Error strictErr
    else
        parseExprLegacy src

/// Strict FParsec-only module parse (no legacy fallback).
let parseModuleWithPosStrict (src: string) : Result<LLModule * LLLang.AST.PosMap, string> =
    runToResult pModule src
    |> Result.map (fun (m, state) -> (m, state.PosMap))

let private parseModuleWithPosLegacy (src: string) : Result<LLModule * LLLang.AST.PosMap, string> =
    match LegacyLexer.tokenize src with
    | Result.Error e ->
        Result.Error (sprintf "Parse error: %s" e)
    | Result.Ok toks ->
        match LegacyParser.parseModuleWithPos toks with
        | Result.Ok parsed -> Result.Ok parsed
        | Result.Error e -> Result.Error (sprintf "Parse error: %s" e)

/// Parse source string into LLModule with position map.
let parseModuleWithPos (src: string) : Result<LLModule * LLLang.AST.PosMap, string> =
    if strictModeEnabled () then
        match parseModuleWithPosStrict src with
        | Result.Ok parsed -> Result.Ok parsed
        | Result.Error strictErr ->
            // Same compatibility policy as parseExpr: strict AST on success,
            // legacy-shaped diagnostics on failure parity.
            match parseModuleWithPosLegacy src with
            | Result.Error legacyErr -> Result.Error legacyErr
            | Result.Ok _ -> Result.Error strictErr
    else
        parseModuleWithPosLegacy src

/// Parse source string
let parseModule (src: string) : Result<LLModule, string> =
    parseModuleWithPos src |> Result.map fst
