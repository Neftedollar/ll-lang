module Std.Codegen

open LLLang.Prelude
open Std.Maybe
open Std.Lexer
open Std.Parser
open Std.Render
open Std.Elaborator

let rec joinWith sep items =
    (match items with | [] -> "" | (x :: []) -> x | (x :: rest) -> ((strConcat x) ((strConcat sep) ((joinWith sep) rest))))

and isFsKeyword s =
    (match s with | "abstract" -> true | "and" -> true | "as" -> true | "assert" -> true | "base" -> true | "begin" -> true | "class" -> true | "default" -> true | "delegate" -> true | "do" -> true | "done" -> true | "downcast" -> true | "downto" -> true | "elif" -> true | "else" -> true | "end" -> true | "exception" -> true | "extern" -> true | "false" -> true | "finally" -> true | "for" -> true | "fun" -> true | "function" -> true | "global" -> true | "if" -> true | "in" -> true | "inherit" -> true | "inline" -> true | "interface" -> true | "internal" -> true | "let" -> true | "match" -> true | "member" -> true | "mod" -> true | "module" -> true | "mutable" -> true | "namespace" -> true | "new" -> true | "not" -> true | "null" -> true | "of" -> true | "open" -> true | "or" -> true | "override" -> true | "private" -> true | "public" -> true | "rec" -> true | "return" -> true | "static" -> true | "struct" -> true | "then" -> true | "to" -> true | "true" -> true | "try" -> true | "type" -> true | "upcast" -> true | "use" -> true | "val" -> true | "void" -> true | "when" -> true | "while" -> true | "with" -> true | "yield" -> true | "params" -> true | "object" -> true | "trait" -> true | _ -> false)

and safeIdent s =
    (if (isFsKeyword s) then ((strConcat "__ll_") s) else s)

and encodeStrEscape c =
    (match c with | '\n' -> "\\n" | '\t' -> "\\t" | '\r' -> "\\r" | '\\' -> "\\\\" | '"' -> "\\\"" | _ -> (strFromChars [c]))

and escapeStr s =
    (((listFold (fun acc c -> ((strConcat acc) (encodeStrEscape c)))) "") (strChars s))

and mapOp op =
    (if (op = "==") then "=" else (if (op = "!=") then "<>" else op))

and isTypeParam s =
    (match (strChars s) with | (c :: []) -> (let n = (charToInt c) in (if (n >= 65L) then (if (n <= 90L) then true else false) else false)) | _ -> false)

and emitType t =
    (match t with | TyName("Int") -> "int64" | TyName("Str") -> "string" | TyName("Bool") -> "bool" | TyName("Char") -> "char" | TyName("Float") -> "float" | TyName("Unit") -> "unit" | TyName(n) -> (if (isTypeParam n) then ((strConcat "'") n) else n) | TyApp(TyName("List"), a) -> ((strConcat (emitType a)) " list") | TyApp(f, a) -> (let head = (collectTyAppHead (TyApp (f, a))) in (let args = (collectTyAppArgs (TyApp (f, a))) in (match args with | (_ :: []) -> ((strConcat (emitType a)) ((strConcat " ") (emitType head))) | _ -> (let inner = ((joinWith ", ") ((listMap emitType) args)) in ((strConcat (emitType head)) ((strConcat "<") ((strConcat inner) ">"))))))) | TyFn(a, b) -> ((strConcat (emitType a)) ((strConcat " -> ") (emitType b))))

and collectTyAppHead t =
    (match t with | TyApp(f, _) -> (collectTyAppHead f) | _ -> t)

and collectTyAppArgs t =
    (match t with | TyApp(f, a) -> ((listAppend (collectTyAppArgs f)) [a]) | _ -> [])

and emitPattern p =
    (match p with | PVar(x) -> (safeIdent x) | PWild -> "_" | PNil -> "[]" | PLitInt(n) -> ((strConcat (intToStr n)) "L") | PLitStr(s) -> ((strConcat "\"") ((strConcat (escapeStr s)) "\"")) | PCons(h, t) -> ((strConcat "(") ((strConcat (emitPattern h)) ((strConcat " :: ") ((strConcat (emitPattern t)) ")")))) | PCon(c, args) -> ((emitConPattern c) args))

