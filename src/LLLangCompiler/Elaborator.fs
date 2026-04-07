module LLLang.Elaborator

open LLLang.AST

/// Symbol table: name → declared type.
type TypeEnv = Map<string, TypeExpr>

type ErrorCode = E001 | E002 | E003 | E004 | E005

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

        | DType(typeName, _typeParams, TBSum ctors) ->
            for (ctorName, argTypes) in ctors do
                let ty =
                    match argTypes with
                    | [] -> TyName typeName
                    | _  -> buildFnType argTypes (TyName typeName)
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
        // In Task 3, just propagate errors; type-checking EApp comes in Task 4
        let (_, fe) = typeOf f env
        let (_, ae) = typeOf arg env
        (TyVar "?", fe @ ae)

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
        let errs =
            branches
            |> List.collect (fun (_, branchExpr) -> snd (typeOf branchExpr env))
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

/// Elaborate an LLModule: build TypeEnv, check for errors.
/// Returns Ok TypeEnv on success, Error errors on any violation.
let elaborate (m: LLModule) : Result<TypeEnv, LLError list> =
    let env = collectDecls m
    let errors = checkDecls m env
    if errors.IsEmpty then Ok env else Error errors
