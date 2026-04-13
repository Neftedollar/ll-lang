module Std.Render

open LLLang.Prelude
open Std.Maybe
open Std.Lexer
open Std.Parser

let rec joinWith sep items =
    (match items with | [] -> "" | (x :: []) -> x | (x :: rest) -> ((strConcat x) ((strConcat sep) ((joinWith sep) rest))))

and encodeStrEscape c =
    (match c with | '\n' -> "\\n" | '\t' -> "\\t" | '\r' -> "\\r" | '\\' -> "\\\\" | '"' -> "\\\"" | _ -> (strFromChars [c]))

and escapeStr s =
    (((listFold (fun acc c -> ((strConcat acc) (encodeStrEscape c)))) "") (strChars s))

and emitCharLit c =
    (match c with | '\n' -> "'\\n'" | '\t' -> "'\\t'" | '\\' -> "'\\\\'" | '\'' -> "'\\''" | _ -> ((strConcat ((strConcat "'") (strFromChars [c]))) "'"))

and emitType t =
    (match t with | TyName(name) -> name | TyApp(head, arg) -> ((strConcat (emitType head)) ((strConcat "[") ((strConcat (emitType arg)) "]"))) | TyFn(a, b) -> ((strConcat (emitTypeAtom a)) ((strConcat " -> ") (emitType b))))

and emitTypeAtom t =
    (match t with | TyFn(_, _) -> ((strConcat "(") ((strConcat (emitType t)) ")")) | _ -> (emitType t))

and emitParam p =
    (match p with | MkParam(name, ty) -> ((strConcat "(") ((strConcat name) ((strConcat " ") ((strConcat (emitType ty)) ")")))))

and emitParams ps =
    ((joinWith "") ((listMap emitParam) ps))

and emitCtor c =
    (match c with | MkCon(name, args) -> (match args with | [] -> name | _ -> ((strConcat name) ((strConcat " ") ((joinWith " ") ((listMap emitTypeAtom) args))))))

and isAtomicExpr e =
    (match e with | EInt(_) -> true | EStr(_) -> true | EBool(_) -> true | EChar(_) -> true | EFloat(_) -> true | EVar(_) -> true | ECon(_) -> true | ENil -> true | _ -> false)

and emitExprAtom e =
    (if (isAtomicExpr e) then (emitExpr e) else ((strConcat "(") ((strConcat (emitExpr e)) ")")))

and emitPattern p =
    (match p with | PVar(name) -> name | PWild -> "_" | PNil -> "[]" | PLitInt(n) -> (intToStr n) | PLitStr(s) -> ((strConcat "\"") ((strConcat (escapeStr s)) "\"")) | PCons(h, t) -> ((strConcat (emitPatternAtom h)) ((strConcat " :: ") (emitPattern t))) | PCon(name, args) -> (match args with | [] -> name | _ -> ((strConcat name) ((strConcat " ") ((joinWith " ") ((listMap emitPatternAtom) args))))))

and emitPatternAtom p =
    (match p with | PCon(_, (_ :: _)) -> ((strConcat "(") ((strConcat (emitPattern p)) ")")) | PCons(_, _) -> ((strConcat "(") ((strConcat (emitPattern p)) ")")) | _ -> (emitPattern p))

and gatherAppHead e =
    (match e with | EApp(f, _) -> (gatherAppHead f) | _ -> e)

and gatherAppArgs e =
    (match e with | EApp(f, a) -> ((listAppend (gatherAppArgs f)) [a]) | _ -> [])

and gatherLamNames e =
    (match e with | ELam(name, body) -> (name :: (gatherLamNames body)) | _ -> [])

and gatherLamBody e =
    (match e with | ELam(_, body) -> (gatherLamBody body) | _ -> e)

and emitArms pats bodies =
    (match pats with | [] -> "" | (p :: restPats) -> (match bodies with | [] -> "" | (b :: restBodies) -> (let arm = ((strConcat "  | ") ((strConcat (emitPattern p)) ((strConcat " -> ") (emitExpr b)))) in (let rest = ((emitArms restPats) restBodies) in (match rest with | "" -> arm | _ -> ((strConcat arm) ((strConcat "\n") rest)))))))

and emitExpr e =
    (match e with | EInt(n) -> (intToStr n) | EStr(s) -> ((strConcat "\"") ((strConcat (escapeStr s)) "\"")) | EBool(b) -> (if b then "true" else "false") | EChar(c) -> (emitCharLit c) | EFloat(s) -> s | EVar(name) -> name | ECon(name) -> name | ENil -> "[]" | EApp(_, _) -> (let head = (gatherAppHead e) in (let args = (gatherAppArgs e) in ((strConcat (emitExprAtom head)) ((strConcat " ") ((joinWith " ") ((listMap emitExprAtom) args)))))) | EIf(cond, thenExpr, elseExpr) -> ((strConcat "if ") ((strConcat (emitExpr cond)) ((strConcat "\n  ") ((strConcat (emitExpr thenExpr)) ((strConcat "\nelse ") (emitExpr elseExpr)))))) | EMatch(scrut, pats, bodies) -> (let arms = ((emitArms pats) bodies) in ((strConcat "match ") ((strConcat (emitExpr scrut)) ((strConcat "\n") arms)))) | ELam(_, _) -> (let __ll_params = (gatherLamNames e) in (let body = (gatherLamBody e) in ((strConcat "\\") ((strConcat ((joinWith " ") __ll_params)) ((strConcat ". ") (emitExpr body)))))) | ELet(name, rhs, body) -> ((strConcat "let ") ((strConcat name) ((strConcat " = ") ((strConcat (emitExpr rhs)) ((strConcat "\n") (emitExpr body)))))) | EList(items) -> ((strConcat "[") ((strConcat ((joinWith " ") ((listMap emitExpr) items))) "]")) | EBinOp(op, lhs, rhs) -> ((strConcat "(") ((strConcat (emitExpr lhs)) ((strConcat " ") ((strConcat op) ((strConcat " ") ((strConcat (emitExpr rhs)) ")")))))) | ETuple(a, b) -> ((strConcat "(") ((strConcat (emitExpr a)) ((strConcat ", ") ((strConcat (emitExpr b)) ")")))) | ECons(h, t) -> ((strConcat (emitExprAtom h)) ((strConcat " :: ") (emitExpr t))))

and emitDecl d =
    (match d with | DFn(name, __ll_params, _, body) -> ((strConcat name) ((strConcat (emitParams __ll_params)) ((strConcat " = ") (emitExpr body)))) | DType(name, tvars, ctors) -> (let header = (match tvars with | [] -> name | _ -> ((strConcat name) ((strConcat " ") ((joinWith " ") tvars)))) in ((strConcat header) ((strConcat " = ") ((joinWith " | ") ((listMap emitCtor) ctors))))) | DImport(segs) -> ((strConcat "import ") ((joinWith ".") segs)) | DExport(inner) -> ((strConcat "export ") (emitDecl inner)) | DLet(name, body) -> ((strConcat "let ") ((strConcat name) ((strConcat " = ") (emitExpr body)))))

and emitDecls ds =
    ((joinWith "\n\n") ((listMap emitDecl) ds))

and renderModule m =
    (match m with | MkModule(path, decls) -> (match path with | [] -> (emitDecls decls) | _ -> ((strConcat "module ") ((strConcat ((joinWith ".") path)) ((strConcat "\n\n") (emitDecls decls))))))