and emitConPattern c args =
    (match args with | [] -> c | (_ :: []) -> (let inner = (emitPattern (patListHead args)) in ((strConcat c) ((strConcat "(") ((strConcat inner) ")")))) | _ -> (let inner = ((joinWith ", ") ((listMap emitPattern) args)) in ((strConcat c) ((strConcat "(") ((strConcat inner) ")")))))

and patListHead xs =
    (match xs with | (x :: _) -> x | _ -> PWild)

and emitCharLit c =
    (if (c = '\n') then "'\\n'" else (if (c = '\t') then "'\\t'" else (if (c = '\\') then "'\\\\'" else (if (c = '\'') then "'\\''" else ((strConcat ((strConcat "'") (strFromChars [c]))) "'")))))

and emitExpr e =
    (match e with | EInt(n) -> ((strConcat (intToStr n)) "L") | EStr(s) -> ((strConcat "\"") ((strConcat (escapeStr s)) "\"")) | EBool(b) -> (if b then "true" else "false") | EChar(c) -> (emitCharLit c) | EFloat(s) -> s | EVar(x) -> (safeIdent x) | ECon(c) -> c | ENil -> "[]" | EApp(f, a) -> ((emitApp f) a) | EIf(c, t, el) -> ((strConcat "(if ") ((strConcat (emitExpr c)) ((strConcat " then ") ((strConcat (emitExpr t)) ((strConcat " else ") ((strConcat (emitExpr el)) ")")))))) | EMatch(scrut, pats, bodies) -> (let arms = ((emitArms pats) bodies) in ((strConcat "(match ") ((strConcat (emitExpr scrut)) ((strConcat " with ") ((strConcat arms) ")"))))) | ELam(x, body) -> ((strConcat "(fun ") ((strConcat (safeIdent x)) ((strConcat " -> ") ((strConcat (emitExpr body)) ")")))) | ELet(x, e, body) -> ((strConcat "(let ") ((strConcat (safeIdent x)) ((strConcat " = ") ((strConcat (emitExpr e)) ((strConcat " in ") ((strConcat (emitExpr body)) ")")))))) | EList(items) -> ((strConcat "[") ((strConcat ((joinWith "; ") ((listMap emitExpr) items))) "]")) | EBinOp(op, l, r) -> ((strConcat "(") ((strConcat (emitExpr l)) ((strConcat " ") ((strConcat (mapOp op)) ((strConcat " ") ((strConcat (emitExpr r)) ")")))))) | ETuple(a, b) -> ((strConcat "(") ((strConcat (emitExpr a)) ((strConcat ", ") ((strConcat (emitExpr b)) ")")))) | ECons(h, t) -> ((strConcat "(") ((strConcat (emitExpr h)) ((strConcat " :: ") ((strConcat (emitExpr t)) ")")))))

and gatherAppHead e =
    (match e with | EApp(f, _) -> (gatherAppHead f) | _ -> e)

and gatherAppArgs e =
    (match e with | EApp(f, a) -> ((listAppend (gatherAppArgs f)) [a]) | _ -> [])

and isUpperStart s =
    (match (strChars s) with | (c :: _) -> (let n = (charToInt c) in (if (n >= 65L) then (if (n <= 90L) then true else false) else false)) | _ -> false)

and emitApp f a =
    (let head = (gatherAppHead (EApp (f, a))) in (let args = (gatherAppArgs (EApp (f, a))) in (match head with | ECon(c) -> ((emitConApp c) args) | EVar(x) -> (if (isUpperStart x) then ((emitConApp x) args) else ((strConcat "(") ((strConcat (emitExpr f)) ((strConcat " ") ((strConcat (emitExpr a)) ")"))))) | _ -> ((strConcat "(") ((strConcat (emitExpr f)) ((strConcat " ") ((strConcat (emitExpr a)) ")")))))))

and exprListHead xs =
    (match xs with | (x :: _) -> x | _ -> ENil)

