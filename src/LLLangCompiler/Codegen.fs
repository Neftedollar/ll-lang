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

let rec private emitType (t: TypeExpr) : string =
    match t with
    | TyName "Int"   -> "int64"
    | TyName "Float" -> "float"
    | TyName "Str"   -> "string"
    | TyName "Bool"  -> "bool"
    | TyName "Unit"  -> "unit"
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

// ---- Recursion detection -----------------------------------------------------

let private containsVar (name: string) (te: TypedExpr) : bool =
    let rec walk e =
        match e.Expr with
        | TEVar x when x = name -> true
        | TEApp(a, b) | TEPipe(a, b) -> walk a || walk b
        | TELam(_, body) | TETagged(body, _) -> walk body
        | TELet(_, _, e1, e2) -> walk e1 || (e2 |> Option.exists walk)
        | TEIf(c, t, el) -> walk c || walk t || walk el
        | TEMatch(s, brs) -> walk s || List.exists (fun (_, b) -> walk b) brs
        | TEList es | TETuple es -> List.exists walk es
        | _ -> false
    walk te

// ---- Declaration emission ---------------------------------------------------

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
        let isMain = sig_.Name = "main" && List.isEmpty sig_.Params
        let isRec = containsVar sig_.Name body
        let recKw = if isRec then "rec " else ""
        let paramStr = sig_.Params |> List.map (fst >> safeIdent) |> String.concat " "
        let bodyStr = emitExpr 4 body
        if isMain then
            "[<EntryPoint>]\nlet main (argv: string[]) =\n    " + bodyStr + "\n    0"
        else
            let paramPart = if paramStr = "" then "" else " " + paramStr
            "let " + recKw + safeIdent sig_.Name + paramPart + " =\n    " + bodyStr

    | TDLet(x, _, e) ->
        "let " + safeIdent x + " = " + emitExpr 0 e

    | TDImpl(_, typeName, methods) ->
        methods |> List.map (fun (sig_, _, body) ->
            let isRec = containsVar sig_.Name body
            let recKw = if isRec then "rec " else ""
            let paramStr = sig_.Params |> List.map (fst >> safeIdent) |> String.concat " "
            let paramPart = if paramStr = "" then "" else " " + paramStr
            "let " + recKw + safeIdent typeName + "_" + safeIdent sig_.Name + paramPart + " =\n    " + emitExpr 4 body
        ) |> String.concat "\n\n"

let private emitModule (tm: TypedModule) : string =
    let header = "module " + String.concat "." tm.Path
    let decls =
        tm.Decls
        |> List.map (fun (d, _) -> emitDecl d)
        |> List.filter (fun s -> s <> "")
        |> String.concat "\n\n"
    header + "\n\n" + decls

/// Emit a fully-inferred module as F# source.
let emit (tm: TypedModule) : string = emitModule tm
