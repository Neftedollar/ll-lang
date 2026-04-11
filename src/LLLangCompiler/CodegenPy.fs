module LLLang.CodegenPy

open System
open LLLang.AST
open LLLang.Types
open LLLang.TypedAST

// ── Python reserved words ─────────────────────────────────────────────────────

let private pyKeywords =
    Set.ofList [
        "False"; "None"; "True"; "and"; "as"; "assert"; "async"; "await"
        "break"; "class"; "continue"; "def"; "del"; "elif"; "else"; "except"
        "finally"; "for"; "from"; "global"; "if"; "import"; "in"; "is"
        "lambda"; "nonlocal"; "not"; "or"; "pass"; "raise"; "return"
        "try"; "while"; "with"; "yield"; "print"; "type" ]

let private safeIdent (s: string) =
    if Set.contains s pyKeywords then "_ll_" + s else s

// ── Type annotation emission ──────────────────────────────────────────────────

let private isTypeParamName (n: string) =
    n.Length = 1 && Char.IsUpper n.[0]

let rec private emitType (t: TypeExpr) : string =
    match t with
    | TyName "Int"   -> "int"
    | TyName "Float" -> "float"
    | TyName "Str"   -> "str"
    | TyName "Bool"  -> "bool"
    | TyName "Unit"  -> "None"
    | TyName "Char"  -> "str"
    | TyName x when isTypeParamName x -> x
    | TyName x       -> x
    | TyVar v        -> v
    | TyApp(TyName "List", a)  -> "list[" + emitType a + "]"
    | TyApp(TyName "Maybe", a) -> "Optional[" + emitType a + "]"
    | TyApp(TyName "Result", a) -> "Union[tuple[bool, " + emitType a + "], tuple[bool, Any]]"
    | TyApp(f, a)    -> emitType f + "[" + emitType a + "]"
    | TyFn(a, b)     -> "Callable[[" + emitType a + "], " + emitType b + "]"
    | TyTagged(t, _) -> emitType t

// ── Literal emission ──────────────────────────────────────────────────────────

let private emitLit (l: Literal) : string =
    match l with
    | LInt n   -> string n
    | LFloat f ->
        let s = sprintf "%g" f
        if s.Contains('.') || s.Contains('e') || s.Contains('E') then s else s + ".0"
    | LStr s   ->
        let escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")
        "\"" + escaped + "\""
    | LBool b  -> if b then "True" else "False"
    | LChar ch ->
        let escaped =
            match ch with
            | '\\' -> "\\\\"
            | '"'  -> "\\\""
            | '\n' -> "\\n"
            | '\t' -> "\\t"
            | '\r' -> "\\r"
            | c -> string c
        "\"" + escaped + "\""

// ── Binary operators ──────────────────────────────────────────────────────────

let private binaryOp (op: string) : string option =
    match op with
    | "+"  -> Some "+" | "-"  -> Some "-" | "*"  -> Some "*" | "/"  -> Some "//"
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
                | Some pyop -> Some (pyop, left, right)
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

// ── Pattern matching helpers ──────────────────────────────────────────────────
// Python 3.10+ structural match statement

let rec private emitMatchCases (ind: string) (scrutVar: string) (branches: (TypedPattern * TypedExpr) list) : string =
    branches |> List.map (fun (tp, body) ->
        let bodyStr = emitExprPy body
        match tp.Pat with
        | PWild ->
            ind + "case _:\n" + ind + "    return " + bodyStr

        | PVar x ->
            ind + "case " + safeIdent x + ":\n" + ind + "    return " + bodyStr

        | PLit l ->
            ind + "case " + emitLit l + ":\n" + ind + "    return " + bodyStr

        | PCon("[]", []) ->
            ind + "case []:\n" + ind + "    return " + bodyStr

        | PCons(PVar h, PVar t) ->
            ind + "case [" + safeIdent h + ", *" + safeIdent t + "]:\n" + ind + "    return " + bodyStr

        | PCon(c, []) ->
            ind + "case _ if " + scrutVar + "._tag == \"" + c + "\":\n" + ind + "    return " + bodyStr

        | PCon(c, args) ->
            let binds =
                args |> List.mapi (fun i arg ->
                    match arg with
                    | PVar v -> ind + "    " + safeIdent v + " = " + scrutVar + "._" + string i
                    | _ -> "")
                |> List.filter (fun s -> s <> "")
                |> String.concat "\n"
            ind + "case _ if " + scrutVar + "._tag == \"" + c + "\":\n" +
            (if binds <> "" then binds + "\n" else "") +
            ind + "    return " + bodyStr

        | PTuple ps ->
            let binds =
                ps |> List.mapi (fun i p ->
                    match p with
                    | PVar v -> ind + "    " + safeIdent v + " = " + scrutVar + "[" + string i + "]"
                    | _ -> "")
                |> List.filter (fun s -> s <> "")
                |> String.concat "\n"
            ind + "case _:\n" +
            (if binds <> "" then binds + "\n" else "") +
            ind + "    return " + bodyStr

        | _ ->
            ind + "case _:\n" + ind + "    return " + bodyStr
    ) |> String.concat "\n"

