module LLLang.HMInfer

open LLLang.AST
open LLLang.Types
open LLLang.TypedAST
open LLLang.Elaborator

// ---- Error helpers -------------------------------------------------------

let private mkErr code msg : LLError = { Code = code; Line = 0; Col = 0; Message = msg }
let private e001 t1 t2 = mkErr E001 $"E001 TypeMismatch {t1} vs {t2}"
let private e002 name  = mkErr E002 $"E002 UnboundVar {name}"
let private e004 t1 t2 = mkErr E004 $"E004 UnitMismatch {t1} vs {t2}"
let private e005 t1 t2 = mkErr E005 $"E005 TaggedUntaggedMismatch {t1} vs {t2}"
let private e006 tr ty = mkErr E006 $"E006 MissingImpl {tr} for {ty}"
let private e008 v  t  = mkErr E008 $"E008 OccursCheck {v} in {t}"

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
}

let private newState () : InferState =
    { Fresh = newFreshState (); Errors = []; Dispatch = Map.empty; NextId = 0 }

let private newId (st: InferState) : ExprId =
    let n = st.NextId in st.NextId <- n + 1; n

let private mkTyped (st: InferState) (kind: TypedExprKind) (ty: TypeExpr) : TypedExpr =
    { Id = newId st; Type = ty; Expr = kind }

/// Unify and push any error into state, returning empty subst on failure.
let private unifyS (st: InferState) (t1: TypeExpr) (t2: TypeExpr) : Subst =
    match unify t1 t2 with
    | Ok s -> s
    | Error e -> st.Errors <- st.Errors @ [e]; empty

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
    | TEIf(c, t, e) -> TEIf(applyTE s c, applyTE s t, applyTE s e)
    | TEMatch(sc, brs) ->
        TEMatch(applyTE s sc, brs |> List.map (fun (p, b) ->
            { p with Type = applyType s p.Type }, applyTE s b))
    | TEPipe(a, b) -> TEPipe(applyTE s a, applyTE s b)
    | TETagged(e, tag) -> TETagged(applyTE s e, tag)
    | TEList es -> TEList (es |> List.map (applyTE s))
    | TETuple es -> TETuple (es |> List.map (applyTE s))

// ---- Literal types -------------------------------------------------------

let private litTy = function
    | LInt _  -> TyName "Int"
    | LFloat _ -> TyName "Float"
    | LStr _  -> TyName "Str"
    | LBool _ -> TyName "Bool"

// ---- Algorithm W ---------------------------------------------------------

