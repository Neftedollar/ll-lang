module Std.Parser

open LLLang.Prelude
open Std.Maybe
open Std.Lexer

type Pattern =
    | PVar of string
    | PWild
    | PCon of string * Pattern list
    | PLitInt of int64
    | PLitStr of string
    | PCons of Pattern * Pattern
    | PNil

type Expr =
    | EInt of int64
    | EStr of string
    | EBool of bool
    | EChar of char
    | EFloat of string
    | EVar of string
    | ECon of string
    | EApp of Expr * Expr
    | EIf of Expr * Expr * Expr
    | EMatch of Expr * Pattern list * Expr list
    | ELam of string * Expr
    | ELet of string * Expr * Expr
    | EList of Expr list
    | EBinOp of string * Expr * Expr
    | ETuple of Expr * Expr
    | ENil
    | ECons of Expr * Expr

type TypeExpr =
    | TyName of string
    | TyApp of TypeExpr * TypeExpr
    | TyFn of TypeExpr * TypeExpr

type Param =
    | MkParam of string * TypeExpr

type Constructor =
    | MkCon of string * TypeExpr list

type Decl =
    | DFn of string * Param list * TypeExpr Maybe * Expr
    | DType of string * string list * Constructor list
    | DImport of string list
    | DExport of Decl
    | DLet of string * Expr

type Module =
    | MkModule of string list * Decl list

let rec skipNewlines toks =
    (match toks with | (Newline :: rest) -> (skipNewlines rest) | (Indent :: rest) -> (skipNewlines rest) | (Dedent :: rest) -> (skipNewlines rest) | _ -> toks)

and isAtomStart t =
    (match t with | IntLit(_) -> true | FloatLit(_) -> true | StrLit(_) -> true | CharLit(_) -> true | KwTrue -> true | KwFalse -> true | Ident(_) -> true | TypeId(_) -> true | LParen -> true | LBrack -> true | _ -> false)

and skipBrackTypeArgs toks =
    (match toks with | (LBrack :: rest) -> (let rest2 = (skipBrackTypeBody rest) in (skipBrackTypeArgs rest2)) | _ -> toks)

and skipBrackTypeBody toks =
    (match toks with | (RBrack :: rest) -> rest | (LBrack :: rest) -> (let rest2 = (skipBrackTypeBody rest) in (skipBrackTypeBody rest2)) | (_ :: rest) -> (skipBrackTypeBody rest) | [] -> [])

and parseTypeExpr toks =
    (match toks with | (TypeId(name) :: rest) -> (let rest2 = (skipBrackTypeArgs rest) in ((TyName name), rest2)) | (Ident(name) :: rest) -> ((TyName name), rest) | _ -> ((TyName "?"), toks))

and parseReturnType toks =
    (match toks with | (TypeId(name) :: rest) -> (let rest2 = (skipBrackTypeArgs rest) in ((Some (TyName name)), rest2)) | _ -> (None, toks))

and parseParamGroups toks =
    (match toks with | (LParen :: (RParen :: rest)) -> ([], rest) | (LParen :: (Ident(pname) :: (TypeId(tname) :: rest))) -> (let rest2 = (skipBrackTypeArgs rest) in (let rest3 = (match rest2 with | (RParen :: r) -> r | _ -> rest2) in (let (ps, rest4) = (parseParamGroups rest3) in (((MkParam (pname, (TyName tname))) :: ps), rest4)))) | (LParen :: (Underscore :: (Ident(pname) :: (TypeId(tname) :: rest)))) -> (let rest2 = (skipBrackTypeArgs rest) in (let rest3 = (match rest2 with | (RParen :: r) -> r | _ -> rest2) in (let (ps, rest4) = (parseParamGroups rest3) in (((MkParam (((strConcat "_") pname), (TyName tname))) :: ps), rest4)))) | (LParen :: (Ident(pname) :: (RParen :: rest))) -> (let (ps, rest2) = (parseParamGroups rest) in (((MkParam (pname, (TyName "?"))) :: ps), rest2)) | _ -> ([], toks))

