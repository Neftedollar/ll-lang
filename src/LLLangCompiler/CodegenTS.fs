module LLLang.CodegenTS

open System
open LLLang.AST
open LLLang.Types
open LLLang.TypedAST

// ── TypeScript reserved words ─────────────────────────────────────────────────

let private tsKeywords =
    Set.ofList [
        "break"; "case"; "catch"; "class"; "const"; "continue"; "debugger"
        "default"; "delete"; "do"; "else"; "enum"; "export"; "extends"
        "false"; "finally"; "for"; "function"; "if"; "import"; "in"
        "instanceof"; "new"; "null"; "return"; "super"; "switch"; "this"
        "throw"; "true"; "try"; "typeof"; "var"; "void"; "while"; "with"
        "as"; "implements"; "interface"; "let"; "package"; "private"
        "protected"; "public"; "static"; "type"; "yield"; "any"; "unknown"
        "never"; "object"; "string"; "number"; "boolean"; "symbol"; "bigint" ]

let private safeIdent (s: string) =
    if Set.contains s tsKeywords then "_ll_" + s else s

// ── Type emission ─────────────────────────────────────────────────────────────

let private isTypeParamName (n: string) =
    n.Length = 1 && Char.IsUpper n.[0]

let rec private emitType (t: TypeExpr) : string =
    match t with
    | TyName "Int"   -> "number"
    | TyName "Float" -> "number"
    | TyName "Str"   -> "string"
    | TyName "Bool"  -> "boolean"
    | TyName "Unit"  -> "void"
    | TyName "Char"  -> "string"
    | TyName x when isTypeParamName x -> x
    | TyName x       -> x
    | TyVar v        -> v
    | TyApp(TyName "List", a)  -> emitType a + "[]"
    | TyApp(TyName "Maybe", a) -> emitType a + " | null"
    | TyApp(TyName "Result", a) -> "{ ok: true; value: " + emitType a + " } | { ok: false; error: unknown }"
    | TyApp(f, a)    -> emitType f + "<" + emitType a + ">"
    | TyFn(a, b)     -> "(x: " + emitType a + ") => " + emitType b
    | TyTagged(t, _) -> emitType t

// ── Literal emission ──────────────────────────────────────────────────────────

let private emitLit (l: Literal) : string =
    match l with
    | LInt n   -> string n
    | LFloat f ->
        let s = sprintf "%g" f
        if s.Contains('.') || s.Contains('e') || s.Contains('E') then s else s + ".0"
    | LStr s   ->
        let escaped = s.Replace("\\", "\\\\").Replace("`", "\\`").Replace("$", "\\$").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")
        "`" + escaped + "`"
    | LBool b  -> if b then "true" else "false"
    | LChar ch ->
        let escaped =
            match ch with
            | '\\' -> "\\\\"
            | '`'  -> "\\`"
            | '\n' -> "\\n"
            | '\t' -> "\\t"
            | '\r' -> "\\r"
            | c -> string c
        "`" + escaped + "`"

// ── Binary operators ──────────────────────────────────────────────────────────

let private binaryOp (op: string) : string option =
    match op with
    | "+"  -> Some "+" | "-"  -> Some "-" | "*"  -> Some "*" | "/"  -> Some "/"
    | "==" -> Some "===" | "!=" -> Some "!==" | "<" -> Some "<" | ">" -> Some ">"
    | "<=" -> Some "<=" | ">=" -> Some ">=" | _ -> None

let private tryAsBinOp (te: TypedExpr) : (string * TypedExpr * TypedExpr) option =
    match te.Expr with
    | TEApp(outer, right) ->
        match outer.Expr with
        | TEApp(inner, left) ->
            match inner.Expr with
            | TEVar op ->
                match binaryOp op with
                | Some tsop -> Some (tsop, left, right)
                | None -> None
            | _ -> None
        | _ -> None
    | _ -> None

// ── Stdlib call mapping ───────────────────────────────────────────────────────
// ll-lang stdlib name → TypeScript expression (may be partial application)