and emitConApp c args =
    (match args with | [] -> c | (_ :: []) -> ((strConcat "(") ((strConcat c) ((strConcat " ") ((strConcat (emitExpr (exprListHead args))) ")")))) | _ -> (let inner = ((joinWith ", ") ((listMap emitExpr) args)) in ((strConcat "(") ((strConcat c) ((strConcat " (") ((strConcat inner) "))"))))))

and emitArms pats bodies =
    (match pats with | (p :: prest) -> (match bodies with | (b :: brest) -> (let arm = ((strConcat "| ") ((strConcat (emitPattern p)) ((strConcat " -> ") (emitExpr b)))) in (let rest = ((emitArms prest) brest) in (if ((strLen rest) = 0L) then arm else ((strConcat arm) ((strConcat " ") rest))))) | _ -> "") | _ -> "")

and emitTypeParam s =
    ((strConcat "'") s)

and emitTypeParams tvars =
    (match tvars with | [] -> "" | _ -> (let inner = ((joinWith ", ") ((listMap emitTypeParam) tvars)) in ((strConcat "<") ((strConcat inner) ">"))))

and emitCtorArgs args =
    ((joinWith " * ") ((listMap emitType) args))

and emitCtor c =
    (match c with | MkCon(name, args) -> (match args with | [] -> ((strConcat "    | ") name) | _ -> ((strConcat "    | ") ((strConcat name) ((strConcat " of ") (emitCtorArgs args))))))

and emitCtors cs =
    ((joinWith "\n") ((listMap emitCtor) cs))

and emitParamName p =
    (match p with | MkParam(n, _) -> (safeIdent n))

and emitParamStr ps =
    (match ps with | [] -> "" | _ -> ((strConcat " ") ((joinWith " ") ((listMap emitParamName) ps))))

and emitDecl d =
    (match d with | DType(name, tvars, ctors) -> (let header = ((strConcat "type ") ((strConcat name) (emitTypeParams tvars))) in (let body = (emitCtors ctors) in ((strConcat header) ((strConcat " =\n") body)))) | DFn(name, __ll_params, _, body) -> (let paramStr = (emitParamStr __ll_params) in ((strConcat "let rec ") ((strConcat (safeIdent name)) ((strConcat paramStr) ((strConcat " =\n    ") (emitExpr body)))))) | DLet(name, body) -> ((strConcat "let ") ((strConcat (safeIdent name)) ((strConcat " = ") (emitExpr body)))) | DImport(parts) -> ((strConcat "// import ") ((joinWith ".") parts)) | DExport(inner) -> (emitDecl inner))

and emitDecls ds =
    ((joinWith "\n\n") ((listMap emitDecl) ds))

and emitPrelude =
    "// --- ll-lang stdlib prelude (auto-generated) ---\nlet listLen (xs: 'a list) : int64 = int64 (List.length xs)\nlet listMap f xs = List.map f xs\nlet listFilter p xs = List.filter p xs\nlet listFold f z xs = List.fold f z xs\nlet listReverse xs = List.rev xs\nlet listAppend xs ys = List.append xs ys\nlet listConcat xss = List.concat xss\nlet listIsEmpty xs = List.isEmpty xs\nlet strLen (s: string) : int64 = int64 s.Length\nlet strConcat (a: string) (b: string) = a + b\nlet strChars (s: string) = s |> Seq.toList\nlet strFromChars (cs: char list) = System.String(cs |> List.toArray)\nlet intToStr (n: int64) = string n\nlet charToInt (c: char) = int64 (int c)\nlet printfn (s: string) = System.Console.WriteLine(s)\nlet print (s: string) = System.Console.Write(s)\nlet listHead xs = match xs with [] -> None | x :: _ -> Some x\nlet listTail xs = match xs with [] -> None | _ :: t -> Some t\n// --- end prelude ---"

and emitModulePath parts =
    ((joinWith ".") parts)

