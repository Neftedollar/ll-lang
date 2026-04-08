module LLLang.Codegen

open System
open LLLang.AST
open LLLang.Types
open LLLang.TypedAST

// ---- F# keyword safety -------------------------------------------------------

let private fsKeywords =
    Set.ofList [
        "abstract"; "and"; "as"; "assert"; "asr"; "base"; "begin"; "class"
        "default"; "delegate"; "do"; "done"; "downcast"; "downto"; "elif"
        "else"; "end"; "exception"; "extern"; "false"; "finally"; "for"
        "fun"; "function"; "global"; "if"; "in"; "inherit"; "inline"; "interface"
        "internal"; "land"; "lazy"; "let"; "lor"; "lsl"; "lsr"; "lxor"
        "match"; "member"; "mod"; "module"; "mutable"; "namespace"; "new"
        "not"; "null"; "of"; "open"; "or"; "override"; "private"; "public"
        "rec"; "return"; "sealed"; "static"; "struct"; "then"; "to"; "true"
        "try"; "type"; "upcast"; "use"; "val"; "virtual"; "void"; "when"
        "while"; "with"; "yield" ]

let private safeIdent (s: string) =
    if Set.contains s fsKeywords then "``" + s + "``" else s

// ---- Type emission -----------------------------------------------------------

/// Single uppercase letter is a type parameter (A, B, C...) that the parser
/// emits as `TyName` and the codegen must render as F# `'A`.
let private isTypeParamName (n: string) =
    n.Length = 1 && System.Char.IsUpper n.[0]

let rec private emitType (t: TypeExpr) : string =
    match t with
    | TyName "Int"   -> "int64"
    | TyName "Float" -> "float"
    | TyName "Str"   -> "string"
    | TyName "Bool"  -> "bool"
    | TyName "Unit"  -> "unit"
    | TyName "Char"  -> "char"
    | TyName x when isTypeParamName x -> "'" + x
    | TyName x       -> x
    | TyVar v        -> "'" + v
    | TyApp(TyName "List", a) -> emitType a + " list"
    | TyApp(f, a)    -> emitType a + " " + emitType f
    | TyFn(a, b)     -> emitType a + " -> " + emitType b
    | TyTagged(t, _) -> emitType t

let private emitTypeParams (ps: TypeParam list) : string =
    let bare = ps |> List.choose (function TPBare n -> Some ("'" + n) | TPPhantom _ -> None)
    if List.isEmpty bare then "" else "<" + String.concat ", " bare + ">"

// ---- Literal emission --------------------------------------------------------

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
            | '\000' -> "\\000"
            | c -> string c
        "'" + escaped + "'"

// ---- Binary operator mapping -------------------------------------------------

let private binaryOp (op: string) : string option =
    match op with
    | "+" -> Some "+" | "-" -> Some "-" | "*" -> Some "*" | "/" -> Some "/"
    | "==" -> Some "=" | "!=" -> Some "<>" | "<" -> Some "<" | ">" -> Some ">"
    | "<=" -> Some "<=" | ">=" -> Some ">=" | _ -> None

// ---- Pattern emission --------------------------------------------------------

let rec private emitPattern (p: Pattern) : string =
    match p with
    | PVar x   -> safeIdent x
    | PWild    -> "_"
    | PLit l   -> emitLit l
    | PCon(c, [])  -> safeIdent c
    | PCon(c, [p]) -> safeIdent c + " " + emitPattern p
    | PCon(c, ps)  -> safeIdent c + "(" + (ps |> List.map emitPattern |> String.concat ", ") + ")"
    | PTuple ps    -> "(" + (ps |> List.map emitPattern |> String.concat ", ") + ")"
    | PCons(h, t)  -> "(" + emitPattern h + " :: " + emitPattern t + ")"

// ---- Expression emission -----------------------------------------------------