let private stdlibMap = Map.ofList [
    // IO
    "printfn",      "(s: string): void => { console.log(s) }"
    "print",        "(s: string): void => { process.stdout.write(s) }"
    "readFile",     "(p: string): string => require('fs').readFileSync(p, 'utf8')"
    "writeFile",    "(p: string) => (c: string): void => require('fs').writeFileSync(p, c)"
    "exit",         "(n: number): never => process.exit(n)"
    "getArgs",      "(): string[] => process.argv.slice(2)"
    // Math
    "abs",          "(x: number): number => Math.abs(x)"
    "absf",         "(x: number): number => Math.abs(x)"
    "sqrt",         "(x: number): number => Math.sqrt(x)"
    "min",          "(a: number) => (b: number): number => Math.min(a, b)"
    "max",          "(a: number) => (b: number): number => Math.max(a, b)"
    "intToFloat",   "(n: number): number => n"
    "floatToInt",   "(f: number): number => Math.trunc(f)"
    // String
    "strLen",       "(s: string): number => s.length"
    "strConcat",    "(a: string) => (b: string): string => a + b"
    "strTrim",      "(s: string): string => s.trim()"
    "strContains",  "(needle: string) => (hay: string): boolean => hay.includes(needle)"
    "strSplit",     "(sep: string) => (s: string): string[] => s.split(sep)"
    "strSlice",     "(s: string) => (start: number) => (len: number): string => s.slice(start, start + len)"
    "strIndexOf",   "(needle: string) => (hay: string): number => hay.indexOf(needle)"
    "strReverse",   "(s: string): string => Array.from(s).reverse().join('')"
    "strFromChars", "(cs: string[]): string => cs.join('')"
    "strChars",     "(s: string): string[] => Array.from(s)"
    "intToStr",     "(n: number): string => String(n)"
    "strToInt",     "(s: string): number | null => { const n = parseInt(s, 10); return isNaN(n) ? null : n }"
    // List
    "listLen",      "(xs: any[]): number => xs.length"
    "listMap",      "(f: (x: any) => any) => (xs: any[]): any[] => xs.map(f)"
    "listFilter",   "(p: (x: any) => boolean) => (xs: any[]): any[] => xs.filter(p)"
    "listFold",     "(f: (acc: any) => (x: any) => any) => (z: any) => (xs: any[]): any => xs.reduce((a: any, x: any) => f(a)(x), z)"
    "listHead",     "(xs: any[]): any | null => xs.length > 0 ? xs[0] : null"
    "listTail",     "(xs: any[]): any[] | null => xs.length > 0 ? xs.slice(1) : null"
    "listReverse",  "(xs: any[]): any[] => [...xs].reverse()"
    "listAppend",   "(xs: any[]) => (ys: any[]): any[] => [...xs, ...ys]"
    "listIsEmpty",  "(xs: any[]): boolean => xs.length === 0"
    "listContains", "(xs: any[]) => (x: any): boolean => xs.includes(x)"
    "listRange",    "(lo: number) => (hi: number): number[] => Array.from({length: hi - lo}, (_, i) => lo + i)"
    "listConcat",   "(xss: any[][]): any[] => xss.flat()"
    "listAt",       "(xs: any[]) => (i: number): any | null => i >= 0 && i < xs.length ? xs[i] : null"
    // Char
    "charToInt",    "(c: string): number => c.charCodeAt(0)"
    "intToChar",    "(n: number): string => String.fromCharCode(n)"
    "charIsDigit",  "(c: string): boolean => /\\d/.test(c)"
    "charIsAlpha",  "(c: string): boolean => /[a-zA-Z]/.test(c)"
    "charIsSpace",  "(c: string): boolean => /\\s/.test(c)"
    // Maybe/Result helpers
    "maybeMap",     "(f: (x: any) => any) => (m: any | null): any | null => m !== null ? f(m) : null"
    "maybeBind",    "(m: any | null) => (f: (x: any) => any | null): any | null => m !== null ? f(m) : null"
    "maybeDefault", "(d: any) => (m: any | null): any => m !== null ? m : d"
    "maybeIsNone",  "(m: any | null): boolean => m === null"
    "resultMap",    "(f: (x: any) => any) => (r: any): any => r.ok ? {ok: true, value: f(r.value)} : r"
    "resultBind",   "(r: any) => (f: (x: any) => any): any => r.ok ? f(r.value) : r"
    "resultIsOk",   "(r: any): boolean => r.ok"
]

// ── String concat operator ────────────────────────────────────────────────────
let private tryAsStrConcat (te: TypedExpr) : (TypedExpr * TypedExpr) option =
    match te.Expr with
    | TEApp(outer, right) ->
        match outer.Expr with
        | TEApp({ Expr = TEVar "++" }, left) -> Some (left, right)
        | _ -> None
    | _ -> None

