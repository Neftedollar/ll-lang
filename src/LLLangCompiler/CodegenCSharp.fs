module LLLang.CodegenCSharp

open System
open LLLang.AST
open LLLang.Types
open LLLang.TypedAST
open LLLang.Platform

let private csharpKeywords =
    Set.ofList [
        "abstract"; "as"; "base"; "bool"; "break"; "byte"; "case"; "catch"; "char"
        "checked"; "class"; "const"; "continue"; "decimal"; "default"; "delegate"; "do"
        "double"; "else"; "enum"; "event"; "explicit"; "extern"; "false"; "finally"
        "fixed"; "float"; "for"; "foreach"; "goto"; "if"; "implicit"; "in"; "int"
        "interface"; "internal"; "is"; "lock"; "long"; "namespace"; "new"; "null"
        "object"; "operator"; "out"; "override"; "params"; "private"; "protected"
        "public"; "readonly"; "ref"; "return"; "sbyte"; "sealed"; "short"; "sizeof"
        "stackalloc"; "static"; "string"; "struct"; "switch"; "this"; "throw"; "true"
        "try"; "typeof"; "uint"; "ulong"; "unchecked"; "unsafe"; "ushort"; "using"
        "virtual"; "void"; "volatile"; "while"; "record"
    ]

let private safeIdent (s: string) =
    if Set.contains s csharpKeywords then s + "_" else s

let private safeTypeIdent (s: string) =
    let mapped =
        s
        |> Seq.map (fun ch ->
            if Char.IsLetterOrDigit ch || ch = '_' then string ch else "_")
        |> String.concat ""
    let withHead =
        if String.IsNullOrWhiteSpace mapped then "T"
        elif Char.IsDigit mapped.[0] then "T_" + mapped
        else mapped
    if Set.contains withHead csharpKeywords then withHead + "_" else withHead

let private isTypeParamName (n: string) =
    n.Length = 1 && Char.IsUpper n.[0]

let rec private collectTyApp (t: TypeExpr) : TypeExpr * TypeExpr list =
    match t with
    | TyApp(f, a) ->
        let (head, args) = collectTyApp f
        (head, args @ [a])
    | _ -> (t, [])

let rec private emitTypeBoxed (t: TypeExpr) : string =
    match t with
    | TyName "object" -> "object"
    | TyName "Int" -> "long"
    | TyName "Float" -> "double"
    | TyName "Str" -> "string"
    | TyName "Bool" -> "bool"
    | TyName "Char" -> "char"
    | TyName "Unit" -> "object"
    | TyName x when isTypeParamName x -> safeTypeIdent x
    | TyName x -> safeTypeIdent x
    | TyVar v when isTypeParamName v -> safeTypeIdent v
    | TyVar _ -> "object"
    | TyApp(TyName "List", a) -> "List<" + emitTypeBoxed a + ">"
    | TyApp(TyName "Maybe", a) -> safeTypeIdent "Maybe" + "<" + emitTypeBoxed a + ">"
    | TyApp _ ->
        let (head, args) = collectTyApp t
        match head with
        | TyName "Tuple" when not (List.isEmpty args) ->
            let argsStr = args |> List.map emitTypeBoxed |> String.concat ", "
            "Tuple<" + argsStr + ">"
        | TyName name when not (List.isEmpty args) ->
            let argsStr = args |> List.map emitTypeBoxed |> String.concat ", "
            safeTypeIdent name + "<" + argsStr + ">"
        | _ ->
            "object"
    | TyFn(a, b) ->
        let aTy = emitTypeBoxed a
        let bTy = emitTypeBoxed b
        "Func<" + aTy + ", " + bTy + ">"
    | TyTagged(inner, _) -> emitTypeBoxed inner

let private emitType (t: TypeExpr) : string =
    emitTypeBoxed t

let private emitLit (l: Literal) : string =
    match l with
    | LInt n -> string n + "L"
    | LFloat f ->
        let s = sprintf "%g" f
        if s.Contains(".") || s.Contains("e") || s.Contains("E") then s else s + ".0"
    | LStr s ->
        let escaped =
            s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
        "\"" + escaped + "\""
    | LBool b -> if b then "true" else "false"
    | LChar ch ->
        let escaped =
            match ch with
            | '\\' -> "\\\\"
            | '\'' -> "\\'"
            | '\n' -> "\\n"
            | '\t' -> "\\t"
            | '\r' -> "\\r"
            | c -> string c
        "'" + escaped + "'"

let private binaryOp (op: string) : string option =
    match op with
    | "+" -> Some "+"
    | "-" -> Some "-"
    | "*" -> Some "*"
    | "/" -> Some "/"
    | "==" -> Some "=="
    | "!=" -> Some "!="
    | "<" -> Some "<"
    | ">" -> Some ">"
    | "<=" -> Some "<="
    | ">=" -> Some ">="
    | _ -> None

let private tryAsBinOp (te: TypedExpr) : (string * TypedExpr * TypedExpr) option =
    match te.Expr with
    | TEApp(outer, right) ->
        match outer.Expr with
        | TEApp(inner, left) ->
            match inner.Expr with
            | TEVar op ->
                match binaryOp op with
                | Some csOp -> Some (csOp, left, right)
                | None -> None
            | _ -> None
        | _ -> None
    | _ -> None

let private tryAsSymbolicOp (te: TypedExpr) : (string * TypedExpr * TypedExpr) option =
    match te.Expr with
    | TEApp(outer, right) ->
        match outer.Expr with
        | TEApp(inner, left) ->
            match inner.Expr with
            | TEVar (">>=" as op)
            | TEVar (">>" as op)
            | TEVar ("<|>" as op) -> Some (op, left, right)
            | _ -> None
        | _ -> None
    | _ -> None

let private tryAsStrConcat (te: TypedExpr) : (TypedExpr * TypedExpr) option =
    match te.Expr with
    | TEApp(outer, right) ->
        match outer.Expr with
        | TEApp({ Expr = TEVar "++" }, left) -> Some (left, right)
        | _ -> None
    | _ -> None

let private needsCallHeadParens (s: string) =
    s.Contains("=>")
    || s.Contains("?")
    || s.Contains(":")
    || s.Contains(" ")

let private emitCall (head: string) (arg: string) =
    let h = if needsCallHeadParens head then "(" + head + ")" else head
    h + "(" + arg + ")"

let private wrapLambdaHeadAsDelegate (headType: TypeExpr) (headExpr: string) : string =
    if headExpr.Contains("=>") then
        "((" + emitType headType + ")(" + headExpr + "))"
    else
        headExpr

let private constructorTypeArgSuffix (t: TypeExpr) : string =
    let (head, args) = collectTyApp t
    match head, args with
    | TyName _, _ when not (List.isEmpty args) ->
        let argsStr = args |> List.map emitTypeBoxed |> String.concat ", "
        "<" + argsStr + ">"
    | _ -> ""

let private collectTypeParamsFromType (t: TypeExpr) : string list =
    let rec loop (acc: string list) (tt: TypeExpr) =
        match tt with
        | TyVar v when isTypeParamName v ->
            let sv = safeTypeIdent v
            if List.contains sv acc then acc else acc @ [sv]
        | TyName n when isTypeParamName n ->
            let sn = safeTypeIdent n
            if List.contains sn acc then acc else acc @ [sn]
        | TyApp(a, b)
        | TyFn(a, b) ->
            loop (loop acc a) b
        | TyTagged(inner, _) ->
            loop acc inner
        | _ -> acc
    loop [] t

let private typeParamsForSig (sig_: TypedFnSig) : string list =
    let fromRet = collectTypeParamsFromType sig_.ReturnType
    sig_.Params
    |> List.map snd
    |> List.fold (fun acc ty ->
        let vars = collectTypeParamsFromType ty
        vars |> List.fold (fun a v -> if List.contains v a then a else a @ [v]) acc) fromRet

let private emitMethodTypeParams (sig_: TypedFnSig) : string =
    let vars = typeParamsForSig sig_
    if List.isEmpty vars then ""
    else "<" + String.concat ", " vars + ">"

