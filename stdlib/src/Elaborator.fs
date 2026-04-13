module Std.Elaborator

open LLLang.Prelude
open Std.Maybe
open Std.Lexer
open Std.Parser
open Std.Render

type ElabError =
    | MkError of string

type Env =
    | MkEnv of string list

let rec errMsg e =
    (match e with
    | MkError(msg) -> msg)

and emptyEnv =
    (MkEnv [])

and envAdd name env =
    (match env with | MkEnv(xs) -> (MkEnv ((listAppend xs) [name])))

and envAddAll names env =
    (((listFold (fun acc n -> ((envAdd n) acc))) env) names)

and strOrEq target acc x =
    (if acc then true else (x = target))

and envHas name env =
    (match env with | MkEnv(xs) -> (((listFold (fun acc x -> (((strOrEq name) acc) x))) false) xs))

and patBinders p =
    (match p with | PVar(name) -> [name] | PWild -> [] | PNil -> [] | PLitInt(_) -> [] | PLitStr(_) -> [] | PCons(h, t) -> ((listAppend (patBinders h)) (patBinders t)) | PCon(_, args) -> (patBindersList args))

and patBindersList ps =
    (match ps with | [] -> [] | (p :: rest) -> ((listAppend (patBinders p)) (patBindersList rest)))

and paramName p =
    (match p with | MkParam(n, _) -> n)

and paramNames ps =
    ((listMap (fun p -> (paramName p))) ps)

and conName c =
    (match c with | MkCon(n, _) -> n)

and conNames cs =
    ((listMap (fun c -> (conName c))) cs)

and listContains name xs =
    (((listFold (fun acc x -> (((strOrEq name) acc) x))) false) xs)

and findDuplicatesAcc xs seen =
    (match xs with | [] -> [] | (x :: rest) -> (if ((listContains x) seen) then (x :: ((findDuplicatesAcc rest) seen)) else ((findDuplicatesAcc rest) (x :: seen))))

and findDuplicates xs =
    ((findDuplicatesAcc xs) [])

and declName d =
    (match d with | DFn(name, _, _, _) -> (Some name) | DLet(name, _) -> (Some name) | DType(name, _, _) -> (Some name) | DImport(_) -> None | DExport(inner) -> (declName inner))

and addDeclName acc d =
    (match (declName d) with | Some(name) -> ((listAppend acc) [name]) | None -> acc)

and collectDeclNames decls =
    (((listFold (fun acc d -> ((addDeclName acc) d))) []) decls)

and collectDecl d env =
    (match d with | DFn(name, _, _, _) -> ((envAdd name) env) | DLet(name, _) -> ((envAdd name) env) | DType(_, _, ctors) -> ((envAddAll (conNames ctors)) env) | DImport(_) -> env | DExport(inner) -> ((collectDecl inner) env))

and collectDecls decls env =
    (((listFold (fun acc d -> ((collectDecl d) acc))) env) decls)

and checkExpr env e =
    (match e with | EInt(_) -> [] | EStr(_) -> [] | EBool(_) -> [] | EChar(_) -> [] | EFloat(_) -> [] | ENil -> [] | EVar(name) -> (if ((envHas name) env) then [] else [(MkError ((strConcat "Unbound variable: ") name))]) | ECon(name) -> (if ((envHas name) env) then [] else [(MkError ((strConcat "Unbound constructor: ") name))]) | EApp(f, x) -> ((listAppend ((checkExpr env) f)) ((checkExpr env) x)) | EBinOp(_, l, r) -> ((listAppend ((checkExpr env) l)) ((checkExpr env) r)) | EIf(cnd, thn, els) -> ((listAppend ((listAppend ((checkExpr env) cnd)) ((checkExpr env) thn))) ((checkExpr env) els)) | ELam(param, body) -> (let env2 = ((envAdd param) env) in ((checkExpr env2) body)) | ELet(name, rhs, body) -> (let rhsErrs = ((checkExpr env) rhs) in (let env2 = ((envAdd name) env) in (let bodyErrs = ((checkExpr env2) body) in ((listAppend rhsErrs) bodyErrs)))) | EMatch(scrut, pats, bodies) -> (let scrutErrs = ((checkExpr env) scrut) in (let armErrs = (((checkArms env) pats) bodies) in ((listAppend scrutErrs) armErrs))) | EList(items) -> (((listFold (fun acc item -> ((listAppend acc) ((checkExpr env) item)))) []) items) | ECons(h, t) -> ((listAppend ((checkExpr env) h)) ((checkExpr env) t)) | ETuple(a, b) -> ((listAppend ((checkExpr env) a)) ((checkExpr env) b)))

and checkArmsCons env p restPats bodies =
    (match bodies with | [] -> [] | (b :: restBodies) -> (let armEnv = ((envAddAll (patBinders p)) env) in (let armErrs = ((checkExpr armEnv) b) in (let restErrs = (((checkArms env) restPats) restBodies) in ((listAppend armErrs) restErrs)))))

and checkArms env pats bodies =
    (match pats with | [] -> [] | (p :: restPats) -> ((((checkArmsCons env) p) restPats) bodies))

