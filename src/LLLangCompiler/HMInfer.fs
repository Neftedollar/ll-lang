module LLLang.HMInfer

open LLLang.AST
open LLLang.Types
open LLLang.TypedAST
open LLLang.Elaborator

// ---- Error helpers -------------------------------------------------------

let private mkErr code line col msg : LLError =
    { Code = code; Line = line; Col = col
      Message = sprintf "%s %d:%d %s" (code.ToString()) line col msg }

/// Position-less error emitters. Unification returns errors without a
/// source position (it doesn't see the source AST); callers that DO know
/// the source expression later reposition the error via `repos`.
let private e001 t1 t2 = mkErr E001 0 0 $"TypeMismatch {t1} vs {t2}"
let private e002 name  = mkErr E002 0 0 $"UnboundVar {name}"
let private e004 t1 t2 = mkErr E004 0 0 $"UnitMismatch {t1} vs {t2}"
let private e005 t1 t2 = mkErr E005 0 0 $"TaggedUntaggedMismatch {t1} vs {t2}"
let private e006 tr ty = mkErr E006 0 0 $"MissingImpl {tr} for {ty}"
let private e008 v  t  = mkErr E008 0 0 $"OccursCheck {v} in {t}"

/// Look up a node's source position in the PosMap side-table.
/// Accepts `obj | null` so callers can pass the result of `box` directly
/// without having to satisfy F#'s nullness checker.
let private posOf (pm: PosMap) (node: obj | null) : int * int =
    PosMap.tryFind pm node |> fun p -> (p.Line, p.Col)

/// Rewrite an error's line/col (and its rendered Message) to match `(ln, col)`.
/// Used to attach a caller-supplied source position to errors produced by
/// position-agnostic helpers like `unify`. If the error already has a
/// non-zero line, keep it (nested errors can supply their own position).
let private repos (ln: int) (col: int) (err: LLError) : LLError =
    if err.Line <> 0 || err.Col <> 0 then err
    else
        // Reconstruct the Message with the new line/col. The format is
        // "EXXX L:C Rest..." — split off the first two whitespace-separated
        // tokens and replace the second ("L:C") with the new one.
        let parts = err.Message.Split([| ' ' |], 3)
        let newMsg =
            if parts.Length >= 2 then
                let rest = if parts.Length = 3 then " " + parts[2] else ""
                sprintf "%s %d:%d%s" parts[0] ln col rest
            else err.Message
        { err with Line = ln; Col = col; Message = newMsg }

// ---- Helpers -------------------------------------------------------------

let private isFlex (v: Ident) = v.Length > 0 && v.[0] = '$'

let private occurs (v: Ident) (t: TypeExpr) : bool =
    let rec walk t =
        match t with
        | TyVar n -> n = v
        | TyApp(a, b) | TyFn(a, b) -> walk a || walk b
        | TyTagged(a, _) -> walk a
        | TyName _ -> false
    walk t

// ---- Unification ---------------------------------------------------------

let rec unify (t1: TypeExpr) (t2: TypeExpr) : Result<Subst, LLError> =
    match t1, t2 with
    | TyVar a, TyVar b when a = b -> Ok empty
    | TyVar v, t when isFlex v ->
        if occurs v t then Error (e008 v t) else Ok (singleton v t)
    | t, TyVar v when isFlex v ->
        if occurs v t then Error (e008 v t) else Ok (singleton v t)
    | TyVar _, _ | _, TyVar _ -> Error (e001 t1 t2)
    | TyName a, TyName b when a = b -> Ok empty
    | TyName _, TyName _ -> Error (e001 t1 t2)
    | TyFn(a1, r1), TyFn(a2, r2) ->
        unify a1 a2 |> Result.bind (fun s1 ->
            unify (applyType s1 r1) (applyType s1 r2)
            |> Result.map (fun s2 -> compose s2 s1))
    | TyApp(f1, a1), TyApp(f2, a2) ->
        unify f1 f2 |> Result.bind (fun s1 ->
            unify (applyType s1 a1) (applyType s1 a2)
            |> Result.map (fun s2 -> compose s2 s1))
    | TyTagged(b1, u1), TyTagged(b2, u2) ->
        if u1 = u2 then unify b1 b2 else Error (e004 t1 t2)
    | TyTagged _, _ | _, TyTagged _ -> Error (e005 t1 t2)
    | _ -> Error (e001 t1 t2)

// ---- Inference state -----------------------------------------------------

type private InferState = {
    Fresh: FreshState
    mutable Errors: LLError list
    mutable Dispatch: Map<ExprId, DispatchInfo>
    mutable NextId: int
    /// Source position side-table from the parser.
    Positions: PosMap
}

let private newState (pm: PosMap) : InferState =
    { Fresh = newFreshState (); Errors = []; Dispatch = Map.empty; NextId = 0; Positions = pm }

let private newId (st: InferState) : ExprId =
    let n = st.NextId in st.NextId <- n + 1; n

let private mkTyped (st: InferState) (kind: TypedExprKind) (ty: TypeExpr) : TypedExpr =
    { Id = newId st; Type = ty; Expr = kind }

/// Unify and push any error into state, returning empty subst on failure.
/// Errors are stamped with 0:0 — callers that want a real position should
/// use `unifyAt` instead (passing the source expression for lookup).
let private unifyS (st: InferState) (t1: TypeExpr) (t2: TypeExpr) : Subst =
    match unify t1 t2 with
    | Ok s -> s
    | Error e -> st.Errors <- st.Errors @ [e]; empty