and emitModule m =
    (match m with | MkModule(path, decls) -> (let header = ((strConcat "module ") (emitModulePath path)) in (let prelude = emitPrelude in (let body = (emitDecls decls) in ((strConcat header) ((strConcat "\n\n") ((strConcat prelude) ((strConcat "\n\n") body))))))))

and codegenCheck label got expected =
    (if (got = expected) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat "\n  got:      ") ((strConcat got) ((strConcat "\n  expected: ") expected)))))))

and checkContains label got needle =
    (if ((strContains needle) got) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat "\n  missing: ") ((strConcat needle) ((strConcat "\n  in: ") got)))))))

and __test_main_Codegen () =
    (let _ = (((codegenCheck "1 EInt 42") (emitExpr (EInt 42L))) "42L") in (let _ = (((codegenCheck "2 EStr hello") (emitExpr (EStr "hello"))) "\"hello\"") in (let _ = (((codegenCheck "3 EBinOp +") (emitExpr (EBinOp ("+", (EInt 1L), (EInt 2L))))) "(1L + 2L)") in (let _ = (((codegenCheck "4 EApp f x") (emitExpr (EApp ((EVar "f"), (EVar "x"))))) "(f x)") in (let _ = (((codegenCheck "5 EIf") (emitExpr (EIf ((EBool true), (EInt 1L), (EInt 0L))))) "(if true then 1L else 0L)") in (let _ = (((codegenCheck "6 ELet") (emitExpr (ELet ("x", (EInt 1L), (EVar "x"))))) "(let x = 1L in x)") in (let _ = (((codegenCheck "7 PCon Some v") (emitPattern (PCon ("Some", ((PVar "v") :: []))))) "Some(v)") in (let _ = (((codegenCheck "8 TyName Int") (emitType (TyName "Int"))) "int64") in (let _ = (((codegenCheck "9 TyApp List Int") (emitType (TyApp ((TyName "List"), (TyName "Int"))))) "int64 list") in (let colorCtors = ((MkCon ("Red", [])) :: ((MkCon ("Blue", [])) :: [])) in (let colorDecl = (DType ("Color", [], colorCtors)) in (let _ = (((checkContains "10 DType Color") (emitDecl colorDecl)) "type Color") in (let addParams = ((MkParam ("x", (TyName "Int"))) :: ((MkParam ("y", (TyName "Int"))) :: [])) in (let addDecl = (DFn ("add", addParams, None, (EBinOp ("+", (EVar "x"), (EVar "y"))))) in (let _ = (((checkContains "11 DFn add") (emitDecl addDecl)) "let rec add") in (let idParams = ((MkParam ("x", (TyName "Int"))) :: []) in (let modDecls = ((DType ("Color", [], colorCtors)) :: ((DFn ("id", idParams, None, (EVar "x"))) :: [])) in (let m = (MkModule (("Test" :: ("Mod" :: [])), modDecls)) in (let out = (emitModule m) in (let _ = (((checkContains "12 module header") out) "module Test.Mod") in (let _ = (((checkContains "12 module type") out) "type Color") in (let _ = (((checkContains "12 module fn") out) "let rec id") in (let _ = (((codegenCheck "13 PWild") (emitPattern PWild)) "_") in (let _ = (((codegenCheck "14 PNil") (emitPattern PNil)) "[]") in (let _ = (((codegenCheck "15 ELam") (emitExpr (ELam ("x", (EVar "x"))))) "(fun x -> x)") in (let twoItems = ((EInt 1L) :: ((EInt 2L) :: [])) in (let _ = (((codegenCheck "16 EList") (emitExpr (EList twoItems))) "[1L; 2L]") in (let _ = (((codegenCheck "17 ETuple") (emitExpr (ETuple ((EVar "a"), (EVar "b"))))) "(a, b)") in (let _ = (((codegenCheck "18 safeIdent let") (safeIdent "let")) "__ll_let") in (let _ = (((codegenCheck "19 safeIdent foo") (safeIdent "foo")) "foo") in (let _ = (((codegenCheck "20 TyFn") (emitType (TyFn ((TyName "Int"), (TyName "Bool"))))) "int64 -> bool") in 0L)))))))))))))))))))))))))))))))