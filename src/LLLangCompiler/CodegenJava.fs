module LLLang.CodegenJava

open System
open LLLang.AST
open LLLang.Types
open LLLang.TypedAST

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

// ── Type emission ─────────────────────────────────────────────────────────────

let private isTypeParamName (n: string) =
    n.Length = 1 && Char.IsUpper n.[0]

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
    | TyName x       -> x
    | TyVar v        -> if isTypeParamName v then v else "Object"
    | TyApp(TyName "List", a)  -> "java.util.List<" + emitTypeBoxed a + ">"
    | TyApp(TyName "Maybe", a) -> "java.util.Optional<" + emitTypeBoxed a + ">"
    | TyApp(f, a)    -> emitTypeBoxed f + "<" + emitTypeBoxed a + ">"
    | TyFn(a, b)     -> "java.util.function.Function<" + emitTypeBoxed a + ", " + emitTypeBoxed b + ">"
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
    | TyName x       -> x
    | TyVar v        -> if isTypeParamName v then v else "Object"
    | TyApp(TyName "List", a)  -> "java.util.List<" + emitTypeBoxed a + ">"
    | TyApp(TyName "Maybe", a) -> "java.util.Optional<" + emitTypeBoxed a + ">"
    | TyApp(f, a)    -> emitTypeBoxed f + "<" + emitTypeBoxed a + ">"
    | TyFn(a, b)     -> "java.util.function.Function<" + emitTypeBoxed a + ", " + emitTypeBoxed b + ">"
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
            Some (scrutVar + " instanceof " + c)
        | PCon(c, _) ->
            // n-arg constructor: check instanceof with inner class
            Some (scrutVar + " instanceof " + c)
        | _ -> None

    let emitBranchBinds (scrutVar: string) (pat: Pattern) : (string * string) list =
        match pat with
        | PVar x -> [(safeIdent x, scrutVar)]
        | PCon(c, args) ->
            // Cast to the concrete type to access record components
            let castVar = "_c" + c
            let castBind = (castVar, "((" + c + ") " + scrutVar + ")")
            let fieldBinds =
                args |> List.mapi (fun i arg ->
                    match arg with
                    | PVar v -> Some (safeIdent v, castVar + "._" + string i + "()")
                    | _ -> None)
                |> List.choose id
            castBind :: fieldBinds
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
        // Use lambda-invoke pattern for binds
        // This is only needed for PCon patterns with field destructuring
        // For simple PVar patterns, inline the scrutinee reference
        binds |> List.fold (fun acc (var, value) ->
            // Replace var with value in acc - simplest approach
            // Since we control generated var names, this is safe
            acc.Replace(var, value)) expr

// ── Expression emission ───────────────────────────────────────────────────────

and private emitExprJava (te: TypedExpr) : string =
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
    | TEVar x  -> safeIdent x
    | TECon c  ->
        match c with
        | "true" -> "true"
        | "false" -> "false"
        | _ -> safeIdent c

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
            "new " + safeIdent c + "(" + argsStr + ")"
        | TEVar fname ->
            // Curried application: chain .apply() calls
            let rec buildApply (f: TypedExpr) (args: TypedExpr list) =
                match args with
                | [] -> emitExprJava f
                | [x] -> emitExprJava f + ".apply(" + emitExprJava x + ")"
                | x :: rest ->
                    buildApply { f with Expr = TEApp(f, x) } rest
            buildApply f [a]
        | _ ->
            let rec buildApply (f: TypedExpr) (args: TypedExpr list) =
                match args with
                | [] -> emitExprJava f
                | [x] -> emitExprJava f + ".apply(" + emitExprJava x + ")"
                | x :: rest ->
                    buildApply { f with Expr = TEApp(f, x) } rest
            buildApply f [a]

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
        "java.util.List.of(" + (es |> List.map emitExprJava |> String.concat ", ") + ")"

    | TETuple es ->
        // Java doesn't have tuples natively; use Object array as a fallback
        "new Object[]{" + (es |> List.map emitExprJava |> String.concat ", ") + "}"

    | TEPipe(a, b) ->
        emitExprJava b + ".apply(" + emitExprJava a + ")"

    | TEMatch(scrut, branches) | TEMatchOf(scrut, branches) ->
        let scrutStr = emitExprJava scrut
        emitMatchChain scrutStr branches

    | TECons(h, t) ->
        // Prepend h to list t: Stream.concat
        "java.util.stream.Stream.concat(java.util.stream.Stream.of(" + emitExprJava h + "), " + emitExprJava t + ".stream()).collect(java.util.stream.Collectors.toList())"

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

/// Build the curried return type for multiple param groups: A -> (B -> RetType)
let rec private buildCurriedRetType (paramTypes: TypeExpr list) (retType: TypeExpr) : string =
    match paramTypes with
    | [] -> emitType retType
    | [_] -> emitType retType
    | t :: rest ->
        "java.util.function.Function<" + emitTypeBoxed t + ", " + buildCurriedRetType rest retType + ">"