and parseTypeParams toks =
    (match toks with | (TypeId(s) :: rest) -> (if ((strLen s) = 1L) then (let (ps, rest2) = (parseTypeParams rest) in ((s :: ps), rest2)) else ([], toks)) | _ -> ([], toks))

and parseConArgs toks =
    (match toks with | (TypeId(_) :: _) -> (let (arg, rest) = (parseTypeExpr toks) in (let (args, rest2) = (parseConArgs rest) in ((arg :: args), rest2))) | _ -> ([], toks))

and parseCon toks =
    (match toks with | (TypeId(name) :: rest) -> (let (args, rest2) = (parseConArgs rest) in ((MkCon (name, args)), rest2)) | _ -> ((MkCon ("?", [])), toks))

and parseConsTail acc toks =
    (let toks2 = (skipNewlines toks) in (match toks2 with | (Bar :: rest) -> (let rest2 = (skipNewlines rest) in (let (c, rest3) = (parseCon rest2) in ((parseConsTail ((listAppend acc) [c])) rest3))) | _ -> (acc, toks)))

and parseConList toks =
    (let toks2 = (skipNewlines toks) in (let toks3 = (match toks2 with | (Bar :: r) -> (skipNewlines r) | _ -> toks2) in (let (c, rest) = (parseCon toks3) in ((parseConsTail [c]) rest))))

and parseTypeDecl toks =
    (match toks with | (TypeId(name) :: rest) -> (let (prms, rest2) = (parseTypeParams rest) in (let rest3 = (match (skipNewlines rest2) with | (Eq :: r) -> (skipNewlines r) | _ -> (skipNewlines rest2)) in (let (ctors, rest4) = (parseConList rest3) in ((DType (name, prms, ctors)), rest4)))) | _ -> ((DType ("?", [], [])), toks))

and parsePrimaryPat toks =
    (match toks with | (IntLit(n) :: rest) -> ((PLitInt n), rest) | (StrLit(s) :: rest) -> ((PLitStr s), rest) | (Underscore :: rest) -> (PWild, rest) | (LBrack :: (RBrack :: rest)) -> (PNil, rest) | (Ident(s) :: rest) -> ((PVar s), rest) | (TypeId(name) :: rest) -> (let (args, rest2) = (parsePatArgs rest) in ((PCon (name, args)), rest2)) | _ -> (PWild, toks))

and parsePatArgs toks =
    (match toks with | (Ident(_) :: _) -> (parsePatArgsCons toks) | (Underscore :: _) -> (parsePatArgsCons toks) | (IntLit(_) :: _) -> (parsePatArgsCons toks) | (LBrack :: (RBrack :: _)) -> (parsePatArgsCons toks) | _ -> ([], toks))

and parsePatArgsCons toks =
    (let (p, rest) = (parsePrimaryPat toks) in (let (ps, rest2) = (parsePatArgs rest) in (((listAppend [p]) ps), rest2)))

and parsePat toks =
    (let (p, rest) = (parsePrimaryPat toks) in (match rest with | (ColonColon :: rest2) -> (let (tail, rest3) = (parsePat rest2) in ((PCons (p, tail)), rest3)) | _ -> (p, rest)))

and parseArmBody toks =
    (let toks2 = (skipNewlines toks) in (match toks2 with | (KwIf :: rest) -> (parseIf rest) | (KwLet :: rest) -> (parseLetIn rest) | (Ident(_) :: (Eq :: _)) -> (parseLetIn toks2) | _ -> (parseCompare toks2)))

and skipArrow toks =
    (match toks with | (Arrow :: r) -> r | _ -> toks)

and parseArm toks =
    (match toks with | (Bar :: rest) -> (let (p, rest2) = (parsePat rest) in (let rest3 = (skipArrow rest2) in (let (body, rest4) = (parseArmBody rest3) in ((p, body), rest4)))) | _ -> ((PWild, (EInt 0L)), toks))

and parseArms toks =
    (let toks2 = (skipNewlines toks) in (match toks2 with | (Bar :: _) -> (let (pb, rest) = (parseArm toks2) in (let (p, b) = pb in (let (morePB, rest2) = (parseArms rest) in (let (ps, bs) = morePB in (((p :: ps), (b :: bs)), rest2))))) | _ -> (([], []), toks2)))