and checkDecl env d =
    (match d with | DFn(_, __ll_params, _, body) -> (let bodyEnv = ((envAddAll (paramNames __ll_params)) env) in ((checkExpr bodyEnv) body)) | DLet(_, body) -> ((checkExpr env) body) | DType(_, _, _) -> [] | DImport(_) -> [] | DExport(inner) -> ((checkDecl env) inner))

and checkDecls env decls =
    (((listFold (fun acc d -> ((listAppend acc) ((checkDecl env) d)))) []) decls)

and makeDupError name =
    (MkError ((strConcat "Duplicate declaration: ") name))

and checkDuplicates decls =
    (let names = (collectDeclNames decls) in (let dups = (findDuplicates names) in ((listMap (fun name -> (makeDupError name))) dups)))

and builtinNames =
    ("abs" :: ("absf" :: ("sqrt" :: ("min" :: ("max" :: ("listLen" :: ("listMap" :: ("listFilter" :: ("listFold" :: ("listReverse" :: ("listAppend" :: ("listConcat" :: ("listIsEmpty" :: ("listHead" :: ("listTail" :: ("listAt" :: ("strLen" :: ("strConcat" :: ("strTrim" :: ("strContains" :: ("strChars" :: ("strFromChars" :: ("strReverse" :: ("strSlice" :: ("strIndexOf" :: ("strSplit" :: ("strToInt" :: ("charToInt" :: ("intToChar" :: ("intToStr" :: ("charIsDigit" :: ("charIsAlpha" :: ("charIsSpace" :: ("print" :: ("printfn" :: ("readFile" :: ("writeFile" :: ("fileExists" :: ("exit" :: ("getArgs" :: ("maybeMap" :: ("maybeBind" :: ("maybeWithDefault" :: ("true" :: ("false" :: [])))))))))))))))))))))))))))))))))))))))))))))

and elaborate m =
    (match m with | MkModule(_, decls) -> (let builtinEnv = ((envAddAll builtinNames) emptyEnv) in (let env = ((collectDecls decls) builtinEnv) in (let dupErrs = (checkDuplicates decls) in (let bodyErrs = ((checkDecls env) decls) in ((listAppend dupErrs) bodyErrs))))))

and assertNoErrors label errs =
    (if (listIsEmpty errs) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) " — unexpected errors"))))

and errContains needle e =
    ((strContains needle) (errMsg e))

and errOrContains needle acc e =
    (if acc then true else ((errContains needle) e))

and hasErrorWith needle errs =
    (((listFold (fun acc e -> (((errOrContains needle) acc) e))) false) errs)

and assertHasError label needle errs =
    (if ((hasErrorWith needle) errs) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat " — expected error: ") needle)))))

and __test_main_Elaborator () =
    (let colorCtors1 = ((MkCon ("Red", [])) :: ((MkCon ("Black", [])) :: [])) in (let decls1 = ((DType ("Color", [], colorCtors1)) :: ((DFn ("id", ((MkParam ("x", (TyName "Int"))) :: []), None, (EVar "x"))) :: [])) in (let m1 = (MkModule (("M" :: []), decls1)) in (let _ = ((assertNoErrors "1 valid program") (elaborate m1)) in (let decls2 = ((DFn ("broken", ((MkParam ("x", (TyName "Int"))) :: []), None, (EVar "y"))) :: []) in (let m2 = (MkModule (("M" :: []), decls2)) in (let _ = (((assertHasError "2 unbound variable") "y") (elaborate m2)) in (let decls3 = ((DFn ("foo", ((MkParam ("x", (TyName "Int"))) :: []), None, (EVar "x"))) :: ((DFn ("foo", ((MkParam ("y", (TyName "Int"))) :: []), None, (EVar "y"))) :: [])) in (let m3 = (MkModule (("M" :: []), decls3)) in (let _ = (((assertHasError "3 duplicate function") "foo") (elaborate m3)) in (let matchExpr = (EMatch ((EVar "x"), ((PCon ("Red", [])) :: ((PCon ("Black", [])) :: [])), ((EInt 1L) :: ((EInt 2L) :: [])))) in (let colorCtors4 = ((MkCon ("Red", [])) :: ((MkCon ("Black", [])) :: [])) in (let decls4 = ((DType ("Color", [], colorCtors4)) :: ((DFn ("describeColor", ((MkParam ("x", (TyName "Color"))) :: []), None, matchExpr)) :: [])) in (let m4 = (MkModule (("M" :: []), decls4)) in (let _ = ((assertNoErrors "4 valid match with constructors") (elaborate m4)) in (let decls5 = ((DFn ("bad", ((MkParam ("x", (TyName "Int"))) :: []), None, (ECon "Nope"))) :: []) in (let m5 = (MkModule (("M" :: []), decls5)) in (let _ = (((assertHasError "5 unbound constructor") "Nope") (elaborate m5)) in (let letExpr = (ELet ("z", (EInt 10L), (EBinOp ("+", (EVar "z"), (EInt 1L))))) in (let decls6 = ((DFn ("useZ", [], None, letExpr)) :: []) in (let m6 = (MkModule (("M" :: []), decls6)) in (let _ = ((assertNoErrors "6 let binding scoping") (elaborate m6)) in 0L))))))))))))))))))))))