let private emitFnJava (sig_: TypedFnSig) (body: TypedExpr) : string =
    if isMainFn sig_ then
        "    public static void main(String[] args) {\n        " + emitExprJava body + ";\n    }"
    else
        match sig_.Params with
        | [] ->
            let retType = emitType sig_.ReturnType
            "    public static " + retType + " " + safeIdent sig_.Name + "() {\n        return " + emitExprJava body + ";\n    }"
        | [(p, pt)] ->
            let retType = emitType sig_.ReturnType
            "    public static " + retType + " " + safeIdent sig_.Name + "(" + emitType pt + " " + safeIdent p + ") {\n        return " + emitExprJava body + ";\n    }"
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
            "    public static java.util.function.Function<" + emitTypeBoxed pt + ", " + innerRetType + "> " +
            safeIdent sig_.Name + "(" + emitType pt + " " + safeIdent p + ") {\n        return " + lambdaBody + ";\n    }"

// ── Declaration emission ──────────────────────────────────────────────────────

let private emitDecl (decl: TypedDecl) : string =
    match decl with
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

    | TDFn(sig_, _, body) -> emitFnJava sig_ body

    | TDLet(x, sch, e) ->
        let t = emitType sch.Body
        "    public static final " + t + " " + safeIdent x + " = " + emitExprJava e + ";"

    | TDLetPat(_, e) ->
        "    // let pattern binding: " + emitExprJava e + ";"

    | TDImpl(_, typeName, methods) ->
        methods |> List.map (fun (sig_, _, body) ->
            match sig_.Params with
            | [] ->
                "    public static " + emitType sig_.ReturnType + " " +
                safeIdent typeName + "_" + safeIdent sig_.Name + "() {\n        return " + emitExprJava body + ";\n    }"
            | [(p, pt)] ->
                "    public static " + emitType sig_.ReturnType + " " +
                safeIdent typeName + "_" + safeIdent sig_.Name + "(" + emitType pt + " " + safeIdent p + ") {\n        return " + emitExprJava body + ";\n    }"
            | ps ->
                let paramList = ps |> List.map (fun (n, t) -> emitType t + " " + safeIdent n) |> String.concat ", "
                "    public static " + emitType sig_.ReturnType + " " +
                safeIdent typeName + "_" + safeIdent sig_.Name + "(" + paramList + ") {\n        return " + emitExprJava body + ";\n    }"
        ) |> String.concat "\n\n"

// ── Java stdlib prelude ───────────────────────────────────────────────────────

