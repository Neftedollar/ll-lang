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
    | CharLit ch -> advance c; Ok (ELit (LChar ch))
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
        let mutable lstCont = true
        while lstCont && curTok c <> RBrack && curTok c <> Eof do
            match parseTagged c with
            | Error _ -> lstCont <- false
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
            // Do NOT skip newlines here: newlines terminate an application
            // in expression position so that indented `let` chains can be
            // parsed correctly. Multi-line applications must be wrapped in
            // parens.
            match curTok c with
            | IntLit _ | FloatLit _ | StrLit _ | CharLit _ | KwTrue | KwFalse
            | Ident _ | TypeId _ | LParen | LBrack ->
                match parseTagged c with
                | Ok arg -> result <- EApp(result, arg)
                | Error _ -> cont <- false
            | _ -> cont <- false
        Ok result

and private parseMul (c: Ctx) : Result<Expr, string> =
    match parseApp c with
    | Error e -> Error e
    | Ok left ->
        let mutable result = left
        let mutable cont = true
        while cont do
            match curTok c with
            | Star | Slash as op ->
                advance c
                match parseApp c with
                | Ok right ->
                    let opName = if op = Star then "*" else "/"
                    result <- EApp(EApp(EVar opName, result), right)
                | Error _ -> cont <- false
            | _ -> cont <- false
        Ok result

and private parseAdd (c: Ctx) : Result<Expr, string> =
    match parseMul c with
    | Error e -> Error e
    | Ok left ->
        let mutable result = left
        let mutable cont = true
        while cont do
            match curTok c with
            | Plus | Minus as op ->
                advance c
                match parseMul c with
                | Ok right ->
                    let opName = if op = Plus then "+" else "-"
                    result <- EApp(EApp(EVar opName, result), right)
                | Error _ -> cont <- false
            | _ -> cont <- false
        Ok result

and private parseCmp (c: Ctx) : Result<Expr, string> =
    match parseAdd c with
    | Error e -> Error e
    | Ok left ->
        let mutable result = left
        let mutable cont = true
        while cont do
            match curTok c with
            | Lt | Gt | Le | Ge | EqEq | Neq as op ->
                advance c
                match parseAdd c with
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
    match parseCmp c with
    | Error e -> Error e
    | Ok left ->
        let mutable result = left
        while curTok c = Arrow do
            advance c
            match parseCmp c with
            | Ok right -> result <- EPipe(result, right)
            | Error _ -> ()
        Ok result

and private parseExprInner (c: Ctx) : Result<Expr, string> =
    skipNewlines c
    match curTok c with
    | Indent ->
        // Expression position opens an indented block (e.g. body of `else`
        // on its own line). Inside the block, a sequence of `let` bindings
        // without the `in` keyword is folded into nested ELets whose body
        // is the remainder of the block — see `parseBlockExpr`.
        advance c
        skipNewlines c
        match parseBlockExpr c with
        | Error e -> Error e
        | Ok body ->
            skipNewlines c
            skip c Dedent |> ignore
            Ok body
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
            skipNewlines c
            match skip c KwThen with
            | Error e -> Error e
            | Ok () ->
                match parseExprInner c with
                | Error e -> Error e
                | Ok thenE ->
                    // Allow `else` on the next line (common multi-line form).
                    skipNewlines c
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

/// Parse an expression inside an indented block. If the parsed expression
/// is a bare `let name = e` (without `in`) AND there is more content on
/// subsequent lines at the same indent (i.e. no DEDENT/EOF in the way),
/// treat the rest of the block as the body: produces nested ELets.
/// This lets users write
///   let x = 1
///   let y = 2
///   x + y
/// instead of the chained `let ... in ... let ... in ...` single-liner.
and private parseBlockExpr (c: Ctx) : Result<Expr, string> =
    match parseExprInner c with
    | Error e -> Error e
    | Ok expr ->
        match expr with
        | ELet(name, e1, None) ->
            // Peek past any trailing newlines. If the next token is a
            // valid continuation (not Dedent/Eof/top-level-decl), parse
            // it as the body of this let.
            let saved = c.Pos
            while curTok c = Newline do advance c
            match curTok c with
            | Dedent | Eof | Bar ->
                // No continuation — restore position so the caller
                // still sees the trailing newlines/dedent and return
                // the bare let.
                c.Pos <- saved
                Ok expr
            | _ ->
                match parseBlockExpr c with
                | Ok body -> Ok (ELet(name, e1, Some body))
                | Error e -> Error e
        | _ -> Ok expr

