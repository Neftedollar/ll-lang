module LLLang.CodegenJava

open System
open System.Text.RegularExpressions
open LLLang.AST
open LLLang.Types
open LLLang.TypedAST
open LLLang.Platform

// ── Java reserved words ───────────────────────────────────────────────────────

let private javaKeywords =
    Set.ofList [
        "abstract"; "assert"; "boolean"; "break"; "byte"; "case"; "catch"
        "char"; "class"; "const"; "continue"; "default"; "do"; "double"
        "else"; "enum"; "extends"; "final"; "finally"; "float"; "for"
        "goto"; "if"; "implements"; "import"; "instanceof"; "int"; "interface"
        "long"; "native"; "new"; "null"; "package"; "private"; "protected"
        "public"; "return"; "short"; "static"; "strictfp"; "super"; "switch"
        "synchronized"; "this"; "throw"; "throws"; "transient"; "true"; "false"
        "try"; "void"; "volatile"; "while"; "record"; "sealed"; "permits"
        "var"; "yield" ]

let private safeIdent (s: string) =
    if Set.contains s javaKeywords then s + "_" else s

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
    if Set.contains withHead javaKeywords then withHead + "_" else withHead

let private javaStdlibCallAliases : Map<string, string> =
    Map.ofList [
        "abs", "abs_"
        "min", "min_"
        "max", "max_"
        "print", "print_"
        "exit", "exit_"
    ]

let private mapJavaCallName (name: string) : string =
    match Map.tryFind name javaStdlibCallAliases with
    | Some mapped -> mapped
    | None -> name

let mutable private currentKnownJavaFunctions : Set<string> = Set.empty
let mutable private currentJavaCtorOwners : Map<string, string> = Map.empty
let mutable private currentJavaDeclaredTypes : Set<string> = Set.empty

let private qualifyJavaCtor (ctorName: string) : string =
    match Map.tryFind ctorName currentJavaCtorOwners with
    | Some owner -> owner + "." + ctorName
    | None -> ctorName

// ── Type emission ─────────────────────────────────────────────────────────────

let private isTypeParamName (n: string) =
    n.Length = 1 && Char.IsUpper n.[0]

let rec private collectTyApp (t: TypeExpr) : TypeExpr * TypeExpr list =
    match t with
    | TyApp(f, a) ->
        let (head, args) = collectTyApp f
        (head, args @ [a])
    | _ -> (t, [])

/// Emit a type as a Java reference type (boxed for generics).
let rec private emitTypeBoxed (t: TypeExpr) : string =
    match t with
    | TyName "Int"   -> "Long"
    | TyName "Float" -> "Double"
    | TyName "Str"   -> "String"
    | TyName "Bool"  -> "Boolean"
    | TyName "Char"  -> "Character"
    | TyName "Unit"  -> "Void"
    | TyName x when isTypeParamName x -> x
    | TyName x       -> safeTypeIdent x
    | TyVar v        -> if isTypeParamName v then v else "Object"
    | TyApp _ ->
        let (head, args) = collectTyApp t
        match head, args with
        | TyName "List", [a] ->
            "List<" + emitTypeBoxed a + ">"
        | TyName "Maybe", [a] ->
            if Set.contains (safeTypeIdent "Maybe") currentJavaDeclaredTypes then
                safeTypeIdent "Maybe" + "<" + emitTypeBoxed a + ">"
            else
                "Optional<" + emitTypeBoxed a + ">"
        | TyName name, _ when not (List.isEmpty args) ->
            let argsStr = args |> List.map emitTypeBoxed |> String.concat ", "
            safeTypeIdent name + "<" + argsStr + ">"
        | _ ->
            "Object"
    | TyFn(a, b)     -> "Function<" + emitTypeBoxed a + ", " + emitTypeBoxed b + ">"
    | TyTagged(t, _) -> emitTypeBoxed t

/// Emit a type as a Java primitive (or reference) for parameter/return positions.
let rec private emitType (t: TypeExpr) : string =
    match t with
    | TyName "Int"   -> "long"
    | TyName "Float" -> "double"
    | TyName "Str"   -> "String"
    | TyName "Bool"  -> "boolean"
    | TyName "Char"  -> "char"
    | TyName "Unit"  -> "void"
    | TyName x when isTypeParamName x -> x
    | TyName x       -> safeTypeIdent x
    | TyVar v        -> if isTypeParamName v then v else "Object"
    | TyApp _ ->
        let (head, args) = collectTyApp t
        match head, args with
        | TyName "List", [a] ->
            "List<" + emitTypeBoxed a + ">"
        | TyName "Maybe", [a] ->
            if Set.contains (safeTypeIdent "Maybe") currentJavaDeclaredTypes then
                safeTypeIdent "Maybe" + "<" + emitTypeBoxed a + ">"
            else
                "Optional<" + emitTypeBoxed a + ">"
        | TyName name, _ when not (List.isEmpty args) ->
            let argsStr = args |> List.map emitTypeBoxed |> String.concat ", "
            safeTypeIdent name + "<" + argsStr + ">"
        | _ ->
            "Object"
    | TyFn(a, b)     -> "Function<" + emitTypeBoxed a + ", " + emitTypeBoxed b + ">"
    | TyTagged(t, _) -> emitType t