let rec private inferExpr (env: Env) (st: InferState) (expr: Expr) : Subst * TypeExpr * TypedExpr =
    match expr with

    | ELit lit ->
        let ty = litTy lit
        (empty, ty, mkTyped st (TELit lit) ty)

    | EVar x ->
        match Map.tryFind x env with
        | None ->
            st.Errors <- st.Errors @ [e002 x]
            let beta = freshVar st.Fresh
            (empty, beta, mkTyped st (TEVar x) beta)
        | Some sch ->
            let ty = instantiate st.Fresh sch
            (empty, ty, mkTyped st (TEVar x) ty)

    | ECon c ->
        match Map.tryFind c env with
        | None ->
            st.Errors <- st.Errors @ [e002 c]
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
        let (s1, tauF, teF) = inferExpr env st f
        let env1 = applyEnv s1 env
        let (s2, tauA, teA) = inferExpr env1 st a
        let beta = freshVar st.Fresh
        let s3 = unifyS st (applyType s2 tauF) (TyFn(tauA, beta))
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

    | EMatch branches ->
        // Match as expression: synthesize scrutinee var, build function type
        let alpha = freshVar st.Fresh  // scrutinee type
        let beta  = freshVar st.Fresh  // result type
        let (sAll, typedBranches) =
            List.fold (fun (sAcc, brsAcc) (pat, body) ->
                let (patTy, patBindings) = patternType st env pat
                let su = unifyS st (applyType sAcc patTy) (applyType sAcc alpha)
                let envExt = List.fold (fun e (n, t) -> Map.add n (mono t) e) (applyEnv (compose su sAcc) env) patBindings
                let (sb, tauB, teB) = inferExpr envExt st body
                let sr = unifyS st (applyType sb tauB) (applyType (compose sb su) beta)
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

    | DFn(sig_, body) ->
        // Normalize param and return types: TyName X -> TyVar X for type params
        let normParams = sig_.Params |> List.map (fun (n, ty) -> n, normalizeTy ty)
        let normRet = sig_.ReturnType |> Option.map normalizeTy
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
                        let su = unifyS st (applyType sAcc patTy) (applyType sAcc lastParamTy)
                        let envExt = List.fold (fun e (n, t) -> Map.add n (mono t) e) (applyEnv (compose su sAcc) paramEnv) patBindings
                        let (sb, tauB, teB) = inferExpr envExt st branchBody
                        let sr = unifyS st (applyType sb tauB) (applyType (compose sb su) beta)
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
        let sRet = unifyS st (applyType sBody tauBody) (applyType sBody expectedRet)
        let sAll = compose sRet sBody
        // Build full fn type (curried)
        let paramTys = normParams |> List.map (fun (_, ty) -> applyType sAll ty)
        let retTy = applyType sAll expectedRet
        let fnTy = buildFnType paramTys retTy
        // Generalize: add declared rigid type vars + any free flex vars
        let baseSch = generalize (applyEnv sAll env) fnTy
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
            let (sBody, tauBody, teBody) =
                match body with
                | EMatch branches when not (List.isEmpty normParams) ->
                    let lastParam = fst (List.last normParams)
                    let lastParamTy = snd (List.last normParams)
                    let beta = freshVar st.Fresh
                    let (sAll, typedBranches) =
                        List.fold (fun (sAcc, brsAcc) (pat, branchBody) ->
                            let (patTy, patBindings) = patternType st paramEnv pat
                            let su = unifyS st (applyType sAcc patTy) (applyType sAcc lastParamTy)
                            let envExt = List.fold (fun e (n, t) -> Map.add n (mono t) e) (applyEnv (compose su sAcc) paramEnv) patBindings
                            let (sb, tauB, teB) = inferExpr envExt st branchBody
                            let sr = unifyS st (applyType sb tauB) (applyType (compose sb su) beta)
                            let sStep = compose sr (compose sb (compose su sAcc))
                            let tp = { Pat = pat; Type = applyType sStep patTy }
                            (sStep, brsAcc @ [(tp, applyTE sStep teB)])
                        ) (empty, []) branches
                    let scrutTE = mkTyped st (TEVar lastParam) (applyType sAll lastParamTy)
                    let matchTE = mkTyped st (TEMatch(scrutTE, typedBranches)) (applyType sAll beta)
                    (sAll, applyType sAll beta, matchTE)
                | _ ->
                    inferExpr paramEnv st body
            let sRet = unifyS st (applyType sBody tauBody) (applyType sBody expectedRet)
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

let infer (m: LLModule) (env0: Elaborator.TypeEnv) : Result<TypedModule, LLError list> =
    let initEnv = fromElaboratorEnv env0
    let st = newState ()
    let (decls, _) =
        List.fold (fun (declsAcc, envAcc) (decl, exported) ->
            let (td, env') = inferDecl envAcc st decl exported
            (declsAcc @ [td], env')
        ) ([], initEnv) m.Decls
    // Collect final env from accumulated decls
    let finalEnv =
        List.fold (fun envAcc (td, _) ->
            match td with
            | TDFn(sig_, sch, _) -> Map.add sig_.Name sch envAcc
            | TDLet(name, sch, _) -> Map.add name sch envAcc
            | TDImpl(_, implType, fns) ->
                List.fold (fun e (sig_: TypedFnSig, sch, _) ->
                    Map.add (sig_.Name + "_" + implType) sch e) envAcc fns
            | _ -> envAcc
        ) initEnv decls
    if st.Errors <> [] then Error st.Errors
    else Ok { Path = m.Path; Decls = decls; Env = finalEnv; Dispatch = st.Dispatch }