let rec private emitDefaultExpr (t: TypeExpr) : string =
    match t with
    | TyName "Int" -> "0L"
    | TyName "Float" -> "0.0"
    | TyName "Bool" -> "false"
    | TyName "Char" -> "'\\0'"
    | TyName "Str" -> "\"\""
    | TyName "Unit" -> "null"
    | TyApp(TyName "List", a) -> "new " + emitType (TyApp(TyName "List", a)) + "()"
    | TyApp(TyName "Maybe", _) -> "default"
    | TyTagged(inner, _) -> emitDefaultExpr inner
    | t when (match collectTyApp t with TyName "RBMap", (_ :: _) -> true | _ -> false) ->
        // RBMap<K,V> — default is an empty Leaf<K,V>
        let typeArgs = constructorTypeArgSuffix t
        "new Leaf" + typeArgs + "()"
    | _ -> "default!"

let private tryListElemType (t: TypeExpr) : TypeExpr option =
    match t with
    | TyApp(TyName "List", a) -> Some a
    | _ -> None

let rec private containsFlexVar (t: TypeExpr) : bool =
    match t with
    | TyVar v when v.Length > 0 && v.[0] = '$' -> true
    | TyVar _ | TyName _ -> false
    | TyApp(a, b) | TyFn(a, b) -> containsFlexVar a || containsFlexVar b
    | TyTagged(inner, _) -> containsFlexVar inner

let rec private collectFlexVars (t: TypeExpr) : string list =
    match t with
    | TyVar v when v.Length > 0 && v.[0] = '$' -> [v]
    | TyVar _ | TyName _ -> []
    | TyApp(a, b) | TyFn(a, b) ->
        let av = collectFlexVars a
        let bv = collectFlexVars b
        av @ (bv |> List.filter (fun x -> not (List.contains x av)))
    | TyTagged(inner, _) -> collectFlexVars inner

let private renameFlexVars (mapping: System.Collections.Generic.Dictionary<string, string>) (t: TypeExpr) : TypeExpr =
    let rec go t =
        match t with
        | TyVar v when mapping.ContainsKey(v) -> TyName mapping.[v]
        | TyApp(a, b) -> TyApp(go a, go b)
        | TyFn(a, b) -> TyFn(go a, go b)
        | TyTagged(inner, tag) -> TyTagged(go inner, tag)
        | _ -> t
    go t

// Track names emitted as polymorphic zero-arg generic methods (e.g. substEmpty<K,V>())
let private polymorphicZeroArgMethods = System.Collections.Generic.HashSet<string>()

/// Find the first CONCRETE (no flex vars) type for variable `name` in the expression.
/// Used to propagate a concrete monomorphic type back to a let-binding whose
/// RHS was inferred with unresolved flex vars (e.g. `s0 = substEmpty`).
let rec private firstConcreteVarType (name: Ident) (te: TypedExpr) : TypeExpr option =
    match te.Expr with
    | TEVar n when n = name ->
        if containsFlexVar te.Type then None else Some te.Type
    | TELet(n, _, v, cont) ->
        if n = name then None  // shadowed by inner binding
        else
            firstConcreteVarType name v
            |> Option.orElse (cont |> Option.bind (firstConcreteVarType name))
    | TELetPat(_, v, cont) ->
        firstConcreteVarType name v
        |> Option.orElse (cont |> Option.bind (firstConcreteVarType name))
    | TEApp(f, a) ->
        firstConcreteVarType name f |> Option.orElse (firstConcreteVarType name a)
    | TELam(ps, b) ->
        if List.exists (fun (p, _) -> p = name) ps then None
        else firstConcreteVarType name b
    | TEIf(c, t, e) ->
        firstConcreteVarType name c
        |> Option.orElse (firstConcreteVarType name t)
        |> Option.orElse (firstConcreteVarType name e)
    | TEMatch(s, bs) | TEMatchOf(s, bs) ->
        firstConcreteVarType name s
        |> Option.orElse (bs |> List.tryPick (fun (_, b) -> firstConcreteVarType name b))
    | TEList es | TETuple es -> es |> List.tryPick (firstConcreteVarType name)
    | TECons(h, t) ->
        firstConcreteVarType name h |> Option.orElse (firstConcreteVarType name t)
    | TETagged(e, _) -> firstConcreteVarType name e
    | TEPipe(a, b) ->
        firstConcreteVarType name a |> Option.orElse (firstConcreteVarType name b)
    | _ -> None