// ── Expression emission ───────────────────────────────────────────────────────

and private emitExprPy (te: TypedExpr) : string =
    match tryAsStrConcat te with
    | Some (a, b) -> "(" + emitExprPy a + " + " + emitExprPy b + ")"
    | None ->
    match tryAsBinOp te with
    | Some (op, a, b) -> "(" + emitExprPy a + " " + op + " " + emitExprPy b + ")"
    | None ->
    match te.Expr with
    | TELit l  -> emitLit l
    | TEVar x  -> safeIdent x
    | TECon c  ->
        match c with
        | "true" | "True" -> "True"
        | "false" | "False" -> "False"
        | "None" | "null" -> "None"
        | _ -> safeIdent c

    | TEApp(f, a) ->
        let rec gatherArgs head acc =
            match head.Expr with
            | TEApp(g, x) -> gatherArgs g (x :: acc)
            | _ -> (head, acc)
        let (head, args) = gatherArgs f [a]
        match head.Expr with
        | TECon c ->
            let argsStr = args |> List.map emitExprPy |> String.concat ", "
            safeIdent c + "(" + argsStr + ")"
        | _ ->
            // Curried: chain single-arg calls
            let rec buildCall (f: TypedExpr) (args: TypedExpr list) =
                match args with
                | [] -> emitExprPy f
                | [x] -> "(" + emitExprPy f + ")(" + emitExprPy x + ")"
                | x :: rest -> buildCall { f with Expr = TEApp(f, x) } rest
            buildCall f [a]

    | TELam(ps, body) ->
        match ps with
        | [(name, _)] ->
            "lambda " + safeIdent name + ": " + emitExprPy body
        | _ ->
            // Multi-param: nested lambdas
            let rec nest = function
                | [] -> emitExprPy body
                | (name, _) :: rest -> "lambda " + safeIdent name + ": " + nest rest
            nest ps

    | TELet(x, _, e, Some body) ->
        // Python doesn't have let-in; use lambda
        "(lambda " + safeIdent x + ": " + emitExprPy body + ")(" + emitExprPy e + ")"

    | TELet(_, _, e, None) ->
        emitExprPy e  // top-level only case

    | TELetPat(tp, e, Some body) ->
        "(lambda _tmp: " + emitExprPy body + ")(" + emitExprPy e + ")"

    | TELetPat(_, e, None) ->
        emitExprPy e

    | TEIf(c, t, e) ->
        "(" + emitExprPy t + " if " + emitExprPy c + " else " + emitExprPy e + ")"

    | TETagged(e, _) -> emitExprPy e

    | TEList es ->
        "[" + (es |> List.map emitExprPy |> String.concat ", ") + "]"

    | TETuple es ->
        "(" + (es |> List.map emitExprPy |> String.concat ", ") + ")"

    | TEPipe(a, b) ->
        "(" + emitExprPy b + ")(" + emitExprPy a + ")"

    | TEMatch(scrut, branches) | TEMatchOf(scrut, branches) ->
        // Python match statement must be inside a def, so wrap in a lambda that calls a nested def
        // For simplicity: use a immediately-invoked nested function approach via exec-like pattern
        // Better: emit as nested function call using a helper
        let scrutStr = emitExprPy scrut
        // Build if/elif/else chain (works everywhere, no Python 3.10 requirement)
        emitMatchAsIfElse scrutStr branches

    | TECons(h, t) ->
        "[" + emitExprPy h + "] + " + emitExprPy t

