module LLLang.Parser

open LLLang.Token
open LLLang.AST

// ---- Parser state ---------------------------------------------------

type private Ctx = {
    Tokens: Tok array
    mutable Pos: int
}

let private cur (c: Ctx) = c.Tokens[c.Pos]
let private curTok (c: Ctx) = c.Tokens[c.Pos].Token

let private advance (c: Ctx) =
    if c.Pos < c.Tokens.Length - 1 then c.Pos <- c.Pos + 1

let private skip (c: Ctx) (t: Token) : Result<unit, string> =
    if curTok c = t then advance c; Ok ()
    else
        let tok = cur c
        Error $"Expected {t} at {tok.Line}:{tok.Col}, got {tok.Token}"

let private skipNewlines (c: Ctx) =
    while curTok c = Newline do advance c

// ---- Expression parser ----------------------------------------------

let rec private parseAtom (c: Ctx) : Result<Expr, string> =
    skipNewlines c
    match curTok c with
    | IntLit n -> advance c; Ok (ELit (LInt n))
    | FloatLit f -> advance c; Ok (ELit (LFloat f))
    | StrLit s -> advance c; Ok (ELit (LStr s))
    | KwTrue -> advance c; Ok (ELit (LBool true))
    | KwFalse -> advance c; Ok (ELit (LBool false))
    | Ident name -> advance c; Ok (EVar name)
    | TypeId name -> advance c; Ok (ECon name)
    | LParen ->
        advance c
        skipNewlines c
        match parseExprInner c with
        | Error e -> Error e
        | Ok expr ->
            match skip c RParen with
            | Error e -> Error e
            | Ok () -> Ok expr
    | LBrack ->
        advance c
        skipNewlines c
        let elems = ResizeArray<Expr>()
        while curTok c <> RBrack && curTok c <> Eof do
            match parseExprInner c with
            | Error _ -> ()   // stop on error
            | Ok e -> elems.Add(e); skipNewlines c
        match skip c RBrack with
        | Error e -> Error e
        | Ok () -> Ok (EList (List.ofSeq elems))
    | t -> Error $"Unexpected token {t} at {(cur c).Line}:{(cur c).Col}"

and private parseTagged (c: Ctx) : Result<Expr, string> =
    match parseAtom c with
    | Error e -> Error e
    | Ok atom ->
        // Check for tag: atom[TypeId]
        if curTok c = LBrack then
            let saved = c.Pos
            advance c
            match curTok c with
            | TypeId name ->
                advance c
                match skip c RBrack with
                | Ok () -> Ok (ETagged(atom, name))
                | Error _ -> c.Pos <- saved; Ok atom  // not a tag, backtrack
            | _ ->
                c.Pos <- saved
                Ok atom
        else Ok atom

and private parseApp (c: Ctx) : Result<Expr, string> =
    match parseTagged c with
    | Error e -> Error e
    | Ok first ->
        let mutable result = first
        let mutable cont = true
        while cont do
            skipNewlines c
            match curTok c with
            | IntLit _ | FloatLit _ | StrLit _ | KwTrue | KwFalse
            | Ident _ | TypeId _ | LParen | LBrack ->
                match parseTagged c with
                | Ok arg -> result <- EApp(result, arg)
                | Error _ -> cont <- false
            | _ -> cont <- false
        Ok result

and private parseArith (c: Ctx) : Result<Expr, string> =
    match parseApp c with
    | Error e -> Error e
    | Ok left ->
        let mutable result = left
        let mutable cont = true
        while cont do
            match curTok c with
            | Plus | Minus as op ->
                advance c
                match parseApp c with
                | Ok right ->
                    let opName = if op = Plus then "+" else "-"
                    result <- EApp(EApp(EVar opName, result), right)
                | Error _ -> cont <- false
            | Star | Slash as op ->
                advance c
                match parseApp c with
                | Ok right ->
                    let opName = if op = Star then "*" else "/"
                    result <- EApp(EApp(EVar opName, result), right)
                | Error _ -> cont <- false
            | Lt | Gt | Le | Ge | EqEq | Neq as op ->
                advance c
                match parseApp c with
                | Ok right ->
                    let opName =
                        match op with
                        | Lt -> "<" | Gt -> ">" | Le -> "<=" | Ge -> ">="
                        | EqEq -> "==" | Neq -> "!=" | _ -> "?"
                    result <- EApp(EApp(EVar opName, result), right)
                | Error _ -> cont <- false
            | _ -> cont <- false
        Ok result

and private parsePipe (c: Ctx) : Result<Expr, string> =
    match parseArith c with
    | Error e -> Error e
    | Ok left ->
        let mutable result = left
        while curTok c = Arrow do
            advance c
            match parseArith c with
            | Ok right -> result <- EPipe(result, right)
            | Error _ -> ()
        Ok result

and private parseExprInner (c: Ctx) : Result<Expr, string> =
    skipNewlines c
    match curTok c with
    | KwLet ->
        advance c
        match curTok c with
        | Ident name ->
            advance c
            match skip c Eq with
            | Error e -> Error e
            | Ok () ->
                match parseExprInner c with
                | Error e -> Error e
                | Ok e1 ->
                    if curTok c = KwIn then
                        advance c
                        match parseExprInner c with
                        | Ok e2 -> Ok (ELet(name, e1, Some e2))
                        | Error e -> Error e
                    else Ok (ELet(name, e1, None))
        | t -> Error $"Expected identifier after 'let', got {t}"
    | KwIf ->
        advance c
        match parseExprInner c with
        | Error e -> Error e
        | Ok cond ->
            match skip c KwThen with
            | Error e -> Error e
            | Ok () ->
                match parseExprInner c with
                | Error e -> Error e
                | Ok thenE ->
                    match skip c KwElse with
                    | Error e -> Error e
                    | Ok () ->
                        match parseExprInner c with
                        | Ok elseE -> Ok (EIf(cond, thenE, elseE))
                        | Error e -> Error e
    | Backslash ->
        advance c
        let parms = ResizeArray<Ident>()
        let mutable collecting = true
        while collecting do
            match curTok c with
            | Ident n -> parms.Add(n); advance c
            | _ -> collecting <- false
        match skip c Dot with
        | Error e -> Error e
        | Ok () ->
            match parseExprInner c with
            | Ok body -> Ok (ELam(List.ofSeq parms, body))
            | Error e -> Error e
    | _ -> parsePipe c

/// Parse an expression from a token list. Returns (expr, remaining tokens).
let parseExpr (tokens: Tok list) : Result<Expr * Tok list, string> =
    let ctx = { Tokens = Array.ofList tokens; Pos = 0 }
    match parseExprInner ctx with
    | Error e -> Error e
    | Ok expr -> Ok (expr, ctx.Tokens |> Array.skip ctx.Pos |> Array.toList)

/// Parse a full module. Stub — implemented in Task 7.
let parseModule (tokens: Tok list) : Result<LLModule, string> =
    Error "parseModule not yet implemented"
