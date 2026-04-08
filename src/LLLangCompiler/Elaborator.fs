module LLLang.Elaborator

open LLLang.AST

/// Symbol table: name → declared type.
type TypeEnv = Map<string, TypeExpr>

type ErrorCode = E001 | E002 | E003 | E004 | E005 | E006 | E008

type LLError = {
    Code: ErrorCode
    Line: int
    Col: int
    /// Compact format: "E001 12:5 TypeMismatch Str Str[UserId]"
    Message: string
}

let private e001 line col got expected = {
    Code = E001; Line = line; Col = col
    Message = sprintf "E001 %d:%d TypeMismatch %A %A" line col got expected }

let private e002 line col name = {
    Code = E002; Line = line; Col = col
    Message = sprintf "E002 %d:%d UnboundVar %s" line col name }

let private e003 line col typeName missing = {
    Code = E003; Line = line; Col = col
    Message = sprintf "E003 %d:%d NonExhaustiveMatch %s missing:%s" line col typeName missing }

let private e004 line col got expected = {
    Code = E004; Line = line; Col = col
    Message = sprintf "E004 %d:%d UnitMismatch %A %A" line col got expected }

let private e005 line col paramType argType = {
    Code = E005; Line = line; Col = col
    Message = sprintf "E005 %d:%d TagViolation %A %A" line col paramType argType }

/// Arithmetic and comparison operators pre-populated as TyVar wildcards.
/// These are parsed as EApp(EApp(EVar "+", ...), ...) and must not trigger E002
/// since they are never declared in source modules.
let private builtinEnv : TypeEnv =
    [ "+"; "-"; "*"; "/"; "=="; "!="; "<"; ">"; "<="; ">=" ]
    |> List.map (fun op -> op, TyFn(TyVar "a", TyFn(TyVar "a", TyVar "a")))
    |> Map.ofList

/// Build a right-associative chain of TyFn from a list of parameter types plus a return type.
/// e.g. [T1; T2] ret  →  TyFn(T1, TyFn(T2, ret))
let private buildFnType (paramTypes: TypeExpr list) (ret: TypeExpr) : TypeExpr =
    List.foldBack (fun t acc -> TyFn(t, acc)) paramTypes ret

/// Pass 1: collect all declared names → types into TypeEnv.
let private collectDecls (m: LLModule) : TypeEnv =
    let mutable env = builtinEnv

    let addFnSig (sigRecord: FnSig) (nameSuffix: string option) =
        let name =
            match nameSuffix with
            | None -> sigRecord.Name
            | Some suffix -> sigRecord.Name + "_" + suffix
        let ret = sigRecord.ReturnType |> Option.defaultValue (TyVar "?")
        let ty =
            match sigRecord.Params with
            | [] -> TyVar "?"
            | ps ->
                let paramTypes = ps |> List.map snd
                buildFnType paramTypes ret
        env <- Map.add name ty env

    for (decl, _isExported) in m.Decls do
        match decl with
        | DFn(sigRecord, _body) ->
            addFnSig sigRecord None

        | DLet(name, expr) ->
            let tyOpt =
                match expr with
                | ELit(LInt _)   -> Some (TyName "Int")
                | ELit(LFloat _) -> Some (TyName "Float")
                | ELit(LStr _)   -> Some (TyName "Str")
                | ETagged(ELit(LInt _),   tag) -> Some (TyTagged(TyName "Int",   UName tag))
                | ETagged(ELit(LFloat _), tag) -> Some (TyTagged(TyName "Float", UName tag))
                | ETagged(ELit(LStr _),   tag) -> Some (TyTagged(TyName "Str",   UName tag))
                | _ -> None
            match tyOpt with
            | Some ty -> env <- Map.add name ty env
            | None    -> ()

        | DType(typeName, typeParams, TBSum ctors) ->
            // Collect declared type parameter names so we can treat them as TyVar
            let tpNames =
                typeParams
                |> List.choose (fun tp ->
                    match tp with
                    | TPBare n | TPPhantom n -> Some n)
                |> Set.ofList
            // Replace TyName x with TyVar x when x is a declared type parameter
            let rec subst (ty: TypeExpr) =
                match ty with
                | TyName n when Set.contains n tpNames -> TyVar n
                | TyApp(a, b) -> TyApp(subst a, subst b)
                | TyFn(a, b)  -> TyFn(subst a, subst b)
                | TyTagged(a, u) -> TyTagged(subst a, u)
                | other -> other
            for (ctorName, argTypes) in ctors do
                let ty =
                    match argTypes with
                    | [] -> TyName typeName
                    | _  -> buildFnType (argTypes |> List.map subst) (TyName typeName)
                env <- Map.add ctorName ty env

        | DImpl(_traitName, implType, fns) ->
            for (sigRecord, _body) in fns do
                addFnSig sigRecord (Some implType)

        | DTag _
        | DUnit _
        | DTrait _
        | DType(_, _, TBRecord _)
        | DType(_, _, TBWrapped _) -> ()

    env