/// Parse an expression from a token list. Returns (expr, remaining tokens).
let parseExpr (tokens: Tok list) : Result<Expr * Tok list, string> =
    let ctx = { Tokens = Array.ofList tokens; Pos = 0 }
    match parseExprInner ctx with
    | Error e -> Error e
    | Ok expr -> Ok (expr, ctx.Tokens |> Array.skip ctx.Pos |> Array.toList)

// ---- Type expression parser -----------------------------------------

let private parseTypeExpr (c: Ctx) : Result<TypeExpr, string> =
    let rec parseBase () =
        match curTok c with
        | TypeId name -> advance c; Ok (TyName name)
        | Ident name -> advance c; Ok (TyVar name)
        | LParen ->
            advance c
            match parseTypeExprTop () with
            | Error e -> Error e
            | Ok te ->
                match skip c RParen with
                | Error e -> Error e
                | Ok () -> Ok te
        | t -> Error $"Expected type at {(cur c).Line}:{(cur c).Col}, got {t}"

    and parseApp () =
        match parseBase () with
        | Error e -> Error e
        | Ok ty ->
            let mutable result = ty
            let mutable cont = true
            while cont && curTok c = LBrack do
                let saved = c.Pos
                advance c
                match parseTypeExprTop () with
                | Ok arg ->
                    match skip c RBrack with
                    | Ok () -> result <- TyApp(result, arg)
                    | Error _ -> c.Pos <- saved; cont <- false
                | Error _ -> c.Pos <- saved; cont <- false
            Ok result

    and parseTypeExprTop () =
        match parseApp () with
        | Error e -> Error e
        | Ok left ->
            if curTok c = Arrow then
                advance c
                match parseTypeExprTop () with
                | Ok right -> Ok (TyFn(left, right))
                | Error e -> Error e
            else Ok left

    parseTypeExprTop ()

// ---- Pattern parser --------------------------------------------------

let private parsePattern (c: Ctx) : Result<Pattern, string> =
    let rec parsePat () =
        match curTok c with
        | Underscore -> advance c; Ok PWild
        | Ident name -> advance c; Ok (PVar name)
        | IntLit n -> advance c; Ok (PLit (LInt n))
        | FloatLit f -> advance c; Ok (PLit (LFloat f))
        | StrLit s -> advance c; Ok (PLit (LStr s))
        | CharLit ch -> advance c; Ok (PLit (LChar ch))
        | KwTrue -> advance c; Ok (PLit (LBool true))
        | KwFalse -> advance c; Ok (PLit (LBool false))
        | TypeId name ->
            advance c
            // collect subpatterns (atoms only, not constructors with args)
            let args = ResizeArray<Pattern>()
            let mutable cont = true
            while cont do
                match curTok c with
                | Ident n -> args.Add(PVar n); advance c
                | Underscore -> args.Add(PWild); advance c
                | IntLit n -> args.Add(PLit (LInt n)); advance c
                | FloatLit f -> args.Add(PLit (LFloat f)); advance c
                | StrLit s -> args.Add(PLit (LStr s)); advance c
                | CharLit ch -> args.Add(PLit (LChar ch)); advance c
                | KwTrue -> args.Add(PLit (LBool true)); advance c
                | KwFalse -> args.Add(PLit (LBool false)); advance c
                | LParen ->
                    advance c
                    match parsePat () with
                    | Ok p ->
                        match skip c RParen with
                        | Ok () -> args.Add(p)
                        | Error _ -> cont <- false
                    | Error _ -> cont <- false
                | _ -> cont <- false
            Ok (PCon(name, List.ofSeq args))
        | LParen ->
            advance c
            match parsePat () with
            | Error e -> Error e
            | Ok p ->
                // If the next token is a comma, parse a tuple pattern: (p, p2, ..., pN)
                if curTok c = Comma then
                    let elems = ResizeArray<Pattern>()
                    elems.Add(p)
                    let mutable cont = true
                    while cont && curTok c = Comma do
                        advance c  // consume comma
                        match parsePat () with
                        | Ok pi -> elems.Add(pi)
                        | Error _ -> cont <- false
                    match skip c RParen with
                    | Error e -> Error e
                    | Ok () -> Ok (PTuple (List.ofSeq elems))
                else
                    match skip c RParen with
                    | Error e -> Error e
                    | Ok () -> Ok p
        | t -> Error $"Expected pattern, got {t}"
    parsePat ()