and parseMatch toks =
    (let (scrut, rest) = (parseExpr toks) in (let (armLists, rest2) = (parseArms rest) in (let (pats, bodies) = armLists in ((EMatch (scrut, pats, bodies)), rest2))))

and parseIf toks =
    (let (cond, rest) = (parseExpr toks) in (let rest2 = (skipNewlines rest) in (let (thenE, rest3) = (parseExpr rest2) in (let rest3a = (skipNewlines rest3) in (let rest4 = (match rest3a with | (KwElse :: r) -> (skipNewlines r) | _ -> rest3a) in (let (elseE, rest5) = (parseExpr rest4) in ((EIf (cond, thenE, elseE)), rest5)))))))

and skipKwIn toks =
    (match toks with | (KwLet :: r) -> (skipNewlines r) | _ -> (skipNewlines toks))

and parseLetIn toks =
    (match toks with | (LParen :: (Ident(a) :: (Comma :: (Ident(b) :: (RParen :: (Eq :: rest)))))) -> (let rest0 = (skipNewlines rest) in (let (e1, rest2) = (parseExpr rest0) in (let rest3 = (skipKwIn rest2) in (let (e2, rest4) = (parseExpr rest3) in ((ELet (a, e1, (ELet (b, (ETuple (e1, e2)), e2)))), rest4))))) | (Ident(name) :: (Eq :: rest)) -> (let rest0 = (skipNewlines rest) in (let (e1, rest2) = (parseExpr rest0) in (let rest3 = (skipKwIn rest2) in (let (e2, rest4) = (parseExpr rest3) in ((ELet (name, e1, e2)), rest4))))) | _ -> ((EInt 0L), toks))

and parseLamParams acc toks =
    (match toks with | (Ident(name) :: rest) -> ((parseLamParams ((listAppend acc) [name])) rest) | _ -> (acc, toks))

and wrapLamParams parms body =
    (match parms with | (p :: rest) -> (ELam (p, ((wrapLamParams rest) body))) | _ -> body)

and parseLam toks =
    (let (parms, rest) = ((parseLamParams []) toks) in (match rest with | (Dot :: rest2) -> (let (body, rest3) = (parseExpr rest2) in (((wrapLamParams parms) body), rest3)) | _ -> ((EInt 0L), toks)))

and parseExpr toks =
    (match toks with | (KwIf :: rest) -> (parseIf rest) | (KwMatch :: rest) -> (parseMatch rest) | (KwLet :: rest) -> (parseLetIn rest) | (Ident(_) :: (Eq :: _)) -> (parseLetIn toks) | (Backslash :: rest) -> (parseLam rest) | _ -> (parseCompare toks))

and parseCompare toks =
    (let (e, rest) = (parseCons toks) in ((parseCompareTail e) rest))

and parseCompareTail lhs toks =
    (match toks with | (EqEq :: r) -> (let (rhs, rest2) = (parseCons r) in ((parseCompareTail (EBinOp ("==", lhs, rhs))) rest2)) | (Neq :: r) -> (let (rhs, rest2) = (parseCons r) in ((parseCompareTail (EBinOp ("!=", lhs, rhs))) rest2)) | (Lt :: r) -> (let (rhs, rest2) = (parseCons r) in ((parseCompareTail (EBinOp ("<", lhs, rhs))) rest2)) | (Gt :: r) -> (let (rhs, rest2) = (parseCons r) in ((parseCompareTail (EBinOp (">", lhs, rhs))) rest2)) | (Le :: r) -> (let (rhs, rest2) = (parseCons r) in ((parseCompareTail (EBinOp ("<=", lhs, rhs))) rest2)) | (Ge :: r) -> (let (rhs, rest2) = (parseCons r) in ((parseCompareTail (EBinOp (">=", lhs, rhs))) rest2)) | _ -> (lhs, toks))

and parseCons toks =
    (let (head, rest) = (parseAddSub toks) in (match rest with | (ColonColon :: rest2) -> (let (tail, rest3) = (parseCons rest2) in ((ECons (head, tail)), rest3)) | _ -> (head, rest)))

