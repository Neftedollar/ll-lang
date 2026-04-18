module LLLang.CodegenTS

open System
open LLLang.AST
open LLLang.Types
open LLLang.TypedAST
open LLLang.Platform

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
    if Set.contains withHead tsKeywords then "_ll_" + withHead else withHead

// ── Type emission ─────────────────────────────────────────────────────────────

let private isTypeParamName (n: string) =
    n.Length = 1 && Char.IsUpper n.[0]

let rec private collectTyApp (t: TypeExpr) : TypeExpr * TypeExpr list =
    match t with
    | TyApp(f, a) ->
        let (head, args) = collectTyApp f
        (head, args @ [a])
    | _ -> (t, [])

let rec private emitType (t: TypeExpr) : string =
    match t with
    | TyName "Int"   -> "number"
    | TyName "Float" -> "number"
    | TyName "Str"   -> "string"
    | TyName "Bool"  -> "boolean"
    | TyName "Unit"  -> "void"
    | TyName "Char"  -> "string"
    // Keep polymorphic holes explicit but never leak `any` in public signatures.
    | TyName x when isTypeParamName x -> "unknown"
    | TyName x       -> safeTypeIdent x
    | TyVar _        -> "unknown"
    | TyApp _ ->
        let (head, args) = collectTyApp t
        match head, args with
        | TyName "List", [a] ->
            emitType a + "[]"
        | TyName "Maybe", [a] ->
            emitType a + " | null"
        | TyName "Result", [okTy; errTy] ->
            "{ ok: true; value: " + emitType okTy + " } | { ok: false; error: " + emitType errTy + " }"
        // HKTs are erased in the current backend. Avoid emitting invalid TS
        // such as `unknown<number>` for `F[Int]`.
        | TyVar _, _ ->
            "unknown"
        | TyName n, _ when isTypeParamName n ->
            "unknown"
        | _ ->
            let headStr = emitType head
            let argsStr = args |> List.map emitType |> String.concat ", "
            headStr + "<" + argsStr + ">"
    | TyFn(_, _)     -> "unknown"
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
            | '\000' -> "\\0"
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
    "floatToStr",   "(f: number): string => String(f)"
    // strToInt / strToFloat return tagged-union Maybe so that `match strToInt x | Some n -> ... | None -> ...` works.
    "strToInt",     "(s: string) => { const n = parseInt(s, 10); return isNaN(n) ? ({ _tag: `None` as const }) : ({ _tag: `Some` as const, _0: n }); }"
    "strToFloat",   "(s: string) => { const n = Number(s); return !Number.isFinite(n) ? ({ _tag: `None` as const }) : ({ _tag: `Some` as const, _0: n }); }"
    // List
    "listLen",      "<T>(xs: T[]): number => xs.length"
    "listMap",      "<A, B>(f: (x: A) => B) => (xs: A[]): B[] => xs.map(f)"
    "listFilter",   "<A>(p: (x: A) => boolean) => (xs: A[]): A[] => xs.filter(p)"
    "listFold",     "<A, B>(f: (acc: B) => (x: A) => B) => (z: B) => (xs: A[]): B => xs.reduce((a, x) => f(a)(x), z)"
    // listHead / listTail / listAt return tagged-union Maybe for the same reason.
    "listHead",     "<T>(xs: T[]) => xs.length > 0 ? ({ _tag: `Some` as const, _0: xs[0] }) : ({ _tag: `None` as const })"
    "listTail",     "<T>(xs: T[]) => xs.length > 0 ? ({ _tag: `Some` as const, _0: xs.slice(1) }) : ({ _tag: `None` as const })"
    "listReverse",  "<T>(xs: T[]): T[] => [...xs].reverse()"
    "listAppend",   "<T>(xs: T[]) => (ys: T[]): T[] => [...xs, ...ys]"
    "listIsEmpty",  "<T>(xs: T[]): boolean => xs.length === 0"
    "listContains", "<T>(xs: T[]) => (x: T): boolean => xs.includes(x)"
    "listRange",    "(lo: number) => (hi: number): number[] => Array.from({length: hi - lo}, (_, i) => lo + i)"
    "listConcat",   "<T>(xss: T[][]): T[] => xss.flat()"
    "listAt",       "<T>(xs: T[]) => (i: number) => i >= 0 && i < xs.length ? ({ _tag: `Some` as const, _0: xs[i] }) : ({ _tag: `None` as const })"
    // Char
    "charToInt",    "(c: string): number => c.charCodeAt(0)"
    "intToChar",    "(n: number): string => String.fromCharCode(n)"
    "charIsDigit",  "(c: string): boolean => /\\d/.test(c)"
    "charIsAlpha",  "(c: string): boolean => /[a-zA-Z]/.test(c)"
    "charIsSpace",  "(c: string): boolean => /\\s/.test(c)"
    // Maybe/Result helpers
    "maybeMap",     "<A, B>(f: (x: A) => B) => (m: A | null): B | null => m !== null ? f(m) : null"
    "maybeBind",    "<A, B>(m: A | null) => (f: (x: A) => B | null): B | null => m !== null ? f(m) : null"
    "maybeWithDefault", "<A>(d: A) => (m: A | null): A => m !== null ? m : d"
    // Backward-compat alias for older generated snapshots.
    "maybeDefault", "<A>(d: A) => (m: A | null): A => m !== null ? m : d"
    "maybeIsNone",  "<A>(m: A | null): boolean => m === null"
    "resultMap",    "<A, B, E>(f: (x: A) => B) => (r: { ok: true; value: A } | { ok: false; error: E }): { ok: true; value: B } | { ok: false; error: E } => r.ok ? { ok: true, value: f(r.value) } : r"
    "resultBind",   "<A, B, E>(r: { ok: true; value: A } | { ok: false; error: E }) => (f: (x: A) => { ok: true; value: B } | { ok: false; error: E }): { ok: true; value: B } | { ok: false; error: E } => r.ok ? f(r.value) : r"
    "resultMapErr", "<A, E, F>(f: (e: E) => F) => (r: { ok: true; value: A } | { ok: false; error: E }): { ok: true; value: A } | { ok: false; error: F } => r.ok ? r : { ok: false, error: f(r.error) }"
    "resultIsOk",   "<A, E>(r: { ok: true; value: A } | { ok: false; error: E }): boolean => r.ok"
]