// ── Pattern emission ──────────────────────────────────────────────────────────

let rec private emitPattern (p: Pattern) : string =
    match p with
    | PVar x   -> safeIdent x
    | PWild    -> "_"
    | PLit l   -> emitLit l
    | PCon("[]", []) -> "[]"
    | PCon(c, [])  -> safeIdent c
    | PCon(c, [p]) -> safeIdent c + "(" + emitPattern p + ")"
    | PCon(c, ps)  -> safeIdent c + "(" + (ps |> List.map emitPattern |> String.concat ", ") + ")"
    | PTuple ps    -> "[" + (ps |> List.map emitPattern |> String.concat ", ") + "]"
    | PCons(h, t)  -> emitPattern h + ", ..." + emitPattern t

// ── TypeScript match: emit branches as nested if/else ─────────────────────────
// Generates an IIFE so the match can be used as an expression.

let rec private emitMatchBranches (scrut: string) (branches: (TypedPattern * TypedExpr) list) : string =
    let emitBranch (tp: TypedPattern) (body: TypedExpr) : string =
        match tp.Pat with
        | PWild ->
            "  return " + emitExprTS body + ";"

        | PVar x ->
            "  const " + safeIdent x + " = " + scrut + "; return " + emitExprTS body + ";"

        | PLit l ->
            "  if (" + scrut + " === " + emitLit l + ") { return " + emitExprTS body + "; }"

        | PCon("[]", []) ->
            "  if (" + scrut + ".length === 0) { return " + emitExprTS body + "; }"

        | PCons(PVar h, PVar t) ->
            "  if (" + scrut + ".length > 0) { const " + safeIdent h + " = " + scrut + "[0], " +
            safeIdent t + " = " + scrut + ".slice(1); return " + emitExprTS body + "; }"

        | PCon(c, []) ->
            // Zero-arg constructor or bare tag
            let cond =
                if c = "true" || c = "false" then scrut + " === " + c
                else scrut + "?._tag === `" + c + "`"
            "  if (" + cond + ") { return " + emitExprTS body + "; }"

        | PCon(c, args) ->
            // N-arg constructor: destructure _0, _1, ...
            let cond = scrut + "?._tag === `" + c + "`"
            let binds =
                args |> List.mapi (fun i arg ->
                    match arg with
                    | PVar v -> "const " + safeIdent v + " = (" + scrut + " as any)._" + string i + ";"
                    | _ -> "") // nested patterns not fully supported in MVP
                |> List.filter (fun s -> s <> "")
                |> String.concat " "
            "  if (" + cond + ") { " + binds + " return " + emitExprTS body + "; }"

        | PTuple ps ->
            let binds =
                ps |> List.mapi (fun i p ->
                    match p with
                    | PVar v -> "const " + safeIdent v + " = " + scrut + "[" + string i + "];"
                    | _ -> "")
                |> List.filter (fun s -> s <> "")
                |> String.concat " "
            "  { " + binds + " return " + emitExprTS body + "; }"

        | _ ->
            "  return " + emitExprTS body + ";"

    let branchLines = branches |> List.map (fun (tp, body) -> emitBranch tp body)
    let body = String.concat "\n" branchLines + "\n  throw new Error(`Non-exhaustive match`);"
    "(() => {\n" + body + "\n})()"

// ── Expression emission ───────────────────────────────────────────────────────