// ── Literal emission ──────────────────────────────────────────────────────────

let private emitLit (l: Literal) : string =
    match l with
    | LInt n   -> string n + "L"
    | LFloat f ->
        let s = sprintf "%g" f
        if s.Contains('.') || s.Contains('e') || s.Contains('E') then s else s + ".0"
    | LStr s   ->
        let escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")
        "\"" + escaped + "\""
    | LBool b  -> if b then "true" else "false"
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

// ── Binary operators ──────────────────────────────────────────────────────────

let private binaryOp (op: string) : string option =
    match op with
    | "+"  -> Some "+" | "-"  -> Some "-" | "*"  -> Some "*" | "/"  -> Some "/"
    | "==" -> Some "==" | "!=" -> Some "!=" | "<" -> Some "<" | ">" -> Some ">"
    | "<=" -> Some "<=" | ">=" -> Some ">=" | _ -> None

let private tryAsBinOp (te: TypedExpr) : (string * TypedExpr * TypedExpr) option =
    match te.Expr with
    | TEApp(outer, right) ->
        match outer.Expr with
        | TEApp(inner, left) ->
            match inner.Expr with
            | TEVar op ->
                match binaryOp op with
                | Some jop -> Some (jop, left, right)
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

// ── String concat ─────────────────────────────────────────────────────────────

let private tryAsStrConcat (te: TypedExpr) : (TypedExpr * TypedExpr) option =
    match te.Expr with
    | TEApp(outer, right) ->
        match outer.Expr with
        | TEApp({ Expr = TEVar "++" }, left) -> Some (left, right)
        | _ -> None
    | _ -> None

// ── Pattern matching for Java ─────────────────────────────────────────────────

// Build a Java if/else chain (as an expression using ternary)
let rec private emitMatchChain (scrutStr: string) (branches: (TypedPattern * TypedExpr) list) : string =
    let emitBranchCond (scrutVar: string) (pat: Pattern) : string option =
        match pat with
        | PWild | PVar _ -> None  // always matches
        | PLit l -> Some (scrutVar + " == " + emitLit l)
        | PCon("[]", []) -> Some (scrutVar + ".isEmpty()")
        | PCons _ -> Some ("!" + scrutVar + ".isEmpty()")
        | PCon(c, []) ->
            // zero-arg constructor: check instanceof with inner class
            Some (scrutVar + " instanceof " + qualifyJavaCtor c)
        | PCon(c, _) ->
            // n-arg constructor: check instanceof with inner class
            Some (scrutVar + " instanceof " + qualifyJavaCtor c)
        | _ -> None

    let emitBranchBinds (scrutVar: string) (pat: Pattern) : (string * string) list =
        match pat with
        | PVar x -> [(safeIdent x, scrutVar)]
        | PCon(c, args) ->
            // Cast to the concrete type to access record components
            let castVar = "_c" + c
            let castBind = (castVar, "((" + qualifyJavaCtor c + ") " + scrutVar + ")")
            let fieldBinds =
                args |> List.mapi (fun i arg ->
                    match arg with
                    | PVar v -> Some (safeIdent v, castVar + "._" + string i + "()")
                    | _ -> None)
                |> List.choose id
            castBind :: fieldBinds
        | PTuple ps ->
            ps
            |> List.mapi (fun i p ->
                match p with
                | PVar v -> Some (safeIdent v, scrutVar + "[" + string i + "]")
                | _ -> None)
            |> List.choose id
        | _ -> []

    let rec buildChain = function
        | [] -> "null /* unreachable */"
        | [(tp, body)] ->
            let bodyStr = emitExprJava body
            let binds = emitBranchBinds scrutStr tp.Pat
            applyBinds binds bodyStr
        | (tp, body) :: rest ->
            let bodyStr = emitExprJava body
            let restStr = buildChain rest
            match emitBranchCond scrutStr tp.Pat with
            | None ->
                let binds = emitBranchBinds scrutStr tp.Pat
                applyBinds binds bodyStr
            | Some cond ->
                let binds = emitBranchBinds scrutStr tp.Pat
                let thenStr = applyBinds binds bodyStr
                "(" + cond + " ? " + thenStr + " : " + restStr + ")"

    buildChain branches