let private tsStdlibNames : Set<string> =
    stdlibMap |> Map.toList |> List.map fst |> Set.ofList

let private exprStdlibUsage (te: TypedExpr) : Set<string> =
    let rec walk (acc: Set<string>) (e: TypedExpr) =
        let acc' =
            match e.Expr with
            | TEVar name when Set.contains name tsStdlibNames -> Set.add name acc
            | _ -> acc
        match e.Expr with
        | TEApp(a, b)
        | TEPipe(a, b)
        | TECons(a, b) -> walk (walk acc' a) b
        | TELam(_, body)
        | TETagged(body, _) -> walk acc' body
        | TELet(_, _, e1, e2)
        | TELetPat(_, e1, e2) ->
            let next = walk acc' e1
            e2 |> Option.map (walk next) |> Option.defaultValue next
        | TEIf(c, t, e2) ->
            walk (walk (walk acc' c) t) e2
        | TEMatch(s, branches)
        | TEMatchOf(s, branches) ->
            let withScrut = walk acc' s
            branches |> List.fold (fun st (_, b) -> walk st b) withScrut
        | TEList es
        | TETuple es ->
            es |> List.fold walk acc'
        | _ -> acc'
    walk Set.empty te

let private moduleStdlibUsage (tm: TypedModule) : Set<string> =
    tm.Decls
    |> List.fold (fun acc (decl, _) ->
        let used =
            match decl with
            | TDFn(_, _, body) -> exprStdlibUsage body
            | TDLet(_, _, e) -> exprStdlibUsage e
            | TDImpl(_, _, methods) ->
                methods
                |> List.fold (fun macc (_, _, body) -> Set.union macc (exprStdlibUsage body)) Set.empty
            | _ -> Set.empty
        Set.union acc used
    ) Set.empty

let private moduleTopLevelNames (tm: TypedModule) : Set<string> =
    tm.Decls
    |> List.choose (fun (decl, _) ->
        match decl with
        | TDFn(sig_, _, _) -> Some sig_.Name
        | TDLet(name, _, _) -> Some name
        | TDExternal(sig_, _) -> Some sig_.Name
        | _ -> None)
    |> Set.ofList

let private emitStdlibBlock (usedStdlib: Set<string>) (reservedNames: Set<string>) : string =
    let decls =
        stdlibMap
        |> Map.toList
        |> List.choose (fun (name, impl) ->
            if Set.contains name usedStdlib && not (Set.contains name reservedNames) then
                Some ("const " + safeIdent name + " = " + impl + ";")
            else
                None)
    if List.isEmpty decls then
        ""
    else
        String.concat "\n\n" ("// --- ll-lang stdlib (TypeScript) ---" :: decls)

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

/// Recursively decompose a nested PCons pattern into guards + bindings.
/// Returns (minLength, guardConditions, varBindings) where:
///   minLength       = minimum list length required
///   guardConditions = complete condition expressions (e.g. "scrut[1]?._tag === `Tok`")
///   varBindings     = "const x = ..." binding statements
let rec private decompCons (scrut: string) (p: Pattern) (depth: int)
    : int * string list * string list =
    match p with
    | PCons(PLit l, rest) ->
        let elemCheck = scrut + "[" + string depth + "] === " + emitLit l
        let (minLen, conds, binds) = decompCons scrut rest (depth + 1)
        (max minLen (depth + 1), elemCheck :: conds, binds)
    | PCons(PVar h, rest) ->
        let bind = "const " + safeIdent h + " = " + scrut + "[" + string depth + "]"
        let (minLen, conds, binds) = decompCons scrut rest (depth + 1)
        (max minLen (depth + 1), conds, bind :: binds)
    | PCons(PWild, rest) ->
        decompCons scrut rest (depth + 1)
    | PCons(PCon(c, args), rest) ->
        // Constructor head: e.g. Newline :: rest  or  IntLit n :: rest
        let tagCheck = scrut + "[" + string depth + "]?._tag === `" + c + "`"
        let argBinds =
            args |> List.mapi (fun i arg ->
                match arg with
                | PVar v ->
                    Some ("const " + safeIdent v + " = (" + scrut + "[" + string depth +
                          "] as Record<string, unknown>)[`_" + string i + "`]")
                | _ -> None)
            |> List.choose id
        let (minLen, conds, tailBinds) = decompCons scrut rest (depth + 1)
        (max minLen (depth + 1), tagCheck :: conds, argBinds @ tailBinds)
    | PVar t ->
        (depth, [], ["const " + safeIdent t + " = " + scrut + ".slice(" + string depth + ")"])
    | PWild ->
        (depth, [], [])
    | PCon("[]", []) ->
        // Nil at tail position: list must end exactly here
        (depth, [scrut + ".length === " + string depth], [])
    | _ ->
        (0, [], [])

let rec private emitMatchBranches (scrut: string) (branches: (TypedPattern * TypedExpr) list) : string =
    // Always alias the scrutinee to a reserved temp name.
    // This prevents TDZ errors when pattern variables shadow the scrutinee variable
    // (e.g. `match e | ELet x e body -> ...` where pattern var 'e' would shadow
    //  the outer 'e' in `const e = e[`_1`]` making the RHS refer to the new TDZ 'e').
    let s = "_ll_s"
    let emitBranch (tp: TypedPattern) (body: TypedExpr) : string =
        match tp.Pat with
        | PWild ->
            "  return " + emitExprTS body + ";"

        | PVar x ->
            "  const " + safeIdent x + " = " + s + "; return " + emitExprTS body + ";"

        | PLit l ->
            "  if (" + s + " === " + emitLit l + ") { return " + emitExprTS body + "; }"

        | PCon("[]", []) ->
            "  if (" + s + ".length === 0) { return " + emitExprTS body + "; }"

        | PCons _ ->
            // General nested cons: h::t,  h::_,  'x'::rest,  Tok::rest,  'x'::'y'::rest, etc.
            let (minLen, conds, varBinds) = decompCons s tp.Pat 0
            let parts =
                [ if minLen > 0 then yield s + ".length >= " + string minLen
                  yield! conds ]
            let guardStr = String.concat " && " parts
            let bindsStr = varBinds |> String.concat "; "
            let inner =
                (if bindsStr <> "" then bindsStr + "; " else "") +
                "return " + emitExprTS body + ";"
            if guardStr = "" then "  { " + inner + " }"
            else "  if (" + guardStr + ") { " + inner + " }"

        | PCon(c, []) ->
            // Zero-arg constructor or bare tag
            let cond =
                if c = "true" || c = "false" then s + " === " + c
                else s + "?._tag === `" + c + "`"
            "  if (" + cond + ") { return " + emitExprTS body + "; }"

        | PCon(c, args) ->
            // N-arg constructor: destructure _0, _1, ...
            // Also emit inner checks for PLit args (e.g. TyName "?" checks _0 === '?').
            let tagCond = s + "?._tag === `" + c + "`"
            let innerConds =
                args |> List.mapi (fun i arg ->
                    match arg with
                    | PLit l -> Some ("(" + s + " as Record<string, unknown>)[`_" + string i + "`] === " + emitLit l)
                    | _ -> None)
                |> List.choose id
            let cond = (tagCond :: innerConds) |> String.concat " && "
            let binds =
                args |> List.mapi (fun i arg ->
                    match arg with
                    | PVar v -> "const " + safeIdent v + " = (" + s + " as Record<string, unknown>)[`_" + string i + "`];"
                    | _ -> "")
                |> List.filter (fun s -> s <> "")
                |> String.concat " "
            "  if (" + cond + ") { " + binds + " return " + emitExprTS body + "; }"

        | PTuple ps ->
            let binds =
                ps |> List.mapi (fun i p ->
                    match p with
                    | PVar v -> "const " + safeIdent v + " = " + s + "[" + string i + "];"
                    | _ -> "")
                |> List.filter (fun s -> s <> "")
                |> String.concat " "
            "  { " + binds + " return " + emitExprTS body + "; }"

    let branchLines = branches |> List.map (fun (tp, body) -> emitBranch tp body)
    let body = String.concat "\n" branchLines + "\n  throw new Error(`Non-exhaustive match`);"
    // Indent the _ll_s alias so build-compiler.js dedup (/^const \w+ =/) doesn't match it.
    "(() => {\n  const " + s + " = " + scrut + ";\n" + body + "\n})()"

// ── Expression emission ───────────────────────────────────────────────────────

and private emitExprTS (te: TypedExpr) : string =
    let rec tryFnDomain (t: TypeExpr) : TypeExpr option =
        match t with
        | TyFn(argTy, _) -> Some argTy
        | TyTagged(inner, _) -> tryFnDomain inner
        | _ -> None

    let rec hasErasedHead (t: TypeExpr) : bool =
        match t with
        | TyVar _ -> true
        | TyName n when isTypeParamName n -> true
        | TyApp(f, _) -> hasErasedHead f
        | TyTagged(inner, _) -> hasErasedHead inner
        | _ -> false

    let rec isErasedType (t: TypeExpr) : bool =
        match t with
        | TyVar _ -> true
        | TyName n when isTypeParamName n -> true
        | TyApp _ -> hasErasedHead t
        | TyTagged(inner, _) -> isErasedType inner
        | _ -> false

    let emitArgFor (fnTy: TypeExpr) (arg: TypedExpr) : string =
        let argStr = emitExprTS arg
        match tryFnDomain fnTy with
        | Some expected when isErasedType arg.Type ->
            let expectedTs = emitType expected
            if expectedTs = "unknown" then argStr
            else "(" + argStr + " as " + expectedTs + ")"
        | _ -> argStr

    match tryAsSymbolicOp te with
    | Some (">>=", left, right) ->
        "(" + emitExprTS right + ")(" + emitArgFor right.Type left + ")"
    | Some (">>", _, right) ->
        "(" + emitExprTS right + ")"
    | Some ("<|>", left, _) ->
        "(" + emitExprTS left + ")"
    | _ ->
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
    | TEVar x  -> safeIdent x
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
            "(" + emitExprTS f + ")(" + emitArgFor f.Type a + ")"
        | _ ->
            "(" + emitExprTS f + ")(" + emitArgFor f.Type a + ")"

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
        let scrutStr = emitExprTS scrut
        emitMatchBranches scrutStr branches

    | TECons(h, t) ->
        "[" + emitExprTS h + ", ..." + emitExprTS t + "]"

// ── Declaration emission ──────────────────────────────────────────────────────

let private isMainFn (sig_: TypedFnSig) =
    sig_.Name = "main" && List.isEmpty sig_.Params

let private emitSumType (name: TypeIdent) (ps: TypeParam list) (branches: (TypeIdent * TypeExpr list) list) : string =
    let typeParams =
        ps |> List.choose (function TPBare n -> Some (safeTypeIdent n) | TPPhantom _ -> None)
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
    let typeDecl = "type " + safeTypeIdent name + tpStr + " = " + (variants |> String.concat " | ") + ";"
    // Constructor functions
    let ctors =
        branches |> List.map (fun (con, args) ->
            match args with
            | [] ->
                // For generic sums, a zero-arg constructor cannot reference free type
                // vars (e.g. `const None: Maybe<A>`). Emit a monomorphic value that
                // remains assignable at use sites.
                let ctorType =
                    if List.isEmpty typeParams then safeTypeIdent name + tpStr
                    else safeTypeIdent name + "<unknown>"
                "const " + safeIdent con + ": " + ctorType + " = { _tag: `" + con + "` as const };"
            | _ ->
                let paramList =
                    args |> List.mapi (fun i t -> "_" + string i + ": " + emitType t)
                    |> String.concat ", "
                let objFields =
                    args |> List.mapi (fun i _ -> "_" + string i)
                    |> String.concat ", "
                let genericPrefix =
                    if List.isEmpty typeParams then ""
                    else "<" + (String.concat ", " typeParams) + ">"
                "const " + safeIdent con + " = " + genericPrefix + "(" + paramList + "): " + safeTypeIdent name + tpStr +
                " => ({ _tag: `" + con + "` as const, " + objFields + " });")
    String.concat "\n" (typeDecl :: ctors)

let private emitCurriedValue (ps: (string * TypeExpr) list) (body: TypedExpr) : string =
    match ps with
    | [] -> "(() => " + emitExprTS body + ")()"
    | _ ->
        let lambdas =
            ps
            |> List.map (fun (n, t) -> "(" + safeIdent n + ": " + emitType t + ") => ")
            |> String.concat ""
        lambdas + emitExprTS body

let private emitExternalDecl (sig_: TypedFnSig) : string =
    match tryGetManifestExternal TypeScript sig_.Name with
    | Some raw ->
        // Per-project manifest externals are assumed to be complete curried
        // JS expressions (or zero-arg arrows for nullary externs); splice raw.
        "const " + safeIdent sig_.Name + ": any = " + raw + ";"
    | None ->
        match tryGetExternalTarget TypeScript sig_.Name with
        | None -> ""
        | Some target ->
            let pname (n, _) = safeIdent n
            let ptype (_, t) = emitType t
            let rec emitCurriedCall (ps: (string * TypeExpr) list) (args: string list) : string =
                match ps with
                | [] ->
                    target + "(" + (String.concat ", " args) + ")"
                | p :: rest ->
                    let n = pname p
                    let t = ptype p
                    "(" + n + ": " + t + ") => " + emitCurriedCall rest (args @ [n])
            let rhs = emitCurriedCall sig_.Params []
            "const " + safeIdent sig_.Name + " = " + rhs + ";"

let private emitOpaqueType (name: TypeIdent) (ps: TypeParam list) : string =
    let typeParams =
        ps |> List.choose (function TPBare n -> Some (safeTypeIdent n) | TPPhantom _ -> None)
    let tpStr = if List.isEmpty typeParams then "" else "<" + String.concat ", " typeParams + ">"
    "type " + safeTypeIdent name + tpStr + " = unknown;"

let private emitDecl (decl: TypedDecl) : string =
    match decl with
    | TDOpaque(name, ps) ->
        emitOpaqueType name ps

    | TDType(name, ps, body) ->
        match body with
        | TBSum branches -> emitSumType name ps branches
        | TBRecord fields ->
            let typeParams =
                ps |> List.choose (function TPBare n -> Some (safeTypeIdent n) | TPPhantom _ -> None)
            let tpStr = if List.isEmpty typeParams then "" else "<" + String.concat ", " typeParams + ">"
            let flds = fields |> List.map (fun (f, t) -> f + ": " + emitType t) |> String.concat "; "
            "type " + safeTypeIdent name + tpStr + " = { " + flds + " };"
        | TBWrapped t ->
            "type " + safeTypeIdent name + " = " + emitType t + ";"

    | TDTag _ | TDUnit _ | TDTrait _ -> ""
    | TDExternal(sig_, _) -> emitExternalDecl sig_

    | TDFn(sig_, _, body) ->
        if isMainFn sig_ then
            "function main(): void {\n  " + emitExprTS body + ";\n}"
        elif sig_.Name.StartsWith("__ll_main_") then
            // Renamed sub-module test runner (from rewriteNonEntryMain).
            // Emit as a no-op so it doesn't execute sub-module tests during library load.
            "const " + safeIdent sig_.Name + " = () => {};"
        else
            let valueExpr = emitCurriedValue sig_.Params body
            "const " + safeIdent sig_.Name + " = " + valueExpr + ";"

    | TDLet(x, _, e) ->
        "const " + safeIdent x + " = " + emitExprTS e + ";"

    | TDLetPat(tp, e) ->
        "const " + emitPattern tp.Pat + " = " + emitExprTS e + ";"

    | TDImpl(_, typeName, methods) ->
        methods |> List.map (fun (sig_, _, body) ->
            "const " + safeIdent sig_.Name + "_" + safeIdent typeName + " = " + emitCurriedValue sig_.Params body + ";"
        ) |> String.concat "\n"

// ── Module emission ───────────────────────────────────────────────────────────

let private emitModule
    (includeHeader: bool)
    (includeStdlib: bool)
    (includeMainCall: bool)
    (stdlibUsage: Set<string>)
    (reservedNames: Set<string>)
    (tm: TypedModule)
    : string =
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

    // Find main function
    let hasMain =
        tm.Decls |> List.exists (fun (d, _) ->
            match d with TDFn(sig_, _, _) -> isMainFn sig_ | _ -> false)

    let stdlibBlock =
        if includeStdlib then emitStdlibBlock stdlibUsage reservedNames else ""

    let parts =
        [ (if includeHeader then "// @ts-nocheck\n// Generated by lllc (ll-lang TypeScript backend)" else "")
          (if typeStr  <> "" then typeStr else "")
          (if stdlibBlock <> "" then stdlibBlock else "")
          (if otherStr <> "" then otherStr else "")
          (if includeMainCall && hasMain then "\nmain();" else "") ]
        |> List.filter (fun s -> s <> "")

    String.concat "\n\n" parts

/// Emit a fully-inferred module as TypeScript source.
let emit (tm: TypedModule) : string =
    emitModule true true true (moduleStdlibUsage tm) (moduleTopLevelNames tm) tm

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

/// Emit multiple modules as a single TypeScript source string.
let emitProjectModules (tms: TypedModule list) : string =
    match tms with
    | [] -> ""
    | [tm] ->
        emitModule true true true (moduleStdlibUsage tm) (moduleTopLevelNames tm) tm
    | _ ->
        let lastIdx = List.length tms - 1
        let rewritten =
            tms
            |> List.mapi (fun i tm ->
                if i = lastIdx then tm
                else rewriteNonEntryMain (moduleSuffix tm) tm)
        let stdlibUsage =
            rewritten |> List.fold (fun acc tm -> Set.union acc (moduleStdlibUsage tm)) Set.empty
        let reservedNames =
            rewritten |> List.fold (fun acc tm -> Set.union acc (moduleTopLevelNames tm)) Set.empty
        let rendered =
            rewritten
            |> List.mapi (fun i tm ->
                emitModule (i = 0) (i = 0) (i = lastIdx) stdlibUsage reservedNames tm)
        String.concat "\n\n" rendered