and private emitExprTS (te: TypedExpr) : string =
    // String concat
    match tryAsStrConcat te with
    | Some (a, b) -> "(" + emitExprTS a + " + " + emitExprTS b + ")"
    | None ->
    // Binary ops
    match tryAsBinOp te with
    | Some (op, a, b) -> "(" + emitExprTS a + " " + op + " " + emitExprTS b + ")"
    | None ->
    match te.Expr with
    | TELit l  -> emitLit l
    | TEVar x  ->
        // Check if it's a known stdlib function
        match Map.tryFind x stdlibMap with
        | Some impl -> "(" + impl + ")"
        | None -> safeIdent x
    | TECon c  -> safeIdent c

    | TEApp(f, a) ->
        // Gather all args for multi-arg constructor
        let rec gatherArgs head acc =
            match head.Expr with
            | TEApp(g, x) -> gatherArgs g (x :: acc)
            | _ -> (head, acc)
        let (head, args) = gatherArgs f [a]
        match head.Expr with
        | TECon c when List.length args > 1 ->
            let argsStr = args |> List.map emitExprTS |> String.concat ", "
            safeIdent c + "(" + argsStr + ")"
        | TECon c ->
            safeIdent c + "(" + emitExprTS a + ")"
        | TEVar fname ->
            // Single application: curried
            "(" + emitExprTS f + ")(" + emitExprTS a + ")"
        | _ ->
            "(" + emitExprTS f + ")(" + emitExprTS a + ")"

    | TELam(ps, body) ->
        let paramStr =
            ps |> List.map (fun (name, typ) ->
                safeIdent name + ": " + emitType typ) |> String.concat ") => ("
        "(" + paramStr + ") => " + emitExprTS body

    | TELet(x, _, e, Some body) ->
        "((" + safeIdent x + ") => " + emitExprTS body + ")(" + emitExprTS e + ")"

    | TELet(x, _, e, None) ->
        "(() => { const " + safeIdent x + " = " + emitExprTS e + "; })()"

    | TELetPat(tp, e, Some body) ->
        // Simple: destructure binding
        let pat = emitPattern tp.Pat
        "((" + pat + ") => " + emitExprTS body + ")(" + emitExprTS e + ")"

    | TELetPat(tp, e, None) ->
        "(() => { const " + emitPattern tp.Pat + " = " + emitExprTS e + "; })()"

    | TEIf(c, t, e) ->
        "(" + emitExprTS c + " ? " + emitExprTS t + " : " + emitExprTS e + ")"

    | TETagged(e, _) -> emitExprTS e

    | TEList es ->
        "[" + (es |> List.map emitExprTS |> String.concat ", ") + "]"

    | TETuple es ->
        "[" + (es |> List.map emitExprTS |> String.concat ", ") + "] as const"

    | TEPipe(a, b) ->
        "(" + emitExprTS b + ")(" + emitExprTS a + ")"

    | TEMatch(scrut, branches) | TEMatchOf(scrut, branches) ->
        let scrutVar = "_m" + string te.Id
        "((_: any) => " + emitMatchBranches scrutVar branches + ")(" + emitExprTS scrut + ")"
        // Simpler IIFE: evaluate scrut once, use named var
        |> fun _ ->
            let scrutStr = emitExprTS scrut
            emitMatchBranches scrutStr branches

    | TECons(h, t) ->
        "[" + emitExprTS h + ", ..." + emitExprTS t + "]"

// ── Declaration emission ──────────────────────────────────────────────────────

let private isMainFn (sig_: TypedFnSig) =
    sig_.Name = "main" && List.isEmpty sig_.Params

let private emitSumType (name: TypeIdent) (ps: TypeParam list) (branches: (TypeIdent * TypeExpr list) list) : string =
    let typeParams =
        ps |> List.choose (function TPBare n -> Some n | TPPhantom _ -> None)
    let tpStr = if List.isEmpty typeParams then "" else "<" + String.concat ", " typeParams + ">"
    // Type union
    let variants =
        branches |> List.map (fun (con, args) ->
            match args with
            | [] -> "{ _tag: `" + con + "` }"
            | _  ->
                let fields =
                    args |> List.mapi (fun i t -> "_" + string i + ": " + emitType t)
                    |> String.concat "; "
                "{ _tag: `" + con + "`; " + fields + " }")
    let typeDecl = "type " + name + tpStr + " = " + (variants |> String.concat " | ") + ";"
    // Constructor functions
    let ctors =
        branches |> List.map (fun (con, args) ->
            match args with
            | [] ->
                "const " + safeIdent con + ": " + name + tpStr + " = { _tag: `" + con + "` as const };"
            | _ ->
                let paramList =
                    args |> List.mapi (fun i t -> "_" + string i + ": " + emitType t)
                    |> String.concat ", "
                let objFields =
                    args |> List.mapi (fun i _ -> "_" + string i)
                    |> String.concat ", "
                "const " + safeIdent con + tpStr + " = (" + paramList + "): " + name + tpStr +
                " => ({ _tag: `" + con + "` as const, " + objFields + " });")
    String.concat "\n" (typeDecl :: ctors)