and private emitMatchAsIfElse (scrutStr: string) (branches: (TypedPattern * TypedExpr) list) : string =
    // For Python we can't easily use match as an expression, so use nested ternary
    // For complex patterns, this means generating a small helper lambda
    let emitCond (scrutVar: string) (pat: Pattern) : string option =
        match pat with
        | PWild | PVar _ -> None  // always matches
        | PLit l -> Some (scrutVar + " == " + emitLit l)
        | PCon("[]", []) -> Some ("len(" + scrutVar + ") == 0")
        | PCons _ -> Some ("len(" + scrutVar + ") > 0")
        | PCon(c, _) -> Some (scrutVar + "._tag == \"" + c + "\"")
        | _ -> None

    let emitBinds (scrutVar: string) (pat: Pattern) : string =
        match pat with
        | PVar x -> safeIdent x + " := " + scrutVar
        | PCon(c, args) ->
            args |> List.mapi (fun i arg ->
                match arg with
                | PVar v -> safeIdent v + " := " + scrutVar + "._" + string i
                | _ -> "")
            |> List.filter (fun s -> s <> "")
            |> String.concat ", "
        | PCons(PVar h, PVar t) ->
            safeIdent h + " := " + scrutVar + "[0], " + safeIdent t + " := " + scrutVar + "[1:]"
        | PTuple ps ->
            ps |> List.mapi (fun i p ->
                match p with PVar v -> safeIdent v + " := " + scrutVar + "[" + string i + "]" | _ -> "")
            |> List.filter (fun s -> s <> "")
            |> String.concat ", "
        | _ -> ""

    let rec buildChain = function
        | [] -> "None  # unreachable"
        | [(tp, body)] ->
            let bodyStr = emitExprPy body
            let binds = emitBinds scrutStr tp.Pat
            if binds <> "" then
                "(lambda " + binds + ": " + bodyStr + ")()"
            else bodyStr
        | (tp, body) :: rest ->
            let bodyStr = emitExprPy body
            let restStr = buildChain rest
            match emitCond scrutStr tp.Pat with
            | None ->
                // Always matches — emit with binds if any
                let binds = emitBinds scrutStr tp.Pat
                if binds <> "" then
                    "(lambda " + binds + ": " + bodyStr + ")()"
                else bodyStr
            | Some cond ->
                "(" + bodyStr + " if " + cond + " else " + restStr + ")"

    buildChain branches

// ── Declaration emission ──────────────────────────────────────────────────────

let private isMainFn (sig_: TypedFnSig) =
    sig_.Name = "main" && List.isEmpty sig_.Params

let private containsVar (name: string) (te: TypedExpr) : bool =
    let rec walk e =
        match e.Expr with
        | TEVar x when x = name -> true
        | TEApp(a, b) | TEPipe(a, b) | TECons(a, b) -> walk a || walk b
        | TELam(_, body) | TETagged(body, _) -> walk body
        | TELet(_, _, e1, e2) -> walk e1 || (e2 |> Option.exists walk)
        | TELetPat(_, e1, e2) -> walk e1 || (e2 |> Option.exists walk)
        | TEIf(c, t, e) -> walk c || walk t || walk e
        | TEMatch(s, brs) | TEMatchOf(s, brs) ->
            walk s || List.exists (fun (_, b) -> walk b) brs
        | TEList es | TETuple es -> List.exists walk es
        | _ -> false
    walk te

let private emitSumTypePy (name: TypeIdent) (branches: (TypeIdent * TypeExpr list) list) : string =
    let ctors =
        branches |> List.map (fun (con, args) ->
            let fields =
                args |> List.mapi (fun i t ->
                    "    _" + string i + ": " + emitType t)
                |> String.concat "\n"
            "@dataclass\nclass " + con + ":\n    _tag: str = \"" + con + "\"\n" +
            (if fields = "" then "    pass" else fields))
    let unionType = name + " = Union[" + (branches |> List.map fst |> String.concat ", ") + "]"
    String.concat "\n\n" ctors + "\n\n" + unionType

