module LLLang.Parser

open LLLang.Token
open LLLang.AST

// ---- Parser state ---------------------------------------------------

type private Ctx = {
    Tokens: Tok array
    mutable Pos: int
    Positions: PosMap
}

let private cur (c: Ctx) = c.Tokens[c.Pos]
let private curTok (c: Ctx) = c.Tokens[c.Pos].Token

let private advance (c: Ctx) =
    if c.Pos < c.Tokens.Length - 1 then c.Pos <- c.Pos + 1

/// Record the source position of an AST node using the position of the
/// token at the given index. Returns the node for fluent chaining.
/// The PosMap is keyed by reference-equality so each allocated AST node
/// gets its own entry; duplicate sub-expressions with the same structure
/// stay distinct.
let inline private recordAt (c: Ctx) (tokIdx: int) (node: 'a) : 'a =
    let t = c.Tokens[tokIdx]
    PosMap.add c.Positions (box node) { Line = t.Line; Col = t.Col }
    node

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
    let startIdx = c.Pos
    match curTok c with
    | IntLit n -> advance c; Ok (ELit (LInt n))
    | FloatLit f -> advance c; Ok (ELit (LFloat f))
    | StrLit s -> advance c; Ok (ELit (LStr s))
    | CharLit ch -> advance c; Ok (ELit (LChar ch))
    | KwTrue -> advance c; Ok (ELit (LBool true))
    | KwFalse -> advance c; Ok (ELit (LBool false))
    | Ident name ->
        advance c
        // Position-tag EVar: E002 on unbound var looks this up.
        Ok (recordAt c startIdx (EVar name))
    | TypeId name ->
        advance c
        // Position-tag ECon: E002 on unbound constructor looks this up.
        Ok (recordAt c startIdx (ECon name))
    | LParen ->
        advance c
        skipNewlines c
        match parseExprInner c with
        | Error e -> Error e
        | Ok expr ->
            // Phase 7.2.1: tuple literal — `(e1, e2, ..., eN)` produces ETuple.
            // A bare `(e)` (no comma) stays as the inner expression: parens
            // are just grouping. Trailing commas like `(a,)` are rejected.
            if curTok c = Comma then
                let elems = ResizeArray<Expr>()
                elems.Add(expr)
                let mutable cont = true
                let mutable err : string option = None
                while cont && curTok c = Comma do
                    advance c  // consume comma
                    skipNewlines c
                    // Reject trailing comma: `(a,)` — Comma followed by RParen.
                    if curTok c = RParen then
                        let t = cur c
                        err <- Some $"Trailing comma in tuple literal at {t.Line}:{t.Col}"
                        cont <- false
                    else
                        match parseExprInner c with
                        | Error e -> err <- Some e; cont <- false
                        | Ok e ->
                            elems.Add(e)
                            skipNewlines c
                match err with
                | Some e -> Error e
                | None ->
                    match skip c RParen with
                    | Error e -> Error e
                    | Ok () ->
                        Ok (recordAt c startIdx (ETuple (List.ofSeq elems)))
            else
                match skip c RParen with
                | Error e -> Error e
                | Ok () -> Ok expr
    | LBrack ->
        advance c
        skipNewlines c
        let elems = ResizeArray<Expr>()
        let mutable lstCont = true
        // Phase 7.3b bug 1: an element that STARTS with a TypeId (a
        // constructor) is parsed via `parseApp` so juxtaposition-application
        // lands in the list literal directly — `[TNum 42]` becomes
        // EList [EApp(TNum, 42)] (one element), not EList [TNum; 42] (two
        // elements). Lowercase-atom / literal-starting elements still fall
        // back to `parseTagged`, so `[1 2 3]` keeps its three-element shape
        // and `[tok]` remains single-atom. This makes `[TNum 42]` a one-element
        // list — users wanting multiple ctor elements must use listAppend /
        // cons rather than whitespace-separate them.
        while lstCont && curTok c <> RBrack && curTok c <> Eof do
            let parseElem =
                match curTok c with
                | TypeId _ -> parseApp
                | _ -> parseTagged
            match parseElem c with
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
        // Phase 7.2.2: `[X]` is consumed as a tag suffix only when the
        // preceding atom is a literal (`ELit _`). For Var / Con / App / List
        // results the `[X]` is left for the outer `parseApp` so it becomes a
        // fresh list-literal argument. This unblocks idiomatic application
        // like `cons TPlus [TMinus]` (would otherwise parse as
        // `cons (ETagged TPlus "TMinus")`) or `listAppend [TFoo] xs` without
        // forcing helper wrappers like the old `pre` function in
        // 09-lexer-real.lll.
        //
        // The tag name itself can be either an Ident (lowercase, e.g. `m`,
        // `s`, `kg`) or a TypeId (uppercase, e.g. `UserId`). Both forms are
        // declared the same way via `tag <name>` at the top level.
        match atom with
        | ELit _ when curTok c = LBrack ->
            let saved = c.Pos
            advance c
            match curTok c with
            | Ident name | TypeId name ->
                advance c
                match skip c RBrack with
                | Ok () -> Ok (ETagged(atom, name))
                | Error _ -> c.Pos <- saved; Ok atom  // not a tag, backtrack
            | _ ->
                c.Pos <- saved
                Ok atom
        | _ -> Ok atom

/// Application parser. When `crossNewlines` is true (normal use) a
/// Newline+Indent sequence is treated as a continuation-argument block.
/// When `crossNewlines` is false (if-condition parsing) the parser
/// stops at the first Newline so the if-body stays separate.
and private parseAppWith (crossNewlines: bool) (c: Ctx) : Result<Expr, string> =
    match parseTagged c with
    | Error e -> Error e
    | Ok first ->
        let mutable result = first
        let mutable cont = true
        while cont do
            // Do NOT skip newlines here in the inline branch: newlines
            // terminate an application in expression position so that
            // indented `let` chains can be parsed correctly.
            //
            // Phase 7.3b bug 2: a multi-line call is opened by a Newline+
            // Indent sequence on the line right after the function position.
            // When we see that shape AND the indented block begins with an
            // atom-starting token, treat the whole block as a continuation-
            // argument list for `result`, collect every atom it contains
            // (across inner newlines at the same block level), consume the
            // matching Dedent, then resume the outer loop in case there are
            // still more args on the parent line after the Dedent.
            match curTok c with
            | IntLit _ | FloatLit _ | StrLit _ | CharLit _ | KwTrue | KwFalse
            | Ident _ | TypeId _ | LParen | LBrack ->
                let argIdx = c.Pos
                match parseTagged c with
                | Ok arg ->
                    // Position of EApp = position of the argument token,
                    // which is where `E001 TypeMismatch` wants to point.
                    result <- recordAt c argIdx (EApp(result, arg))
                | Error _ -> cont <- false
            | Newline when crossNewlines ->
                // Peek past any newlines to see if we're at a multi-line
                // continuation block (Indent + atom). If not, rewind.
                let saved = c.Pos
                while curTok c = Newline do advance c
                if curTok c = Indent then
                    advance c
                    while curTok c = Newline do advance c
                    let isAtomStart =
                        match curTok c with
                        | IntLit _ | FloatLit _ | StrLit _ | CharLit _
                        | KwTrue | KwFalse | Ident _ | TypeId _
                        | LParen | LBrack -> true
                        | _ -> false
                    if isAtomStart then
                        // Consume every atom in the block as a continuation
                        // argument. Newlines between atoms are transparent
                        // inside the block (same indent level = no nested
                        // Indent/Dedent). Stop at Dedent — that's the block
                        // boundary — or at any non-atom token.
                        let mutable blockCont = true
                        while blockCont do
                            while curTok c = Newline do advance c
                            match curTok c with
                            | IntLit _ | FloatLit _ | StrLit _ | CharLit _
                            | KwTrue | KwFalse | Ident _ | TypeId _
                            | LParen | LBrack ->
                                let argIdx = c.Pos
                                match parseTagged c with
                                | Ok arg ->
                                    result <- recordAt c argIdx (EApp(result, arg))
                                | Error _ -> blockCont <- false
                            | _ -> blockCont <- false
                        while curTok c = Newline do advance c
                        // Consume the Dedent that closes the continuation
                        // block. If it's missing something has gone wrong
                        // upstream; fall back to stopping the outer loop.
                        match skip c Dedent with
                        | Ok () -> ()
                        | Error _ -> cont <- false
                    else
                        c.Pos <- saved
                        cont <- false
                else
                    c.Pos <- saved
                    cont <- false
            | _ -> cont <- false
        Ok result

and private parseApp (c: Ctx) : Result<Expr, string> = parseAppWith true c

/// Application parser that stops at newlines — used for if-condition
/// parsing so that an indented body is not consumed as an argument.
and private parseAppInline (c: Ctx) : Result<Expr, string> = parseAppWith false c

and private parseMulWith (app: Ctx -> Result<Expr, string>) (c: Ctx) : Result<Expr, string> =
    match app c with
    | Error e -> Error e
    | Ok left ->
        let mutable result = left
        let mutable cont = true
        while cont do
            match curTok c with
            | Star | Slash as op ->
                let opIdx = c.Pos
                advance c
                match app c with
                | Ok right ->
                    let opName = if op = Star then "*" else "/"
                    // Tag both the operator EVar and the outer EApp at the
                    // operator token's position. This makes E001 / E002 on
                    // binary operators report the operator column.
                    let opVar = recordAt c opIdx (EVar opName)
                    let inner = recordAt c opIdx (EApp(opVar, result))
                    result <- recordAt c opIdx (EApp(inner, right))
                | Error _ -> cont <- false
            | _ -> cont <- false
        Ok result

and private parseMul (c: Ctx) : Result<Expr, string> = parseMulWith parseApp c
and private parseMulInline (c: Ctx) : Result<Expr, string> = parseMulWith parseAppInline c

and private parseAddWith (mul: Ctx -> Result<Expr, string>) (c: Ctx) : Result<Expr, string> =
    match mul c with
    | Error e -> Error e
    | Ok left ->
        let mutable result = left
        let mutable cont = true
        while cont do
            match curTok c with
            | Plus | Minus as op ->
                let opIdx = c.Pos
                advance c
                match mul c with
                | Ok right ->
                    let opName = if op = Plus then "+" else "-"
                    let opVar = recordAt c opIdx (EVar opName)
                    let inner = recordAt c opIdx (EApp(opVar, result))
                    result <- recordAt c opIdx (EApp(inner, right))
                | Error _ -> cont <- false
            | _ -> cont <- false
        Ok result

and private parseAdd (c: Ctx) : Result<Expr, string> = parseAddWith parseMul c
and private parseAddInline (c: Ctx) : Result<Expr, string> = parseAddWith parseMulInline c

and private parseCons (c: Ctx) : Result<Expr, string> =
    // Right-associative cons. `1 :: 2 :: xs` -> ECons(1, ECons(2, xs)).
    // Precedence sits between `==` (parseCmp) and `+` (parseAdd):
    //   a == 1 :: rest        -> a == (1 :: rest)
    //   1 + 2 :: xs           -> (1 + 2) :: xs
    match parseAdd c with
    | Error e -> Error e
    | Ok left ->
        if curTok c = ColonColon then
            advance c
            match parseCons c with
            | Ok right -> Ok (ECons(left, right))
            | Error e -> Error e
        else Ok left

and private parseConsInline (c: Ctx) : Result<Expr, string> =
    match parseAddInline c with
    | Error e -> Error e
    | Ok left ->
        if curTok c = ColonColon then
            advance c
            match parseConsInline c with
            | Ok right -> Ok (ECons(left, right))
            | Error e -> Error e
        else Ok left

and private parseCmpWith (cons: Ctx -> Result<Expr, string>) (c: Ctx) : Result<Expr, string> =
    match cons c with
    | Error e -> Error e
    | Ok left ->
        let mutable result = left
        let mutable cont = true
        while cont do
            match curTok c with
            | Lt | Gt | Le | Ge | EqEq | Neq as op ->
                let opIdx = c.Pos
                advance c
                match cons c with
                | Ok right ->
                    let opName =
                        match op with
                        | Lt -> "<" | Gt -> ">" | Le -> "<=" | Ge -> ">="
                        | EqEq -> "==" | Neq -> "!=" | _ -> "?"
                    let opVar = recordAt c opIdx (EVar opName)
                    let inner = recordAt c opIdx (EApp(opVar, result))
                    result <- recordAt c opIdx (EApp(inner, right))
                | Error _ -> cont <- false
            | _ -> cont <- false
        Ok result

and private parseCmp (c: Ctx) : Result<Expr, string> = parseCmpWith parseCons c
and private parseCmpInline (c: Ctx) : Result<Expr, string> = parseCmpWith parseConsInline c

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

/// Pipe-precedence expression that does NOT consume Newline+Indent
/// continuation blocks. Used for parsing if-conditions so the
/// indented body is not mistaken for application arguments.
and private parsePipeInline (c: Ctx) : Result<Expr, string> =
    match parseCmpInline c with
    | Error e -> Error e
    | Ok left ->
        let mutable result = left
        while curTok c = Arrow do
            advance c
            match parseCmpInline c with
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
        // Phase 7.1.6: try pattern-first. If the pattern is a bare PVar
        // we fall back to the existing ELet form so all downstream code
        // that special-cases ELet (codegen, etc) keeps working.
        let saved = c.Pos
        match parsePattern c with
        | Error _ ->
            c.Pos <- saved
            Error $"Expected pattern after 'let' at {(cur c).Line}:{(cur c).Col}"
        | Ok pat ->
            // Only accept the pattern if `=` immediately follows; otherwise
            // restore. This matters for forms like `let x = e` where parsePattern
            // would succeed greedily on `x` but the standard path is fine.
            if curTok c <> Eq then
                c.Pos <- saved
                Error $"Expected '=' after pattern in let, got {curTok c}"
            else
                advance c  // consume =
                match parseExprInner c with
                | Error e -> Error e
                | Ok e1 ->
                    let body = None
                    match pat with
                    | PVar name -> Ok (ELet(name, e1, body))
                    | _ -> Ok (ELetPat(pat, e1, body))
    | KwIf ->
        advance c
        // Use parsePipeInline for the condition so that a following
        // Newline+Indent block is NOT consumed as application arguments —
        // that block is the if-body, not a continuation of the condition.
        match parsePipeInline c with
        | Error e -> Error e
        | Ok cond ->
            skipNewlines c
            // `then` has been removed from the language. The condition is
            // separated from the body by a newline (already consumed by
            // skipNewlines above). The body begins at the current token.
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
    | KwMatch ->
        // match <scrut> with | pat -> body | pat -> body ...
        // Arms can appear inline OR on subsequent indented lines.
        let matchIdx = c.Pos
        advance c
        match parseExprInner c with
        | Error e -> Error e
        | Ok scrut ->
            skipNewlines c
            match skip c KwWith with
            | Error e -> Error e
            | Ok () ->
                skipNewlines c
                // Optional INDENT for indented arm block.
                let hadIndent = curTok c = Indent
                if hadIndent then advance c
                skipNewlines c
                // Phase 7.3a bugfix (bug 1): arm bodies use parseBlockExpr
                // so multi-line `let .. in` chains inside a match arm fold
                // their continuations at the arm's indent level and keep
                // their bindings in scope. parseExprInner alone would hand
                // off only the first let to the arm, leaving subsequent
                // ones to float out to the surrounding module and produce
                // E002 UnboundVar on the names they were meant to bind.
                //
                // Phase 7.3a bugfix (bug 2): propagate the first arm-level
                // parse error via `armErr` instead of silently dropping
                // every arm after a bad pattern. With the old behaviour a
                // single `| [] -> ...` arm would truncate the whole match
                // and leave only the arms that happened to come before it
                // in the AST, producing a runtime MatchFailure.
                let branches = ResizeArray<Pattern * Expr>()
                let mutable cont = true
                let mutable armErr : string option = None
                while cont && curTok c = Bar do
                    advance c  // consume |
                    match parsePattern c with
                    | Error e -> armErr <- Some e; cont <- false
                    | Ok pat ->
                        match skip c Arrow with
                        | Error e -> armErr <- Some e; cont <- false
                        | Ok () ->
                            match parseBlockExpr c with
                            | Error e -> armErr <- Some e; cont <- false
                            | Ok body ->
                                branches.Add((pat, body))
                                skipNewlines c
                if hadIndent then skip c Dedent |> ignore
                match armErr with
                | Some e -> Error e
                | None ->
                    // Tag at the `match` keyword so E003 non-exhaustive points
                    // at the match expression rather than 0:0.
                    if branches.Count > 0 then Ok (recordAt c matchIdx (EMatchOf(scrut, List.ofSeq branches)))
                    else Error "Expected match branches after 'with'"
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
                c.Pos <- saved
                Ok expr
            | _ ->
                match parseBlockExpr c with
                | Ok body -> Ok (ELet(name, e1, Some body))
                | Error e -> Error e
        | ELetPat(pat, e1, None) ->
            // Same continuation-handling for the destructuring let form.
            let saved = c.Pos
            while curTok c = Newline do advance c
            match curTok c with
            | Dedent | Eof | Bar ->
                c.Pos <- saved
                Ok expr
            | _ ->
                match parseBlockExpr c with
                | Ok body -> Ok (ELetPat(pat, e1, Some body))
                | Error e -> Error e
        | _ -> Ok expr

// ---- Pattern parser --------------------------------------------------
// In the same `let rec` group as parseExprInner so the `match`-expression
// branch of parseExprInner can call parsePattern, and so PCons-recursive
// pattern parsing can recurse into parsePatternInner.

and private parsePattern (c: Ctx) : Result<Pattern, string> =
    parsePatCons c

and private parsePatCons (c: Ctx) : Result<Pattern, string> =
    // Right-associative cons. `a :: b :: rest` -> PCons(a, PCons(b, rest)).
    // Cons is the lowest-precedence pattern form, so a constructor like
    // `Some x` still parses as PCon-with-args before the `::` layer.
    match parsePatAtom c with
    | Error e -> Error e
    | Ok lhs ->
        if curTok c = ColonColon then
            advance c
            match parsePatCons c with
            | Ok rhs -> Ok (PCons(lhs, rhs))
            | Error e -> Error e
        else Ok lhs

and private parsePatAtom (c: Ctx) : Result<Pattern, string> =
    match curTok c with
    | Underscore -> advance c; Ok PWild
    | Ident name -> advance c; Ok (PVar name)
    | IntLit n -> advance c; Ok (PLit (LInt n))
    | FloatLit f -> advance c; Ok (PLit (LFloat f))
    | StrLit s -> advance c; Ok (PLit (LStr s))
    | CharLit ch -> advance c; Ok (PLit (LChar ch))
    | KwTrue -> advance c; Ok (PLit (LBool true))
    | KwFalse -> advance c; Ok (PLit (LBool false))
    | LBrack ->
        // Phase 7.3a bugfix (bug 2): list-literal patterns.
        //   []                → PCon("[]", [])                 (empty list)
        //   [p]               → PCons(p, PCon("[]", []))       (one-elem)
        //   [p1, p2, ..., pN] → PCons(p1, PCons(p2, ..., PCon("[]", [])))
        // The special ctor name "[]" is recognised by HMInfer's patternType
        // (list Nil) and by Codegen.emitPattern (rendered as the F# `[]`
        // literal). Elements are comma-separated, matching the tuple and
        // list-literal expression surface syntax used elsewhere. This
        // unblocks idiomatic `| [] -> ...` arms in fn-body clause sugar
        // that the old parser silently dropped (see bug 2).
        advance c
        skipNewlines c
        if curTok c = RBrack then
            advance c
            Ok (PCon("[]", []))
        else
            let elems = ResizeArray<Pattern>()
            let rec readOne () : Result<unit, string> =
                match parsePatCons c with
                | Error e -> Error e
                | Ok p ->
                    elems.Add(p)
                    skipNewlines c
                    if curTok c = Comma then
                        advance c
                        skipNewlines c
                        readOne ()
                    else Ok ()
            match readOne () with
            | Error e -> Error e
            | Ok () ->
                match skip c RBrack with
                | Error e -> Error e
                | Ok () ->
                    // Fold elements right-associatively into a cons chain
                    // terminated by the empty-list constructor.
                    let nil = PCon("[]", [])
                    let result =
                        Seq.foldBack (fun p acc -> PCons(p, acc)) elems nil
                    Ok result
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
                match parsePatCons c with
                | Ok p ->
                    match skip c RParen with
                    | Ok () -> args.Add(p)
                    | Error _ -> cont <- false
                | Error _ -> cont <- false
            | _ -> cont <- false
        Ok (PCon(name, List.ofSeq args))
    | LParen ->
        advance c
        match parsePatCons c with
        | Error e -> Error e
        | Ok p ->
            // If the next token is a comma, parse a tuple pattern: (p, p2, ..., pN)
            if curTok c = Comma then
                let elems = ResizeArray<Pattern>()
                elems.Add(p)
                let mutable cont = true
                while cont && curTok c = Comma do
                    advance c  // consume comma
                    match parsePatCons c with
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

/// Parse an expression from a token list. Returns (expr, remaining tokens).
/// Position information is discarded — use `parseExprWithPos` if you need it.
let parseExpr (tokens: Tok list) : Result<Expr * Tok list, string> =
    let ctx = { Tokens = Array.ofList tokens; Pos = 0; Positions = PosMap.create () }
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
            // Phase 7.3b bug 3: inside `( ... )` we also accept juxtaposition
            // as type application — `(Maybe TypeRef)` parses to the same
            // `TyApp(Maybe, TypeRef)` that `Maybe[TypeRef]` produces. This
            // is scoped to the parenthesised group (the top-level sum-arm
            // loop still reads `Rect Float Float` as a ctor with two
            // independent args). After the first inner type, fold any
            // additional atom-starting types into a left-associative TyApp
            // chain until we hit RParen.
            advance c
            match parseTypeExprTop () with
            | Error e -> Error e
            | Ok first ->
                let mutable result = first
                let mutable jxCont = true
                while jxCont do
                    match curTok c with
                    | TypeId _ | Ident _ | LParen ->
                        let saved = c.Pos
                        match parseTypeExprTop () with
                        | Ok next -> result <- TyApp(result, next)
                        | Error _ -> c.Pos <- saved; jxCont <- false
                    | _ -> jxCont <- false
                match skip c RParen with
                | Error e -> Error e
                | Ok () -> Ok result
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

// ---- Match expression parser ----------------------------------------

let private parseMatchBranches (c: Ctx) : Result<Expr, string> =
    // We're inside an indented block already (or at top-level of fn body with |)
    // Phase 7.3a bugfix (bug 1): the arm body is parsed with parseBlockExpr
    // (not parseExprInner) so multi-line `let .. in` chains inside a
    // clause-sugar arm body fold their continuations at the arm's indent
    // level and keep their bindings in scope. With parseExprInner alone
    // only the first `let` got attached to the arm; the rest floated to
    // top level and the body referenced names the module parser never
    // saw, producing E002 UnboundVar.
    //
    // Phase 7.3a bugfix (bug 2): propagate the first arm-level parse
    // error via `armErr` instead of silently dropping every arm after a
    // bad pattern. With the old behaviour a single `| [] -> ...` arm
    // would truncate the whole clause-sugar body and leave only the arms
    // that happened to come before it in the AST, producing a runtime
    // MatchFailure when a dropped arm matched at runtime.
    let startIdx = c.Pos
    let branches = ResizeArray<Pattern * Expr>()
    let mutable cont = true
    let mutable armErr : string option = None
    while cont && curTok c = Bar do
        advance c  // consume |
        match parsePattern c with
        | Error e -> armErr <- Some e; cont <- false
        | Ok pat ->
            match skip c Arrow with
            | Error e -> armErr <- Some e; cont <- false
            | Ok () ->
                match parseBlockExpr c with
                | Error e -> armErr <- Some e; cont <- false
                | Ok expr ->
                    branches.Add((pat, expr))
                    skipNewlines c
    match armErr with
    | Some e -> Error e
    | None ->
        // Tag at the first `|` so E003 non-exhaustive points to the first arm.
        if branches.Count > 0 then Ok (recordAt c startIdx (EMatch (List.ofSeq branches)))
        else Error "Expected match branches"

// ---- Declaration parser ---------------------------------------------

let private parseParam (c: Ctx) : Result<Param, string> =
    match skip c LParen with
    | Error e -> Error e
    | Ok () ->
        match curTok c with
        | Ident name ->
            advance c
            // Phase 6.8: allow `(name)` with no type annotation. Mark with
            // TyVar "?" so HMInfer can replace it with a fresh flex var at
            // top-level inference time. Enables `fn fst(p) = ...` and allows
            // inference to discover e.g. `(A, B) -> A` for tuple-destructuring
            // fns without a tuple-type surface syntax.
            if curTok c = RParen then
                advance c
                Ok (name, TyVar "?")
            else
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
        let mutable paramErr : string option = None
        while paramCont && paramErr.IsNone && curTok c = LParen do
            let saved = c.Pos
            // Handle empty parens `()` as "no params" marker (e.g. `fn main()`).
            advance c
            if curTok c = RParen then
                advance c
                // `()` group contributes zero params; continue loop to allow mixing.
            else
                // Phase 7.5d bugfix: committed-param detection. After
                // advancing past `(`, decide whether this group is
                // definitely a named param (committed) or could still be a
                // parenthesised return type (non-committed, so a parse
                // failure should just stop the param loop, not kill the
                // whole decl).
                //
                // A parenthesised return type must START with something a
                // return type can start with: `TypeId` (e.g. `(Int)`) or
                // nested `LParen` (e.g. `((Int))`). Anything else — in
                // particular any KEYWORD like `KwTag` for `(tag Int)` —
                // can only be a broken named param, and we propagate the
                // `parseParam` error upward. The old behaviour silently
                // rewound and abandoned the param loop, leaving the `(`
                // in the stream so the outer `skip c Eq` failed with a
                // confusing `Expected Eq, got LParen`. Combined with the
                // parseModule swallow (also fixed in Phase 7.5d), that
                // stranded the whole fn decl and cascaded into cryptic
                // `E002 UnboundVar` at every call site. Lowercase `Ident`
                // is always a committed param too — `parseParam` handles
                // both `(name Type)` and the untyped `(name)` shape.
                let committed =
                    match curTok c with
                    | TypeId _ | LParen | RParen -> false
                    | _ -> true   // Ident + any keyword + anything else
                c.Pos <- saved
                match parseParam c with
                | Ok p -> parms.Add(p)
                | Error e ->
                    if committed then paramErr <- Some e
                    else c.Pos <- saved; paramCont <- false
        match paramErr with
        | Some e -> Error e
        | None ->
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
    // Helper: parse a single sum-type arm `Ctor arg1 arg2 ...` starting at a TypeId.
    // Returns Ok None if curTok is not a TypeId.
    let parseSumArm () : Result<(TypeIdent * TypeExpr list) option, string> =
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
            Ok (Some (name, List.ofSeq args))
        | _ -> Ok None

    // Phase 7.1.6: multi-line sum type form.
    //   type Token =
    //     | TIdent Str
    //     | TNum Str
    //     | TLParen
    // After `=`, the parser sees Newline+Indent. Parse a sequence of `| Ctor args`
    // arms separated by newlines until Dedent. Both forms produce TBSum.
    let tryParseMultiLineSum () : Result<TypeBody, string> option =
        // Save position so we can rewind if it's not actually a multi-line sum.
        let saved = c.Pos
        // Skip leading newlines and look for INDENT followed by `|`.
        while curTok c = Newline do advance c
        if curTok c = Indent then
            advance c
            while curTok c = Newline do advance c
            if curTok c = Bar then
                let ctors = ResizeArray<TypeIdent * TypeExpr list>()
                let mutable cont = true
                let mutable err = None
                while cont && curTok c = Bar do
                    advance c  // consume |
                    match parseSumArm () with
                    | Ok (Some arm) ->
                        ctors.Add(arm)
                        // Skip trailing newlines between arms.
                        while curTok c = Newline do advance c
                    | Ok None ->
                        err <- Some "Expected constructor name after '|' in sum type body"
                        cont <- false
                    | Error e ->
                        err <- Some e
                        cont <- false
                // Consume the closing DEDENT (and trailing newlines).
                while curTok c = Newline do advance c
                skip c Dedent |> ignore
                match err with
                | Some e -> Some (Error e)
                | None ->
                    if ctors.Count > 0 then Some (Ok (TBSum (List.ofSeq ctors)))
                    else Some (Error "Expected at least one constructor arm in sum type body")
            else
                // Not a multi-line sum — rewind.
                c.Pos <- saved
                None
        else
            c.Pos <- saved
            None

    match tryParseMultiLineSum () with
    | Some result -> result
    | None ->
    match curTok c with
    | TypeId _ ->
        // Sum type: Ctor1 args | Ctor2 args | ...
        let ctors = ResizeArray<TypeIdent * TypeExpr list>()
        let mutable cont = true
        while cont do
            match parseSumArm () with
            | Ok (Some arm) ->
                ctors.Add(arm)
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
    let declIdx = c.Pos
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
                | Ok expr -> Ok (recordAt c declIdx (DFn(sig', expr)))
                | Error e -> Error e
    | KwLet ->
        advance c
        // Phase 7.1.6: try pattern-first. PVar falls back to DLet so all
        // downstream code keeps the regular form; non-trivial patterns
        // produce DLetPat.
        let saved = c.Pos
        match parsePattern c with
        | Error _ ->
            c.Pos <- saved
            Error $"Expected pattern after 'let' at {(cur c).Line}:{(cur c).Col}"
        | Ok pat ->
            if curTok c <> Eq then
                c.Pos <- saved
                Error $"Expected '=' after pattern in let, got {curTok c}"
            else
                advance c
                match parseExprInner c with
                | Error e -> Error e
                | Ok expr ->
                    match pat with
                    | PVar name -> Ok (recordAt c declIdx (DLet(name, expr)))
                    | _ -> Ok (recordAt c declIdx (DLetPat(pat, expr)))
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
                        Ok (recordAt c declIdx (DImpl(traitName, typeName, List.ofSeq impls)))
            | t -> Error $"Expected type name in impl, got {t}"
        | t -> Error $"Expected trait name in impl, got {t}"
    | t -> Error $"Unexpected token {t} at {(cur c).Line}:{(cur c).Col}"

// ---- Module parser --------------------------------------------------

let private parseModuleCtx (c: Ctx) : Result<LLModule, string> =
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
        // Phase 7.5d bugfix: propagate the first decl parse error instead
        // of silently advancing one token and dropping the whole decl.
        // The old behaviour (`| Error _ -> advance c`) turned any parse
        // error — e.g. a keyword like `tag` accidentally used as a binder
        // name — into a cascading `E002 UnboundVar` at every call site of
        // the dropped decl, with the actual parse error never surfacing.
        // Now the first broken decl aborts the module parse and the
        // driver reports the real `Expected ... got ...` message pointing
        // at the offending token's line:col.
        let mutable declErr : string option = None
        while curTok c <> Eof && declErr.IsNone do
            skipNewlines c
            if curTok c = Eof then ()
            else
                let exported = curTok c = KwExport
                if exported then advance c
                match parseDecl c with
                | Ok d -> decls.Add((d, exported)); skipNewlines c
                | Error e -> declErr <- Some e
        match declErr with
        | Some e -> Error e
        | None ->
            Ok {
                Path = List.ofSeq path
                Imports = List.ofSeq imports
                Decls = List.ofSeq decls
            }

/// Parse a module and return both the AST and the side-table of source
/// positions for a subset of nodes (EVar, ECon, EApp, EMatch, EMatchOf,
/// DFn, DLet, DLetPat, DImpl). Downstream passes use the PosMap to attach
/// real line:col to error messages instead of the old 0:0 placeholder.
let parseModuleWithPos (tokens: Tok list) : Result<LLModule * PosMap, string> =
    let c = { Tokens = Array.ofList tokens; Pos = 0; Positions = PosMap.create () }
    parseModuleCtx c |> Result.map (fun m -> m, c.Positions)

/// Parse a full ll-lang module from a token list (position map discarded).
/// Kept for callers that don't need positions; use `parseModuleWithPos`
/// if you need source locations for error reporting.
let parseModule (tokens: Tok list) : Result<LLModule, string> =
    parseModuleWithPos tokens |> Result.map fst