and parseAddSub toks =
    (let (e, rest) = (parseMulDiv toks) in ((parseAddSubTail e) rest))

and parseAddSubTail lhs toks =
    (match toks with | (Plus :: rest) -> (let (r, rest2) = (parseMulDiv rest) in ((parseAddSubTail (EBinOp ("+", lhs, r))) rest2)) | (Minus :: rest) -> (let (r, rest2) = (parseMulDiv rest) in ((parseAddSubTail (EBinOp ("-", lhs, r))) rest2)) | _ -> (lhs, toks))

and parseMulDiv toks =
    (let (e, rest) = (parseApp toks) in ((parseMulDivTail e) rest))

and parseMulDivTail lhs toks =
    (match toks with | (Star :: rest) -> (let (r, rest2) = (parseApp rest) in ((parseMulDivTail (EBinOp ("*", lhs, r))) rest2)) | (Slash :: rest) -> (let (r, rest2) = (parseApp rest) in ((parseMulDivTail (EBinOp ("/", lhs, r))) rest2)) | _ -> (lhs, toks))

and parseApp toks =
    (let (e, rest) = (parseAtom toks) in ((parseAppTail e) rest))

and parseAppTail lhs toks =
    (match toks with | (t :: _) -> (if (isAtomStart t) then (let (arg, rest) = (parseAtom toks) in ((parseAppTail (EApp (lhs, arg))) rest)) else (lhs, toks)) | _ -> (lhs, toks))

and parseListLit toks =
    (match toks with | (RBrack :: rest) -> (ENil, rest) | _ -> (let (e, rest) = (parseListElem toks) in (match rest with | (RBrack :: rest2) -> ((ECons (e, ENil)), rest2) | (Comma :: rest2) -> (let (tail, rest3) = (parseListLit rest2) in ((ECons (e, tail)), rest3)) | _ -> (let (tail, rest2) = (parseListLit rest) in ((ECons (e, tail)), rest2)))))

and parseListElem toks =
    (match toks with | (TypeId(_) :: _) -> (parseApp toks) | _ -> (parseAtom toks))

and parseAtomParenTail e toks =
    (match toks with | (Comma :: rest) -> (let rest2 = (skipNewlines rest) in (let (e2, rest3) = (parseExpr rest2) in (match rest3 with | (RParen :: rest4) -> ((ETuple (e, e2)), rest4) | _ -> ((ETuple (e, e2)), rest3)))) | (RParen :: rest) -> (e, rest) | _ -> (e, toks))

and parseAtom toks =
    (match toks with | (IntLit(n) :: rest) -> ((EInt n), rest) | (FloatLit(s) :: rest) -> ((EFloat s), rest) | (StrLit(s) :: rest) -> ((EStr s), rest) | (CharLit(c) :: rest) -> ((EChar c), rest) | (KwTrue :: rest) -> ((EBool true), rest) | (KwFalse :: rest) -> ((EBool false), rest) | (Ident(s) :: rest) -> ((EVar s), rest) | (TypeId(s) :: rest) -> ((ECon s), rest) | (LBrack :: (RBrack :: rest)) -> (ENil, rest) | (LBrack :: rest) -> (parseListLit rest) | (LParen :: rest) -> (let (e, rest2) = (parseExpr rest) in ((parseAtomParenTail e) rest2)) | _ -> ((EInt 0L), toks))

and lastParamVar prms =
    (match prms with | [] -> (EInt 0L) | (MkParam(n, _) :: []) -> (EVar n) | (_ :: rest) -> (lastParamVar rest))

and parseFnBody prms toks =
    (let toks2 = (skipNewlines toks) in (match toks2 with | (Bar :: _) -> (let (armLists, rest) = (parseArms toks2) in (let (pats, bodies) = armLists in (let scrut = (lastParamVar prms) in ((EMatch (scrut, pats, bodies)), rest)))) | _ -> (parseExpr toks2)))