let private tryStdlibGenericHead (name: string) (args: TypedExpr list) (retTy: TypeExpr) : string option =
    let mk2 a b = "<" + emitTypeBoxed a + "," + emitTypeBoxed b + ">"
    let mk1 a = "<" + emitTypeBoxed a + ">"
    let mk3 a b c = "<" + emitTypeBoxed a + "," + emitTypeBoxed b + "," + emitTypeBoxed c + ">"
    // Extract K from a comparator type: Func<K, Func<K, long>>
    let tryKFromCmp (t: TypeExpr) =
        match t with
        | TyFn(k, _) -> Some k
        | _ -> None
    // Extract (K, V) from RBMap<K, V>
    let tryKVFromRBMap (t: TypeExpr) =
        match collectTyApp t with
        | TyName "RBMap", [k; v] -> Some (k, v)
        | _ -> None
    match name, args with
    | "listMap", a0 :: _ ->
        match a0.Type with
        | TyFn(a, b) -> Some ("listMap" + mk2 a b)
        | _ -> None
    | "listFilter", a0 :: _ ->
        match a0.Type with
        | TyFn(a, _) -> Some ("listFilter" + mk1 a)
        | _ -> None
    | "listFold", a0 :: _ ->
        match a0.Type with
        | TyFn(b, TyFn(a, _)) -> Some ("listFold" + mk2 a b)
        | _ -> None
    | "listLen", a0 :: _
    | "listHead", a0 :: _
    | "listTail", a0 :: _
    | "listReverse", a0 :: _
    | "listIsEmpty", a0 :: _
    | "listContains", a0 :: _
    | "listAppend", a0 :: _
    | "listAt", a0 :: _ ->
        tryListElemType a0.Type |> Option.map (fun a -> name + mk1 a)
    | "listConcat", a0 :: _ ->
        match a0.Type with
        | TyApp(TyName "List", TyApp(TyName "List", a)) -> Some ("listConcat" + mk1 a)
        | _ -> None
    // List functions with first arg = count (long): A must come from second arg (the list)
    | "listTake", [a0] ->
        match retTy with
        | TyFn(TyApp(TyName "List", a), _) | TyApp(TyName "List", a) -> Some ("listTake" + mk1 a)
        | _ -> None
    | "listTake", _ :: a1 :: _ ->
        tryListElemType a1.Type |> Option.map (fun a -> "listTake" + mk1 a)
    | "listDrop", [a0] ->
        match retTy with
        | TyFn(TyApp(TyName "List", a), _) | TyApp(TyName "List", a) -> Some ("listDrop" + mk1 a)
        | _ -> None
    | "listDrop", _ :: a1 :: _ ->
        tryListElemType a1.Type |> Option.map (fun a -> "listDrop" + mk1 a)
    // Functions where A comes from predicate arg type
    | "listAny", a0 :: _
    | "listAll", a0 :: _
    | "listFind", a0 :: _
    | "listPartition", a0 :: _ ->
        match a0.Type with
        | TyFn(a, _) when not (containsFlexVar a) -> Some (name + mk1 a)
        | _ ->
            // Fallback: try the list arg (second arg) element type
            match args with
            | _ :: a1 :: _ -> tryListElemType a1.Type |> Option.map (fun a -> name + mk1 a)
            | _ -> None
    | "listFlatMap", a0 :: _ ->
        // signature: listFlatMap<B, A>(Func<A, List<B>> f)
        match a0.Type with
        | TyFn(a, TyApp(TyName "List", b)) -> Some ("listFlatMap" + mk2 b a)
        | _ -> None
    | "listFindIndex", a0 :: _ ->
        match a0.Type with
        | TyFn(a, _) when not (containsFlexVar a) -> Some ("listFindIndex" + mk1 a)
        | _ ->
            match args with
            | _ :: a1 :: _ -> tryListElemType a1.Type |> Option.map (fun a -> "listFindIndex" + mk1 a)
            | _ -> None
    // listFindIndexFrom<A>(long i): A from second arg (predicate) or third (list)
    | "listFindIndexFrom", _ :: a1 :: _ ->
        match a1.Type with
        | TyFn(a, _) when not (containsFlexVar a) -> Some ("listFindIndexFrom" + mk1 a)
        | _ ->
            match args with
            | _ :: _ :: a2 :: _ -> tryListElemType a2.Type |> Option.map (fun a -> "listFindIndexFrom" + mk1 a)
            | _ -> None
    | "listFindIndexFrom", _ ->
        match retTy with
        | TyFn(TyFn(a, _), _) | TyFn(a, _) -> Some ("listFindIndexFrom" + mk1 a)
        | _ -> None
    // Map functions: C# can't infer all type params from only the first curried argument.
    // Provide explicit type arguments so the compiler doesn't reject partially-applied calls.
    // Only emit explicit params when the types are concrete (no flex vars).
    | "mapInsert", a0 :: _ ->
        match tryKFromCmp a0.Type with
        | Some k when not (containsFlexVar k) ->
            let vOpt =
                match collectTyApp retTy with
                | TyName "RBMap", [_; v] when not (containsFlexVar v) -> Some v
                | _ ->
                    match args with
                    | _ :: _ :: a2 :: _ when not (containsFlexVar a2.Type) -> Some a2.Type
                    | _ -> None
            match vOpt with
            | Some v -> Some ("mapInsert" + mk2 k v)
            | None -> None
        | _ -> None
    | "mapLookup", a0 :: _ ->
        // C# signature is mapLookup<V, K> (V first)
        match tryKFromCmp a0.Type with
        | Some k when not (containsFlexVar k) ->
            let vOpt =
                match retTy with
                | TyApp(TyName "Maybe", v) when not (containsFlexVar v) -> Some v
                | _ -> None
            match vOpt with
            | Some v -> Some ("mapLookup<" + emitTypeBoxed v + "," + emitTypeBoxed k + ">")
            | None -> None
        | _ -> None
    | "mapContains", a0 :: _ ->
        match tryKFromCmp a0.Type with
        | Some k when not (containsFlexVar k) ->
            let vOpt =
                match args with
                    | _ :: _ :: a2 :: _ ->
                        match tryKVFromRBMap a2.Type with
                        | Some (_, v) when not (containsFlexVar v) -> Some v
                        | _ -> None
                    | _ -> None
            match vOpt with
            | Some v -> Some ("mapContains" + mk2 k v)
            | None -> None
        | _ -> None
    | "mapFold", a0 :: _ ->
        // signature: mapFold<B, K, V>(Func<B, Func<K, Func<V, B>>> f)
        // C# order: B, K, V
        match a0.Type with
        | TyFn(b, TyFn(k, TyFn(v, _))) when not (containsFlexVar b || containsFlexVar k || containsFlexVar v) ->
            Some ("mapFold" + mk3 b k v)
        | _ -> None
    | "mapKeys", a0 :: _ ->
        match tryKVFromRBMap a0.Type with
        | Some (k, v) when not (containsFlexVar k || containsFlexVar v) -> Some ("mapKeys" + mk2 k v)
        | _ -> None
    | "mapSize", a0 :: _ ->
        // Only emit explicit type params when they're concrete; otherwise let C# infer from arg
        match tryKVFromRBMap a0.Type with
        | Some (k, v) when not (containsFlexVar k || containsFlexVar v) -> Some ("mapSize" + mk2 k v)
        | _ -> None
    | _ ->
        match name with
        | "listMap"
        | "listFilter"
        | "listFold"
        | "listLen"
        | "listHead"
        | "listTail"
        | "listReverse"
        | "listAppend"
        | "listIsEmpty"
        | "listContains"
        | "listConcat"
        | "listAt" ->
            // Fallback from return type for partially applied calls.
            match name, retTy with
            | "listMap", TyApp(TyName "List", b) ->
                Some ("listMap<object," + emitTypeBoxed b + ">")
            | "listFilter", TyApp(TyName "List", a) ->
                Some ("listFilter" + mk1 a)
            | "listLen", TyName "Int" ->
                Some ("listLen<object>")
            | _ -> None
        | _ -> None

let rec private tryFnArgRet (t: TypeExpr) : (TypeExpr * TypeExpr) option =
    match t with
    | TyFn(a, b) -> Some(a, b)
    | TyTagged(inner, _) -> tryFnArgRet inner
    | _ -> None

let rec private applyTySubst (subst: Map<string, TypeExpr>) (t: TypeExpr) : TypeExpr =
    match t with
    | TyVar v ->
        match Map.tryFind v subst with
        | Some ty -> ty
        | None -> t
    | TyName n when isTypeParamName n ->
        match Map.tryFind n subst with
        | Some ty -> ty
        | None -> t
    | TyApp(a, b) -> TyApp(applyTySubst subst a, applyTySubst subst b)
    | TyFn(a, b) -> TyFn(applyTySubst subst a, applyTySubst subst b)
    | TyTagged(inner, u) -> TyTagged(applyTySubst subst inner, u)
    | _ -> t

let private bindTypeVar (subst: Map<string, TypeExpr>) (name: string) (ty: TypeExpr) : Map<string, TypeExpr> =
    if Map.containsKey name subst then subst else Map.add name ty subst

let rec private inferTySubstFromArg (subst: Map<string, TypeExpr>) (paramTy: TypeExpr) (argTy: TypeExpr) : Map<string, TypeExpr> =
    let p = applyTySubst subst paramTy
    let a = applyTySubst subst argTy
    match p, a with
    | TyVar v, ty -> bindTypeVar subst v ty
    | TyName n, ty when isTypeParamName n -> bindTypeVar subst n ty
    | TyApp(pa, pb), TyApp(aa, ab) ->
        let s1 = inferTySubstFromArg subst pa aa
        inferTySubstFromArg s1 pb ab
    | TyFn(pa, pr), TyFn(aa, ar) ->
        let s1 = inferTySubstFromArg subst pa aa
        inferTySubstFromArg s1 pr ar
    | TyTagged(pi, _), TyTagged(ai, _) ->
        inferTySubstFromArg subst pi ai
    | TyTagged(pi, _), other
    | other, TyTagged(pi, _) ->
        inferTySubstFromArg subst pi other
    | _ -> subst

let private expectedArgTypesForCall (fnTy: TypeExpr) (args: TypedExpr list) : TypeExpr list =
    let rec loop ty remaining subst acc =
        match remaining with
        | [] -> List.rev acc
        | arg :: rest ->
            match tryFnArgRet (applyTySubst subst ty) with
            | Some(paramTy, restTy) ->
                let paramTy' = applyTySubst subst paramTy
                let subst' = inferTySubstFromArg subst paramTy' arg.Type
                loop restTy rest subst' (paramTy' :: acc)
            | None -> List.rev acc
    loop fnTy args Map.empty []

let rec private hasErasedHead (t: TypeExpr) : bool =
    match t with
    | TyVar _ -> true
    | TyName n when isTypeParamName n -> true
    | TyApp(f, _) -> hasErasedHead f
    | TyTagged(inner, _) -> hasErasedHead inner
    | _ -> false

let rec private isErasedType (t: TypeExpr) : bool =
    match t with
    | TyVar _ -> true
    | TyName n when isTypeParamName n -> true
    | TyApp _ -> hasErasedHead t
    | TyTagged(inner, _) -> isErasedType inner
    | _ -> false

let private castArgIfNeeded (expectedTy: TypeExpr) (actualTy: TypeExpr) (isErasingArg: bool) (argExpr: string) : string =
    let expectedCs = emitTypeBoxed expectedTy
    if expectedCs = "object" then argExpr
    elif isErasedType actualTy || isErasingArg then
        "(" + expectedCs + ")(" + argExpr + ")"
    elif containsFlexVar actualTy && not (containsFlexVar expectedTy) then
        // Erasing container (e.g. RBMap<$0,$1>) where concrete type expected —
        // replace with a type-correct default (e.g. new Leaf<K,V>())
        emitDefaultExpr expectedTy
    else argExpr

let private emitCtorCastType (ctor: string) (patTy: TypeExpr) : string =
    let (_, args) = collectTyApp patTy
    let safeCtor = safeTypeIdent ctor
    if List.isEmpty args then
        safeCtor
    else
        safeCtor + "<" + (args |> List.map emitTypeBoxed |> String.concat ", ") + ">"