and private emitExpr (indent: int) (te: TypedExpr) : string =
    let ind = String.replicate indent " "
    match te.Expr with
    | TELit l  -> emitLit l
    | TEVar x  -> safeIdent x
    | TECon c  -> safeIdent c

    | TEApp(outer, b) when (match outer.Expr with TEApp(inner, _) -> (match inner.Expr with TEVar op -> binaryOp op <> None | _ -> false) | _ -> false) ->
        let (a, op) =
            match outer.Expr with
            | TEApp(inner, a) ->
                match inner.Expr with
                | TEVar op -> (a, op)
                | _ -> failwith "unreachable"
            | _ -> failwith "unreachable"
        let fop = (binaryOp op).Value
        "(" + emitExpr indent a + " " + fop + " " + emitExpr indent b + ")"

    | TEApp(f, a) ->
        "(" + emitExpr indent f + " " + emitExpr indent a + ")"

    | TELam(ps, body) ->
        let paramStr = ps |> List.map (fst >> safeIdent) |> String.concat " "
        "(fun " + paramStr + " -> " + emitExpr indent body + ")"

    | TELet(x, _, e, Some body) ->
        "(let " + safeIdent x + " = " + emitExpr indent e + " in\n" + ind + "  " + emitExpr (indent+2) body + ")"

    | TELet(x, _, e, None) ->
        "(let " + safeIdent x + " = " + emitExpr indent e + ")"

    | TELetPat(tp, e, Some body) ->
        // F# accepts `let <pat> = <e> in <body>` directly for tuple/wildcard.
        "(let " + emitPattern tp.Pat + " = " + emitExpr indent e + " in\n" + ind + "  " + emitExpr (indent+2) body + ")"

    | TELetPat(tp, e, None) ->
        "(let " + emitPattern tp.Pat + " = " + emitExpr indent e + ")"

    | TEIf(c, t, e) ->
        "(if " + emitExpr indent c + " then " + emitExpr indent t + " else " + emitExpr indent e + ")"

    | TETagged(e, _) -> emitExpr indent e

    | TEList es ->
        "[" + (es |> List.map (emitExpr indent) |> String.concat "; ") + "]"

    | TETuple es ->
        "(" + (es |> List.map (emitExpr indent) |> String.concat ", ") + ")"

    | TEPipe(a, b) ->
        "(" + emitExpr indent b + " " + emitExpr indent a + ")"

    | TEMatch(scrut, branches) ->
        let brsStr =
            branches |> List.map (fun (tp, body) ->
                ind + "| " + emitPattern tp.Pat + " -> " + emitExpr indent body)
            |> String.concat "\n"
        "(match " + emitExpr indent scrut + " with\n" + brsStr + ")"

    | TEMatchOf(scrut, branches) ->
        // Inline form keeps the entire match-expression on one line so it
        // can be embedded in any expression position without offside-rule
        // surprises. F# accepts `(match x with | p1 -> e1 | p2 -> e2)`.
        let brsStr =
            branches
            |> List.map (fun (tp, body) ->
                "| " + emitPattern tp.Pat + " -> " + emitExpr indent body)
            |> String.concat " "
        "(match " + emitExpr indent scrut + " with " + brsStr + ")"

    | TECons(h, t) ->
        "(" + emitExpr indent h + " :: " + emitExpr indent t + ")"

// ---- Recursion detection -----------------------------------------------------

let private containsVar (name: string) (te: TypedExpr) : bool =
    let rec walk e =
        match e.Expr with
        | TEVar x when x = name -> true
        | TEApp(a, b) | TEPipe(a, b) | TECons(a, b) -> walk a || walk b
        | TELam(_, body) | TETagged(body, _) -> walk body
        | TELet(_, _, e1, e2) -> walk e1 || (e2 |> Option.exists walk)
        | TELetPat(_, e1, e2) -> walk e1 || (e2 |> Option.exists walk)
        | TEIf(c, t, el) -> walk c || walk t || walk el
        | TEMatch(s, brs) | TEMatchOf(s, brs) ->
            walk s || List.exists (fun (_, b) -> walk b) brs
        | TEList es | TETuple es -> List.exists walk es
        | _ -> false
    walk te

// ---- Declaration emission ---------------------------------------------------