/// Normalize a type annotation that the parser may represent as either
/// TyApp(base, TyName tag) or TyTagged(base, UName tag).
/// Returns Some(base, tagName) if the type is a tagged/app form, else None.
let private asTagged (ty: TypeExpr) : (TypeExpr * string) option =
    match ty with
    | TyTagged(base', UName tag) -> Some(base', tag)
    | TyApp(base', TyName tag)   -> Some(base', tag)
    | TyApp(base', TyVar tag)    -> Some(base', tag)
    | _                          -> None

/// Structural equality for types. TyVar matches anything (wildcard).
/// Treats TyApp(b, TyName t) and TyTagged(b, UName t) as equivalent.
let rec private tyEqual (a: TypeExpr) (b: TypeExpr) : bool =
    match a, b with
    | TyVar _, _          -> true
    | _, TyVar _          -> true
    | TyName x, TyName y  -> x = y
    | TyTagged(b1, u1), TyTagged(b2, u2) -> tyEqual b1 b2 && u1 = u2
    | TyFn(a1, r1), TyFn(a2, r2)         -> tyEqual a1 a2 && tyEqual r1 r2
    | _ ->
        // Normalize both sides and compare
        match asTagged a, asTagged b with
        | Some(ba, ta), Some(bb, tb) -> tyEqual ba bb && ta = tb
        | _ -> false