let private emitFnPy (sig_: TypedFnSig) (body: TypedExpr) : string =
    let isRec = containsVar sig_.Name body
    match sig_.Params with
    | [] ->
        if isMainFn sig_ then
            "def main() -> None:\n    " + emitExprPy body
        else
            safeIdent sig_.Name + ": " + emitType sig_.ReturnType + " = " + emitExprPy body
    | [(p, pt)] ->
        "def " + safeIdent sig_.Name + "(" + safeIdent p + ": " + emitType pt +
        ") -> " + emitType sig_.ReturnType + ":\n    return " + emitExprPy body
    | ps ->
        // Curried: top-level fn takes first arg, returns nested defs
        let first = List.head ps
        let rest = List.tail ps
        let rec buildCurried indent = function
            | [] -> indent + "return " + emitExprPy body
            | [(p, pt)] ->
                indent + "def _f_" + safeIdent p + "(" + safeIdent p + ": " + emitType pt + "):\n" +
                indent + "    return " + emitExprPy body + "\n" +
                indent + "return _f_" + safeIdent p
            | (p, pt) :: rest2 ->
                indent + "def _f_" + safeIdent p + "(" + safeIdent p + ": " + emitType pt + "):\n" +
                buildCurried (indent + "    ") rest2 + "\n" +
                indent + "return _f_" + safeIdent p
        let (fp, fpt) = first
        "def " + safeIdent sig_.Name + "(" + safeIdent fp + ": " + emitType fpt +
        "):\n" + buildCurried "    " rest

let rec private emitPattern (p: Pattern) : string =
    match p with
    | PVar x    -> safeIdent x
    | PWild     -> "_"
    | PLit l    -> emitLit l
    | PCon(c, []) -> safeIdent c
    | PCon(c, ps) -> safeIdent c + "(" + (ps |> List.map emitPattern |> String.concat ", ") + ")"
    | PTuple ps -> "(" + (ps |> List.map emitPattern |> String.concat ", ") + ")"
    | PCons(h, t) -> "[" + emitPattern h + ", *" + emitPattern t + "]"

let private emitDecl (decl: TypedDecl) : string =
    match decl with
    | TDType(name, _, body) ->
        match body with
        | TBSum branches -> emitSumTypePy name branches
        | TBRecord fields ->
            let flds = fields |> List.map (fun (f, t) -> "    " + f + ": " + emitType t) |> String.concat "\n"
            "@dataclass\nclass " + name + ":\n" + flds
        | TBWrapped t ->
            "@dataclass\nclass " + name + ":\n    value: " + emitType t

    | TDTag _ | TDUnit _ | TDTrait _ -> ""

    | TDFn(sig_, _, body) -> emitFnPy sig_ body

    | TDLet(x, _, e) ->
        safeIdent x + " = " + emitExprPy e

    | TDLetPat(tp, e) ->
        emitPattern tp.Pat + " = " + emitExprPy e

    | TDImpl(_, typeName, methods) ->
        methods |> List.map (fun (sig_, _, body) ->
            "def " + safeIdent typeName + "_" + safeIdent sig_.Name + "(" +
            (sig_.Params |> List.map (fun (n, t) -> safeIdent n + ": " + emitType t) |> String.concat ", ") +
            ") -> " + emitType sig_.ReturnType + ":\n    return " + emitExprPy body
        ) |> String.concat "\n\n"

// ── Python stdlib prelude ─────────────────────────────────────────────────────

