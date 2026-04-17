module LLLang.Elaborator

open LLLang.AST

/// Symbol table: name → declared type.
type TypeEnv = Map<string, TypeExpr>

type ErrorCode = E001 | E002 | E003 | E004 | E005 | E006 | E008
                | E020 | E024 | E025 | E026

type LLError = {
    Code: ErrorCode
    Line: int
    Col: int
    /// Position-free detail: "TypeMismatch Str Str[UserId]" (no code or pos prefix).
    /// Used to rebuild Message when position is patched in after construction.
    Body: string
    /// Full compact format: "E001 12:5 TypeMismatch Str Str[UserId]"
    Message: string
}

let internal mkLLError code line col body = {
    Code = code; Line = line; Col = col; Body = body
    Message = sprintf "%s %d:%d %s" (code.ToString()) line col body }

let private e003 line col typeName missing =
    mkLLError E003 line col (sprintf "NonExhaustiveMatch %s missing:%s" typeName missing)

let private e026 line col target name =
    mkLLError E026 line col (sprintf "UnknownExternalMapping target:%s name:%s" target name)

/// Arithmetic and comparison operators pre-populated as TyVar wildcards.
/// These are parsed as EApp(EApp(EVar "+", ...), ...) and must not trigger E002
/// since they are never declared in source modules.
///
/// Phase 6 adds a minimal stdlib: Math, List, Maybe, Result, Str, IO builtins.
/// The codegen F# prelude block (see Codegen.fsharpPrelude) provides the
/// runtime bindings. Note: Maybe / Result are NOT built-in types — user code
/// that consumes e.g. `listHead` (which returns `Maybe A`) must also declare
/// `type Maybe A = Some A | None`. Same for Result.
let private builtinEnv : TypeEnv =
    let arithOps = [ "+"; "-"; "*"; "/" ]
    let cmpOps   = [ "=="; "!="; "<"; ">"; "<="; ">=" ]
    let arith = arithOps |> List.map (fun op -> op, TyFn(TyVar "a", TyFn(TyVar "a", TyVar "a")))
    let cmp   = cmpOps   |> List.map (fun op -> op, TyFn(TyVar "a", TyFn(TyVar "a", TyName "Bool")))
    // IO builtins (emitted verbatim in codegen).
    let io = [
        "printfn", TyFn(TyName "Str", TyName "Unit")
        "print",   TyFn(TyName "Str", TyName "Unit")
    ]
    // --- stdlib type shorthands ---
    let tA = TyVar "A"
    let tB = TyVar "B"
    let tE = TyVar "E"
    let tF = TyVar "F"
    let tInt   = TyName "Int"
    let tFloat = TyName "Float"
    let tStr   = TyName "Str"
    let tBool  = TyName "Bool"
    let listOf t = TyApp(TyName "List", t)
    let maybeOf t = TyApp(TyName "Maybe", t)
    let resultOf a e = TyApp(TyApp(TyName "Result", a), e)
    // Math
    let math = [
        "abs",  TyFn(tInt, tInt)
        "absf", TyFn(tFloat, tFloat)
        "sqrt", TyFn(tFloat, tFloat)
        "min",  TyFn(tInt, TyFn(tInt, tInt))
        "max",  TyFn(tInt, TyFn(tInt, tInt))
    ]
    // List
    let list = [
        "listLen",     TyFn(listOf tA, tInt)
        "listMap",     TyFn(TyFn(tA, tB), TyFn(listOf tA, listOf tB))
        "listFilter",  TyFn(TyFn(tA, tBool), TyFn(listOf tA, listOf tA))
        "listFold",    TyFn(TyFn(tB, TyFn(tA, tB)), TyFn(tB, TyFn(listOf tA, tB)))
        "listHead",    TyFn(listOf tA, maybeOf tA)
        "listTail",    TyFn(listOf tA, maybeOf (listOf tA))
        "listReverse", TyFn(listOf tA, listOf tA)
        "listAppend",  TyFn(listOf tA, TyFn(listOf tA, listOf tA))
    ]
    // Maybe
    let maybe = [
        "maybeMap",         TyFn(TyFn(tA, tB), TyFn(maybeOf tA, maybeOf tB))
        "maybeBind",        TyFn(maybeOf tA, TyFn(TyFn(tA, maybeOf tB), maybeOf tB))
        "maybeWithDefault", TyFn(tA, TyFn(maybeOf tA, tA))
    ]
    // Result
    let result = [
        "resultMap",    TyFn(TyFn(tA, tB), TyFn(resultOf tA tE, resultOf tB tE))
        "resultBind",   TyFn(resultOf tA tE, TyFn(TyFn(tA, resultOf tB tE), resultOf tB tE))
        "resultMapErr", TyFn(TyFn(tE, tF), TyFn(resultOf tA tE, resultOf tA tF))
    ]
    // Compact symbolic operators used by parser/state/result-heavy code.
    // These are fixed built-ins (not user-defined operator declarations).
    // Without higher-kinded constraints we still keep operator typing simple.
    // We nevertheless force bind/sequence/choice to stay on one carrier `m`.
    // TODO(selfhost:operators):
    // blocker: no higher-kinded constraints / dictionary elaboration for
    //   operator-driven abstractions (`>>=`, `>>`, `<|>`).
    // works-now: both parsers accept symbolic operators with stable precedence;
    //   elaboration/codegen preserve operator surface end-to-end; `>>=` now
    //   requires a function RHS and same-carrier return (`m >>= (a -> m) -> m`);
    //   `>>`/`<|>` are constrained to one carrier (`m -> m -> m`).
    // next-step: introduce constrained operator typing (Monad/Alt-like
    //   evidence encoding) with principled inference boundaries.
    // works-now: all backends lower symbolic operators without emitting raw
    //   symbolic identifiers (`>>=` lowers to application, `>>` returns rhs,
    //   `<|>` returns lhs), so codegen is stable cross-target.
    // known-gap: semantics are intentionally minimal and not trait-evidence
    //   driven (no true Monad/Alt dispatch yet).
    // coverage-note: HM and codegen tests pin symbolic-chain mismatch
    //   diagnostics and backend lowering output.
    let symbolic = [
        "<|>", TyFn(TyVar "m", TyFn(TyVar "m", TyVar "m"))
        ">>=", TyFn(TyVar "m", TyFn(TyFn(TyVar "a", TyVar "m"), TyVar "m"))
        ">>",  TyFn(TyVar "m", TyFn(TyVar "m", TyVar "m"))
    ]
    // Str
    let str = [
        "strLen",      TyFn(tStr, tInt)
        "strConcat",   TyFn(tStr, TyFn(tStr, tStr))
        "strTrim",     TyFn(tStr, tStr)
        "strContains", TyFn(tStr, TyFn(tStr, tBool))
        "strToInt",    TyFn(tStr, maybeOf tInt)
        "strToFloat",  TyFn(tStr, maybeOf tFloat)
    ]
    // --- Phase 6.5 extensions ---
    let charT = TyName "Char"
    // String / char
    let strChar = [
        "strChars",     TyFn(tStr, listOf charT)
        "charToInt",    TyFn(charT, tInt)
        "intToChar",    TyFn(tInt, charT)
        "intToStr",     TyFn(tInt, tStr)
        "floatToStr",   TyFn(tFloat, tStr)
        "strSlice",     TyFn(tStr, TyFn(tInt, TyFn(tInt, tStr)))
        "strIndexOf",   TyFn(tStr, TyFn(tStr, tInt))
        "strSplit",     TyFn(tStr, TyFn(tStr, listOf tStr))
        "strFromChars", TyFn(listOf charT, tStr)
        "strReverse",   TyFn(tStr, tStr)
        "charIsDigit",  TyFn(charT, tBool)
        "charIsAlpha",  TyFn(charT, tBool)
        "charIsSpace",  TyFn(charT, tBool)
    ]
    // File IO
    let fileIO = [
        "readFile",   TyFn(tStr, tStr)
        "writeFile",  TyFn(tStr, TyFn(tStr, TyName "Unit"))
        "fileExists", TyFn(tStr, tBool)
        "dirList",    TyFn(tStr, listOf tStr)
    ]
    // Process
    let proc = [
        "exit",          TyFn(tInt, TyName "Unit")
        "getArgs",       listOf tStr
        "processSpawn",  TyFn(tStr, TyFn(listOf tStr, tInt))
    ]
    // List extras (listAt requires user-declared `type Maybe`)
    let listExtra = [
        "listConcat",  TyFn(listOf (listOf tA), listOf tA)
        "listIsEmpty", TyFn(listOf tA, tBool)
        "listAt",      TyFn(listOf tA, TyFn(tInt, maybeOf tA))
    ]
    Map.ofList (arith @ cmp @ io @ math @ list @ maybe @ result @ str
                @ symbolic
                @ strChar @ fileIO @ proc @ listExtra)