// ---- Match expression parser ----------------------------------------

let private parseMatchBranches (c: Ctx) : Result<Expr, string> =
    // We're inside an indented block already (or at top-level of fn body with |)
    let branches = ResizeArray<Pattern * Expr>()
    let mutable cont = true
    while cont && curTok c = Bar do
        advance c  // consume |
        match parsePattern c with
        | Error e -> Error e |> ignore; cont <- false
        | Ok pat ->
            match skip c Arrow with
            | Error e -> Error e |> ignore; cont <- false
            | Ok () ->
                match parseExprInner c with
                | Error e -> Error e |> ignore; cont <- false
                | Ok expr ->
                    branches.Add((pat, expr))
                    skipNewlines c
    if branches.Count > 0 then Ok (EMatch (List.ofSeq branches))
    else Error "Expected match branches"

// ---- Declaration parser ---------------------------------------------

let private parseParam (c: Ctx) : Result<Param, string> =
    match skip c LParen with
    | Error e -> Error e
    | Ok () ->
        match curTok c with
        | Ident name ->
            advance c
            match parseTypeExpr c with
            | Error e -> Error e
            | Ok ty ->
                match skip c RParen with
                | Error e -> Error e
                | Ok () -> Ok (name, ty)
        | t -> Error $"Expected param name, got {t}"

let private parseConstraint (c: Ctx) : Result<Constraint, string> =
    match skip c LBrack with
    | Error e -> Error e
    | Ok () ->
        match curTok c with
        | Ident v | TypeId v ->
            advance c
            match skip c Colon with
            | Error e -> Error e
            | Ok () ->
                match curTok c with
                | TypeId name ->
                    advance c
                    match skip c RBrack with
                    | Error e -> Error e
                    | Ok () -> Ok (v, name)
                | t -> Error $"Expected trait name in constraint, got {t}"
        | t -> Error $"Expected type var in constraint, got {t}"

let private parseFnSig (c: Ctx) : Result<FnSig, string> =
    match curTok c with
    | Ident name | TypeId name ->
        advance c
        let constraints = ResizeArray<Constraint>()
        let mutable cont = true
        while cont && curTok c = LBrack do
            let saved = c.Pos
            match parseConstraint c with
            | Ok cn -> constraints.Add(cn)
            | Error _ -> c.Pos <- saved; cont <- false
        let parms = ResizeArray<Param>()
        let mutable paramCont = true
        while paramCont && curTok c = LParen do
            let saved = c.Pos
            // Handle empty parens `()` as "no params" marker (e.g. `fn main()`).
            advance c
            if curTok c = RParen then
                advance c
                // `()` group contributes zero params; continue loop to allow mixing.
            else
                c.Pos <- saved
                match parseParam c with
                | Ok p -> parms.Add(p)
                | Error _ -> c.Pos <- saved; paramCont <- false
        let retType =
            match curTok c with
            | TypeId _ | Ident _ | LParen ->
                let saved = c.Pos
                match parseTypeExpr c with
                | Ok te -> Some te
                | Error _ -> c.Pos <- saved; None
            | _ -> None
        Ok { Name = name; Constraints = List.ofSeq constraints; Params = List.ofSeq parms; ReturnType = retType }
    | t -> Error $"Expected function name, got {t}"

let private parseFnBody (c: Ctx) : Result<Expr, string> =
    // fn body: either direct expr, or indented block, or match branches starting with |
    skipNewlines c
    match curTok c with
    | Bar ->
        // Pattern match branches at top level of fn
        parseMatchBranches c
    | Indent ->
        advance c  // consume INDENT
        skipNewlines c
        let result =
            match curTok c with
            | Bar -> parseMatchBranches c
            | _ -> parseBlockExpr c  // Phase 6.7: allow indented let-chains
        skipNewlines c
        skip c Dedent |> ignore
        result
    | _ -> parseExprInner c