let private javaPrelude = """    // --- ll-lang stdlib (Java) ---
    private static long abs_(long x) { return Math.abs(x); }
    private static double absf(double x) { return Math.abs(x); }
    private static double sqrt(double x) { return Math.sqrt(x); }
    private static java.util.function.Function<Long, Long> min_(long a) { return b -> Math.min(a, b); }
    private static java.util.function.Function<Long, Long> max_(long a) { return b -> Math.max(a, b); }
    private static double intToFloat(long n) { return (double) n; }
    private static long floatToInt(double f) { return (long) f; }
    private static void printfn(String s) { System.out.println(s); }
    private static void print_(String s) { System.out.print(s); }
    private static String readFile(String path) {
        try { return java.nio.file.Files.readString(java.nio.file.Path.of(path)); }
        catch (Exception e) { throw new RuntimeException(e); }
    }
    private static java.util.function.Function<String, Void> writeFile(String path) {
        return contents -> {
            try { java.nio.file.Files.writeString(java.nio.file.Path.of(path), contents); }
            catch (Exception e) { throw new RuntimeException(e); }
            return null;
        };
    }
    private static void exit_(long n) { System.exit((int) n); }
    private static java.util.List<String> getArgs(String[] _args) { return java.util.List.of(_args); }
    private static long strLen(String s) { return s.length(); }
    private static java.util.function.Function<String, String> strConcat(String a) { return b -> a + b; }
    private static String strTrim(String s) { return s.strip(); }
    private static java.util.function.Function<String, Boolean> strContains(String needle) { return hay -> hay.contains(needle); }
    private static java.util.function.Function<String, java.util.List<String>> strSplit(String sep) { return s -> java.util.List.of(s.split(java.util.regex.Pattern.quote(sep))); }
    private static java.util.function.Function<Long, java.util.function.Function<Long, String>> strSlice(String s) { return start -> len -> s.substring((int)(long)start, (int)(long)(start + len)); }
    private static java.util.function.Function<String, Long> strIndexOf(String needle) { return hay -> (long) hay.indexOf(needle); }
    private static String strReverse(String s) { return new StringBuilder(s).reverse().toString(); }
    private static String strFromChars(java.util.List<Character> cs) { StringBuilder sb = new StringBuilder(); for (char c : cs) sb.append(c); return sb.toString(); }
    private static java.util.List<Character> strChars(String s) { java.util.List<Character> cs = new java.util.ArrayList<>(); for (char c : s.toCharArray()) cs.add(c); return cs; }
    private static String intToStr(long n) { return Long.toString(n); }
    private static java.util.Optional<Long> strToInt(String s) { try { return java.util.Optional.of(Long.parseLong(s)); } catch (NumberFormatException e) { return java.util.Optional.empty(); } }
    private static <A> long listLen(java.util.List<A> xs) { return xs.size(); }
    private static <A, B> java.util.function.Function<java.util.List<A>, java.util.List<B>> listMap(java.util.function.Function<A, B> f) { return xs -> xs.stream().map(f::apply).collect(java.util.stream.Collectors.toList()); }
    private static <A> java.util.function.Function<java.util.List<A>, java.util.List<A>> listFilter(java.util.function.Function<A, Boolean> p) { return xs -> xs.stream().filter(x -> p.apply(x)).collect(java.util.stream.Collectors.toList()); }
    @SuppressWarnings("unchecked")
    private static <A, B> java.util.function.Function<B, java.util.function.Function<java.util.List<A>, B>> listFold(java.util.function.Function<B, java.util.function.Function<A, B>> f) { return z -> xs -> { B acc = z; for (A x : xs) acc = f.apply(acc).apply(x); return acc; }; }
    private static <A> java.util.Optional<A> listHead(java.util.List<A> xs) { return xs.isEmpty() ? java.util.Optional.empty() : java.util.Optional.of(xs.get(0)); }
    private static <A> java.util.Optional<java.util.List<A>> listTail(java.util.List<A> xs) { return xs.isEmpty() ? java.util.Optional.empty() : java.util.Optional.of(xs.subList(1, xs.size())); }
    private static <A> java.util.List<A> listReverse(java.util.List<A> xs) { java.util.List<A> r = new java.util.ArrayList<>(xs); java.util.Collections.reverse(r); return r; }
    private static <A> java.util.function.Function<java.util.List<A>, java.util.List<A>> listAppend(java.util.List<A> xs) { return ys -> { java.util.List<A> r = new java.util.ArrayList<>(xs); r.addAll(ys); return r; }; }
    private static <A> boolean listIsEmpty(java.util.List<A> xs) { return xs.isEmpty(); }
    private static <A> java.util.function.Function<A, Boolean> listContains(java.util.List<A> xs) { return x -> xs.contains(x); }
    private static java.util.function.Function<Long, java.util.List<Long>> listRange(long lo) { return hi -> { java.util.List<Long> r = new java.util.ArrayList<>(); for (long i = lo; i < hi; i++) r.add(i); return r; }; }
    private static <A> java.util.List<A> listConcat(java.util.List<java.util.List<A>> xss) { java.util.List<A> r = new java.util.ArrayList<>(); for (java.util.List<A> xs : xss) r.addAll(xs); return r; }
    private static <A> java.util.function.Function<Long, java.util.Optional<A>> listAt(java.util.List<A> xs) { return i -> { int idx = (int)(long)i; return (idx >= 0 && idx < xs.size()) ? java.util.Optional.of(xs.get(idx)) : java.util.Optional.empty(); }; }
    private static long charToInt(char c) { return (long) c; }
    private static char intToChar(long n) { return (char)(int) n; }
    private static boolean charIsDigit(char c) { return Character.isDigit(c); }
    private static boolean charIsAlpha(char c) { return Character.isLetter(c); }
    private static boolean charIsSpace(char c) { return Character.isWhitespace(c); }
    private static <A, B> java.util.function.Function<java.util.Optional<A>, java.util.Optional<B>> maybeMap(java.util.function.Function<A, B> f) { return m -> m.map(f::apply); }
    private static <A, B> java.util.function.Function<java.util.function.Function<A, java.util.Optional<B>>, java.util.Optional<B>> maybeBind(java.util.Optional<A> m) { return f -> m.flatMap(f::apply); }
    private static <A> java.util.function.Function<java.util.Optional<A>, A> maybeDefault(A d) { return m -> m.orElse(d); }
    private static <A> boolean maybeIsNone(java.util.Optional<A> m) { return !m.isPresent(); }
    // --- end prelude ---"""

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

let private emitModule (tm: TypedModule) : string =
    let className = javaClassName tm.Path

    let isTypeDecl (d: TypedDecl) = match d with TDType _ | TDTag _ | TDUnit _ -> true | _ -> false
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
        "import java.util.List;\n" +
        "import java.util.Optional;\n" +
        "import java.util.function.Function;\n"

    let innerParts =
        [ javaPrelude
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
let emit (tm: TypedModule) : string = emitModule tm