let private isSimpleMatchReturnType (t: TypeExpr) : bool =
    match t with
    | TyName "Int" -> true
    | _ -> false

// Returns true when 'te' is a call to a known type-erasing prelude function
// (those declared with object/object? in Map.cs) whose C# result is 'object'
// even though the TypedAST has a concrete type.
let rec private isErasingCallExpr (te: TypedExpr) : bool =
    match te.Expr with
    | TEApp(f, _) -> isErasingCallExpr f
    | TEVar name ->
        match name with
        | "maybeWithDefault" | "maybeDefault" | "maybeBind" | "maybeMap"
        | "listHead" | "listTail" | "listAt" -> true
        | _ -> false
    | _ -> false

let rec private tryEmitExpr (te: TypedExpr) : string option =
    match tryAsSymbolicOp te with
    | Some (">>=", left, right) ->
        match tryEmitExpr right, tryEmitExpr left with
        | Some rightStr, Some leftStr ->
            let callHead = wrapLambdaHeadAsDelegate right.Type rightStr
            Some (emitCall callHead leftStr)
        | _ -> None
    | Some (">>", _, right) ->
        tryEmitExpr right
    | Some ("<|>", left, _) ->
        tryEmitExpr left
    | _ ->
    match tryAsStrConcat te with
    | Some (a, b) ->
        match tryEmitExpr a, tryEmitExpr b with
        | Some aStr, Some bStr -> Some ("(" + aStr + " + " + bStr + ")")
        | _ -> None
    | None ->
        match tryAsBinOp te with
        | Some (op, a, b) ->
            match tryEmitExpr a, tryEmitExpr b with
            | Some aStr, Some bStr ->
                // String ordering comparisons (<, >, <=, >=) are not valid C# operators
                // for System.String.  Emit string.Compare(...) instead.
                let isStrTy (t: TypeExpr) =
                    match t with
                    | TyName "Str" -> true
                    | _ -> false
                let isOrderOp o = o = "<" || o = ">" || o = "<=" || o = ">="
                let isEqOp o = o = "==" || o = "!="
                // Check if a type is a C# reference type that can't use == with primitives
                let isObjectLikeTy (t: TypeExpr) =
                    match t with
                    | TyVar _ -> true  // erased flex var → object
                    | _ -> false
                // True when the expression will produce 'object' in C# due to prelude type erasure
                let isErasingOrObjectLike (expr: TypedExpr) =
                    isObjectLikeTy expr.Type || isErasingCallExpr expr
                let isPrimitiveTy (t: TypeExpr) =
                    match t with
                    | TyName "Int" | TyName "Float" | TyName "Bool" | TyName "Char" -> true
                    | _ -> false
                if isStrTy a.Type && isOrderOp op then
                    Some ("(string.Compare(" + aStr + ", " + bStr + ", System.StringComparison.Ordinal) " + op + " 0)")
                elif isEqOp op && isErasingOrObjectLike a && isPrimitiveTy b.Type then
                    // `object == long` etc. — use Equals to avoid CS0019
                    let neg = if op = "!=" then " == false" else ""
                    Some ("(System.Object.Equals(" + aStr + ", " + bStr + ")" + neg + ")")
                elif isEqOp op && isPrimitiveTy a.Type && isErasingOrObjectLike b then
                    let neg = if op = "!=" then " == false" else ""
                    Some ("(System.Object.Equals(" + bStr + ", " + aStr + ")" + neg + ")")
                else
                    Some ("(" + aStr + " " + op + " " + bStr + ")")
            | _ -> None
        | None ->
            match te.Expr with
            | TELit l -> Some (emitLit l)
            | TEVar x ->
                // Polymorphic zero-arg generic methods (e.g. substEmpty<K,V>()) need type args + () at call sites
                if polymorphicZeroArgMethods.Contains(x) && not (containsFlexVar te.Type) then
                    Some (safeIdent x + constructorTypeArgSuffix te.Type + "()")
                else
                    Some (safeIdent x)
            | TECon c ->
                match c with
                | "true"
                | "True" -> Some "true"
                | "false"
                | "False" -> Some "false"
                | _ ->
                    let ctorTypeArgs = constructorTypeArgSuffix te.Type
                    if c.Length > 0 && Char.IsUpper c.[0] then
                        Some ("new " + safeTypeIdent c + ctorTypeArgs + "()")
                    else
                        Some (safeTypeIdent c)
            | TEApp(f, a) ->
                let rec gatherArgs head acc =
                    match head.Expr with
                    | TEApp(g, x) -> gatherArgs g (x :: acc)
                    | _ -> (head, acc)
                let (head, args) = gatherArgs f [a]
                let expectedArgTypes = expectedArgTypesForCall head.Type args
                let argsStrOpt =
                    args
                    |> List.mapi (fun i arg ->
                        match tryEmitExpr arg with
                        | None -> None
                        | Some argStr ->
                            match List.tryItem i expectedArgTypes with
                            | Some expectedTy ->
                                // Fix C: also cast when the arg is an erasing prelude call
                                Some (castArgIfNeeded expectedTy arg.Type (isErasingCallExpr arg) argStr)
                            | None -> Some argStr)
                    |> List.fold (fun acc x ->
                        match acc, x with
                        | Some xs, Some v -> Some (v :: xs)
                        | _ -> None) (Some [])
                    |> Option.map List.rev
                match head.Expr, argsStrOpt with
                | TECon c, Some argsStr ->
                    let ctorTypeArgs = constructorTypeArgSuffix te.Type
                    Some ("new " + safeTypeIdent c + ctorTypeArgs + "(" + String.concat ", " argsStr + ")")
                | TEVar fname, Some argsStr ->
                    match tryStdlibGenericHead fname args te.Type with
                    | Some h ->
                        // Fix A: apply first arg directly to avoid (genericHead<T with spaces>)(arg)
                        // C# cast syntax. After first application the head already has `=>`
                        // from the lambda arg, so subsequent emitCall wraps correctly.
                        match argsStr with
                        | first :: rest -> Some (rest |> List.fold emitCall (h + "(" + first + ")"))
                        | [] -> Some h
                    | None ->
                        let callHead =
                            match tryEmitExpr head with
                            | Some h -> h
                            | None -> safeIdent fname
                        Some (argsStr |> List.fold emitCall callHead)
                | _, Some argsStr ->
                    match tryEmitExpr head with
                    | Some headStr ->
                        let callHead = wrapLambdaHeadAsDelegate head.Type headStr
                        Some (argsStr |> List.fold emitCall callHead)
                    | None -> None
                | _ -> None
            | TELam(ps, body) ->
                match tryEmitExpr body with
                | None -> None
                | Some bodyStr ->
                    match ps with
                    | [] -> Some bodyStr
                    | _ ->
                        let names = ps |> List.map (fst >> safeIdent)
                        let lambda = List.foldBack (fun p acc -> p + " => " + acc) names bodyStr
                        Some lambda
            | TELet(x, _, e, Some body) ->
                // When binding value is a polymorphic zero-arg method with unresolved flex vars
                // (e.g. `e0 = typeEnvEmpty` where typeEnvEmpty : RBMap<$k,$v>), find the first
                // concrete type of `x` in the body to emit e.g. typeEnvEmpty<string,Scheme>().
                let resolvedE =
                    match e.Expr with
                    | TEVar v when polymorphicZeroArgMethods.Contains(v) && containsFlexVar e.Type ->
                        match firstConcreteVarType x body with
                        | Some concreteTy when not (containsFlexVar concreteTy) -> { e with Type = concreteTy }
                        | _ -> e
                    | _ -> e
                match tryEmitExpr resolvedE, tryEmitExpr body with
                | Some eStr, Some bodyStr ->
                    let retType = emitType te.Type
                    if retType = "void" then
                        Some ("((Action)(() => { var " + safeIdent x + " = " + eStr + "; " + bodyStr + "; }))()")
                    else
                        Some ("((Func<" + retType + ">)(() => { var " + safeIdent x + " = " + eStr + "; return " + bodyStr + "; }))()")
                | _ -> None
            | TELet(_, _, e, None) ->
                tryEmitExpr e
            | TELetPat(_, _, _) -> None
            | TEIf(c, t, e) ->
                match tryEmitExpr c, tryEmitExpr t, tryEmitExpr e with
                | Some cStr, Some tStr, Some eStr ->
                    match te.Type with
                    | TyName "Str" ->
                        Some ("(" + cStr + " ? ((" + tStr + ")?.ToString() ?? \"\") : ((" + eStr + ")?.ToString() ?? \"\"))")
                    | _ ->
                        Some ("(" + cStr + " ? " + tStr + " : " + eStr + ")")
                | _ -> None
            | TEMatch(scrut, branches)
            | TEMatchOf(scrut, branches) ->
                emitMatchExprCSharp scrut branches te.Type
            | TEPipe(a, b) ->
                match tryEmitExpr a, tryEmitExpr b with
                | Some aStr, Some bStr ->
                    let pipeHead = wrapLambdaHeadAsDelegate b.Type bStr
                    Some (emitCall pipeHead aStr)
                | _ -> None
            | TETagged(e, _) -> tryEmitExpr e
            | TEList es ->
                let elemType =
                    match te.Type with
                    | TyApp(TyName "List", inner) -> emitType inner
                    | _ -> "object"
                let emittedElems =
                    es
                    |> List.map tryEmitExpr
                    |> List.fold (fun acc x ->
                        match acc, x with
                        | Some xs, Some v -> Some (v :: xs)
                        | _ -> None) (Some [])
                    |> Option.map (List.rev >> String.concat ", ")
                emittedElems
                |> Option.map (fun elems -> "new List<" + elemType + "> { " + elems + " }")
            | TETuple es ->
                let emittedElems =
                    es
                    |> List.map tryEmitExpr
                    |> List.fold (fun acc x ->
                        match acc, x with
                        | Some xs, Some v -> Some (v :: xs)
                        | _ -> None) (Some [])
                    |> Option.map List.rev
                match emittedElems with
                | Some [] -> Some "default"
                | Some [_] -> None
                | Some elems ->
                    // Fix E: ll-lang tuples are reference Tuple<>, not C# value tuples (a, b)
                    match collectTyApp te.Type with
                    | TyName "Tuple", (_ :: _) ->
                        let tpArgs = constructorTypeArgSuffix te.Type
                        Some ("new Tuple" + tpArgs + "(" + String.concat ", " elems + ")")
                    | _ ->
                        Some ("(" + String.concat ", " elems + ")")
                | None -> None
            | TECons(_, _) -> None