and private applyBinds (binds: (string * string) list) (expr: string) : string =
    // For Java: we can't easily do let-in inside a ternary with complex binds
    // Use a simple inlining approach - only inline if binds are just aliases
    match binds with
    | [] -> expr
    | _ ->
        // Replace only whole identifier occurrences to avoid corrupting
        // constructor/type names that contain the same substring.
        // Apply from specific value vars to cast-alias vars.
        // This ensures replacements like `a -> _cSome._0()` happen
        // before `_cSome -> ((Maybe.Some) fa)`.
        (List.rev binds) |> List.fold (fun acc (var, value) ->
            let pattern = @"\b" + Regex.Escape(var) + @"\b"
            Regex.Replace(acc, pattern, value)) expr

// ── Expression emission ───────────────────────────────────────────────────────

and private emitExprJava (te: TypedExpr) : string =
    let rec stripTaggedType (t: TypeExpr) : TypeExpr =
        match t with
        | TyTagged(inner, _) -> stripTaggedType inner
        | _ -> t

    let tryFnType (t: TypeExpr) : (TypeExpr * TypeExpr) option =
        match stripTaggedType t with
        | TyFn(argTy, retTy) -> Some(argTy, retTy)
        | _ -> None

    let emitApplyStep (fnExpr: string) (fnType: TypeExpr option) (argExpr: TypedExpr) : string * TypeExpr option =
        let argStr = emitExprJava argExpr
        match fnType |> Option.bind tryFnType with
        | Some(argTy, retTy) ->
            let fnCast = "((Function<" + emitTypeBoxed argTy + ", " + emitTypeBoxed retTy + ">) (" + fnExpr + "))"
            let argValue =
                match stripTaggedType argTy with
                | TyName "Int"
                | TyName "Float"
                | TyName "Bool"
                | TyName "Char"
                | TyName "Unit" -> argStr
                | _ -> "((" + emitTypeBoxed argTy + ") (" + argStr + "))"
            (fnCast + ".apply(" + argValue + ")", Some retTy)
        | None ->
            ("((Function) (" + fnExpr + ")).apply(" + argStr + ")", None)

    let emitKnownCall (fnName: string) (args: TypedExpr list) (resultType: TypeExpr option) =
        match args with
        | [] -> fnName + "()"
        | first :: rest ->
            let firstCall = fnName + "(" + emitExprJava first + ")"
            let applied =
                rest
                |> List.fold (fun accExpr arg -> "((Function) (" + accExpr + ")).apply(" + emitExprJava arg + ")") firstCall
            match resultType with
            | Some t when not (List.isEmpty rest) ->
                let castTy = emitTypeBoxed t
                if castTy = "Void" then applied
                else "((" + castTy + ") (" + applied + "))"
            | _ -> applied

    let emitApplyChain (headExpr: TypedExpr) (args: TypedExpr list) =
        let startExpr = emitExprJava headExpr
        args
        |> List.fold (fun (accExpr, accTy) arg -> emitApplyStep accExpr accTy arg) (startExpr, Some headExpr.Type)
        |> fst

    match tryAsSymbolicOp te with
    | Some (">>=", left, right) ->
        emitApplyStep (emitExprJava right) (Some right.Type) left |> fst
    | Some (">>", _, right) ->
        "(" + emitExprJava right + ")"
    | Some ("<|>", left, _) ->
        "(" + emitExprJava left + ")"
    | _ ->
    // String concat
    match tryAsStrConcat te with
    | Some (a, b) -> "(" + emitExprJava a + " + " + emitExprJava b + ")"
    | None ->
    // Binary ops
    match tryAsBinOp te with
    | Some (op, a, b) -> "(" + emitExprJava a + " " + op + " " + emitExprJava b + ")"
    | None ->
    match te.Expr with
    | TELit l  -> emitLit l
    | TEVar x  ->
        let sx = safeIdent x
        match te.Type with
        | TyFn(_, _) when Set.contains sx currentKnownJavaFunctions ->
            // Java needs an explicit callable value when passing a known static method.
            "__ll_arg -> " + sx + "(__ll_arg)"
        | _ ->
            sx
    | TECon c  ->
        match c with
        | "true" -> "true"
        | "false" -> "false"
        | _ ->
            let qualified = qualifyJavaCtor c
            if String.Equals(qualified, c, StringComparison.Ordinal) then
                safeIdent c
            else
                "new " + safeIdent qualified + "()"

    | TEApp(f, a) ->
        let rec gatherArgs head acc =
            match head.Expr with
            | TEApp(g, x) -> gatherArgs g (x :: acc)
            | _ -> (head, acc)
        let (head, args) = gatherArgs f [a]
        match head.Expr with
        | TECon c ->
            // Constructor application: new OuterClass.InnerClass(args)
            let argsStr = args |> List.map emitExprJava |> String.concat ", "
            "new " + safeIdent (qualifyJavaCtor c) + "(" + argsStr + ")"
        | TEVar fname ->
            let baseName = safeIdent fname
            let mappedName = safeIdent (mapJavaCallName fname)
            let callName =
                if Set.contains baseName currentKnownJavaFunctions then baseName
                elif Set.contains mappedName currentKnownJavaFunctions then mappedName
                else baseName
            if Set.contains callName currentKnownJavaFunctions then
                emitKnownCall callName args (Some te.Type)
            else
                emitApplyChain head args
        | _ ->
            emitApplyChain head args

    | TELam(ps, body) ->
        match ps with
        | [(name, _)] ->
            safeIdent name + " -> " + emitExprJava body
        | _ ->
            // Multi-param: nested lambdas
            let rec nestLambdas = function
                | [] -> emitExprJava body
                | (name, _) :: rest ->
                    safeIdent name + " -> " + nestLambdas rest
            nestLambdas ps

    | TELet(x, _, e, Some body) ->
        // Use a trick: since Java ternary can't declare locals, we inline
        // This is a simplification; for complex cases the body may reference x
        // We substitute x with the expression value in body
        let eStr = emitExprJava e
        let bodyStr = emitExprJava body
        // Simple approach: substitute x occurrences in body string
        // (works for simple cases; x is a fresh name from the compiler)
        let subst = bodyStr.Replace(safeIdent x, "(" + eStr + ")")
        subst

    | TELet(_, _, e, None) ->
        emitExprJava e

    | TELetPat(tp, e, Some body) ->
        emitExprJava body  // simplified

    | TELetPat(_, e, None) ->
        emitExprJava e

    | TEIf(c, t, e) ->
        "(" + emitExprJava c + " ? " + emitExprJava t + " : " + emitExprJava e + ")"

    | TETagged(e, _) -> emitExprJava e

    | TEList es ->
        "List.of(" + (es |> List.map emitExprJava |> String.concat ", ") + ")"

    | TETuple es ->
        // Java doesn't have tuples natively; use Object array as a fallback
        "new Object[]{" + (es |> List.map emitExprJava |> String.concat ", ") + "}"

    | TEPipe(a, b) ->
        match b.Expr with
        | TEVar fname ->
            let baseName = safeIdent fname
            let mappedName = safeIdent (mapJavaCallName fname)
            let callName =
                if Set.contains baseName currentKnownJavaFunctions then baseName
                elif Set.contains mappedName currentKnownJavaFunctions then mappedName
                else baseName
            if Set.contains callName currentKnownJavaFunctions then
                emitKnownCall callName [a] (Some te.Type)
            else
                emitApplyStep (emitExprJava b) (Some b.Type) a |> fst
        | _ ->
            emitApplyStep (emitExprJava b) (Some b.Type) a |> fst

    | TEMatch(scrut, branches) | TEMatchOf(scrut, branches) ->
        let scrutStr = emitExprJava scrut
        emitMatchChain scrutStr branches

    | TECons(h, t) ->
        // Prepend h to list t: Stream.concat
        "Stream.concat(Stream.of(" + emitExprJava h + "), " + emitExprJava t + ".stream()).collect(Collectors.toList())"