let private isMainFn (sig_: TypedFnSig) =
    sig_.Name = "main" && List.isEmpty sig_.Params

/// Emit a single TDFn's body clause: `<name> <params> =\n    <body>`.
/// Shared between stand-alone `let ...` and `let rec ... and ...` emission.
let private emitFnClause (sig_: TypedFnSig) (body: TypedExpr) : string =
    let paramStr = sig_.Params |> List.map (fst >> safeIdent) |> String.concat " "
    let paramPart = if paramStr = "" then "" else " " + paramStr
    safeIdent sig_.Name + paramPart + " =\n    " + emitExpr 4 body

let private emitDecl (decl: TypedDecl) : string =
    match decl with

    | TDType(name, ps, body) ->
        let params' = emitTypeParams ps
        let header = "type " + name + params' + " ="
        match body with
        | TBSum branches ->
            let arms =
                branches |> List.map (fun (con, args) ->
                    match args with
                    | [] -> "    | " + con
                    | _  -> "    | " + con + " of " + (args |> List.map emitType |> String.concat " * "))
            header + "\n" + String.concat "\n" arms
        | TBRecord fields ->
            let flds = fields |> List.map (fun (f, t) -> f + ": " + emitType t) |> String.concat "; "
            header + " { " + flds + " }"
        | TBWrapped t ->
            header + "\n    | " + name + " of " + emitType t

    | TDTag _  -> ""
    | TDUnit _ -> ""
    | TDTrait _ -> ""

    | TDFn(sig_, _, body) ->
        if isMainFn sig_ then
            "[<EntryPoint>]\nlet main (argv: string[]) =\n    " + emitExpr 4 body + "\n    0"
        else
            let isRec = containsVar sig_.Name body
            let recKw = if isRec then "rec " else ""
            "let " + recKw + emitFnClause sig_ body

    | TDLet(x, _, e) ->
        "let " + safeIdent x + " = " + emitExpr 0 e

    | TDLetPat(tp, e) ->
        "let " + emitPattern tp.Pat + " = " + emitExpr 0 e

    | TDImpl(_, typeName, methods) ->
        methods |> List.map (fun (sig_, _, body) ->
            let isRec = containsVar sig_.Name body
            let recKw = if isRec then "rec " else ""
            let paramStr = sig_.Params |> List.map (fst >> safeIdent) |> String.concat " "
            let paramPart = if paramStr = "" then "" else " " + paramStr
            "let " + recKw + safeIdent typeName + "_" + safeIdent sig_.Name + paramPart + " =\n    " + emitExpr 4 body
        ) |> String.concat "\n\n"

// ---- Group consecutive fn decls into `let rec ... and ...` blocks ---------
//
// Two or more sibling top-level fns in ll-lang can be mutually recursive
// (see HMInfer's two-pass top-level inference). At codegen time we must emit
// them as a single F# `let rec f ... and g ...` group or the second fn will
// fail to resolve the first. We partition the non-type decls into runs of
// consecutive non-main TDFn decls; each run is emitted as a `let rec ... and
// ...` block iff at least one fn in the run references another fn in the
// same run. Otherwise runs are split into singleton groups so existing
// output stays as `let f ...` (no unnecessary `rec`).

/// Partition declarations into groups: either a single non-fn decl, a single
/// main fn, a singleton non-main fn, or a multi-fn mutually-recursive group.
let private groupDecls (ds: (TypedDecl * bool) list) : (TypedDecl * bool) list list =
    let runs = ResizeArray<(TypedDecl * bool) list>()
    let current = ResizeArray<TypedDecl * bool>()
    let flush () =
        if current.Count > 0 then
            runs.Add(List.ofSeq current)
            current.Clear()
    for (d, exported) in ds do
        match d with
        | TDFn(sig_, _, _) when not (isMainFn sig_) ->
            current.Add((d, exported))
        | _ ->
            flush ()
            runs.Add([(d, exported)])
    flush ()
    let result = ResizeArray<(TypedDecl * bool) list>()
    for run in runs do
        let isFnRun =
            match run with
            | (TDFn(sig_, _, _), _) :: _ when not (isMainFn sig_) -> run.Length >= 1
            | _ -> false
        if isFnRun && run.Length >= 2 then
            let names =
                run |> List.choose (fun (d, _) ->
                    match d with TDFn(sig_, _, _) -> Some sig_.Name | _ -> None)
            let mentionsOther =
                run
                |> List.exists (fun (d, _) ->
                    match d with
                    | TDFn(sig_, _, body) ->
                        names
                        |> List.exists (fun n ->
                            n <> sig_.Name && containsVar n body)
                    | _ -> false)
            if mentionsOther then
                result.Add(run)
            else
                for item in run do result.Add([item])
        else
            result.Add(run)
    List.ofSeq result