and parseFnDecl toks =
    (match toks with | (Ident(name) :: rest) -> (let (prms, rest2) = (parseParamGroups rest) in (let (retTy, rest3) = (parseReturnType rest2) in (let rest4 = (match (skipNewlines rest3) with | (Eq :: r) -> (skipNewlines r) | _ -> (skipNewlines rest3)) in (let (body, rest5) = ((parseFnBody prms) rest4) in ((DFn (name, prms, retTy, body)), rest5))))) | _ -> ((DFn ("?", [], None, (EInt 0L))), toks))

and parseLetDecl toks =
    (match toks with | (KwLet :: (Ident(name) :: (Eq :: rest))) -> (let rest0 = (skipNewlines rest) in (let (body, rest2) = (parseExpr rest0) in ((DLet (name, body)), rest2))) | _ -> ((DLet ("?", (EInt 0L))), toks))

and parseImportPath acc toks =
    (match toks with | (Dot :: (TypeId(seg) :: rest)) -> ((parseImportPath ((listAppend acc) [seg])) rest) | _ -> (acc, toks))

and parseImportDecl toks =
    (match toks with | (KwImport :: (TypeId(head) :: rest)) -> (let (segs, rest2) = ((parseImportPath [head]) rest) in ((DImport segs), rest2)) | _ -> ((DImport []), toks))

and parseOneDecl toks =
    (match toks with | (TypeId(_) :: _) -> (parseTypeDecl toks) | (KwLet :: _) -> (parseLetDecl toks) | (Ident(_) :: _) -> (parseFnDecl toks) | (KwImport :: _) -> (parseImportDecl toks) | (KwExport :: rest) -> (let (inner, rest2) = (parseOneDecl rest) in ((DExport inner), rest2)) | _ -> ((DLet ("?", (EInt 0L))), toks))

and parseDecls toks =
    (let toks2 = (skipNewlines toks) in (match toks2 with | (Eof :: _) -> [] | [] -> [] | (TypeId(_) :: _) -> (let (d, rest) = (parseTypeDecl toks2) in (d :: (parseDecls rest))) | (KwLet :: _) -> (let (d, rest) = (parseLetDecl toks2) in (d :: (parseDecls rest))) | (Ident(_) :: _) -> (let (d, rest) = (parseFnDecl toks2) in (d :: (parseDecls rest))) | (KwImport :: _) -> (let (d, rest) = (parseImportDecl toks2) in (d :: (parseDecls rest))) | (KwExport :: rest) -> (let (inner, rest2) = (parseOneDecl rest) in ((DExport inner) :: (parseDecls rest2))) | _ -> []))

and parseModulePath acc toks =
    (match toks with | (Dot :: (TypeId(seg) :: rest)) -> ((parseModulePath ((listAppend acc) [seg])) rest) | _ -> (acc, toks))

and parseModuleHeader toks =
    (match toks with | (KwModule :: (TypeId(head) :: rest)) -> (let (segs, rest2) = ((parseModulePath [head]) rest) in (segs, rest2)) | _ -> ([], toks))

and parseModule toks =
    (let toks2 = (skipNewlines toks) in (let (path, rest) = (parseModuleHeader toks2) in (let decls = (parseDecls (skipNewlines rest)) in (MkModule (path, decls)))))

and boolToStr b =
    (if b then "true" else "false")