// ── Sum type emission ─────────────────────────────────────────────────────────

let private emitSumTypeJava (name: TypeIdent) (ps: TypeParam list) (branches: (TypeIdent * TypeExpr list) list) : string =
    let typeParams =
        ps |> List.choose (function TPBare n -> Some n | TPPhantom _ -> None)
    let tpStr = if List.isEmpty typeParams then "" else "<" + String.concat ", " typeParams + ">"
    let permitsStr = branches |> List.map (fun (con, _) -> name + "." + con) |> String.concat ", "
    let sealedDecl = "    sealed interface " + name + tpStr + " permits " + permitsStr + " {"
    let records =
        branches |> List.map (fun (con, args) ->
            match args with
            | [] ->
                "        record " + con + tpStr + "() implements " + name + tpStr + " {}"
            | _ ->
                let fields =
                    args |> List.mapi (fun i t -> emitTypeBoxed t + " _" + string i)
                    |> String.concat ", "
                "        record " + con + tpStr + "(" + fields + ") implements " + name + tpStr + " {}")
    String.concat "\n" ([sealedDecl] @ records @ ["    }"])

// ── Function emission ─────────────────────────────────────────────────────────

let private isMainFn (sig_: TypedFnSig) =
    sig_.Name = "main" && List.isEmpty sig_.Params

let rec private isUnitType (t: TypeExpr) : bool =
    match t with
    | TyName "Unit" -> true
    | TyTagged(inner, _) -> isUnitType inner
    | _ -> false