/// Emit a group: a single decl delegates to emitDecl; a 2+ fn group becomes
/// a `let rec <first> and <rest>` block.
let private emitDeclGroup (group: (TypedDecl * bool) list) : string =
    match group with
    | [] -> ""
    | [(d, _)] -> emitDecl d
    | fns ->
        let clauses =
            fns
            |> List.map (fun (d, _) ->
                match d with
                | TDFn(sig_, _, body) -> emitFnClause sig_ body
                | _ -> failwith "groupDecls invariant violated")
        match clauses with
        | first :: rest ->
            "let rec " + first + "\n\n"
            + (rest |> List.map (fun c -> "and " + c) |> String.concat "\n\n")
        | [] -> ""

// ---- F# prelude block (Phase 6 stdlib) --------------------------------------
//
// Prepended to every emitted module to provide ll-lang stdlib bindings that
// forward to F# standard library. Emitted AFTER user type declarations so that
// references to user-declared constructors (e.g. Some/None from
// `type Maybe A = Some A | None`) resolve to the user's own types rather than
// F#'s Option. Users of Maybe-returning stdlib fns (listHead, listTail,
// strToInt, maybeMap, ...) must declare `type Maybe A = Some A | None` in
// their module; likewise `type Result A E = Ok A | Err E` for Result fns.
//
// Maybe/Result-dependent bindings are emitted conditionally: only when the
// user module actually declares the corresponding type. This avoids referencing
// undefined constructors in modules that don't use them.

/// Core prelude block — always emitted. Has no dependencies on user-declared types.
let private fsharpPreludeCore : string = """// --- ll-lang stdlib prelude (auto-generated) ---
let abs (x: int64) = System.Math.Abs(x)
let absf (x: float) = System.Math.Abs(x)
let sqrt (x: float) = System.Math.Sqrt(x)
let min (a: int64) (b: int64) = if a < b then a else b
let max (a: int64) (b: int64) = if a > b then a else b
let listLen (xs: 'a list) : int64 = int64 (List.length xs)
let listMap f xs = List.map f xs
let listFilter p xs = List.filter p xs
let listFold f z xs = List.fold f z xs
let listReverse xs = List.rev xs
let listAppend xs ys = List.append xs ys
let strLen (s: string) : int64 = int64 s.Length
let strConcat (a: string) (b: string) = a + b
let strTrim (s: string) = s.Trim()
let strContains (needle: string) (haystack: string) = haystack.Contains(needle: string)
let print (s: string) = System.Console.Write(s)
let printfn (s: string) = System.Console.WriteLine(s)
let strChars (s: string) = s |> Seq.toList
let charToInt (c: char) = int64 (int c)
let intToChar (n: int64) = char (int n)
let intToStr (n: int64) = string n
let strSlice (s: string) (start: int64) (len: int64) = s.Substring(int start, int len)
let strIndexOf (needle: string) (haystack: string) : int64 = int64 (haystack.IndexOf(needle: string))
let strSplit (sep: string) (s: string) = s.Split([| sep |], System.StringSplitOptions.None) |> Array.toList
let strFromChars (cs: char list) = System.String(cs |> List.toArray)
let strReverse (s: string) = System.String(s.ToCharArray() |> Array.rev)
let charIsDigit (c: char) = System.Char.IsDigit(c)
let charIsAlpha (c: char) = System.Char.IsLetter(c)
let charIsSpace (c: char) = System.Char.IsWhiteSpace(c)
let readFile (path: string) = System.IO.File.ReadAllText(path: string)
let writeFile (path: string) (contents: string) = System.IO.File.WriteAllText(path, contents)
let fileExists (path: string) = System.IO.File.Exists(path: string)
let exit (code: int64) : unit = System.Environment.Exit(int code)
let listConcat (xss: 'a list list) = List.concat xss
let listIsEmpty (xs: 'a list) = List.isEmpty xs"""