let private pyPrelude = """# --- ll-lang stdlib (Python) ---
from __future__ import annotations
from dataclasses import dataclass, field
from typing import Optional, Union, Callable, Any
import sys
import os
import math

def _ll_abs(x: int) -> int: return abs(x)
def absf(x: float) -> float: return abs(x)
def sqrt(x: float) -> float: return math.sqrt(x)
def _ll_min(a: int) -> Callable[[int], int]: return lambda b: min(a, b)
def _ll_max(a: int) -> Callable[[int], int]: return lambda b: max(a, b)
def intToFloat(n: int) -> float: return float(n)
def floatToInt(f: float) -> int: return int(f)
def printfn(s: str) -> None: print(s)
def _ll_print(s: str) -> None: sys.stdout.write(s)
def readFile(path: str) -> str:
    with open(path, 'r', encoding='utf-8') as f: return f.read()
def writeFile(path: str) -> Callable[[str], None]:
    return lambda contents: open(path, 'w', encoding='utf-8').write(contents)
def _ll_exit(n: int) -> None: sys.exit(n)
def getArgs() -> list[str]: return sys.argv[1:]
def strLen(s: str) -> int: return len(s)
def strConcat(a: str) -> Callable[[str], str]: return lambda b: a + b
def strTrim(s: str) -> str: return s.strip()
def strContains(needle: str) -> Callable[[str], bool]: return lambda hay: needle in hay
def strSplit(sep: str) -> Callable[[str], list[str]]: return lambda s: s.split(sep)
def strSlice(s: str) -> Callable: return lambda start: lambda length: s[start:start+length]
def strIndexOf(needle: str) -> Callable[[str], int]: return lambda hay: hay.find(needle)
def strReverse(s: str) -> str: return s[::-1]
def strFromChars(cs: list[str]) -> str: return ''.join(cs)
def strChars(s: str) -> list[str]: return list(s)
def intToStr(n: int) -> str: return str(n)
def strToInt(s: str) -> Optional[int]:
    try: return int(s)
    except ValueError: return None
def listLen(xs: list) -> int: return len(xs)
def listMap(f) -> Callable: return lambda xs: list(map(f, xs))
def listFilter(p) -> Callable: return lambda xs: list(filter(p, xs))
def listFold(f) -> Callable: return lambda z: lambda xs: (lambda: [z := f(z)(x) for x in xs] or z)()
def listHead(xs: list) -> Optional[Any]: return xs[0] if xs else None
def listTail(xs: list) -> Optional[list]: return xs[1:] if xs else None
def listReverse(xs: list) -> list: return list(reversed(xs))
def listAppend(xs: list) -> Callable[[list], list]: return lambda ys: xs + ys
def listIsEmpty(xs: list) -> bool: return len(xs) == 0
def listContains(xs: list) -> Callable[[Any], bool]: return lambda x: x in xs
def listRange(lo: int) -> Callable[[int], list[int]]: return lambda hi: list(range(lo, hi))
def listConcat(xss: list) -> list: return [x for xs in xss for x in xs]
def listAt(xs: list) -> Callable[[int], Optional[Any]]: return lambda i: xs[i] if 0 <= i < len(xs) else None
def charToInt(c: str) -> int: return ord(c)
def intToChar(n: int) -> str: return chr(n)
def charIsDigit(c: str) -> bool: return c.isdigit()
def charIsAlpha(c: str) -> bool: return c.isalpha()
def charIsSpace(c: str) -> bool: return c.isspace()
def maybeMap(f) -> Callable: return lambda m: f(m) if m is not None else None
def maybeBind(m) -> Callable: return lambda f: f(m) if m is not None else None
def maybeDefault(d) -> Callable: return lambda m: m if m is not None else d
def maybeIsNone(m) -> bool: return m is None
def resultMap(f) -> Callable: return lambda r: (True, f(r[1])) if r[0] else r
def resultBind(r) -> Callable: return lambda f: f(r[1]) if r[0] else r
def resultIsOk(r) -> bool: return r[0]
# --- end prelude ---
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

    let hasMain =
        tm.Decls |> List.exists (fun (d, _) ->
            match d with TDFn(sig_, _, _) -> isMainFn sig_ | _ -> false)

    let parts =
        [ "# Generated by lllc (ll-lang Python backend)"
          pyPrelude
          (if typeStr  <> "" then typeStr else "")
          (if otherStr <> "" then otherStr else "")
          (if hasMain then "\nif __name__ == '__main__':\n    main()" else "") ]
        |> List.filter (fun s -> s <> "")

    String.concat "\n\n" parts

/// Emit a fully-inferred module as Python source.
let emit (tm: TypedModule) : string = emitModule tm

/// Emit multiple modules as a single Python source string.
let emitProjectModules (tms: TypedModule list) : string =
    tms |> List.map emitModule |> String.concat "\n\n"