let rec private collectFnTypeParams (t: TypeExpr) : Set<string> =
    match t with
    | TyName n when isTypeParamName n -> Set.singleton n
    | TyVar v when isTypeParamName v -> Set.singleton v
    | TyApp(a, b) -> Set.union (collectFnTypeParams a) (collectFnTypeParams b)
    | TyFn(a, b) -> Set.union (collectFnTypeParams a) (collectFnTypeParams b)
    | TyTagged(inner, _) -> collectFnTypeParams inner
    | _ -> Set.empty

let private methodTypeParamsPrefix (sig_: TypedFnSig) : string =
    let fromParams =
        sig_.Params
        |> List.map snd
        |> List.map collectFnTypeParams
        |> List.fold Set.union Set.empty
    let all = Set.union fromParams (collectFnTypeParams sig_.ReturnType)
    if Set.isEmpty all then
        ""
    else
        "<" + (all |> Set.toList |> String.concat ", ") + "> "

/// Build the curried return type for multiple param groups: A -> (B -> RetType)
let rec private buildCurriedRetType (paramTypes: TypeExpr list) (retType: TypeExpr) : string =
    match paramTypes with
    | [] -> emitTypeBoxed retType
    | [t] ->
        "Function<" + emitTypeBoxed t + ", " + emitTypeBoxed retType + ">"
    | t :: rest ->
        "Function<" + emitTypeBoxed t + ", " + buildCurriedRetType rest retType + ">"

let private emitFnJava (sig_: TypedFnSig) (body: TypedExpr) : string =
    if isMainFn sig_ then
        let bodyExpr = emitExprJava body
        if isUnitType sig_.ReturnType then
            "    public static void main(String[] args) {\n        " + bodyExpr + ";\n    }"
        else
            "    public static void main(String[] args) {\n        var _ll_unused = " + bodyExpr + ";\n    }"
    else
        let methodTp = methodTypeParamsPrefix sig_
        match sig_.Params with
        | [] ->
            let retType = emitType sig_.ReturnType
            "    public static " + methodTp + retType + " " + safeIdent sig_.Name + "() {\n        return " + emitExprJava body + ";\n    }"
        | [(p, pt)] ->
            let retType = emitType sig_.ReturnType
            "    public static " + methodTp + retType + " " + safeIdent sig_.Name + "(" + emitType pt + " " + safeIdent p + ") {\n        return " + emitExprJava body + ";\n    }"
        | (p, pt) :: rest ->
            // Curried: fn takes first param, returns Function<...>
            let restTypes = rest |> List.map snd
            let innerRetType = buildCurriedRetType restTypes sig_.ReturnType
            // Build nested lambda for the remaining params
            let rec buildLambda = function
                | [] -> emitExprJava body
                | [(rp, rpt)] -> safeIdent rp + " -> " + emitExprJava body
                | (rp, _) :: rest2 -> safeIdent rp + " -> " + buildLambda rest2
            let lambdaBody = buildLambda rest
            "    public static " + methodTp + innerRetType + " " +
            safeIdent sig_.Name + "(" + emitType pt + " " + safeIdent p + ") {\n        return " + lambdaBody + ";\n    }"

let rec private emitJavaCurriedLambda (sigParams: (string * TypeExpr) list) (expr: string) : string =
    match sigParams with
    | [] -> expr
    | [(name, _)] ->
        safeIdent name + " -> " + expr
    | (name, _) :: rest ->
        safeIdent name + " -> " + emitJavaCurriedLambda rest expr

let private emitExternalDecl (sig_: TypedFnSig) : string =
    match tryGetExternalTarget Java sig_.Name with
    | None -> ""
    | Some target ->
        let methodTp = methodTypeParamsPrefix sig_
        let argNames = sig_.Params |> List.map (fun (n, _) -> safeIdent n)
        let callExpr = target + "(" + String.concat ", " argNames + ")"
        let isUnit = isUnitType sig_.ReturnType
        match sig_.Params with
        | [] ->
            let retType = emitType sig_.ReturnType
            if isUnit then
                "    public static " + methodTp + retType + " " + safeIdent sig_.Name + "() {\n        " + callExpr + ";\n    }"
            else
                "    public static " + methodTp + retType + " " + safeIdent sig_.Name + "() {\n        return " + callExpr + ";\n    }"
        | [(p, pt)] ->
            let retType = emitType sig_.ReturnType
            if isUnit then
                "    public static " + methodTp + retType + " " + safeIdent sig_.Name + "(" + emitType pt + " " + safeIdent p + ") {\n        " + callExpr + ";\n    }"
            else
                "    public static " + methodTp + retType + " " + safeIdent sig_.Name + "(" + emitType pt + " " + safeIdent p + ") {\n        return " + callExpr + ";\n    }"
        | (p, pt) :: rest ->
            let restTypes = rest |> List.map snd
            let retType = buildCurriedRetType restTypes sig_.ReturnType
            let lambdaBody = emitJavaCurriedLambda (rest |> List.map (fun (n, t) -> (n, t))) callExpr
            "    public static " + methodTp + retType + " " + safeIdent sig_.Name + "(" + emitType pt + " " + safeIdent p + ") {\n        return " + lambdaBody + ";\n    }"