/// Unify with a source expression for position reporting. Any emitted error
/// gets its line/col stamped from `srcNode`'s entry in the PosMap (or 0:0
/// if not present).
let private unifyAt (st: InferState) (srcNode: obj | null) (t1: TypeExpr) (t2: TypeExpr) : Subst =
    match unify t1 t2 with
    | Ok s -> s
    | Error e ->
        let (ln, col) = posOf st.Positions srcNode
        st.Errors <- st.Errors @ [repos ln col e]
        empty

/// Push an unbound-var/ctor error with the source position of `srcNode`.
let private pushE002 (st: InferState) (srcNode: obj | null) (name: string) =
    let (ln, col) = posOf st.Positions srcNode
    st.Errors <- st.Errors @ [repos ln col (e002 name)]

// ---- Walk typed tree applying a substitution --------------------------------

let rec private applyTE (s: Subst) (te: TypedExpr) : TypedExpr =
    { te with
        Type = applyType s te.Type
        Expr = applyTEK s te.Expr }

and private applyTEK (s: Subst) (k: TypedExprKind) : TypedExprKind =
    match k with
    | TELit _ | TEVar _ | TECon _ -> k
    | TEApp(a, b) -> TEApp(applyTE s a, applyTE s b)
    | TELam(ps, body) -> TELam(ps |> List.map (fun (n, t) -> n, applyType s t), applyTE s body)
    | TELet(n, sch, e1, e2) ->
        let sch' = { sch with Body = applyType s sch.Body }
        TELet(n, sch', applyTE s e1, e2 |> Option.map (applyTE s))
    | TELetPat(tp, e1, e2) ->
        let tp' = { tp with Type = applyType s tp.Type }
        TELetPat(tp', applyTE s e1, e2 |> Option.map (applyTE s))
    | TEIf(c, t, e) -> TEIf(applyTE s c, applyTE s t, applyTE s e)
    | TEMatch(sc, brs) ->
        TEMatch(applyTE s sc, brs |> List.map (fun (p, b) ->
            { p with Type = applyType s p.Type }, applyTE s b))
    | TEMatchOf(sc, brs) ->
        TEMatchOf(applyTE s sc, brs |> List.map (fun (p, b) ->
            { p with Type = applyType s p.Type }, applyTE s b))
    | TEPipe(a, b) -> TEPipe(applyTE s a, applyTE s b)
    | TETagged(e, tag) -> TETagged(applyTE s e, tag)
    | TEList es -> TEList (es |> List.map (applyTE s))
    | TETuple es -> TETuple (es |> List.map (applyTE s))
    | TECons(h, t) -> TECons(applyTE s h, applyTE s t)

// ---- Literal types -------------------------------------------------------

let private litTy = function
    | LInt _  -> TyName "Int"
    | LFloat _ -> TyName "Float"
    | LStr _  -> TyName "Str"
    | LBool _ -> TyName "Bool"
    | LChar _ -> TyName "Char"

// ---- Algorithm W ---------------------------------------------------------

let rec private inferExpr (env: Env) (st: InferState) (expr: Expr) : Subst * TypeExpr * TypedExpr =
    match expr with

    | ELit lit ->
        let ty = litTy lit
        (empty, ty, mkTyped st (TELit lit) ty)

    | EVar x ->
        match Map.tryFind x env with
        | None ->
            pushE002 st (box expr) x
            let beta = freshVar st.Fresh
            (empty, beta, mkTyped st (TEVar x) beta)
        | Some sch ->
            let ty = instantiate st.Fresh sch
            (empty, ty, mkTyped st (TEVar x) ty)

    | ECon c ->
        match Map.tryFind c env with
        | None ->
            pushE002 st (box expr) c
            let beta = freshVar st.Fresh
            (empty, beta, mkTyped st (TECon c) beta)
        | Some sch ->
            let ty = instantiate st.Fresh sch
            (empty, ty, mkTyped st (TECon c) ty)

    | ETagged(e, tag) ->
        let (s, tau, te) = inferExpr env st e
        let ty = TyTagged(tau, UName tag)
        (s, ty, mkTyped st (TETagged(te, tag)) ty)

    | EApp(f, a) ->
        // Special case: arithmetic operator on two tagged-numeric operands.
        //
        // `d / t` where `d : Float[m]` and `t : Float[s]` would normally fail
        // because `(/) : ∀a. a -> a -> a` forces both operands to the same
        // type. Composite unit algebra (`Float[m/s]`) is not yet implemented;
        // we leave the result type as a fresh flex var. This keeps inference
        // live for code that mixes units in arithmetic (see 03-tags.lll).
        let isArithOp name = name = "+" || name = "-" || name = "*" || name = "/"
        let isTaggedNumeric ty =
            match ty with
            | TyTagged(TyName "Float", _) | TyTagged(TyName "Int", _) -> true
            | _ -> false
        match expr with
        | EApp(EApp(EVar op, lhs), rhs) when isArithOp op ->
            let (s1, tauL, teL) = inferExpr env st lhs
            let (s2, tauR, teR) = inferExpr (applyEnv s1 env) st rhs
            let tauLApplied = applyType s2 tauL
            let tauRApplied = applyType s2 tauR
            if isTaggedNumeric tauLApplied && isTaggedNumeric tauRApplied then
                // Don't unify operand types; return a fresh flex var.
                let beta = freshVar st.Fresh
                let sAll = compose s2 s1
                let opSch =
                    match Map.tryFind op env with
                    | Some s -> s
                    | None -> mono (TyFn(tauLApplied, TyFn(tauRApplied, beta)))
                let opTy = instantiate st.Fresh opSch
                let opTE = mkTyped st (TEVar op) opTy
                let innerApp = mkTyped st (TEApp(opTE, applyTE sAll teL)) (TyFn(tauRApplied, beta))
                let outerApp = mkTyped st (TEApp(innerApp, applyTE sAll teR)) beta
                (sAll, beta, outerApp)
            else
                // Fall through to default EApp handling.
                let (s1, tauF, teF) = inferExpr env st f
                let env1 = applyEnv s1 env
                let (s2, tauA, teA) = inferExpr env1 st a
                let beta = freshVar st.Fresh
                // EApp position is at the argument token — that's where
                // E001 TypeMismatch should point for the caller.
                let s3 = unifyAt st (box expr) (applyType s2 tauF) (TyFn(tauA, beta))
                let sAll = compose s3 (compose s2 s1)
                let retTy = applyType sAll beta
                let te = mkTyped st (TEApp(applyTE sAll teF, applyTE sAll teA)) retTy
                (sAll, retTy, te)
        | _ ->
            let (s1, tauF, teF) = inferExpr env st f
            let env1 = applyEnv s1 env
            let (s2, tauA, teA) = inferExpr env1 st a
            let beta = freshVar st.Fresh
            let s3 = unifyAt st (box expr) (applyType s2 tauF) (TyFn(tauA, beta))
            let sAll = compose s3 (compose s2 s1)
            let retTy = applyType sAll beta
            let te = mkTyped st (TEApp(applyTE sAll teF, applyTE sAll teA)) retTy
            (sAll, retTy, te)

    | ELam(ps, body) ->
        // Each param gets a fresh flex var
        let paramAlphas = ps |> List.map (fun p -> p, freshVar st.Fresh)
        let env' = List.fold (fun e (p, alpha) -> Map.add p (mono alpha) e) env paramAlphas
        let (sBody, tauBody, teBody) = inferExpr env' st body
        let paramTypes = paramAlphas |> List.map (fun (p, alpha) -> p, applyType sBody alpha)
        // Build curried function type: p1 -> p2 -> ... -> body
        let fnTy = List.foldBack (fun (_, t) acc -> TyFn(t, acc)) paramTypes tauBody
        (sBody, fnTy, mkTyped st (TELam(paramTypes, applyTE sBody teBody)) fnTy)

    | ELet(x, e1, None) ->
        let (s1, tau1, te1) = inferExpr env st e1
        let env1 = applyEnv s1 env
        let sch = generalize env1 tau1
        (s1, tau1, mkTyped st (TELet(x, sch, te1, None)) tau1)

    | ELet(x, e1, Some body) ->
        let (s1, tau1, te1) = inferExpr env st e1
        let env1 = applyEnv s1 env
        let sch = generalize env1 tau1
        let env2 = Map.add x sch env1
        let (s2, tau2, te2) = inferExpr env2 st body
        let sAll = compose s2 s1
        (sAll, tau2, mkTyped st (TELet(x, sch, applyTE s2 te1, Some te2)) tau2)

    | ELetPat(pat, e1, bodyOpt) ->
        // Infer RHS, derive pattern type via patternType, unify the two,
        // extend env with each pattern binding (monomorphic — no generalize
        // since the bound names are projections of a single value).
        let (s1, tau1, te1) = inferExpr env st e1
        let env1 = applyEnv s1 env
        let (patTy, bindings) = patternType st env1 pat
        let su = unifyS st (applyType s1 patTy) (applyType s1 tau1)
        let s12 = compose su s1
        let envWithBindings =
            List.fold
                (fun e (n, t) -> Map.add n (mono (applyType s12 t)) e)
                (applyEnv s12 env)
                bindings
        let typedPat = { Pat = pat; Type = applyType s12 patTy }
        match bodyOpt with
        | Some body ->
            let (s2, tau2, te2) = inferExpr envWithBindings st body
            let sAll = compose s2 s12
            let resultTy = applyType sAll tau2
            (sAll,
             resultTy,
             mkTyped st (TELetPat(typedPat, applyTE sAll te1, Some te2)) resultTy)
        | None ->
            // No body — type is the RHS type (matches ELet semantics).
            let resultTy = applyType s12 tau1
            (s12,
             resultTy,
             mkTyped st (TELetPat(typedPat, applyTE s12 te1, None)) resultTy)

    | EIf(cond, thn, els) ->
        let (s1, tauC, teC) = inferExpr env st cond
        let sC = unifyS st (applyType s1 tauC) (TyName "Bool")
        let env1 = applyEnv (compose sC s1) env
        let (s2, tauT, teT) = inferExpr env1 st thn
        let (s3, tauE, teE) = inferExpr (applyEnv s2 env1) st els
        let sTE = unifyS st (applyType s3 tauT) tauE
        let sAll = compose sTE (compose s3 (compose s2 (compose sC s1)))
        let retTy = applyType sAll tauT
        (sAll, retTy, mkTyped st (TEIf(applyTE sAll teC, applyTE sAll teT, applyTE sAll teE)) retTy)

    | EPipe(e1, e2) ->
        // e1 -> e2  desugars to  e2 e1
        let (s1, tau1, te1) = inferExpr env st e1
        let (s2, tau2, te2) = inferExpr (applyEnv s1 env) st e2
        let beta = freshVar st.Fresh
        let s3 = unifyS st (applyType s2 tau2) (TyFn(applyType s2 tau1, beta))
        let sAll = compose s3 (compose s2 s1)
        let retTy = applyType sAll beta
        // Store as TEPipe but the type is the application result
        (sAll, retTy, mkTyped st (TEPipe(applyTE sAll te1, applyTE sAll te2)) retTy)

    | EList [] ->
        let alpha = freshVar st.Fresh
        let ty = TyApp(TyName "List", alpha)
        (empty, ty, mkTyped st (TEList []) ty)

    | EList (e :: rest) ->
        let (s0, tau0, te0) = inferExpr env st e
        // Fold: unify each element type with the first
        let (sAll, tauElem, tes) =
            List.fold (fun (sAcc, tauAcc, tesAcc) ei ->
                let (si, taui, tei) = inferExpr (applyEnv sAcc env) st ei
                let su = unifyS st (applyType si (applyType sAcc tauAcc)) taui
                (compose su (compose si sAcc), applyType su taui, tesAcc @ [applyTE su tei])
            ) (s0, tau0, [applyTE s0 te0]) rest
        let listTy = TyApp(TyName "List", applyType sAll tauElem)
        let allTEs = List.map (applyTE sAll) tes
        (sAll, listTy, mkTyped st (TEList allTEs) listTy)

    | ETuple elems ->
        let (sAll, tys, tes) =
            List.fold (fun (sAcc, tysAcc, tesAcc) ei ->
                let (si, taui, tei) = inferExpr (applyEnv sAcc env) st ei
                (compose si sAcc, tysAcc @ [taui], tesAcc @ [tei])
            ) (empty, [], []) elems
        // Encode tuple as nested TyApp for now
        let tupleTy = List.fold (fun acc t -> TyApp(acc, t)) (TyName "Tuple") tys
        (sAll, tupleTy, mkTyped st (TETuple (List.map (applyTE sAll) tes)) tupleTy)

    | ECons(h, t) ->
        // h :: t  where t : List τh and result : List τh
        let (s1, tauH, teH) = inferExpr env st h
        let (s2, tauT, teT) = inferExpr (applyEnv s1 env) st t
        let listOfH = TyApp(TyName "List", applyType s2 tauH)
        let s3 = unifyS st (applyType s2 tauT) listOfH
        let sAll = compose s3 (compose s2 s1)
        let resultTy = applyType sAll listOfH
        let te = mkTyped st (TECons(applyTE sAll teH, applyTE sAll teT)) resultTy
        (sAll, resultTy, te)

    | EMatchOf(scrut, branches) ->
        // Explicit-scrutinee match expression. Unlike EMatch (which the
        // DFn/DImpl special-case turns into a fn body), this form is usable
        // in any expression position: `let v = match x with | ... | ...`.
        let (s0, tauScrut, teScrut) = inferExpr env st scrut
        let env0 = applyEnv s0 env
        let alpha = freshVar st.Fresh   // result type
        // Any error bubbling up from this match should be attributed to
        // the match expression's source position.
        let matchObj = box expr
        let (sAll, typedBranches) =
            List.fold (fun (sAcc, brsAcc) (pat, body) ->
                let (patTy, bindings) = patternType st env0 pat
                let su = unifyAt st matchObj (applyType sAcc patTy) (applyType sAcc tauScrut)
                let sAccPlusSu = compose su sAcc
                let envExt =
                    List.fold
                        (fun e (n, t) -> Map.add n (mono (applyType sAccPlusSu t)) e)
                        (applyEnv sAccPlusSu env0)
                        bindings
                let (sb, tauB, teB) = inferExpr envExt st body
                let sbAll = compose sb sAccPlusSu
                let sr = unifyAt st matchObj (applyType sbAll tauB) (applyType sbAll alpha)
                let sStep = compose sr sbAll
                let tp = { Pat = pat; Type = applyType sStep patTy }
                (sStep, brsAcc @ [(tp, applyTE sStep teB)])
            ) (s0, []) branches
        let resultTy = applyType sAll alpha
        let te = mkTyped st (TEMatchOf(applyTE sAll teScrut, typedBranches)) resultTy
        (sAll, resultTy, te)

    | EMatch branches ->
        // Match as expression: synthesize scrutinee var, build function type
        let alpha = freshVar st.Fresh  // scrutinee type
        let beta  = freshVar st.Fresh  // result type
        let matchObj = box expr
        let (sAll, typedBranches) =
            List.fold (fun (sAcc, brsAcc) (pat, body) ->
                let (patTy, patBindings) = patternType st env pat
                let su = unifyAt st matchObj (applyType sAcc patTy) (applyType sAcc alpha)
                let envExt = List.fold (fun e (n, t) -> Map.add n (mono t) e) (applyEnv (compose su sAcc) env) patBindings
                let (sb, tauB, teB) = inferExpr envExt st body
                let sr = unifyAt st matchObj (applyType sb tauB) (applyType (compose sb su) beta)
                let sStep = compose sr (compose sb (compose su sAcc))
                let tp = { Pat = pat; Type = applyType sStep patTy }
                (sStep, brsAcc @ [(tp, applyTE sStep teB)])
            ) (empty, []) branches
        let scrutTy = applyType sAll alpha
        let retTy   = applyType sAll beta
        // Wrap in a lambda taking the scrutinee
        let scrutName = "$scrut"
        let scrutTE = mkTyped st (TEVar scrutName) scrutTy
        let matchTE = mkTyped st (TEMatch(scrutTE, typedBranches)) retTy
        let lamTy = TyFn(scrutTy, retTy)
        (sAll, lamTy, mkTyped st (TELam([scrutName, scrutTy], matchTE)) lamTy)

and private patternType (st: InferState) (env: Env) (pat: Pattern) : TypeExpr * (Ident * TypeExpr) list =
    match pat with
    | PVar x ->
        let alpha = freshVar st.Fresh
        (alpha, [x, alpha])
    | PWild ->
        let alpha = freshVar st.Fresh
        (alpha, [])
    | PLit lit ->
        (litTy lit, [])
    | PTuple subPats ->
        // Recursively type each subpattern, collect bindings, and build the
        // same tuple encoding used by inferExpr ETuple:
        //   TyApp(TyApp(TyName "Tuple", t1), t2) ... for N elements.
        let (tys, bindings) =
            subPats
            |> List.map (patternType st env)
            |> List.fold (fun (tysAcc, bsAcc) (t, bs) ->
                (tysAcc @ [t], bsAcc @ bs)) ([], [])
        let tupleTy = List.fold (fun acc t -> TyApp(acc, t)) (TyName "Tuple") tys
        (tupleTy, bindings)
    | PCons(h, t) ->
        // h :: t  →  scrutinee is List αElem; h binds αElem; t binds List αElem.
        // patternType has no way to thread a substitution back to its caller,
        // so we apply the unify result manually to the binding types so the
        // bindings reference αElem (not the sub-pattern's fresh flex var).
        let alphaElem = freshVar st.Fresh
        let (typeH, bindingsH) = patternType st env h
        let listTy = TyApp(TyName "List", alphaElem)
        let (typeT, bindingsT) = patternType st env t
        let s1 =
            match unify typeH alphaElem with
            | Ok s -> s
            | Error e -> st.Errors <- st.Errors @ [e]; empty
        let s2 =
            match unify (applyType s1 typeT) listTy with
            | Ok s -> compose s s1
            | Error e -> st.Errors <- st.Errors @ [e]; s1
        let bindings =
            (bindingsH @ bindingsT)
            |> List.map (fun (n, t) -> (n, applyType s2 t))
        (applyType s2 listTy, bindings)
    | PCon("[]", []) ->
        // Phase 7.3a bugfix (bug 2): empty-list pattern sentinel. The
        // parser produces `PCon("[]", [])` for `[]` and as the tail of
        // `[p1, ..., pN]` cons chains. Types as `List αElem` where
        // αElem is a fresh flex var so it unifies against any list
        // element type the arm needs.
        let alphaElem = freshVar st.Fresh
        (TyApp(TyName "List", alphaElem), [])
    | PCon(name, argPats) ->
        match Map.tryFind name env with
        | None ->
            st.Errors <- st.Errors @ [e002 name]
            let alpha = freshVar st.Fresh
            (alpha, [])
        | Some sch ->
            let conTy = instantiate st.Fresh sch
            // Unroll conTy: TyFn(t1, TyFn(t2, ... R))
            let rec unrollFn ty =
                match ty with
                | TyFn(a, b) -> let (args, ret) = unrollFn b in (a :: args, ret)
                | _ -> ([], ty)
            let (expectedArgTys, retTy) = unrollFn conTy
            let bindings =
                List.zip argPats (List.truncate (List.length argPats) (expectedArgTys @ [freshVar st.Fresh]))
                |> List.collect (fun (subPat, expTy) ->
                    let (actualTy, bs) = patternType st env subPat
                    ignore (unifyS st actualTy expTy)
                    bs)
            (retTy, bindings)

// ---- Build fn type from param list + return ---------------------------------

let private buildFnType (paramTys: TypeExpr list) (retTy: TypeExpr) : TypeExpr =
    List.foldBack (fun p acc -> TyFn(p, acc)) paramTys retTy

/// True if a TyName looks like a declared type parameter (single uppercase letter).
/// The ll-lang parser emits uppercase identifiers as TyName; single-letter uppercase
/// like A, B, C are type parameters by convention.
let private isTyParam (name: Ident) =
    name.Length = 1 && System.Char.IsUpper name.[0]

/// Normalize a type expression: convert TyName X to TyVar X when X is a type parameter.
let rec private normalizeTy (ty: TypeExpr) : TypeExpr =
    match ty with
    | TyName n when isTyParam n -> TyVar n
    | TyApp(a, b) -> TyApp(normalizeTy a, normalizeTy b)
    | TyFn(a, b) -> TyFn(normalizeTy a, normalizeTy b)
    | TyTagged(a, u) -> TyTagged(normalizeTy a, u)
    | _ -> ty

/// Collect all rigid (non-flex) TyVar names from a type.
let private collectRigidVars (ty: TypeExpr) : Ident list =
    let rec go t acc =
        match t with
        | TyVar v when not (isFlex v) -> v :: acc
        | TyApp(a, b) | TyFn(a, b) -> go a acc |> go b
        | TyTagged(a, _) -> go a acc
        | _ -> acc
    go ty [] |> List.distinct |> List.sort

// ---- Infer top-level decl --------------------------------------------------

let private inferDecl (env: Env) (st: InferState) (decl: Decl) (exported: bool) : (TypedDecl * bool) * Env =
    match decl with

    | DLet(x, expr) ->
        let (s, tau, te) = inferExpr env st expr
        let env1 = applyEnv s env
        let sch = generalize env1 tau
        let env2 = Map.add x sch env1
        let td = TDLet(x, sch, applyTE s te)
        ((td, exported), env2)

    | DLetPat(pat, expr) ->
        // Top-level destructuring let. Infer expr, unify with the pattern's
        // synthesized type, and add each binding to the env as a monomorphic
        // scheme — bindings can't be generalized independently because they
        // alias parts of a single value.
        let (s, tau, te) = inferExpr env st expr
        let env1 = applyEnv s env
        let (patTy, bindings) = patternType st env1 pat
        let su = unifyS st (applyType s patTy) (applyType s tau)
        let sAll = compose su s
        let env2 =
            List.fold
                (fun e (n, t) -> Map.add n (mono (applyType sAll t)) e)
                (applyEnv sAll env)
                bindings
        let typedPat = { Pat = pat; Type = applyType sAll patTy }
        let td = TDLetPat(typedPat, applyTE sAll te)
        ((td, exported), env2)

    | DFn(sig_, body) ->
        // Normalize param and return types: TyName X -> TyVar X for type params.
        // `TyVar "?"` marks an untyped slot (e.g. `fn f(p) = ...`) and gets
        // replaced with a fresh flex var for inference.
        // If a tentative monomorphic scheme for this fn is already in `env`
        // (populated by Pass 1 of the two-pass top-level inference), reuse
        // its param/return types so recursive call sites resolving this fn's
        // name observe the SAME fresh flex vars we're about to infer against.
        // Otherwise (e.g. DImpl's inner fns, which skip Pass 1), fall back to
        // freshly resolving each slot from the raw sig.
        let resolveSlot (ty: TypeExpr) =
            match ty with
            | TyVar "?" -> freshVar st.Fresh
            | other -> normalizeTy other
        let (normParams, normRet) =
            match Map.tryFind sig_.Name env with
            | Some { Vars = []; Body = tentBody } ->
                // Unroll the tentative fn type into [paramTy...] + retTy.
                let rec unroll ty remaining =
                    match remaining with
                    | [] -> ([], ty)
                    | _ :: rest ->
                        match ty with
                        | TyFn(a, b) ->
                            let (restTys, r) = unroll b rest
                            (a :: restTys, r)
                        | _ -> ([], ty)
                let (paramTys, retTy) = unroll tentBody sig_.Params
                let paramsZipped =
                    List.zip (sig_.Params |> List.map fst) paramTys
                (paramsZipped, Some retTy)
            | _ ->
                let nps = sig_.Params |> List.map (fun (n, ty) -> n, resolveSlot ty)
                let nr = sig_.ReturnType |> Option.map normalizeTy
                (nps, nr)
        // Collect declared type parameter names (rigid vars) from params + return
        let declaredTyVars =
            let paramVars = normParams |> List.collect (fun (_, ty) -> collectRigidVars ty)
            let retVars = normRet |> Option.map collectRigidVars |> Option.defaultValue []
            (paramVars @ retVars) |> List.distinct |> List.sort
        // Build per-fn env: add params with their normalized types
        let paramEnv =
            List.fold (fun e (name, ty) -> Map.add name (mono ty) e) env normParams
        // Determine expected return type
        let expectedRet =
            match normRet with
            | Some ty -> ty
            | None -> freshVar st.Fresh
        // Special case: body is EMatch -> last param is scrutinee
        let bodyObj = box body
        let (sBody, tauBody, teBody) =
            match body with
            | EMatch branches when not (List.isEmpty normParams) ->
                // Last param is the scrutinee
                let lastParam = fst (List.last normParams)
                let lastParamTy = snd (List.last normParams)
                let beta = freshVar st.Fresh
                let (sAll, typedBranches) =
                    List.fold (fun (sAcc, brsAcc) (pat, branchBody) ->
                        let (patTy, patBindings) = patternType st paramEnv pat
                        let su = unifyAt st bodyObj (applyType sAcc patTy) (applyType sAcc lastParamTy)
                        let envExt = List.fold (fun e (n, t) -> Map.add n (mono t) e) (applyEnv (compose su sAcc) paramEnv) patBindings
                        let (sb, tauB, teB) = inferExpr envExt st branchBody
                        let sr = unifyAt st bodyObj (applyType sb tauB) (applyType (compose sb su) beta)
                        let sStep = compose sr (compose sb (compose su sAcc))
                        let tp = { Pat = pat; Type = applyType sStep patTy }
                        (sStep, brsAcc @ [(tp, applyTE sStep teB)])
                    ) (empty, []) branches
                let scrutTE = mkTyped st (TEVar lastParam) (applyType sAll lastParamTy)
                let matchTE = mkTyped st (TEMatch(scrutTE, typedBranches)) (applyType sAll beta)
                (sAll, applyType sAll beta, matchTE)
            | _ ->
                inferExpr paramEnv st body
        // Unify body type with expected return
        let sRet = unifyAt st bodyObj (applyType sBody tauBody) (applyType sBody expectedRet)
        let sAll = compose sRet sBody
        // Build full fn type (curried)
        let paramTys = normParams |> List.map (fun (_, ty) -> applyType sAll ty)
        let retTy = applyType sAll expectedRet
        let fnTy = buildFnType paramTys retTy
        // Generalize against the env WITHOUT this fn's own tentative scheme.
        // Pass-1 of the two-pass inference added a tentative mono scheme for
        // this fn; if we left it in `env` here, any fresh flex vars from
        // Pass 1 (e.g. for untyped params) would appear in ftvEnv and escape
        // generalization, leaving the final scheme monomorphic. Remove the
        // tentative scheme so flex vars that only appear in this fn's
        // signature get properly quantified.
        let envForGen = Map.remove sig_.Name env
        let baseSch = generalize (applyEnv sAll envForGen) fnTy
        let sch = { baseSch with Vars = (declaredTyVars @ baseSch.Vars) |> List.distinct |> List.sort }
        let env2 = Map.add sig_.Name sch env
        let typedSig : TypedFnSig = {
            Name = sig_.Name
            Constraints = sig_.Constraints
            Params = normParams |> List.map (fun (n, t) -> n, applyType sAll t)
            ReturnType = retTy
        }
        ((TDFn(typedSig, sch, applyTE sAll teBody), exported), env2)

    | DType(name, ps, body) ->
        ((TDType(name, ps, body), exported), env)

    | DTag name ->
        ((TDTag name, exported), env)

    | DUnit name ->
        ((TDUnit name, exported), env)

    | DTrait(name, vars, sigs) ->
        ((TDTrait(name, vars, sigs), exported), env)

    | DImpl(traitName, implType, fns) ->
        let inferImplFn (envAcc: Env) (fnsAcc: (TypedFnSig * TypeScheme * TypedExpr) list) (sig_: FnSig) (body: Expr) =
            // Normalize type params (TyName X -> TyVar X for single-uppercase vars)
            let normParams = sig_.Params |> List.map (fun (n, ty) -> n, normalizeTy ty)
            let normRet = sig_.ReturnType |> Option.map normalizeTy
            // Collect declared type parameter names for generalization
            let declaredTyVars =
                let paramVars = normParams |> List.collect (fun (_, ty) -> collectRigidVars ty)
                let retVars = normRet |> Option.map collectRigidVars |> Option.defaultValue []
                (paramVars @ retVars) |> List.distinct |> List.sort
            let paramEnv = List.fold (fun e (n, ty) -> Map.add n (mono ty) e) envAcc normParams
            let expectedRet =
                match normRet with
                | Some ty -> ty
                | None -> freshVar st.Fresh
            // Handle match bodies (last param is scrutinee)
            let bodyObj = box body
            let (sBody, tauBody, teBody) =
                match body with
                | EMatch branches when not (List.isEmpty normParams) ->
                    let lastParam = fst (List.last normParams)
                    let lastParamTy = snd (List.last normParams)
                    let beta = freshVar st.Fresh
                    let (sAll, typedBranches) =
                        List.fold (fun (sAcc, brsAcc) (pat, branchBody) ->
                            let (patTy, patBindings) = patternType st paramEnv pat
                            let su = unifyAt st bodyObj (applyType sAcc patTy) (applyType sAcc lastParamTy)
                            let envExt = List.fold (fun e (n, t) -> Map.add n (mono t) e) (applyEnv (compose su sAcc) paramEnv) patBindings
                            let (sb, tauB, teB) = inferExpr envExt st branchBody
                            let sr = unifyAt st bodyObj (applyType sb tauB) (applyType (compose sb su) beta)
                            let sStep = compose sr (compose sb (compose su sAcc))
                            let tp = { Pat = pat; Type = applyType sStep patTy }
                            (sStep, brsAcc @ [(tp, applyTE sStep teB)])
                        ) (empty, []) branches
                    let scrutTE = mkTyped st (TEVar lastParam) (applyType sAll lastParamTy)
                    let matchTE = mkTyped st (TEMatch(scrutTE, typedBranches)) (applyType sAll beta)
                    (sAll, applyType sAll beta, matchTE)
                | _ ->
                    inferExpr paramEnv st body
            let sRet = unifyAt st bodyObj (applyType sBody tauBody) (applyType sBody expectedRet)
            let sAll = compose sRet sBody
            let paramTys = normParams |> List.map (fun (_, ty) -> applyType sAll ty)
            let retTy = applyType sAll expectedRet
            let fnTy = buildFnType paramTys retTy
            let baseSch = generalize (applyEnv sAll envAcc) fnTy
            let sch = { baseSch with Vars = (declaredTyVars @ baseSch.Vars) |> List.distinct |> List.sort }
            let mangledName = sig_.Name + "_" + implType
            let envNew = Map.add mangledName sch envAcc
            let typedSig : TypedFnSig = {
                Name = sig_.Name; Constraints = sig_.Constraints
                Params = normParams |> List.map (fun (n, t) -> n, applyType sAll t)
                ReturnType = retTy
            }
            (envNew, fnsAcc @ [(typedSig, sch, applyTE sAll teBody)])
        let (env2, typedFns) =
            List.fold (fun (envAcc, fnsAcc) (sig_, body) ->
                inferImplFn envAcc fnsAcc sig_ body
            ) (env, []) fns
        ((TDImpl(traitName, implType, typedFns), exported), env2)

// ---- Main entry point -------------------------------------------------------

let infer (pm: PosMap) (m: LLModule) (env0: Elaborator.TypeEnv) : Result<TypedModule, LLError list> =
    let initEnv = fromElaboratorEnv env0
    let st = newState pm
    // ---- Pass 1: add tentative mono schemes for every top-level DFn ----
    //
    // Build a tentative TyFn type from declared param / return types. Untyped
    // slots (`TyVar "?"`, emitted by the parser for `(p)` form or elided
    // return types) get fresh flex vars so inference of the bodies can refine
    // them. The tentative scheme is stored as a monomorphic scheme: declared
    // rigid type vars stay as TyVar (unchanged), so recursive call sites see
    // a consistent type. This enables forward references and mutual recursion
    // between top-level fns.
    //
    // Limitation: if both fns in a mutually recursive pair elide their return
    // type, the fresh flex vars may not resolve unless at least one fn's body
    // pins them down. Requiring at least one declared return type is a
    // documented constraint; see the tests in HMInferTests `Phase 6.8`.
    let resolveSlot (ty: TypeExpr) =
        match ty with
        | TyVar "?" -> freshVar st.Fresh
        | other -> normalizeTy other
    let envWithTentative =
        List.fold (fun envAcc (decl, _exported) ->
            match decl with
            | DFn(sig_, _body) ->
                let pTys = sig_.Params |> List.map (fun (_, t) -> resolveSlot t)
                let rTy =
                    match sig_.ReturnType with
                    | Some t -> normalizeTy t
                    | None -> freshVar st.Fresh
                let fnTy = buildFnType pTys rTy
                Map.add sig_.Name (mono fnTy) envAcc
            | _ -> envAcc
        ) initEnv m.Decls
    // ---- Pass 2: infer each decl against the env that already contains
    // every sibling's tentative scheme. For each DFn, the inferDecl DFn
    // branch detects its tentative scheme in env and reuses the pre-allocated
    // param/return types so inference and the Pass-1 types agree.
    let (decls, _) =
        List.fold (fun (declsAcc, envAcc) (decl, exported) ->
            let (td, env') = inferDecl envAcc st decl exported
            (declsAcc @ [td], env')
        ) ([], envWithTentative) m.Decls
    // Collect final env from accumulated decls
    let finalEnv =
        // Walk a Pattern and pair each name with the corresponding sub-type
        // of the resolved pattern type. Used by TDLetPat to expose every
        // bound name in the final module env.
        let rec destructure (pat: Pattern) (ty: TypeExpr) : (string * TypeExpr) list =
            match pat with
            | PVar n -> [n, ty]
            | PWild -> []
            | PLit _ -> []
            | PTuple subPats ->
                // Tuples are encoded as TyApp(TyApp(...TyName "Tuple", t1), t2)... .
                // Unroll right-associatively to extract per-element types.
                let rec collectTypes acc t =
                    match t with
                    | TyApp(inner, last) -> collectTypes (last :: acc) inner
                    | _ -> acc
                let elemTys = collectTypes [] ty
                if List.length elemTys = List.length subPats then
                    List.zip subPats elemTys
                    |> List.collect (fun (p, t) -> destructure p t)
                else
                    // Fallback: bind every name to the whole type (best-effort).
                    let rec patNames p =
                        match p with
                        | PVar n -> [n]
                        | PCon(_, ps) -> ps |> List.collect patNames
                        | PTuple ps -> ps |> List.collect patNames
                        | PCons(h, t) -> patNames h @ patNames t
                        | PLit _ | PWild -> []
                    patNames (PTuple subPats) |> List.map (fun n -> n, ty)
            | PCons(h, t) ->
                // Best-effort: head is element type, tail is the list type.
                match ty with
                | TyApp(TyName "List", elemTy) ->
                    destructure h elemTy @ destructure t ty
                | _ ->
                    let rec patNames p =
                        match p with
                        | PVar n -> [n]
                        | PCon(_, ps) -> ps |> List.collect patNames
                        | PTuple ps -> ps |> List.collect patNames
                        | PCons(h, t) -> patNames h @ patNames t
                        | PLit _ | PWild -> []
                    (patNames h @ patNames t) |> List.map (fun n -> n, ty)
            | PCon(_, _) ->
                // Best-effort fallback for constructor patterns.
                let rec patNames p =
                    match p with
                    | PVar n -> [n]
                    | PCon(_, ps) -> ps |> List.collect patNames
                    | PTuple ps -> ps |> List.collect patNames
                    | PCons(h, t) -> patNames h @ patNames t
                    | PLit _ | PWild -> []
                patNames pat |> List.map (fun n -> n, ty)
        List.fold (fun envAcc (td, _) ->
            match td with
            | TDFn(sig_, sch, _) -> Map.add sig_.Name sch envAcc
            | TDLet(name, sch, _) -> Map.add name sch envAcc
            | TDLetPat(typedPat, _) ->
                destructure typedPat.Pat typedPat.Type
                |> List.fold (fun e (n, t) -> Map.add n (mono t) e) envAcc
            | TDImpl(_, implType, fns) ->
                List.fold (fun e (sig_: TypedFnSig, sch, _) ->
                    Map.add (sig_.Name + "_" + implType) sch e) envAcc fns
            | _ -> envAcc
        ) initEnv decls
    if st.Errors <> [] then Error st.Errors
    else Ok { Path = m.Path; Decls = decls; Env = finalEnv; Dispatch = st.Dispatch }
