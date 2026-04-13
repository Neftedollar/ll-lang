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
    | _ -> "default!"

let private tryListElemType (t: TypeExpr) : TypeExpr option =
    match t with
    | TyApp(TyName "List", a) -> Some a
    | _ -> None

let private tryStdlibGenericHead (name: string) (args: TypedExpr list) (retTy: TypeExpr) : string option =
    let mk2 a b = "<" + emitTypeBoxed a + "," + emitTypeBoxed b + ">"
    let mk1 a = "<" + emitTypeBoxed a + ">"
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

let private castArgIfNeeded (expectedTy: TypeExpr) (actualTy: TypeExpr) (argExpr: string) : string =
    let expectedCs = emitTypeBoxed expectedTy
    if expectedCs = "object" then argExpr
    elif isErasedType actualTy then
        "(" + expectedCs + ")(" + argExpr + ")"
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

let rec private tryEmitExpr (te: TypedExpr) : string option =
    match tryAsStrConcat te with
    | Some (a, b) ->
        match tryEmitExpr a, tryEmitExpr b with
        | Some aStr, Some bStr -> Some ("(" + aStr + " + " + bStr + ")")
        | _ -> None
    | None ->
        match tryAsBinOp te with
        | Some (op, a, b) ->
            match tryEmitExpr a, tryEmitExpr b with
            | Some aStr, Some bStr -> Some ("(" + aStr + " " + op + " " + bStr + ")")
            | _ -> None
        | None ->
            match te.Expr with
            | TELit l -> Some (emitLit l)
            | TEVar x -> Some (safeIdent x)
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
                            | Some expectedTy -> Some (castArgIfNeeded expectedTy arg.Type argStr)
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
                    let callHead =
                        match tryStdlibGenericHead fname args te.Type with
                        | Some h -> h
                        | None ->
                            match tryEmitExpr head with
                            | Some h -> h
                            | None -> safeIdent fname
                    Some (argsStr |> List.fold emitCall callHead)
                | _, Some argsStr ->
                    match tryEmitExpr head with
                    | Some headStr ->
                        Some (argsStr |> List.fold emitCall headStr)
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
                match tryEmitExpr e, tryEmitExpr body with
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
                | Some aStr, Some bStr -> Some ("((" + bStr + ")(" + aStr + "))")
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
                | Some elems -> Some ("(" + String.concat ", " elems + ")")
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

let private emitMainExpr (body: TypedExpr) : string =
    match tryEmitExpr body with
    | None -> "0"
    | Some expr ->
        match body.Type with
        | TyName "Int" -> "unchecked((int)(" + expr + "))"
        | TyName "Float" -> "unchecked((int)(" + expr + "))"
        | TyName "Bool" -> "(" + expr + " ? 1 : 0)"
        | _ -> "0"

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
        "    public static int Main(string[] args) => " + emitMainExpr body + ";"
    else
        let tpStr = emitMethodTypeParams sig_
        match sig_.Params with
        | [] ->
            let ret = emitType sig_.ReturnType
            if ret = "void" then
                "    public static void " + safeIdent sig_.Name + tpStr + "() { }"
            else
                "    public static " + ret + " " + safeIdent sig_.Name + tpStr + "() => " + emitExprOrDefault sig_.ReturnType body + ";"
        | [(p, pt)] ->
            let ret = emitType sig_.ReturnType
            if ret = "void" then
                "    public static void " + safeIdent sig_.Name + tpStr + "(" + emitType pt + " " + safeIdent p + ") { }"
            else
                "    public static " + ret + " " + safeIdent sig_.Name + tpStr + "(" + emitType pt + " " + safeIdent p + ") => " + emitExprOrDefault sig_.ReturnType body + ";"
        | (p, pt) :: rest ->
            let restTypes = rest |> List.map snd
            let ret = buildCurriedRetType restTypes sig_.ReturnType
            let lambda = buildCurriedLambda (rest |> List.map fst) (emitExprOrDefault sig_.ReturnType body)
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