// ── Declaration emission ──────────────────────────────────────────────────────

let private emitDecl (decl: TypedDecl) : string =
    match decl with
    | TDOpaque(name, ps) ->
        let typeParams =
            ps |> List.choose (function TPBare n -> Some n | TPPhantom _ -> None)
        let tpStr = if List.isEmpty typeParams then "" else "<" + String.concat ", " typeParams + ">"
        "    static final class " + safeTypeIdent name + tpStr + " {}"

    | TDType(name, ps, body) ->
        match body with
        | TBSum branches -> emitSumTypeJava name ps branches
        | TBRecord fields ->
            let typeParams =
                ps |> List.choose (function TPBare n -> Some n | TPPhantom _ -> None)
            let tpStr = if List.isEmpty typeParams then "" else "<" + String.concat ", " typeParams + ">"
            let flds = fields |> List.map (fun (f, t) -> emitTypeBoxed t + " " + safeIdent f) |> String.concat ", "
            "    record " + name + tpStr + "(" + flds + ") {}"
        | TBWrapped t ->
            "    record " + name + "(" + emitTypeBoxed t + " value) {}"

    | TDTag _ | TDUnit _ | TDTrait _ -> ""
    | TDExternal(sig_, _) -> emitExternalDecl sig_

    | TDFn(sig_, _, body) -> emitFnJava sig_ body

    | TDLet(x, sch, e) ->
        let t = emitType sch.Body
        "    public static final " + t + " " + safeIdent x + " = " + emitExprJava e + ";"

    | TDLetPat(_, e) ->
        "    // let pattern binding: " + emitExprJava e + ";"

    | TDImpl(_, typeName, methods) ->
        methods
        |> List.map (fun (sig_, sch, body) ->
            let sig2 = { sig_ with Name = safeIdent sig_.Name + "_" + safeIdent typeName }
            emitFnJava sig2 body)
        |> String.concat "\n\n"

// ── Java stdlib prelude ───────────────────────────────────────────────────────