/// Extract the base type from a tagged/app type (or return the type itself).
let private baseOf (ty: TypeExpr) : TypeExpr =
    match asTagged ty with
    | Some(base', _) -> base'
    | None           -> ty

/// Classify the mismatch between paramType and argType into E001/E004/E005.
let private classifyMismatch (paramType: TypeExpr) (argType: TypeExpr) line col : LLError =
    match asTagged paramType, asTagged argType with
    | Some(pBase, pTag), Some(aBase, aTag) when tyEqual pBase aBase && pTag <> aTag ->
        // Same base type, different unit/tag annotation
        match pBase with
        | TyName "Float" | TyName "Int" -> e004 line col argType paramType
        | _                              -> e001 line col argType paramType
    | Some(pBase, _), None when tyEqual argType pBase ->
        // arg has the right base type but is missing the tag
        e005 line col paramType argType
    | _ ->
        e001 line col argType paramType

/// Pass 2: type-check an expression, accumulating errors.
/// Returns (inferred type, errors). Does NOT throw — errors are collected.
let rec private typeOf (expr: Expr) (env: TypeEnv) : TypeExpr * LLError list =
    match expr with
    | ELit(LInt _)   -> (TyName "Int",   [])
    | ELit(LFloat _) -> (TyName "Float", [])
    | ELit(LStr _)   -> (TyName "Str",   [])
    | ELit(LBool _)  -> (TyName "Bool",  [])

    | ETagged(e, tag) ->
        let (innerType, errs) = typeOf e env
        (TyTagged(innerType, UName tag), errs)

    | EVar x ->
        match Map.tryFind x env with
        | Some ty -> (ty, [])
        | None    -> (TyVar "?", [e002 0 0 x])

    | ECon c ->
        match Map.tryFind c env with
        | Some ty -> (ty, [])
        | None    -> (TyVar "?", [e002 0 0 c])

    | EApp(f, arg) ->
        let (fType, fe) = typeOf f env
        let (argType, ae) = typeOf arg env
        let allErrors = fe @ ae
        match fType with
        | TyFn(paramType, returnType) ->
            if tyEqual argType paramType then
                (returnType, allErrors)
            else
                let err = classifyMismatch paramType argType 0 0
                (returnType, allErrors @ [err])
        | _ ->
            (TyVar "?", allErrors)

    | ELam(_, _) -> (TyVar "?", [])

    | EIf(cond, t, e) ->
        let (_, ce) = typeOf cond env
        let (_, te) = typeOf t env
        let (_, ee) = typeOf e env
        (TyVar "?", ce @ te @ ee)

    | ELet(x, e, bodyOpt) ->
        let (eTy, eErrs) = typeOf e env
        let env' = Map.add x eTy env
        match bodyOpt with
        | Some body ->
            let (bTy, bErrs) = typeOf body env'
            (bTy, eErrs @ bErrs)
        | None -> (eTy, eErrs)

    | EMatch(branches) ->
        // Collect all variable names bound by a pattern
        let rec patVars (pat: Pattern) : string list =
            match pat with
            | PVar n -> [n]
            | PCon(_, pats) -> pats |> List.collect patVars
            | PLit _ | PWild -> []
        let errs =
            branches
            |> List.collect (fun (pat, branchExpr) ->
                let localEnv =
                    patVars pat
                    |> List.fold (fun acc n -> Map.add n (TyVar "?") acc) env
                snd (typeOf branchExpr localEnv))
        (TyVar "?", errs)

    | EPipe(e, f) ->
        let (_, ee) = typeOf e env
        let (_, fe) = typeOf f env
        (TyVar "?", ee @ fe)

    | EList(elems) ->
        let errs = elems |> List.collect (fun el -> snd (typeOf el env))
        (TyVar "?", errs)

    | ETuple(elems) ->
        let errs = elems |> List.collect (fun el -> snd (typeOf el env))
        (TyVar "?", errs)

/// Check a single declaration for errors, given the already-built TypeEnv.
let private checkDecl (decl: Decl) (env: TypeEnv) : LLError list =
    match decl with
    | DFn(sigRecord, body) ->
        // Extend env with the function's own parameters
        let localEnv =
            sigRecord.Params
            |> List.fold (fun acc (paramName, paramType) -> Map.add paramName paramType acc) env
        snd (typeOf body localEnv)

    | DLet(_, expr) ->
        snd (typeOf expr env)

    | DImpl(_, _, fns) ->
        fns |> List.collect (fun (sigRecord, body) ->
            let localEnv =
                sigRecord.Params
                |> List.fold (fun acc (paramName, paramType) -> Map.add paramName paramType acc) env
            snd (typeOf body localEnv))

    | DType _ | DTag _ | DUnit _ | DTrait _ -> []

/// Check all declarations in a module, accumulating errors.
let private checkDecls (m: LLModule) (env: TypeEnv) : LLError list =
    m.Decls
    |> List.collect (fun (decl, _isExported) -> checkDecl decl env)

/// Recursively find all EMatch branch lists within an expression.
/// Does NOT recurse into ELam (separate scope).
let rec private collectMatches (expr: Expr) : (Pattern * Expr) list list =
    match expr with
    | EMatch(branches) ->
        let nested = branches |> List.collect (fun (_, e) -> collectMatches e)
        branches :: nested
    | EApp(f, arg) ->
        collectMatches f @ collectMatches arg
    | EIf(cond, t, e) ->
        collectMatches cond @ collectMatches t @ collectMatches e
    | ELet(_, e, bodyOpt) ->
        let bodyMatches =
            match bodyOpt with
            | Some body -> collectMatches body
            | None -> []
        collectMatches e @ bodyMatches
    | EPipe(e, f) ->
        collectMatches e @ collectMatches f
    | EList(elems) | ETuple(elems) ->
        elems |> List.collect collectMatches
    | ETagged(e, _) -> collectMatches e
    | ELam _ | ELit _ | EVar _ | ECon _ -> []

/// Pass 3: exhaustiveness check for match expressions.
/// For each DFn whose first parameter type is a named sum type,
/// verify that every match in the body covers all constructors.
let private exhaustivenessCheck (m: LLModule) (_env: TypeEnv) : LLError list =
    // Build map: typeName → constructor name list
    let typeToCtors =
        m.Decls
        |> List.choose (fun (decl, _) ->
            match decl with
            | DType(typeName, _, TBSum ctors) ->
                Some (typeName, ctors |> List.map fst)
            | _ -> None)
        |> Map.ofList

    [ for (decl, _) in m.Decls do
        match decl with
        | DFn(sigRecord, body) when not sigRecord.Params.IsEmpty ->
            let (_, firstParamType) = sigRecord.Params[0]
            match firstParamType with
            | TyName typeName when Map.containsKey typeName typeToCtors ->
                let requiredCtors = typeToCtors[typeName]
                let matchBlocks = collectMatches body
                for branches in matchBlocks do
                    let coveredCtors =
                        branches
                        |> List.choose (fun (pat, _) ->
                            match pat with
                            | PCon(name, _) -> Some name
                            | _ -> None)
                    for c in requiredCtors do
                        if not (List.contains c coveredCtors) then
                            yield e003 0 0 typeName c
            | _ -> ()
        | _ -> () ]

/// Elaborate an LLModule: build TypeEnv, check for errors.
/// Returns Ok TypeEnv on success, Error errors on any violation.
let elaborate (m: LLModule) : Result<TypeEnv, LLError list> =
    let env = collectDecls m
    let checkErrors = checkDecls m env
    let exhaustErrors = exhaustivenessCheck m env
    let errors = checkErrors @ exhaustErrors
    if errors.IsEmpty then Ok env else Error errors