and private emitMatchExprCSharp (scrut: TypedExpr) (branches: (TypedPattern * TypedExpr) list) (retTy: TypeExpr) : string option =
    let isNominalCtor (c: string) = c.Length > 0 && Char.IsUpper c.[0]

    let rec isSimplePattern (pat: Pattern) : bool =
        match pat with
        | PWild
        | PLit _ -> true
        | PCon(c, args) when isNominalCtor c ->
            args |> List.forall (function PVar _ -> true | _ -> false)
        | _ -> false

    if not (isSimpleMatchReturnType retTy) then
        None
    elif not (branches |> List.forall (fun (tp, _) -> isSimplePattern tp.Pat)) then
        None
    else
        match tryEmitExpr scrut with
        | None -> None
        | Some scrutExpr ->
            let scrutVar = "__ll_match"
            let retCs = emitTypeBoxed retTy

            let emitCondition (tp: TypedPattern) : string option =
                match tp.Pat with
                | PWild
                    -> None
                | PLit l -> Some (scrutVar + " == " + emitLit l)
                | PCon(c, _) when isNominalCtor c ->
                    Some ("(" + scrutVar + " is " + emitCtorCastType c tp.Type + ")")
                | _ -> None

            let emitBindLines (branchIndex: int) (tp: TypedPattern) : string list =
                match tp.Pat with
                | PWild
                | PLit _ -> []
                | PCon(c, args) when isNominalCtor c ->
                    let ctorAlias = "__ll_case_" + string branchIndex
                    let castExpr = "((" + emitCtorCastType c tp.Type + ")" + scrutVar + ")"
                    let ctorLine = "var " + ctorAlias + " = " + castExpr + ";"
                    let argLines =
                        args
                        |> List.mapi (fun i p ->
                            match p with
                            | PVar v -> Some ("var " + safeIdent v + " = " + ctorAlias + "._" + string i + ";")
                            | _ -> None)
                        |> List.choose id
                    ctorLine :: argLines
                | _ -> []

            let emitBoundBody (branchIndex: int) (tp: TypedPattern) (body: TypedExpr) : string option =
                match tryEmitExpr body with
                | None -> None
                | Some bodyStr ->
                    let bindLines = emitBindLines branchIndex tp
                    if List.isEmpty bindLines then
                        Some bodyStr
                    else
                        Some ("((Func<" + retCs + ">)(() => { " + String.concat " " bindLines + " return " + bodyStr + "; }))()")

            let rec buildChain (idx: int) (remaining: (TypedPattern * TypedExpr) list) : string option =
                match remaining with
                | [] -> Some (emitDefaultExpr retTy)
                | (tp, body) :: rest ->
                    match emitBoundBody idx tp body with
                    | None -> None
                    | Some bodyExpr ->
                        match emitCondition tp with
                        | None -> Some bodyExpr
                        | Some cond ->
                            match buildChain (idx + 1) rest with
                            | None -> None
                            | Some tailExpr ->
                                Some ("(" + cond + " ? " + bodyExpr + " : " + tailExpr + ")")

            match buildChain 0 branches with
            | None -> None
            | Some chainExpr ->
                Some ("((Func<" + retCs + ">)(() => { var " + scrutVar + " = " + scrutExpr + "; return " + chainExpr + "; }))()")

let private emitExprOrDefault (t: TypeExpr) (te: TypedExpr) : string =
    match tryEmitExpr te with
    | Some expr -> expr
    | None -> emitDefaultExpr t

let private isMainFn (sig_: TypedFnSig) =
    sig_.Name = "main" && List.isEmpty sig_.Params

let rec private buildCurriedRetType (paramTypes: TypeExpr list) (retType: TypeExpr) : string =
    match paramTypes with
    | [] -> emitType retType
    | t :: rest -> "Func<" + emitTypeBoxed t + ", " + buildCurriedRetType rest retType + ">"

let private buildCurriedLambda (paramNames: string list) (retExpr: string) =
    match paramNames with
    | [] -> retExpr
    | [p] -> safeIdent p + " => " + retExpr
    | _ ->
        paramNames
        |> List.map safeIdent
        |> fun ps -> List.foldBack (fun p acc -> p + " => " + acc) ps retExpr

let private isUnitType (t: TypeExpr) : bool =
    t = TyName "Unit"

let rec private flattenMainPrelude (te: TypedExpr) : string list * TypedExpr =
    match te.Expr with
    | TELet(name, _, valueExpr, Some body) ->
        // For constructors/vars with erased type params (e.g. `m0 = Leaf`, `s0 = substEmpty`),
        // find the concrete type from the first use of the variable in the body.
        // For TECon, just update the type (constructor suffix picks up the concrete args).
        // For TEVar (erasing globals like substEmpty), also replace the expr itself with a
        // type-correct default (e.g. new Leaf<K,V>()) so the C# variable gets the right type.
        let (effectiveValueExpr, useDefault) =
            match valueExpr.Expr with
            | TECon _ when containsFlexVar valueExpr.Type ->
                match firstConcreteVarType name body with
                | Some t -> ({ valueExpr with Type = t }, false)
                | None -> (valueExpr, false)
            | TEVar _ when containsFlexVar valueExpr.Type ->
                match firstConcreteVarType name body with
                | Some t when not (containsFlexVar t) -> ({ valueExpr with Type = t }, true)
                | _ -> (valueExpr, false)
            | _ -> (valueExpr, false)
        let valueText =
            if useDefault then emitDefaultExpr effectiveValueExpr.Type
            else emitExprOrDefault effectiveValueExpr.Type effectiveValueExpr
        let bindLine =
            if isUnitType valueExpr.Type then
                valueText + ";"
            else
                "var " + safeIdent name + " = " + valueText + ";"
        let rest, tail = flattenMainPrelude body
        (bindLine :: rest, tail)
    | TELetPat(tp, valueExpr, Some body) ->
        let valueText = emitExprOrDefault valueExpr.Type valueExpr
        let rest, tail = flattenMainPrelude body
        match tp.Pat with
        | PWild ->
            (valueText + ";" :: rest, tail)
        | PVar name when not (isUnitType valueExpr.Type) ->
            ("var " + safeIdent name + " = " + valueText + ";" :: rest, tail)
        | PTuple pats ->
            // Fix D: destructure tuple into named vars via a temp
            // pats : Pattern list (AST.PTuple of Pattern list)
            let tmpName = "__ll_tup_" + string (abs (hash valueText) % 99991)
            let tmpBind = "var " + tmpName + " = " + valueText + ";"
            let elemBinds =
                pats |> List.mapi (fun i pat ->
                    match pat with
                    | PVar n -> "var " + safeIdent n + " = " + tmpName + ".Item" + string (i + 1) + ";"
                    | _ -> "")
                |> List.filter (fun s -> s <> "")
            (tmpBind :: elemBinds @ rest, tail)
        | _ ->
            (valueText + ";" :: rest, tail)
    | _ -> ([], te)