let private parseTypeBody (c: Ctx) : Result<TypeBody, string> =
    match curTok c with
    | TypeId _ ->
        // Sum type: Ctor1 args | Ctor2 args | ...
        let ctors = ResizeArray<TypeIdent * TypeExpr list>()
        let mutable cont = true
        while cont do
            match curTok c with
            | TypeId name ->
                advance c
                let args = ResizeArray<TypeExpr>()
                let mutable argCont = true
                while argCont do
                    match curTok c with
                    | TypeId _ | Ident _ | LParen ->
                        let saved = c.Pos
                        match parseTypeExpr c with
                        | Ok te -> args.Add(te)
                        | Error _ -> c.Pos <- saved; argCont <- false
                    | _ -> argCont <- false
                ctors.Add((name, List.ofSeq args))
                if curTok c = Bar then advance c
                else cont <- false
            | _ -> cont <- false
        if ctors.Count > 0 then Ok (TBSum (List.ofSeq ctors))
        else Error "Expected type body"
    | Ident fieldName ->
        // Could be record (field Type, field Type) or wrapped/newtype
        let saved = c.Pos
        advance c
        match curTok c with
        | TypeId _ | Ident _ | LParen ->
            // Looks like a record field: name Type
            match parseTypeExpr c with
            | Ok ty ->
                let fields = ResizeArray<Ident * TypeExpr>()
                fields.Add((fieldName, ty))
                while curTok c = Comma do
                    advance c
                    match curTok c with
                    | Ident fn ->
                        advance c
                        match parseTypeExpr c with
                        | Ok ft -> fields.Add((fn, ft))
                        | Error _ -> ()
                    | _ -> ()
                Ok (TBRecord (List.ofSeq fields))
            | Error _ ->
                c.Pos <- saved
                match parseTypeExpr c with
                | Ok te -> Ok (TBWrapped te)
                | Error e -> Error e
        | _ ->
            c.Pos <- saved
            match parseTypeExpr c with
            | Ok te -> Ok (TBWrapped te)
            | Error e -> Error e
    | _ ->
        match parseTypeExpr c with
        | Ok te -> Ok (TBWrapped te)
        | Error e -> Error e