/// Maybe-dependent prelude block — emitted only when user declares `type Maybe`.
let private fsharpPreludeMaybe : string = """let listHead xs = match xs with [] -> None | x :: _ -> Some x
let listTail xs = match xs with [] -> None | _ :: t -> Some t
let maybeMap f m = match m with Some x -> Some (f x) | None -> None
let maybeBind m f = match m with Some x -> f x | None -> None
let maybeWithDefault d m = match m with Some x -> x | None -> d
let strToInt (s: string) =
    match System.Int64.TryParse(s: string) with
    | true, n -> Some n
    | false, _ -> None
let listAt (xs: 'a list) (i: int64) =
    if int i < 0 || int i >= List.length xs then None else Some (List.item (int i) xs)"""

/// Result-dependent prelude block — emitted only when user declares `type Result`.
let private fsharpPreludeResult : string = """let resultMap f r = match r with Ok x -> Ok (f x) | Err e -> Err e
let resultBind r f = match r with Ok x -> f x | Err e -> Err e
let resultMapErr f r = match r with Ok x -> Ok x | Err e -> Err (f e)"""

let private preludeEnd : string = "// --- end prelude ---"

/// Assemble the prelude block for a given module: core + optional
/// Maybe/Result sections based on whether the user declared those types.
let private assemblePrelude (tm: TypedModule) : string =
    let typeNames =
        tm.Decls
        |> List.choose (fun (d, _) ->
            match d with TDType(n, _, _) -> Some n | _ -> None)
        |> Set.ofList
    let hasMaybe  = Set.contains "Maybe"  typeNames
    let hasResult = Set.contains "Result" typeNames
    let sections =
        [ yield fsharpPreludeCore
          if hasMaybe  then yield fsharpPreludeMaybe
          if hasResult then yield fsharpPreludeResult
          yield preludeEnd ]
    String.concat "\n" sections

let private emitModule (tm: TypedModule) : string =
    let header = "module " + String.concat "." tm.Path
    // Split decls: types first (so the prelude can reference user's Maybe/Result),
    // then prelude, then everything else (fns, lets, impls).
    let isTypeDecl (d: TypedDecl) =
        match d with
        | TDType _ -> true
        | _ -> false
    let typeDecls = tm.Decls |> List.filter (fun (d, _) -> isTypeDecl d)
    let otherDecls = tm.Decls |> List.filter (fun (d, _) -> not (isTypeDecl d))
    // Types: flat emit (no mutual-rec grouping).
    let typeStr =
        typeDecls
        |> List.map (fun (d, _) -> emitDecl d)
        |> List.filter (fun s -> s <> "")
        |> String.concat "\n\n"
    // Non-type decls: group consecutive non-main TDFn decls so F# sees a
    // `let rec ... and ...` block, enabling mutual recursion across siblings.
    let otherStr =
        groupDecls otherDecls
        |> List.map emitDeclGroup
        |> List.filter (fun s -> s <> "")
        |> String.concat "\n\n"
    let prelude = assemblePrelude tm
    let parts =
        [ header
          (if typeStr = "" then "" else typeStr)
          prelude
          (if otherStr = "" then "" else otherStr) ]
        |> List.filter (fun s -> s <> "")
    String.concat "\n\n" parts

/// Emit a fully-inferred module as F# source.
let emit (tm: TypedModule) : string = emitModule tm

/// Expose the core F# prelude block as a public constant (for tests).
let preludeBlock : string = fsharpPreludeCore