/// Build a right-associative chain of TyFn from a list of parameter types plus a return type.
/// e.g. [T1; T2] ret  →  TyFn(T1, TyFn(T2, ret))
let private buildFnType (paramTypes: TypeExpr list) (ret: TypeExpr) : TypeExpr =
    List.foldBack (fun t acc -> TyFn(t, acc)) paramTypes ret

/// Normalize a type in a function signature: single-uppercase-letter TyName values
/// (like A, B, C used as type parameters) are converted to TyVar so that the
/// elaborator treats them as wildcards during type checking.
let private normalizeFnTy (ty: TypeExpr) : TypeExpr =
    let rec go t =
        match t with
        | TyName n when n.Length = 1 && System.Char.IsUpper n.[0] -> TyVar n
        | TyApp(a, b) -> TyApp(go a, go b)
        | TyFn(a, b) -> TyFn(go a, go b)
        | TyTagged(a, u) -> TyTagged(go a, u)
        | _ -> t
    go ty

/// Pass 1: collect all declared names → types into TypeEnv.
let private collectDecls (m: LLModule) : TypeEnv =
    let mutable env = builtinEnv

    let addFnSig (sigRecord: FnSig) (nameSuffix: string option) =
        let name =
            match nameSuffix with
            | None -> sigRecord.Name
            | Some suffix -> sigRecord.Name + "_" + suffix
        let ret = sigRecord.ReturnType |> Option.map normalizeFnTy |> Option.defaultValue (TyVar "?")
        let ty =
            match sigRecord.Params with
            | [] -> TyVar "?"
            | ps ->
                let paramTypes = ps |> List.map (fun (_, t) -> normalizeFnTy t)
                buildFnType paramTypes ret
        env <- Map.add name ty env

    for (decl, _isExported) in m.Decls do
        match decl with
        | DFn(sigRecord, _body) ->
            addFnSig sigRecord None

        | DExternal sigRecord ->
            addFnSig sigRecord None

        | DLet(name, expr) ->
            let ty =
                match expr with
                | ELit(LInt _)   -> TyName "Int"
                | ELit(LFloat _) -> TyName "Float"
                | ELit(LStr _)   -> TyName "Str"
                | ELit(LChar _)  -> TyName "Char"
                | ETagged(ELit(LInt _),   tag) -> TyTagged(TyName "Int",   UName tag)
                | ETagged(ELit(LFloat _), tag) -> TyTagged(TyName "Float", UName tag)
                | ETagged(ELit(LStr _),   tag) -> TyTagged(TyName "Str",   UName tag)
                | _ -> TyVar "?"
            env <- Map.add name ty env

        | DLetPat(pat, _expr) ->
            // Bind every name introduced by the pattern as a wildcard;
            // HMInfer will refine to concrete types in pass 2.
            let rec patVars (p: Pattern) : string list =
                match p with
                | PVar n -> [n]
                | PCon(_, ps) -> ps |> List.collect patVars
                | PTuple ps -> ps |> List.collect patVars
                | PCons(h, t) -> patVars h @ patVars t
                | PLit _ | PWild -> []
            for n in patVars pat do
                env <- Map.add n (TyVar "?") env

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
            // Build the fully-applied return type, e.g. Maybe[A] for type Maybe A
            let retTy =
                List.fold (fun acc tp ->
                    match tp with
                    | TPBare n | TPPhantom n -> TyApp(acc, TyVar n)
                ) (TyName typeName) typeParams
            for (ctorName, argTypes) in ctors do
                let ty =
                    match argTypes with
                    | [] -> retTy
                    | _  -> buildFnType (argTypes |> List.map subst) retTy
                env <- Map.add ctorName ty env

        | DImpl(_traitName, implType, fns) ->
            for (sigRecord, _body) in fns do
                addFnSig sigRecord (Some implType)

        | DTrait(_traitName, _traitVars, sigs) ->
            // Expose trait method signatures as plain names so constrained
            // calls like `map f xs` pass elaboration (E002) before HMInfer
            // rewrites monomorphic call sites to `map_Maybe`-style symbols.
            for sigRecord in sigs do
                if not (Map.containsKey sigRecord.Name env) then
                    addFnSig sigRecord None

        | DTag _
        | DUnit _
        | DOpaque _
        | DType(_, _, TBRecord _)
        | DType(_, _, TBWrapped _) -> ()

    env