/// Emit the body of a void (Unit-returning) function as C# statements.
/// Uses flattenMainPrelude to convert let-binding chains into statement lines,
/// then emits the final expression as a statement.
/// Lines that reduce to bare "null;" or "default!;" are filtered out — these
/// represent expressions the C# codegen can't yet lower; omitting them is no
/// worse than the previous behaviour (empty body).
let private emitVoidBody (indent: string) (body: TypedExpr) : string =
    let preludeLines, finalExpr = flattenMainPrelude body
    let validLines = preludeLines |> List.filter (fun l -> l <> "null;" && l <> "default!;")
    let finalText = emitExprOrDefault finalExpr.Type finalExpr
    let finalLine =
        if finalText = "null" || finalText = "default!" then ""
        elif isUnitType finalExpr.Type then finalText + ";"
        else "var _ = (" + finalText + ");"
    let allLines = validLines @ (if finalLine = "" then [] else [finalLine])
    allLines
    |> List.map (fun line -> indent + line)
    |> String.concat "\n"

let private emitMainBody (body: TypedExpr) : string =
    let preludeLines, finalExpr = flattenMainPrelude body
    let preludeBlock =
        preludeLines
        |> List.map (fun line -> "        " + line)
        |> String.concat "\n"
    let finalExprText = emitExprOrDefault finalExpr.Type finalExpr
    let finalBlock =
        match finalExpr.Type with
        | TyName "Int" ->
            "        var __ll_main_result = " + finalExprText + ";\n        return unchecked((int)(__ll_main_result));"
        | TyName "Float" ->
            "        var __ll_main_result = " + finalExprText + ";\n        return unchecked((int)(__ll_main_result));"
        | TyName "Bool" ->
            "        var __ll_main_result = " + finalExprText + ";\n        return __ll_main_result ? 1 : 0;"
        | _ ->
            "        " + finalExprText + ";\n        return 0;"
    if preludeBlock = "" then
        finalBlock
    else
        preludeBlock + "\n" + finalBlock

let private emitCSharpExternalDecl (sig_: TypedFnSig) : string =
    match tryGetExternalTarget CSharp sig_.Name with
    | None -> ""
    | Some target ->
        let tpStr = emitMethodTypeParams sig_
        let args = sig_.Params |> List.map (fun (n, _) -> safeIdent n)
        let callExpr = target + "(" + String.concat ", " args + ")"
        let ret = emitType sig_.ReturnType
        let isUnit = isUnitType sig_.ReturnType
        match sig_.Params with
        | [] ->
            if isUnit then
                "    public static void " + safeIdent sig_.Name + tpStr + "() { " + callExpr + "; }"
            elif ret = "void" then
                "    public static " + ret + " " + safeIdent sig_.Name + tpStr + "() => " + callExpr + ";"
            else
                "    public static " + ret + " " + safeIdent sig_.Name + tpStr + "() => " + callExpr + ";"
        | [(p, pt)] ->
            if isUnit then
                "    public static void " + safeIdent sig_.Name + tpStr + "(" + emitType pt + " " + safeIdent p + ") { " + callExpr + "; }"
            elif ret = "void" then
                "    public static void " + safeIdent sig_.Name + tpStr + "(" + emitType pt + " " + safeIdent p + ") { " + callExpr + "; }"
            else
                "    public static " + ret + " " + safeIdent sig_.Name + tpStr + "(" + emitType pt + " " + safeIdent p + ") => " + callExpr + ";"
        | (p, pt) :: rest ->
            if isUnit then
                ""
            else
            let restTypes = rest |> List.map snd
            let lambdaRet = buildCurriedRetType restTypes sig_.ReturnType
            let lambda = buildCurriedLambda (rest |> List.map fst) callExpr
            "    public static " + lambdaRet + " " + safeIdent sig_.Name + tpStr + "(" + emitType pt + " " + safeIdent p + ") => " + lambda + ";"

let private emitFnCSharp (sig_: TypedFnSig) (body: TypedExpr) : string =
    if isMainFn sig_ then
        "    public static int Main(string[] args)\n    {\n" + emitMainBody body + "\n    }"
    else
        let tpStr = emitMethodTypeParams sig_
        let isUnit = isUnitType sig_.ReturnType
        // Fix F: when sig_ return type has unresolved flex vars but the body type is concrete,
        // use body.Type — this fixes cases where a function is declared before its return-type
        // dependency is in scope (e.g. ftvEnvList calling strNub before strNub is processed).
        let effectiveRetTy =
            if containsFlexVar sig_.ReturnType && not (containsFlexVar body.Type)
            then body.Type
            else sig_.ReturnType
        match sig_.Params with
        | [] ->
            let ret = emitType effectiveRetTy
            if isUnit || ret = "void" then
                let bodyBlock = emitVoidBody "        " body
                if bodyBlock = "" then
                    "    public static void " + safeIdent sig_.Name + tpStr + "() { }"
                else
                    "    public static void " + safeIdent sig_.Name + tpStr + "()\n    {\n" + bodyBlock + "\n    }"
            elif tpStr = "" && containsFlexVar effectiveRetTy then
                // Polymorphic zero-arg (e.g. substEmpty = Leaf, typeEnvEmpty = Leaf):
                // emit as generic method substEmpty<K,V>() => new Leaf<K,V>() so call sites
                // can supply concrete type args instead of RBMap<object,object>.
                let flexVars = collectFlexVars effectiveRetTy
                let paramLetters = [| "K"; "V"; "W"; "T"; "U"; "A"; "B"; "C" |]
                let paramNames = flexVars |> List.mapi (fun i _ -> paramLetters.[min i (paramLetters.Length - 1)])
                let mapping = System.Collections.Generic.Dictionary<string, string>()
                List.iter2 (fun fv pn -> mapping.[fv] <- pn) flexVars paramNames
                let renamedTy = renameFlexVars mapping effectiveRetTy
                let tpStr2 = "<" + String.concat ", " paramNames + ">"
                let bodyStr =
                    match body.Expr with
                    | TECon c ->
                        "new " + safeTypeIdent c + constructorTypeArgSuffix renamedTy + "()"
                    | _ -> emitExprOrDefault renamedTy body
                polymorphicZeroArgMethods.Add(sig_.Name) |> ignore
                "    public static " + emitType renamedTy + " " + safeIdent sig_.Name + tpStr2 + "() => " + bodyStr + ";"
            elif tpStr = "" then
                // Zero-arg non-void with no type params: emit as static readonly field
                // so it can be used as a value in C# without () (avoids "method group" errors)
                "    public static readonly " + ret + " " + safeIdent sig_.Name + " = " + emitExprOrDefault effectiveRetTy body + ";"
            else
                "    public static " + ret + " " + safeIdent sig_.Name + tpStr + "() => " + emitExprOrDefault effectiveRetTy body + ";"
        | [(p, pt)] ->
            let ret = emitType effectiveRetTy
            if isUnit || ret = "void" then
                let bodyBlock = emitVoidBody "        " body
                if bodyBlock = "" then
                    "    public static void " + safeIdent sig_.Name + tpStr + "(" + emitType pt + " " + safeIdent p + ") { }"
                else
                    "    public static void " + safeIdent sig_.Name + tpStr + "(" + emitType pt + " " + safeIdent p + ")\n    {\n" + bodyBlock + "\n    }"
            else
                "    public static " + ret + " " + safeIdent sig_.Name + tpStr + "(" + emitType pt + " " + safeIdent p + ") => " + emitExprOrDefault effectiveRetTy body + ";"
        | (p, pt) :: rest ->
            let restTypes = rest |> List.map snd
            let ret = buildCurriedRetType restTypes effectiveRetTy
            let lambda = buildCurriedLambda (rest |> List.map fst) (emitExprOrDefault effectiveRetTy body)
            "    public static " + ret + " " +
            safeIdent sig_.Name + tpStr + "(" + emitType pt + " " + safeIdent p + ") => " + lambda + ";"