let private parseDecl (c: Ctx) : Result<Decl, string> =
    skipNewlines c
    match curTok c with
    | KwFn ->
        advance c
        match parseFnSig c with
        | Error e -> Error e
        | Ok sig' ->
            match skip c Eq with
            | Error e -> Error e
            | Ok () ->
                match parseFnBody c with
                | Ok expr -> Ok (DFn(sig', expr))
                | Error e -> Error e
    | KwLet ->
        advance c
        match curTok c with
        | Ident name ->
            advance c
            match skip c Eq with
            | Error e -> Error e
            | Ok () ->
                match parseExprInner c with
                | Ok expr -> Ok (DLet(name, expr))
                | Error e -> Error e
        | t -> Error $"Expected identifier after 'let', got {t}"
    | KwType ->
        advance c
        match curTok c with
        | TypeId name ->
            advance c
            let parms = ResizeArray<TypeParam>()
            let mutable cont = true
            while cont do
                match curTok c with
                | Ident p | TypeId p -> parms.Add(TPBare p); advance c
                | LBrack ->
                    let saved = c.Pos
                    advance c
                    match curTok c with
                    | Ident p | TypeId p ->
                        advance c
                        if curTok c = RBrack then
                            advance c
                            parms.Add(TPPhantom p)
                        else
                            c.Pos <- saved; cont <- false
                    | _ -> c.Pos <- saved; cont <- false
                | _ -> cont <- false
            match skip c Eq with
            | Error e -> Error e
            | Ok () ->
                match parseTypeBody c with
                | Ok body -> Ok (DType(name, List.ofSeq parms, body))
                | Error e -> Error e
        | t -> Error $"Expected type name, got {t}"
    | KwTag ->
        advance c
        match curTok c with
        | TypeId name -> advance c; Ok (DTag name)
        | Ident name -> advance c; Ok (DTag name)  // tag m, tag s (lowercase)
        | t -> Error $"Expected tag name, got {t}"
    | KwUnit ->
        advance c
        match curTok c with
        | TypeId name -> advance c; Ok (DUnit name)
        | Ident name -> advance c; Ok (DUnit name)
        | t -> Error $"Expected unit name, got {t}"
    | KwTrait ->
        advance c
        match curTok c with
        | TypeId name ->
            advance c
            let tvars = ResizeArray<Ident>()
            let mutable cont = true
            while cont do
                match curTok c with
                | Ident v | TypeId v -> tvars.Add(v); advance c
                | _ -> cont <- false
            match skip c Eq with
            | Error e -> Error e
            | Ok () ->
                skipNewlines c
                match skip c Indent with
                | Error e -> Error e
                | Ok () ->
                    let sigs = ResizeArray<FnSig>()
                    skipNewlines c
                    while curTok c <> Dedent && curTok c <> Eof do
                        skipNewlines c
                        if curTok c = Dedent || curTok c = Eof then ()
                        else
                            match skip c KwFn with
                            | Error _ -> ()
                            | Ok () ->
                                match parseFnSig c with
                                | Ok s -> sigs.Add(s)
                                | Error _ -> ()
                        skipNewlines c
                    skip c Dedent |> ignore
                    Ok (DTrait(name, List.ofSeq tvars, List.ofSeq sigs))
        | t -> Error $"Expected trait name, got {t}"
    | KwImpl ->
        advance c
        match curTok c with
        | TypeId traitName ->
            advance c
            match curTok c with
            | TypeId typeName ->
                advance c
                match skip c Eq with
                | Error e -> Error e
                | Ok () ->
                    skipNewlines c
                    match skip c Indent with
                    | Error e -> Error e
                    | Ok () ->
                        let impls = ResizeArray<FnSig * Expr>()
                        skipNewlines c
                        while curTok c <> Dedent && curTok c <> Eof do
                            skipNewlines c
                            if curTok c = Dedent || curTok c = Eof then ()
                            else
                                match skip c KwFn with
                                | Error _ -> advance c  // skip bad token
                                | Ok () ->
                                    match parseFnSig c with
                                    | Error _ -> ()
                                    | Ok sig' ->
                                        match skip c Eq with
                                        | Error _ -> ()
                                        | Ok () ->
                                            match parseFnBody c with
                                            | Ok expr -> impls.Add((sig', expr))
                                            | Error _ -> ()
                            skipNewlines c
                        skip c Dedent |> ignore
                        Ok (DImpl(traitName, typeName, List.ofSeq impls))
            | t -> Error $"Expected type name in impl, got {t}"
        | t -> Error $"Expected trait name in impl, got {t}"
    | t -> Error $"Unexpected token {t} at {(cur c).Line}:{(cur c).Col}"

// ---- Module parser --------------------------------------------------

/// Parse a full ll-lang module from a token list.
let parseModule (tokens: Tok list) : Result<LLModule, string> =
    let c = { Tokens = Array.ofList tokens; Pos = 0 }
    skipNewlines c
    match skip c KwModule with
    | Error e -> Error e
    | Ok () ->
        let path = ResizeArray<string>()
        let mutable cont = true
        while cont do
            match curTok c with
            | TypeId seg -> path.Add(seg); advance c
            | Ident seg -> path.Add(seg); advance c
            | _ -> cont <- false
            if cont then
                if curTok c = Dot then advance c
                else cont <- false
        skipNewlines c
        let imports = ResizeArray<string list>()
        while curTok c = KwImport do
            advance c
            let parts = ResizeArray<string>()
            let mutable ic = true
            while ic do
                match curTok c with
                | TypeId seg -> parts.Add(seg); advance c
                | Ident seg -> parts.Add(seg); advance c
                | _ -> ic <- false
                if ic then
                    if curTok c = Dot then advance c
                    else ic <- false
            imports.Add(List.ofSeq parts)
            skipNewlines c
        let decls = ResizeArray<Decl * bool>()
        while curTok c <> Eof do
            skipNewlines c
            if curTok c = Eof then ()
            else
                let exported = curTok c = KwExport
                if exported then advance c
                match parseDecl c with
                | Ok d -> decls.Add((d, exported)); skipNewlines c
                | Error _ -> advance c  // skip on error to continue
        Ok {
            Path = List.ofSeq path
            Imports = List.ofSeq imports
            Decls = List.ofSeq decls
        }