/// Look up the source position of an AST node in the PosMap side-table.
/// Returns 0:0 when a node was synthesized (no source location recorded).
let private posOf (pm: PosMap) (node: obj | null) : int * int =
    let p = PosMap.tryFind pm node
    (p.Line, p.Col)

/// Pass 2: exhaustiveness check for match expressions.
/// For each DFn whose body is a clause-sugar top-level `EMatch` (i.e.
/// `fn f(..)(x T) = | PatA -> .. | PatB -> ..`) AND whose LAST parameter
/// type is a named sum type, verify that the body's branches cover all
/// constructors of that type.
///
/// Scope limitation: the check is deliberately narrow to avoid false
/// positives.  We only inspect the *direct* top-level `EMatch` of the fn
/// body, because:
///   1. Clause-sugar curries over the LAST parameter — that's the implicit
///      scrutinee. Checking against the first parameter is incorrect for
///      multi-param fns (e.g. `fn f(lhs Expr)(toks List[Token]) = | TPlus :: rest -> ..`
///      where the top-level match is on `toks`, not `lhs`).
///   2. Nested `EMatch` / `EMatchOf` expressions inside the body scrutinize
///      *arbitrary* intermediate values whose types we can't know without
///      full H-M inference. Treating them all as "matches against the
///      fn's first parameter type" produces cascading spurious E003s.
///
/// HMInfer (Phase 4) is the proper place for full exhaustiveness across
/// every match. This pass only catches the most common clause-sugar case.
/// `pm` is used to attach the source position of the offending match
/// expression to each emitted E003 error.
let private exhaustivenessCheck (pm: PosMap) (m: LLModule) (_env: TypeEnv) : LLError list =
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
            // Clause-sugar scrutinizes the LAST curried param, not the first.
            let (_, lastParamType) = List.last sigRecord.Params
            // Only check the TOP-LEVEL body if it is an `EMatch` clause sugar.
            // Nested matches and explicit `match ... with` (EMatchOf) are
            // skipped — their scrutinee type requires H-M inference to know.
            match body, lastParamType with
            | EMatch branches, TyName typeName when Map.containsKey typeName typeToCtors ->
                let requiredCtors = typeToCtors[typeName]
                // A PWild / PVar / PTuple / PCons branch is a catch-all
                // relative to a sum type: tuples are product types, not sum
                // types, and a cons pattern is already open-ended.
                let hasCatchAll =
                    branches
                    |> List.exists (fun (pat, _) ->
                        match pat with
                        | PWild | PVar _ | PTuple _ | PCons _ -> true
                        | _ -> false)
                if not hasCatchAll then
                    let coveredCtors =
                        branches
                        |> List.choose (fun (pat, _) ->
                            match pat with
                            | PCon(name, _) -> Some name
                            | _ -> None)
                    // Look up the match expression's position so E003
                    // points at the `match` / first `|` arm rather than 0:0.
                    let (lnBody, colBody) = posOf pm (box body)
                    // Clause-sugar `|` bodies can be synthesized as a bare
                    // `EMatch` node without a direct PosMap entry. Fall back
                    // to the function signature position instead of 0:0.
                    let (ln, col) =
                        if lnBody > 0 then
                            (lnBody, colBody)
                        else
                            posOf pm (box sigRecord)
                    for c in requiredCtors do
                        if not (List.contains c coveredCtors) then
                            yield e003 ln col typeName c
            | _ -> ()
        | _ -> () ]