let private emitSumTypeCSharp (name: TypeIdent) (ps: TypeParam list) (branches: (TypeIdent * TypeExpr list) list) : string =
    let typeParams =
        ps |> List.choose (function TPBare n -> Some (safeTypeIdent n) | TPPhantom _ -> None)
    let tpStr = if List.isEmpty typeParams then "" else "<" + String.concat ", " typeParams + ">"
    let safeName = safeTypeIdent name
    let iface = "    public interface " + safeName + tpStr + " { }"
    let records =
        branches
        |> List.map (fun (con, args) ->
            let safeCon = safeTypeIdent con
            match args with
            | [] ->
                "    public sealed record " + safeCon + tpStr + "() : " + safeName + tpStr + ";"
            | _ ->
                let fields =
                    args
                    |> List.mapi (fun i t -> emitTypeBoxed t + " _" + string i)
                    |> String.concat ", "
                "    public sealed record " + safeCon + tpStr + "(" + fields + ") : " + safeName + tpStr + ";")
    String.concat "\n" (iface :: records)

let private emitDecl (decl: TypedDecl) : string =
    match decl with
    | TDOpaque(name, ps) ->
        let typeParams =
            ps |> List.choose (function TPBare n -> Some (safeTypeIdent n) | TPPhantom _ -> None)
        let tpStr = if List.isEmpty typeParams then "" else "<" + String.concat ", " typeParams + ">"
        "    public sealed class " + safeTypeIdent name + tpStr + " { }"

    | TDType(name, ps, body) ->
        match body with
        | TBSum branches -> emitSumTypeCSharp name ps branches
        | TBRecord fields ->
            let typeParams =
                ps |> List.choose (function TPBare n -> Some (safeTypeIdent n) | TPPhantom _ -> None)
            let tpStr = if List.isEmpty typeParams then "" else "<" + String.concat ", " typeParams + ">"
            let flds =
                fields
                |> List.map (fun (f, t) -> emitTypeBoxed t + " " + safeIdent f)
                |> String.concat ", "
            "    public sealed record " + safeTypeIdent name + tpStr + "(" + flds + ");"
        | TBWrapped t ->
            "    public sealed record " + safeTypeIdent name + "(" + emitTypeBoxed t + " Value);"
    | TDTag _ | TDUnit _ | TDTrait _ -> ""
    | TDExternal(sig_, _) -> emitCSharpExternalDecl sig_
    | TDFn(sig_, _, body) -> emitFnCSharp sig_ body
    | TDLet(x, sch, e) ->
        let t =
            if List.isEmpty (collectTypeParamsFromType sch.Body) then emitType sch.Body
            else "object"
        let value =
            if t = "void" then "default"
            else emitExprOrDefault sch.Body e
        "    public static readonly " + t + " " + safeIdent x + " = " + value + ";"
    | TDLetPat(_, _) -> ""
    | TDImpl(_, typeName, methods) ->
        methods
        |> List.map (fun (sig_, _, body) ->
            let sig2 = { sig_ with Name = safeIdent sig_.Name + "_" + safeIdent typeName }
            emitFnCSharp sig2 body)
        |> String.concat "\n"

let private className (path: string list) =
    match path with
    | [] -> "LlLangGenerated"
    | _ ->
        let parts =
            path
            |> List.filter (fun p -> not (String.IsNullOrWhiteSpace p))
            |> List.map (fun p ->
                if p.Length = 0 then "X"
                else string (Char.ToUpper p.[0]) + p.[1..])
        if List.isEmpty parts then "LlLangGenerated"
        else String.concat "_" parts

let private prelude = ""

let private preludeMembers = """    // --- ll-lang stdlib (C#) ---
    public static long abs(long x) => Math.Abs(x);
    public static double absf(double x) => Math.Abs(x);
    public static double sqrt(double x) => Math.Sqrt(x);
    public static Func<long, long> min(long a) => b => Math.Min(a, b);
    public static Func<long, long> max(long a) => b => Math.Max(a, b);
    public static double intToFloat(long n) => (double)n;
    public static long floatToInt(double f) => (long)f;

    public static object? printfn(string s) { Console.WriteLine(s); return null; }
    public static object? print(string s) { Console.Write(s); return null; }

    public static string readFile(string path) => File.ReadAllText(path);
    public static Func<string, object?> writeFile(string path) => contents => { File.WriteAllText(path, contents); return null; };
    public static bool fileExists(string path) => File.Exists(path);
    public static object? exit(long n) { Environment.Exit(unchecked((int)n)); return null; }
    public static readonly List<string> getArgs = new(Environment.GetCommandLineArgs());

    public static long strLen(string s) => s.Length;
    public static Func<object, string> strConcat(string a) => b => a + (b?.ToString() ?? "");
    public static string strTrim(string s) => s.Trim();
    public static Func<object, bool> strContains(string needle) => hay => (hay?.ToString() ?? "").Contains(needle);
    public static Func<string, List<string>> strSplit(string sep) =>
        s => new List<string>(s.Split(sep, StringSplitOptions.None));
    public static Func<long, Func<long, string>> strSlice(string s) =>
        start => len => {
            var i = (int)start;
            var n = (int)len;
            if (i < 0 || n <= 0 || i >= s.Length) return "";
            var max = s.Length - i;
            if (n > max) n = max;
            return s.Substring(i, n);
        };
    public static Func<string, long> strIndexOf(string needle) =>
        hay => hay.IndexOf(needle, StringComparison.Ordinal);
    public static string strReverse(string s) { var a = s.ToCharArray(); Array.Reverse(a); return new string(a); }
    public static string strFromChars(IEnumerable<char> cs) => new string(new List<char>(cs).ToArray());
    public static List<char> strChars(string s) => new(s.ToCharArray());
    public static string intToStr(long n) => n.ToString();
    public static string floatToStr(double f) => f.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static object? strToInt(string s) => long.TryParse(s, out var n) ? n : null;
    public static object? strToFloat(string s) => double.TryParse(s, out var n) ? n : null;

    public static long charToInt(char c) => c;
    public static char intToChar(long n) => (char)n;
    public static bool charIsDigit(char c) => char.IsDigit(c);
    public static bool charIsAlpha(char c) => char.IsLetter(c);
    public static bool charIsSpace(char c) => char.IsWhiteSpace(c);

    public static long listLen<T>(List<T> xs) => xs.Count;
    public static Func<List<A>, List<B>> listMap<A, B>(Func<A, B> f) =>
        xs => { var r = new List<B>(xs.Count); foreach (var x in xs) r.Add(f(x)); return r; };
    public static Func<List<A>, List<A>> listFilter<A>(Func<A, bool> p) =>
        xs => { var r = new List<A>(); foreach (var x in xs) if (p(x)) r.Add(x); return r; };
    public static Func<B, Func<List<A>, B>> listFold<A, B>(Func<B, Func<A, B>> f) =>
        z => xs => { var acc = z; foreach (var x in xs) acc = f(acc)(x); return acc; };
    public static T? listHead<T>(List<T> xs) =>
        xs.Count > 0 ? xs[0] : default;
    public static List<T>? listTail<T>(List<T> xs) =>
        xs.Count > 0 ? new List<T>(xs.GetRange(1, xs.Count - 1)) : null;
    public static List<T> listReverse<T>(List<T> xs) {
        var r = new List<T>(xs);
        r.Reverse();
        return r;
    }
    public static Func<List<T>, List<T>> listAppend<T>(List<T> xs) =>
        ys => { var r = new List<T>(xs.Count + ys.Count); r.AddRange(xs); r.AddRange(ys); return r; };
    public static bool listIsEmpty<T>(List<T> xs) => xs.Count == 0;
    public static Func<T, bool> listContains<T>(List<T> xs) => x => xs.Contains(x);
    public static Func<long, List<long>> listRange(long lo) => hi => {
        var r = new List<long>();
        for (long i = lo; i < hi; i++) r.Add(i);
        return r;
    };
    public static List<T> listConcat<T>(List<List<T>> xss) {
        var r = new List<T>();
        foreach (var xs in xss)
            r.AddRange(xs);
        return r;
    }
    public static Func<long, T?> listAt<T>(List<T> xs) => i =>
        i >= 0 && i < xs.Count ? xs[(int)i] : default;

    public static Func<object, object?> maybeMap(Func<object, object> f) =>
        m => m is null ? null : f(m);
    public static Func<Func<object, object?>, object?> maybeBind(object? m) =>
        f => m is null ? null : f(m);
    public static Func<object?, object> maybeWithDefault(object d) =>
        m => m ?? d;
    public static Func<object?, object> maybeDefault(object d) =>
        maybeWithDefault(d);
    public static bool maybeIsNone(object? m) => m is null;
    // --- end prelude ---
"""