let private javaPrelude = """    // --- ll-lang stdlib (Java) ---
    private static long abs_(long x) { return Math.abs(x); }
    private static double absf(double x) { return Math.abs(x); }
    private static double sqrt(double x) { return Math.sqrt(x); }
    private static Function<Long, Long> min_(long a) { return b -> Math.min(a, b); }
    private static Function<Long, Long> max_(long a) { return b -> Math.max(a, b); }
    private static double intToFloat(long n) { return (double) n; }
    private static long floatToInt(double f) { return (long) f; }
    private static void printfn(String s) { System.out.println(s); }
    private static void print_(String s) { System.out.print(s); }
    private static String readFile(String path) {
        try { return java.nio.file.Files.readString(java.nio.file.Path.of(path)); }
        catch (Exception e) { throw new RuntimeException(e); }
    }
    private static Function<String, Void> writeFile(String path) {
        return contents -> {
            try { java.nio.file.Files.writeString(java.nio.file.Path.of(path), contents); }
            catch (Exception e) { throw new RuntimeException(e); }
            return null;
        };
    }
    private static void exit_(long n) { System.exit((int) n); }
    private static List<String> getArgs(String[] _args) { return List.of(_args); }
    private static long strLen(String s) { return s.length(); }
    private static Function<String, String> strConcat(String a) { return b -> a + b; }
    private static String strTrim(String s) { return s.strip(); }
    private static Function<String, Boolean> strContains(String needle) { return hay -> hay.contains(needle); }
    private static Function<String, List<String>> strSplit(String sep) { return s -> List.of(s.split(Pattern.quote(sep))); }
    private static Function<Long, Function<Long, String>> strSlice(String s) { return start -> len -> s.substring((int)(long)start, (int)(long)(start + len)); }
    private static Function<String, Long> strIndexOf(String needle) { return hay -> (long) hay.indexOf(needle); }
    private static String strReverse(String s) { return new StringBuilder(s).reverse().toString(); }
    private static String strFromChars(List<Character> cs) { StringBuilder sb = new StringBuilder(); for (char c : cs) sb.append(c); return sb.toString(); }
    private static List<Character> strChars(String s) { List<Character> cs = new ArrayList<>(); for (char c : s.toCharArray()) cs.add(c); return cs; }
    private static String intToStr(long n) { return Long.toString(n); }
    private static String floatToStr(double f) { return Double.toString(f); }
    private static Optional<Long> strToInt(String s) { try { return Optional.of(Long.parseLong(s)); } catch (NumberFormatException e) { return Optional.empty(); } }
    private static Optional<Double> strToFloat(String s) { try { double n = Double.parseDouble(s); return Double.isFinite(n) ? Optional.of(n) : Optional.empty(); } catch (NumberFormatException e) { return Optional.empty(); } }
    private static <A> long listLen(List<A> xs) { return xs.size(); }
    private static <A, B> Function<List<A>, List<B>> listMap(Function<A, B> f) { return xs -> xs.stream().map(f::apply).collect(Collectors.toList()); }
    private static <A> Function<List<A>, List<A>> listFilter(Function<A, Boolean> p) { return xs -> xs.stream().filter(x -> p.apply(x)).collect(Collectors.toList()); }
    @SuppressWarnings("unchecked")
    private static <A, B> Function<B, Function<List<A>, B>> listFold(Function<B, Function<A, B>> f) { return z -> xs -> { B acc = z; for (A x : xs) acc = f.apply(acc).apply(x); return acc; }; }
    private static <A> Optional<A> listHead(List<A> xs) { return xs.isEmpty() ? Optional.empty() : Optional.of(xs.get(0)); }
    private static <A> Optional<List<A>> listTail(List<A> xs) { return xs.isEmpty() ? Optional.empty() : Optional.of(xs.subList(1, xs.size())); }
    private static <A> List<A> listReverse(List<A> xs) { List<A> r = new ArrayList<>(xs); Collections.reverse(r); return r; }
    private static <A> Function<List<A>, List<A>> listAppend(List<A> xs) { return ys -> { List<A> r = new ArrayList<>(xs); r.addAll(ys); return r; }; }
    private static <A> boolean listIsEmpty(List<A> xs) { return xs.isEmpty(); }
    private static <A> Function<A, Boolean> listContains(List<A> xs) { return x -> xs.contains(x); }
    private static Function<Long, List<Long>> listRange(long lo) { return hi -> { List<Long> r = new ArrayList<>(); for (long i = lo; i < hi; i++) r.add(i); return r; }; }
    private static <A> List<A> listConcat(List<List<A>> xss) { List<A> r = new ArrayList<>(); for (List<A> xs : xss) r.addAll(xs); return r; }
    private static <A> Function<Long, Optional<A>> listAt(List<A> xs) { return i -> { int idx = (int)(long)i; return (idx >= 0 && idx < xs.size()) ? Optional.of(xs.get(idx)) : Optional.empty(); }; }
    private static long charToInt(char c) { return (long) c; }
    private static char intToChar(long n) { return (char)(int) n; }
    private static boolean charIsDigit(char c) { return Character.isDigit(c); }
    private static boolean charIsAlpha(char c) { return Character.isLetter(c); }
    private static boolean charIsSpace(char c) { return Character.isWhitespace(c); }
    private static <A, B> Function<Optional<A>, Optional<B>> maybeMap(Function<A, B> f) { return m -> m.map(f::apply); }
    private static <A, B> Function<Function<A, Optional<B>>, Optional<B>> maybeBind(Optional<A> m) { return f -> m.flatMap(f::apply); }
    private static <A> Function<Optional<A>, A> maybeWithDefault(A d) { return m -> m.orElse(d); }
    private static <A> Function<Optional<A>, A> maybeDefault(A d) { return maybeWithDefault(d); }
    private static <A> boolean maybeIsNone(Optional<A> m) { return !m.isPresent(); }
    // --- end prelude ---"""

let private javaStdlibNames : Set<string> =
    Set.ofList [
        "abs"; "abs_"; "absf"; "sqrt"; "min"; "min_"; "max"; "max_"; "intToFloat"; "floatToInt"
        "printfn"; "print"; "print_"; "readFile"; "writeFile"; "exit"; "exit_"; "getArgs"
        "strLen"; "strConcat"; "strTrim"; "strContains"; "strSplit"; "strSlice"; "strIndexOf"
        "strReverse"; "strFromChars"; "strChars"; "intToStr"; "floatToStr"; "strToInt"; "strToFloat"
        "charToInt"; "intToChar"; "charIsDigit"; "charIsAlpha"; "charIsSpace"
        "listLen"; "listMap"; "listFilter"; "listFold"; "listHead"; "listTail"; "listReverse"
        "listAppend"; "listIsEmpty"; "listContains"; "listRange"; "listConcat"; "listAt"
        "maybeMap"; "maybeBind"; "maybeWithDefault"; "maybeDefault"; "maybeIsNone"
    ]