// ---- Tag rewrite pass -------------------------------------------------
//
// The parser cannot tell `Float[m]` (tagged type, `m` is a unit tag) from
// `Maybe[A]` (type application, `A` is a type parameter), so it emits every
// `T[X]` as `TyApp(T, TyVar X)`. After we know the set of declared tag
// names from `tag m` / `tag s` declarations, we rewrite occurrences of
// `TyApp(T, TyVar t)` / `TyApp(T, TyName t)` into `TyTagged(T, UName t)`
// whenever `t` is a declared tag. We rewrite types inside function
// signatures (params + return), variant constructor argument types and
// `let` type annotations. Composite unit algebra (`Float[m/s]`) is NOT
// modelled here — a return type like `d / t` stays as whatever the H-M
// pass infers (typically a fresh flex var); this is a known limitation.

/// Collect all tag and unit names declared in the module (from `tag X` / `unit x`).
/// Both use TyTagged in the type system and need the TyApp→TyTagged rewrite.
let private collectTagNames (m: LLModule) : Set<string> =
    m.Decls
    |> List.choose (fun (decl, _) ->
        match decl with
        | DTag name -> Some name
        | DUnit name -> Some name
        | _ -> None)
    |> Set.ofList

/// Rewrite a single type expression: `TyApp(base, TyVar t)` or
/// `TyApp(base, TyName t)` becomes `TyTagged(base, UName t)` when `t` is
/// a known tag name. Applied structurally.
let rec private rewriteTyWithTags (tags: Set<string>) (ty: TypeExpr) : TypeExpr =
    match ty with
    | TyApp(b, TyVar t) when Set.contains t tags ->
        TyTagged(rewriteTyWithTags tags b, UName t)
    | TyApp(b, TyName t) when Set.contains t tags ->
        TyTagged(rewriteTyWithTags tags b, UName t)
    | TyApp(a, b) -> TyApp(rewriteTyWithTags tags a, rewriteTyWithTags tags b)
    | TyFn(a, b) -> TyFn(rewriteTyWithTags tags a, rewriteTyWithTags tags b)
    | TyTagged(a, u) -> TyTagged(rewriteTyWithTags tags a, u)
    | TyName _ | TyVar _ -> ty