and showExpr e =
    (match e with | EInt(n) -> ((strConcat "EInt ") (intToStr n)) | EStr(s) -> ((strConcat "EStr ") s) | EBool(b) -> ((strConcat "EBool ") (boolToStr b)) | EChar(c) -> ((strConcat "EChar ") (strFromChars [c])) | EFloat(s) -> ((strConcat "EFloat ") s) | EVar(s) -> ((strConcat "EVar ") s) | ECon(s) -> ((strConcat "ECon ") s) | ENil -> "ENil" | ECons(h, t) -> ((strConcat ((strConcat "ECons(") (showExpr h))) ((strConcat " ") ((strConcat (showExpr t)) ")"))) | EApp(f, x) -> ((strConcat ((strConcat "EApp(") (showExpr f))) ((strConcat " ") ((strConcat (showExpr x)) ")"))) | EIf(c, a, b) -> ((strConcat "EIf(") ((strConcat (showExpr c)) ((strConcat " ") ((strConcat (showExpr a)) ((strConcat " ") ((strConcat (showExpr b)) ")")))))) | EBinOp(op, l, r) -> ((strConcat "EBinOp(") ((strConcat op) ((strConcat " ") ((strConcat (showExpr l)) ((strConcat " ") ((strConcat (showExpr r)) ")")))))) | ETuple(a, b) -> ((strConcat "ETuple(") ((strConcat (showExpr a)) ((strConcat " ") ((strConcat (showExpr b)) ")")))) | ELet(n, e1, e2) -> ((strConcat "ELet ") ((strConcat n) ((strConcat "=(") ((strConcat (showExpr e1)) ((strConcat ") in (") ((strConcat (showExpr e2)) ")")))))) | ELam(n, b) -> ((strConcat "ELam ") ((strConcat n) ((strConcat ".") (showExpr b)))) | EMatch(s, pats, bodies) -> ((strConcat "EMatch(") ((strConcat (showExpr s)) "...)")) | EList(es) -> "EList(...)")

and showPattern p =
    (match p with | PVar(s) -> ((strConcat "PVar ") s) | PWild -> "PWild" | PCon(name, args) -> ((strConcat "PCon ") name) | PLitInt(n) -> ((strConcat "PLitInt ") (intToStr n)) | PLitStr(s) -> ((strConcat "PLitStr ") s) | PCons(h, t) -> ((strConcat "PCons(") ((strConcat (showPattern h)) ((strConcat " ") ((strConcat (showPattern t)) ")")))) | PNil -> "PNil")

and showMaybeTy m =
    (match m with | Some(_) -> "Some" | None -> "None")

and joinDot segs =
    (((listFold (fun acc s -> (if ((strLen acc) = 0L) then s else ((strConcat ((strConcat acc) ".")) s)))) "") segs)

and showDecl d =
    (match d with | DFn(name, prms, retTy, body) -> ((strConcat "DFn ") ((strConcat name) ((strConcat " body=(") ((strConcat (showExpr body)) ")")))) | DType(name, prms, ctors) -> ((strConcat "DType ") name) | DImport(segs) -> (let path = (joinDot segs) in ((strConcat "DImport ") path)) | DExport(inner) -> ((strConcat "DExport(") ((strConcat (showDecl inner)) ")")) | DLet(name, body) -> ((strConcat "DLet ") ((strConcat name) ((strConcat "=(") ((strConcat (showExpr body)) ")")))))

and parserCheckExpr label toks expected =
    (let (e, _rest) = (parseExpr (skipNewlines toks)) in (let got = (showExpr e) in (if (got = expected) then (printfn ((strConcat "OK ") label)) else (let p1 = ((strConcat "FAIL ") label) in (let p2 = ((strConcat p1) "\n  expected: ") in (let p3 = ((strConcat p2) expected) in (let p4 = ((strConcat p3) "\n  got:      ") in (let p5 = ((strConcat p4) got) in (printfn p5)))))))))

and parserCheckDecl label toks expected =
    (let (d, _rest) = (parseOneDecl (skipNewlines toks)) in (let got = (showDecl d) in (if (got = expected) then (printfn ((strConcat "OK ") label)) else (let p1 = ((strConcat "FAIL ") label) in (let p2 = ((strConcat p1) "\n  expected: ") in (let p3 = ((strConcat p2) expected) in (let p4 = ((strConcat p3) "\n  got:      ") in (let p5 = ((strConcat p4) got) in (printfn p5)))))))))

and parserCheckModule label toks expectedDeclCount =
    (let m = (parseModule toks) in (match m with | MkModule(path, decls) -> (let n = (listLen decls) in (if (n = expectedDeclCount) then (printfn ((strConcat "OK ") label)) else (let p1 = ((strConcat "FAIL ") label) in (let p2 = ((strConcat p1) " expected ") in (let p3 = ((strConcat p2) (intToStr expectedDeclCount)) in (let p4 = ((strConcat p3) " decls, got ") in (let p5 = ((strConcat p4) (intToStr n)) in (printfn p5))))))))))