let private javaStdlibEmitNames : Set<string> =
    javaStdlibNames
    |> Set.map mapJavaCallName
    |> Set.map safeIdent

let private exprUsesStdlib (te: TypedExpr) : bool =
    let rec walk (e: TypedExpr) =
        match e.Expr with
        | TEVar name when Set.contains name javaStdlibNames -> true
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

let private collectModuleFunctionNames (tm: TypedModule) : Set<string> =
    let topLevelFns =
        tm.Decls
        |> List.choose (fun (decl, _) ->
            match decl with
            | TDFn(sig_, _, _) -> Some (safeIdent sig_.Name)
            | TDExternal(sig_, _) -> Some (safeIdent sig_.Name)
            | _ -> None)
        |> Set.ofList
    let implFns =
        tm.Decls
        |> List.collect (fun (decl, _) ->
            match decl with
            | TDImpl(_, typeName, methods) ->
                methods
                |> List.map (fun (sig_, _, _) -> safeIdent sig_.Name + "_" + safeIdent typeName)
            | _ -> [])
        |> Set.ofList
    Set.union topLevelFns implFns

let private collectCtorOwners (tm: TypedModule) : Map<string, string> =
    tm.Decls
    |> List.collect (fun (decl, _) ->
        match decl with
        | TDType(typeName, _, TBSum branches) ->
            branches |> List.map (fun (ctorName, _) -> ctorName, typeName)
        | _ -> [])
    |> Map.ofList

let private collectDeclaredTypeNames (tm: TypedModule) : Set<string> =
    tm.Decls
    |> List.choose (fun (decl, _) ->
        match decl with
        | TDType(name, _, _)
        | TDOpaque(name, _) -> Some (safeTypeIdent name)
        | _ -> None)
    |> Set.ofList

// ── Module emission ───────────────────────────────────────────────────────────

/// Derive a Java class name from the module path.
let private javaClassName (path: string list) : string =
    match path with
    | [] -> "LlLang"
    | _ ->
        let last = List.last path
        // Capitalize first letter
        if last.Length = 0 then "LlLang"
        else string (Char.ToUpper last.[0]) + last.[1..]

let private emitModule (includePrelude: bool) (tm: TypedModule) : string =
    let knownModuleFns = collectModuleFunctionNames tm
    currentKnownJavaFunctions <-
        if includePrelude then Set.union knownModuleFns javaStdlibEmitNames
        else knownModuleFns
    currentJavaCtorOwners <- collectCtorOwners tm
    currentJavaDeclaredTypes <- collectDeclaredTypeNames tm

    let className = javaClassName tm.Path

    let isTypeDecl (d: TypedDecl) = match d with TDType _ | TDOpaque _ | TDTag _ | TDUnit _ -> true | _ -> false
    let typeDecls  = tm.Decls |> List.filter (fun (d, _) -> isTypeDecl d)
    let otherDecls = tm.Decls |> List.filter (fun (d, _) -> not (isTypeDecl d))

    let typeStr =
        typeDecls
        |> List.map (fun (d, _) -> emitDecl d)
        |> List.filter (fun s -> s <> "")
        |> String.concat "\n\n"

    let otherStr =
        otherDecls
        |> List.map (fun (d, _) -> emitDecl d)
        |> List.filter (fun s -> s <> "")
        |> String.concat "\n\n"

    let hasMain =
        tm.Decls |> List.exists (fun (d, _) ->
            match d with TDFn(sig_, _, _) -> isMainFn sig_ | _ -> false)

    let header =
        "// Generated by lllc (ll-lang Java backend)\n" +
        "// Requires Java 21+\n" +
        "\n" +
        "import java.util.ArrayList;\n" +
        "import java.util.Collections;\n" +
        "import java.util.List;\n" +
        "import java.util.Optional;\n" +
        "import java.util.function.Function;\n" +
        "import java.util.regex.Pattern;\n" +
        "import java.util.stream.Collectors;\n" +
        "import java.util.stream.Stream;\n"

    let innerParts =
        [ (if includePrelude then javaPrelude else "")
          (if typeStr  <> "" then typeStr else "")
          (if otherStr <> "" then otherStr else "") ]
        |> List.filter (fun s -> s <> "")
        |> String.concat "\n\n"

    header +
    "\npublic class " + className + " {\n" +
    innerParts + "\n" +
    (if hasMain then "" else "") +
    "}\n"

/// Emit a fully-inferred module as Java source.
let emit (tm: TypedModule) : string =
    emitModule (moduleNeedsPrelude tm) tm

/// Emit multiple modules as a single Java source string.
let emitProjectModules (tms: TypedModule list) : string =
    tms
    |> List.map (fun tm -> emitModule (moduleNeedsPrelude tm) tm)
    |> String.concat "\n\n"