let private csharpStdlibNames : Set<string> =
    Set.ofList [
        "abs"; "absf"; "sqrt"; "min"; "max"; "intToFloat"; "floatToInt"
        "printfn"; "print"; "readFile"; "writeFile"; "fileExists"; "exit"; "getArgs"
        "strLen"; "strConcat"; "strTrim"; "strContains"; "strSplit"; "strSlice"; "strIndexOf"
        "strReverse"; "strFromChars"; "strChars"; "intToStr"; "floatToStr"; "strToInt"; "strToFloat"
        "charToInt"; "intToChar"; "charIsDigit"; "charIsAlpha"; "charIsSpace"
        "listLen"; "listMap"; "listFilter"; "listFold"; "listHead"; "listTail"; "listReverse"
        "listAppend"; "listIsEmpty"; "listContains"; "listRange"; "listConcat"; "listAt"
        "maybeMap"; "maybeBind"; "maybeWithDefault"; "maybeDefault"; "maybeIsNone"
    ]

let private exprUsesStdlib (te: TypedExpr) : bool =
    let rec walk (e: TypedExpr) =
        match e.Expr with
        | TEVar name when Set.contains name csharpStdlibNames -> true
        | TEApp(a, b)
        | TEPipe(a, b)
        | TECons(a, b) -> walk a || walk b
        | TELam(_, body)
        | TETagged(body, _) -> walk body
        | TELet(_, _, e1, e2)
        | TELetPat(_, e1, e2) -> walk e1 || (e2 |> Option.exists walk)
        | TEIf(c, t, e2) -> walk c || walk t || walk e2
        | TEMatch(s, branches)
        | TEMatchOf(s, branches) ->
            walk s || (branches |> List.exists (fun (_, b) -> walk b))
        | TEList es
        | TETuple es -> es |> List.exists walk
        | _ -> false
    walk te

let private moduleNeedsPrelude (tm: TypedModule) : bool =
    tm.Decls
    |> List.exists (fun (decl, _) ->
        match decl with
        | TDFn(_, _, body) -> exprUsesStdlib body
        | TDLet(_, _, e) -> exprUsesStdlib e
        | TDImpl(_, _, methods) ->
            methods |> List.exists (fun (_, _, body) -> exprUsesStdlib body)
        | _ -> false)

let private moduleNeedsJsonExternal (tm: TypedModule) : bool =
    tm.Decls
    |> List.exists (fun (decl, _) ->
        match decl with
        | TDExternal(sig_, _) ->
            match tryGetExternalTarget CSharp sig_.Name with
            | Some "System.Text.Json.JsonSerializer.Deserialize<object>" -> true
            | _ -> false
        | _ -> false)

let private emitModule (includePreludeMembers: bool) (tm: TypedModule) : string =
    let cls = className tm.Path
    let moduleDecls =
        tm.Decls
        |> List.map fst
        |> List.map emitDecl
        |> List.filter (fun s -> not (String.IsNullOrWhiteSpace s))
    let body =
        let allDecls =
            if includePreludeMembers then preludeMembers :: moduleDecls
            else moduleDecls
        allDecls |> String.concat "\n\n"
    "public static class " + cls + "\n" +
    "{\n" +
    body + "\n" +
    "}\n"

let private emitSingleModule (tm: TypedModule) : string =
    let includePreludeMembers = moduleNeedsPrelude tm
    let includeJsonUsing = moduleNeedsJsonExternal tm
    "// Generated by lllc (ll-lang C# backend)\n" +
    "// Expression-capable backend.\n" +
    "#nullable enable\n" +
    "using System;\n" +
    "using System.IO;\n" +
    (if includeJsonUsing then "using System.Text.Json;\n" else "") +
    "using System.Collections.Generic;\n\n" +
    prelude + "\n" +
    emitModule includePreludeMembers tm

let private moduleSuffix (tm: TypedModule) =
    let raw = String.concat "_" tm.Path
    if String.IsNullOrWhiteSpace raw then "Main" else safeIdent raw

let private rewriteNonEntryMain (suffix: string) (tm: TypedModule) : TypedModule =
    let renamedDecls =
        tm.Decls
        |> List.map (fun (decl, exported) ->
            match decl with
            | TDFn(sig_, sch, body) when isMainFn sig_ ->
                let sig2 = { sig_ with Name = "__ll_main_" + suffix }
                (TDFn(sig2, sch, body), exported)
            | _ -> (decl, exported))
    { tm with Decls = renamedDecls }

let emit (tm: TypedModule) : string =
    emitSingleModule tm

let emitProjectModules (tms: TypedModule list) : string =
    match tms with
    | [] -> ""
    | [tm] -> emitSingleModule tm
        | _ ->
        let lastIdx = List.length tms - 1
        let rewritten =
            tms
            |> List.mapi (fun i tm ->
                if i = lastIdx then tm
                else rewriteNonEntryMain (moduleSuffix tm) tm)
        let declKey (decl: TypedDecl) : string option =
            match decl with
            | TDType(name, _, _) -> Some ("type:" + safeTypeIdent name)
            | TDOpaque(name, _) -> Some ("opaque:" + safeTypeIdent name)
            | TDFn(sig_, _, _) -> Some ("fn:" + safeIdent sig_.Name)
            | TDExternal(sig_, _) -> Some ("ext:" + safeIdent sig_.Name)
            | TDLet(name, _, _) -> Some ("let:" + safeIdent name)
            | _ -> None
        let dedupedDecls =
            rewritten
            |> List.collect (fun tm -> tm.Decls |> List.map fst)
            |> List.fold (fun (seen, acc) decl ->
                match declKey decl with
                | None -> (seen, decl :: acc)
                | Some key when Set.contains key seen -> (seen, acc)
                | Some key -> (Set.add key seen, decl :: acc)
            ) (Set.empty, [])
            |> snd
            |> List.rev
        let includePreludeMembers =
            rewritten |> List.exists moduleNeedsPrelude
        let includeJsonUsing =
            rewritten |> List.exists moduleNeedsJsonExternal
        let projectDecls =
            dedupedDecls
            |> List.map emitDecl
            |> List.filter (fun s -> not (String.IsNullOrWhiteSpace s))
            |> fun ds ->
                if includePreludeMembers then
                    String.concat "\n\n" (preludeMembers :: ds)
                else
                    String.concat "\n\n" ds
        let projectClass =
            "public static class LlLangProject\n{\n"
            + projectDecls
            + "\n}\n"
        "// Generated by lllc (ll-lang C# backend)\n" +
        "// Expression-capable backend.\n" +
        "#nullable enable\n" +
        "using System;\n" +
        "using System.IO;\n" +
        (if includeJsonUsing then "using System.Text.Json;\n" else "") +
        "using System.Collections.Generic;\n\n" +
        prelude + "\n" +
        projectClass