let private emitDecl (decl: TypedDecl) : string =
    match decl with
    | TDType(name, ps, body) ->
        match body with
        | TBSum branches -> emitSumType name ps branches
        | TBRecord fields ->
            let typeParams =
                ps |> List.choose (function TPBare n -> Some n | TPPhantom _ -> None)
            let tpStr = if List.isEmpty typeParams then "" else "<" + String.concat ", " typeParams + ">"
            let flds = fields |> List.map (fun (f, t) -> f + ": " + emitType t) |> String.concat "; "
            "type " + name + tpStr + " = { " + flds + " };"
        | TBWrapped t ->
            "type " + name + " = " + emitType t + ";"

    | TDTag _ | TDUnit _ | TDTrait _ -> ""

    | TDFn(sig_, _, body) ->
        if isMainFn sig_ then
            "function main(): void {\n  " + emitExprTS body + ";\n}"
        else
            let paramStr =
                sig_.Params |> List.map (fun (n, t) ->
                    safeIdent n + ": " + emitType t)
                |> String.concat ") => ("
            let retType = emitType sig_.ReturnType
            let isRec =
                let rec contains name (te: TypedExpr) =
                    match te.Expr with
                    | TEVar x when x = name -> true
                    | TEApp(a, b) | TEPipe(a, b) | TECons(a, b) -> contains name a || contains name b
                    | TELam(_, b) | TETagged(b, _) -> contains name b
                    | TELet(_, _, e1, e2) -> contains name e1 || e2 |> Option.exists (contains name)
                    | TELetPat(_, e1, e2) -> contains name e1 || e2 |> Option.exists (contains name)
                    | TEIf(c, t, e) -> contains name c || contains name t || contains name e
                    | TEMatch(s, brs) | TEMatchOf(s, brs) ->
                        contains name s || List.exists (fun (_, b) -> contains name b) brs
                    | TEList es | TETuple es -> List.exists (contains name) es
                    | _ -> false
                contains sig_.Name body
            let valueExpr =
                if sig_.Params.IsEmpty then
                    ": " + retType + " = " + emitExprTS body
                else
                    " = (" + paramStr + "): " + retType + " => " + emitExprTS body
            if isRec then
                // TypeScript doesn't have let rec — use var for forward reference
                "const " + safeIdent sig_.Name + valueExpr + ";"
            else
                "const " + safeIdent sig_.Name + valueExpr + ";"

    | TDLet(x, _, e) ->
        "const " + safeIdent x + " = " + emitExprTS e + ";"

    | TDLetPat(tp, e) ->
        "const " + emitPattern tp.Pat + " = " + emitExprTS e + ";"

    | TDImpl(_, typeName, methods) ->
        methods |> List.map (fun (sig_, _, body) ->
            let paramStr =
                sig_.Params |> List.map (fun (n, t) -> safeIdent n + ": " + emitType t)
                |> String.concat ") => ("
            let valueExpr =
                if sig_.Params.IsEmpty then
                    " = " + emitExprTS body
                else
                    " = (" + paramStr + "): " + emitType sig_.ReturnType + " => " + emitExprTS body
            "const " + safeIdent typeName + "_" + safeIdent sig_.Name + valueExpr + ";"
        ) |> String.concat "\n"

// ── TypeScript stdlib prelude ─────────────────────────────────────────────────

let private tsPrelude = """// --- ll-lang stdlib (TypeScript) ---
"""

// ── Module emission ───────────────────────────────────────────────────────────

let private emitModule (tm: TypedModule) : string =
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

    // Find main function
    let hasMain =
        tm.Decls |> List.exists (fun (d, _) ->
            match d with TDFn(sig_, _, _) -> isMainFn sig_ | _ -> false)

    let parts =
        [ "// Generated by lllc (ll-lang TypeScript backend)"
          (if typeStr  <> "" then typeStr else "")
          tsPrelude
          (if otherStr <> "" then otherStr else "")
          (if hasMain then "\nmain();" else "") ]
        |> List.filter (fun s -> s <> "")

    String.concat "\n\n" parts

/// Emit a fully-inferred module as TypeScript source.
let emit (tm: TypedModule) : string = emitModule tm

/// Emit multiple modules as a single TypeScript source string.
let emitProjectModules (tms: TypedModule list) : string =
    tms |> List.map emitModule |> String.concat "\n\n"