/// Rewrite tag-named TyApp occurrences in a function signature's parameter
/// and return types.
let private rewriteFnSigTags (tags: Set<string>) (s: FnSig) : FnSig =
    { s with
        Params = s.Params |> List.map (fun (n, t) -> n, rewriteTyWithTags tags t)
        ReturnType = s.ReturnType |> Option.map (rewriteTyWithTags tags) }

/// Rewrite a full module, replacing tag-named TyApp with TyTagged in all
/// function signatures (including trait + impl), variant constructor args,
/// and record / wrapped type bodies.
let rewriteTagsInModule (m: LLModule) : LLModule =
    let tags = collectTagNames m
    if Set.isEmpty tags then m
    else
        let rewriteBody = function
            | TBSum ctors ->
                TBSum (ctors |> List.map (fun (n, args) ->
                    n, args |> List.map (rewriteTyWithTags tags)))
            | TBRecord fields ->
                TBRecord (fields |> List.map (fun (n, t) -> n, rewriteTyWithTags tags t))
            | TBWrapped t -> TBWrapped (rewriteTyWithTags tags t)
        let rewriteDecl = function
            | DFn(s, body) -> DFn(rewriteFnSigTags tags s, body)
            | DExternal s -> DExternal (rewriteFnSigTags tags s)
            | DType(name, ps, body) -> DType(name, ps, rewriteBody body)
            | DTrait(name, vars, sigs) ->
                DTrait(name, vars, sigs |> List.map (rewriteFnSigTags tags))
            | DImpl(tr, ty, fns) ->
                DImpl(tr, ty,
                    fns |> List.map (fun (s, e) -> rewriteFnSigTags tags s, e))
            | other -> other
        { m with Decls = m.Decls |> List.map (fun (d, exp) -> rewriteDecl d, exp) }

/// Elaborate an LLModule: build TypeEnv, check for errors.
/// Returns Ok (rewrittenModule, TypeEnv) on success, Error errors on any violation.
/// The rewritten module has all `TyApp(T, TyVar t)` (where `t` is a declared tag)
/// normalised to `TyTagged(T, UName t)` so HMInfer sees correct types.
/// `pm` is the side-table of source positions populated by the parser; pass
/// `PosMap.empty ()` if positions are unavailable (errors will fall back to 0:0).
let elaborate (pm: PosMap) (m: LLModule) : Result<LLModule * TypeEnv, LLError list> =
    let m' = rewriteTagsInModule m
    let env = collectDecls m'
    let exhaustErrors = exhaustivenessCheck pm m' env
    if exhaustErrors.IsEmpty then Ok (m', env) else Error exhaustErrors

/// Elaborate an LLModule with an additional imported environment (from previously
/// compiled files). The imported bindings are merged into the initial env before
/// this module's own declarations are collected, so imported names are visible
/// during elaboration and will not produce E002 UnboundVar errors.
let elaborateWithImports (pm: PosMap) (m: LLModule) (importedEnv: TypeEnv) : Result<LLModule * TypeEnv, LLError list> =
    let m' = rewriteTagsInModule m
    // Start collectDecls from builtinEnv + importedEnv instead of just builtinEnv.
    // We do this by building the env with imports merged in, then collecting
    // this module's own decls on top.
    let baseEnv = Map.fold (fun acc k v -> Map.add k v acc) builtinEnv importedEnv
    // Re-use collectDecls by temporarily shadowing builtinEnv is not possible
    // from outside, so we inline the merge: run collectDecls and then merge.
    // collectDecls always starts from builtinEnv, so we re-overlay importedEnv
    // on top of the result (module-local decls win over imports).
    let localEnv = collectDecls m'
    let env = Map.fold (fun acc k v -> Map.add k v acc) baseEnv localEnv
    let exhaustErrors = exhaustivenessCheck pm m' env
    if exhaustErrors.IsEmpty then Ok (m', env) else Error exhaustErrors
