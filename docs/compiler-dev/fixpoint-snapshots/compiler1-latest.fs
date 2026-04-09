/Users/roman/Documents/dev/tens/code/ll-lang/src/LLLangTool/Program.fs(19,13): warning FS3261: Nullness warning: The types 'string' and 'string | null' do not have compatible nullability. [/Users/roman/Documents/dev/tens/code/ll-lang/src/LLLangTool/LLLangTool.fsproj]
/Users/roman/Documents/dev/tens/code/ll-lang/src/LLLangTool/Program.fs(57,13): warning FS3261: Nullness warning: The types 'Process' and 'Process | null' do not have compatible nullability. [/Users/roman/Documents/dev/tens/code/ll-lang/src/LLLangTool/LLLangTool.fsproj]
/Users/roman/Documents/dev/tens/code/ll-lang/src/LLLangTool/Program.fs(57,13): warning FS3261: Nullness warning: The types 'Process' and 'Process | null' do not have compatible nullability. [/Users/roman/Documents/dev/tens/code/ll-lang/src/LLLangTool/LLLangTool.fsproj]
/Users/roman/Documents/dev/tens/code/ll-lang/src/LLLangTool/Program.fs(59,13): warning FS3261: Nullness warning: The types 'Process' and 'Process | null' do not have compatible nullability. [/Users/roman/Documents/dev/tens/code/ll-lang/src/LLLangTool/LLLangTool.fsproj]
/Users/roman/Documents/dev/tens/code/ll-lang/src/LLLangTool/Program.fs(59,13): warning FS3261: Nullness warning: The types 'Process' and 'Process | null' do not have compatible nullability. [/Users/roman/Documents/dev/tens/code/ll-lang/src/LLLangTool/LLLangTool.fsproj]


/var/folders/_3/c83rr8ys2qq_tb9cgbthkhjm0000gn/T/tmp6Y6ehl.tmp.fsx(830,197): warning FS0026: This rule will never be matched

module Examples.Bootstrap

// --- ll-lang stdlib prelude (auto-generated) ---
let listMap f xs = List.map f xs
let listLen (xs: 'a list) : int64 = int64 (List.length xs)
let strLen (s: string) : int64 = int64 s.Length
let strConcat (a: string) (b: string) = a + b
let print (s: string) = System.Console.Write(s)
// --- end prelude ---

type Maybe<'A> =
    | Some of 'A
    | None

type Token =
    | TKwModule
    | TKwType
    | TKwFn
    | TKwIf
    | TKwThen
    | TKwElse
    | TKwLet
    | TKwIn
    | TKwMatch
    | TKwWith
    | TKwTag
    | TKwImport
    | TKwExport
    | TLower of string
    | TUpper of string
    | TInt of int64
    | TStr of string
    | TChar of Char
    | TLParen
    | TRParen
    | TLBrack
    | TRBrack
    | TEq
    | TEqEq
    | TLt
    | TGt
    | TBar
    | TDot
    | TBackslash
    | TArrow
    | TUnder
    | TColonColon
    | TPlus
    | TMinus
    | TStar
    | TSlash
    | TNewline
    | TEnd

type TypeArg =
    | TAVar of string
    | TACon of string
    | TAApp of string * List<TypeArg>

type Ctor =
    | MkCtor of string * List<TypeArg>

type TypeDecl =
    | MkTypeDecl of string * List<string> * List<Ctor>

type TypeRef =
    | TR of string

type Param =
    | MkParam of string * TypeRef

type Pat =
    | PInt of int64
    | PStr of string
    | PVar of string
    | PWild
    | PNil
    | PCons of Pat * Pat
    | PCon of string * List<Pat>

type Expr =
    | EInt of int64
    | EStr of string
    | EChar of Char
    | EVar of string
    | ETagged of Expr * string
    | EAdd of Expr * Expr
    | ESub of Expr * Expr
    | EMul of Expr * Expr
    | EDiv of Expr * Expr
    | EEq of Expr * Expr
    | ELt of Expr * Expr
    | EGt of Expr * Expr
    | EApp of Expr * Expr
    | ELam of string * Expr
    | ELetIn of string * Expr * Expr
    | ELetInTup2 of string * string * Expr * Expr
    | EIf of Expr * Expr * Expr
    | EMatch of Expr * List<Pat> * List<Expr>
    | ENil
    | ECons of Expr * Expr

type LetDecl =
    | MkLet of string * Expr

type FnDecl =
    | MkFn of string * List<Param> * Maybe<TypeRef> * Expr

type Decl =
    | DType of TypeDecl
    | DLet of LetDecl
    | DFn of FnDecl
    | DTag of string
    | DImport of string
    | DExport of Decl

type Module =
    | MkModule of string * List<Decl>

let rec isUpperChar c = (let n = (charToInt c) in (if (n < 65L) then false else (if (n > 90L) then false else true)))
and isLowerChar c = (let n = (charToInt c) in (if (n < 97L) then false else (if (n > 122L) then false else true)))
and isIdStart c = (if (isUpperChar c) then true else (isLowerChar c))
and isIdCont c = (if (isIdStart c) then true else (charIsDigit c))
and takeIdCont cs = (match cs with | (c :: rest) -> (if (isIdCont c) then ((listAppend (c :: [])) (takeIdCont rest)) else []) | _ -> [])
and dropIdCont cs = (match cs with | (c :: rest) -> (if (isIdCont c) then (dropIdCont rest) else cs) | _ -> [])
and takeDigit cs = (match cs with | (c :: rest) -> (if (charIsDigit c) then ((listAppend (c :: [])) (takeDigit rest)) else []) | _ -> [])
and dropDigit cs = (match cs with | (c :: rest) -> (if (charIsDigit c) then (dropDigit rest) else cs) | _ -> [])
and parseIntStr s = (match (strToInt s) with | (Some n) -> n | (None) -> 0L)
and classifyIdentByHead s cs = (match cs with | (c :: _) -> (if (isUpperChar c) then (TUpper s) else (TLower s)) | _ -> (TLower s))
and classifyIdent s = (match s with | "module" -> TKwModule | "type" -> TKwType | "fn" -> TKwFn | "if" -> TKwIf | "then" -> TKwThen | "else" -> TKwElse | "let" -> TKwLet | "in" -> TKwIn | "match" -> TKwMatch | "with" -> TKwWith | "tag" -> TKwTag | "import" -> TKwImport | "export" -> TKwExport | _ -> ((classifyIdentByHead s) (strChars s)))
and lexId cs = (let idChars = (takeIdCont cs) in (let leftover = (dropIdCont cs) in (let tok = (classifyIdent (strFromChars idChars)) in ((listAppend (tok :: [])) (lexChars leftover)))))
and lexNum cs = (let digits = (takeDigit cs) in (let leftover = (dropDigit cs) in ((listAppend (TInt :: ((parseIntStr (strFromChars digits)) :: []))) (lexChars leftover))))
and takeStrBody cs = (match cs with | (c :: rest) -> (if (c = '"') then ([] rest) else (if (c = '\\') then (takeStrBodyEsc rest) else (let (body, leftover) = (takeStrBody rest) in (c :: (body leftover))))) | _ -> ([] []))
and takeStrBodyEsc cs = (match cs with | (esc :: rest) -> (let (body, leftover) = (takeStrBody rest) in ((decodeEscape esc) :: (body leftover))) | _ -> ([] []))
and lexStr cs = (let (body, leftover) = (takeStrBody cs) in ((listAppend (TStr :: ((strFromChars body) :: []))) (lexChars leftover)))
and decodeEscape c = (if (c = 'n') then '\n' else (if (c = 't') then '\t' else (if (c = '\\') then '\\' else (if (c = '\'') then '\'' else (if (c = '"') then '"' else c)))))
and lexCharEscAfter esc rest cs = (match rest with | (c :: r) -> (if (c = '\'') then ((listAppend (TChar :: ((decodeEscape esc) :: []))) (lexChars r)) else (lexChars cs)) | _ -> (lexChars cs))
and lexCharEsc cs = (match cs with | (esc :: rest) -> (((lexCharEscAfter esc) rest) cs) | _ -> [])
and lexCharLitAfter ch rest cs = (match rest with | (c :: r) -> (if (c = '\'') then ((listAppend (TChar :: (ch :: []))) (lexChars r)) else (lexChars cs)) | _ -> (lexChars cs))
and lexCharLit cs = (match cs with | (ch :: rest) -> (if (ch = '\\') then (lexCharEsc rest) else (((lexCharLitAfter ch) rest) cs)) | _ -> [])
and lexColonOrCons cs = (match cs with | (c :: rest) -> (if (c = ':') then ((listAppend (TColonColon :: [])) (lexChars rest)) else (lexChars cs)) | _ -> (TEnd :: []))
and skipLineComment cs = (match cs with | (c :: rest) -> (if (c = '\n') then ((listAppend (TNewline :: [])) (lexChars rest)) else (skipLineComment rest)) | _ -> (TEnd :: []))
and lexMinusOrArrow cs = (match cs with | (c :: rest) -> (if (c = '>') then ((listAppend (TArrow :: [])) (lexChars rest)) else (if (c = '-') then (skipLineComment rest) else ((listAppend (TMinus :: [])) (lexChars cs)))) | _ -> (TMinus :: []))
and lexEqOrEqEq cs = (match cs with | (c :: rest) -> (if (c = '=') then ((listAppend (TEqEq :: [])) (lexChars rest)) else ((listAppend (TEq :: [])) (lexChars cs))) | _ -> (TEq :: []))
and lexChars cs = (match cs with | (c :: rest) -> (if (c = '\n') then ((listAppend (TNewline :: [])) (lexChars rest)) else (if (charIsSpace c) then (lexChars rest) else (if (isIdStart c) then (lexId cs) else (if (charIsDigit c) then (lexNum cs) else (if (c = '"') then (lexStr rest) else (if (c = '\'') then (lexCharLit rest) else (if (c = '(') then ((listAppend (TLParen :: [])) (lexChars rest)) else (if (c = ')') then ((listAppend (TRParen :: [])) (lexChars rest)) else (if (c = '[') then ((listAppend (TLBrack :: [])) (lexChars rest)) else (if (c = ']') then ((listAppend (TRBrack :: [])) (lexChars rest)) else (if (c = '=') then (lexEqOrEqEq rest) else (if (c = '<') then ((listAppend (TLt :: [])) (lexChars rest)) else (if (c = '>') then ((listAppend (TGt :: [])) (lexChars rest)) else (if (c = '|') then ((listAppend (TBar :: [])) (lexChars rest)) else (if (c = '.') then ((listAppend (TDot :: [])) (lexChars rest)) else (if (c = '\\') then ((listAppend (TBackslash :: [])) (lexChars rest)) else (if (c = '_') then ((listAppend (TUnder :: [])) (lexChars rest)) else (if (c = ':') then (lexColonOrCons rest) else (if (c = '+') then ((listAppend (TPlus :: [])) (lexChars rest)) else (if (c = '-') then (lexMinusOrArrow rest) else (if (c = '*') then ((listAppend (TStar :: [])) (lexChars rest)) else (if (c = '/') then ((listAppend (TSlash :: [])) (lexChars rest)) else (lexChars rest))))))))))))))))))))))) | _ -> (TEnd :: []))
and tokenize src = (lexChars (strChars src))
and skipNewlines toks = (match toks with | ((TNewline) :: rest) -> (skipNewlines rest) | _ -> toks)
and parseModuleNameTail acc toks = (match toks with | ((TDot) :: ((TUpper seg) :: rest)) -> (let acc2 = ((strConcat ((strConcat acc) ".")) seg) in ((parseModuleNameTail acc2) rest)) | _ -> (acc toks))
and parseModuleHeader toks = (match toks with | ((TKwModule) :: ((TUpper head) :: rest)) -> ((parseModuleNameTail head) rest) | _ -> ("?" toks))
and parseModule toks = (let (name, rest) = (parseModuleHeader toks) in (let decls = (parseDecls (skipNewlines rest)) in ((MkModule name) decls)))
and parseDecls toks = (match toks with | ((TKwType) :: _) -> (consTypeDecl (parseTypeDecl toks)) | ((TKwLet) :: _) -> (consLetDecl (parseLetDecl toks)) | ((TKwFn) :: _) -> (consFnDecl (parseFnDecl toks)) | ((TKwTag) :: _) -> (consDecl (parseTagDecl toks)) | ((TKwImport) :: _) -> (consDecl (parseImportDecl toks)) | ((TKwExport) :: rest) -> (let (inner, rest2) = (parseOneDecl rest) in ((DExport inner) :: (parseDecls (skipNewlines rest2)))) | _ -> [])
and consTypeDecl pair = (let (td, rest) = pair in ((DType td) :: (parseDecls (skipNewlines rest))))
and consLetDecl pair = (let (ld, rest) = pair in ((DLet ld) :: (parseDecls (skipNewlines rest))))
and consFnDecl pair = (let (fd, rest) = pair in ((DFn fd) :: (parseDecls (skipNewlines rest))))
and consDecl pair = (let (d, rest) = pair in (d :: (parseDecls (skipNewlines rest))))
and parseOneDecl toks = (match toks with | ((TKwType) :: _) -> (let (td, rest) = (parseTypeDecl toks) in ((DType td) rest)) | ((TKwLet) :: _) -> (let (ld, rest) = (parseLetDecl toks) in ((DLet ld) rest)) | ((TKwFn) :: _) -> (let (fd, rest) = (parseFnDecl toks) in ((DFn fd) rest)) | ((TKwTag) :: _) -> (parseTagDecl toks) | ((TKwImport) :: _) -> (parseImportDecl toks) | _ -> ((DTag "?") toks))
and parseTagDecl toks = (match toks with | ((TKwTag) :: ((TUpper name) :: rest)) -> ((DTag name) rest) | _ -> ((DTag "?") toks))
and parseImportDecl toks = (match toks with | ((TKwImport) :: ((TUpper head) :: rest)) -> (let (path, rest2) = ((parseModuleNameTail head) rest) in ((DImport path) rest2)) | _ -> ((DImport "?") toks))
and parseLetDecl toks = (match toks with | ((TKwLet) :: ((TLower name) :: ((TEq) :: rest))) -> (let rest0 = (skipNewlines rest) in (let (body, rest2) = (parseExpr rest0) in (((MkLet name) body) rest2))) | _ -> (((MkLet "?") (EInt 0L)) toks))
and skipTEqNewlines toks = (match toks with | ((TEq) :: r) -> (skipNewlines r) | _ -> toks)
and parseTypeDecl toks = (match toks with | ((TKwType) :: ((TUpper name) :: rest)) -> (let (prms, rest2) = (parseTypeParams rest) in (let rest3 = (skipTEqNewlines rest2) in (let (ctors, rest4) = (parseCtors rest3) in ((((MkTypeDecl name) prms) ctors) rest4)))) | _ -> ((((MkTypeDecl "?") []) []) toks))
and parseTypeParams toks = (match toks with | ((TUpper s) :: rest) -> (if ((strLen s) = 1L) then (let (ps, rest2) = (parseTypeParams rest) in (s :: (ps rest2))) else ([] toks)) | _ -> ([] toks))
and parseCtors toks = (let toks2 = (skipNewlines toks) in (let toks3 = (match toks2 with | ((TBar) :: r) -> (skipNewlines r) | _ -> toks2) in (let (c, rest) = (parseCtor toks3) in ((parseCtorsTail (c :: [])) rest))))
and parseCtorsTail acc toks = (match (skipNewlines toks) with | ((TBar) :: rest) -> (let rest2 = (skipNewlines rest) in (let (c, rest3) = (parseCtor rest2) in ((parseCtorsTail ((listAppend acc) (c :: []))) rest3))) | _ -> (acc toks))
and parseCtor toks = (match toks with | ((TUpper name) :: rest) -> (let (args, rest2) = (parseTypeArgs rest) in (((MkCtor name) args) rest2)) | _ -> (((MkCtor "?") []) toks))
and parseTypeArgs toks = (match toks with | ((TUpper _) :: _) -> (let (arg, rest) = (parseOneTypeArg toks) in (let (args, rest2) = (parseTypeArgs rest) in (arg :: (args rest2)))) | _ -> ([] toks))
and parseOneTypeArg toks = (match toks with | ((TUpper s) :: rest) -> (let (brackArgs, rest2) = ((parseBrackArgs []) rest) in (if (listIsEmpty brackArgs) then (let arg = (if ((strLen s) = 1L) then (TAVar s) else (TACon s)) in (arg rest2)) else (((TAApp s) brackArgs) rest2))) | _ -> ((TACon "?") toks))
and skipTRBrack toks = (match toks with | ((TRBrack) :: r) -> r | _ -> toks)
and parseBrackArgs acc toks = (match toks with | ((TLBrack) :: rest) -> (let (inner, rest2) = (parseOneTypeArg rest) in (let rest3 = (skipTRBrack rest2) in ((parseBrackArgs ((listAppend acc) (inner :: []))) rest3))) | _ -> (acc toks))
and parseFnDecl toks = (match toks with | ((TKwFn) :: ((TLower name) :: rest)) -> (let (prms, rest2) = (parseParamGroups rest) in (let (retTy, rest3) = (parseReturnType rest2) in (let rest4 = (skipTEqNewlines rest3) in (let (body, rest5) = ((parseFnBody prms) rest4) in (((((MkFn name) prms) retTy) body) rest5))))) | _ -> (((((MkFn "?") []) None) (EInt 0L)) toks))
and parseFnBody prms toks = (let toks2 = (skipNewlines toks) in (match toks2 with | ((TBar) :: _) -> (let (armLists, rest) = (parseArms toks2) in (let (pats, bodies) = armLists in (let scrut = (lastParamVar prms) in ((((EMatch scrut) pats) bodies) rest)))) | _ -> (parseExpr toks2)))
and lastParamVar prms = (match prms with | [] -> (EInt 0L) | ((MkParam n _) :: []) -> (EVar n) | (_ :: rest) -> (lastParamVar rest))
and parseSkipBrackType toks = (match toks with | ((TLBrack) :: ((TUpper _) :: rest)) -> (let rest2 = (parseSkipBrackType rest) in (let rest3 = (match rest2 with | ((TRBrack) :: r) -> r | _ -> rest2) in (parseSkipBrackType rest3))) | _ -> toks)
and parseParamGroups toks = (match toks with | ((TLParen) :: ((TRParen) :: rest)) -> ([] rest) | ((TLParen) :: ((TLower pname) :: ((TUpper tname) :: rest))) -> (let rest2 = (parseSkipBrackType rest) in (let rest3 = (match rest2 with | ((TRParen) :: r) -> r | _ -> rest2) in (let (ps, rest4) = (parseParamGroups rest3) in (((MkParam pname) (TR tname)) :: (ps rest4))))) | ((TLParen) :: ((TLower pname) :: ((TRParen) :: rest))) -> (let (ps, rest2) = (parseParamGroups rest) in (((MkParam pname) (TR "?")) :: (ps rest2))) | _ -> ([] toks))
and parseReturnType toks = (match toks with | ((TUpper tname) :: rest) -> (let rest2 = (parseSkipBrackType rest) in ((Some (TR tname)) rest2)) | _ -> (None toks))
and isAtomStart t = (match t with | (TInt _) -> true | (TStr _) -> true | (TChar _) -> true | (TLower _) -> true | (TUpper _) -> true | (TLParen) -> true | (TLBrack) -> true | _ -> false)
and parseExpr toks = (match toks with | ((TKwIf) :: rest) -> (parseIf rest) | ((TKwMatch) :: rest) -> (parseMatch rest) | ((TKwLet) :: rest) -> (parseLetIn rest) | ((TBackslash) :: rest) -> (parseLam rest) | _ -> (parseCompare toks))
and skipTKwInNewlines toks = (match toks with | ((TKwIn) :: r) -> (skipNewlines r) | _ -> toks)
and parseLetIn toks = (match toks with | ((TLParen) :: ((TLower a) :: ((TLower b) :: ((TRParen) :: ((TEq) :: rest))))) -> (let rest0 = (skipNewlines rest) in (let (e1, rest2) = (parseExpr rest0) in (let rest3 = (skipTKwInNewlines rest2) in (let (e2, rest4) = (parseExpr rest3) in (((((ELetInTup2 a) b) e1) e2) rest4))))) | ((TLower name) :: ((TEq) :: rest)) -> (let rest0 = (skipNewlines rest) in (let (e1, rest2) = (parseExpr rest0) in (let rest3 = (skipTKwInNewlines rest2) in (let (e2, rest4) = (parseExpr rest3) in ((((ELetIn name) e1) e2) rest4))))) | _ -> ((EInt 0L) toks))
and parseLamParams acc toks = (match toks with | ((TLower name) :: rest) -> ((parseLamParams ((listAppend acc) (name :: []))) rest) | _ -> (acc toks))
and wrapLamParams params body = (match params with | (p :: rest) -> ((ELam p) ((wrapLamParams rest) body)) | _ -> body)
and parseLam toks = (let (params, rest) = ((parseLamParams []) toks) in (match rest with | ((TDot) :: rest2) -> (let (body, rest3) = (parseExpr rest2) in (((wrapLamParams params) body) rest3)) | _ -> ((EInt 0L) toks)))
and parseIf toks = (let (c, rest) = (parseExpr toks) in (let rest1 = (skipNewlines rest) in (let rest2 = (match rest1 with | ((TKwThen) :: r) -> (skipNewlines r) | _ -> rest1) in (let (a, rest3) = (parseExpr rest2) in (let rest3a = (skipNewlines rest3) in (let rest4 = (match rest3a with | ((TKwElse) :: r) -> (skipNewlines r) | _ -> rest3a) in (let (b, rest5) = (parseExpr rest4) in ((((EIf c) a) b) rest5))))))))
and parseMatch toks = (let (scrut, rest) = (parseExpr toks) in (let rest2 = (match rest with | ((TKwWith) :: r) -> r | _ -> rest) in (let (armLists, rest3) = (parseArms rest2) in (let (pats, bodies) = armLists in ((((EMatch scrut) pats) bodies) rest3)))))
and parseArms toks = (let toks2 = (skipNewlines toks) in (match toks2 with | ((TBar) :: _) -> (let (armPair, rest) = (parseArm toks2) in (let (p, b) = armPair in (let (moreLists, rest2) = (parseArms rest) in (let (ps, bs) = moreLists in ((p :: ((ps b) :: bs)) rest2))))) | _ -> (([] []) toks2)))
and skipTArrow toks = (match toks with | ((TArrow) :: r) -> r | _ -> toks)
and parseArm toks = (match toks with | ((TBar) :: rest) -> (let (p, rest2) = (parsePat rest) in (let rest3 = (skipTArrow rest2) in (let (body, rest4) = (parseArmBody rest3) in ((p body) rest4)))) | _ -> (((PWild EInt) 0L) toks))
and parseArmBody toks = (let toks2 = (skipNewlines toks) in (match toks2 with | ((TKwIf) :: rest) -> (parseIf rest) | ((TKwLet) :: rest) -> (parseLetIn rest) | _ -> (parseCompare toks2)))
and parsePatArgs toks = (match toks with | ((TLower _) :: _) -> (parsePatArgsCons toks) | ((TUnder) :: _) -> (parsePatArgsCons toks) | ((TInt _) :: _) -> (parsePatArgsCons toks) | ((TLBrack) :: ((TRBrack) :: _)) -> (parsePatArgsCons toks) | _ -> ([] toks))
and parsePatArgsCons toks = (let (p, rest) = (parsePrimaryPat toks) in (let (ps, rest2) = (parsePatArgs rest) in (((listAppend (p :: [])) ps) rest2)))
and parseCtorArgs name toks = (let (args, rest) = (parsePatArgs toks) in (((PCon name) args) rest))
and parsePrimaryPat toks = (match toks with | ((TInt n) :: rest) -> ((PInt n) rest) | ((TStr s) :: rest) -> ((PStr s) rest) | ((TUnder) :: rest) -> (PWild rest) | ((TLBrack) :: ((TRBrack) :: rest)) -> (PNil rest) | ((TLower s) :: rest) -> ((PVar s) rest) | ((TUpper name) :: rest) -> ((parseCtorArgs name) rest) | _ -> (PWild toks))
and parsePat toks = (let (p, rest) = (parsePrimaryPat toks) in (match rest with | ((TColonColon) :: rest2) -> (let (tail, rest3) = (parsePat rest2) in (((PCons p) tail) rest3)) | _ -> (p rest)))
and parseCompare toks = (let (e, rest) = (parseCons toks) in ((parseCompareTail e) rest))
and parseCompareTail lhs toks = (match toks with | ((TEqEq) :: r) -> (let (rhs, rest2) = (parseCons r) in ((parseCompareTail ((EEq lhs) rhs)) rest2)) | ((TLt) :: r) -> (let (rhs, rest2) = (parseCons r) in ((parseCompareTail ((ELt lhs) rhs)) rest2)) | ((TGt) :: r) -> (let (rhs, rest2) = (parseCons r) in ((parseCompareTail ((EGt lhs) rhs)) rest2)) | _ -> (lhs toks))
and parseCons toks = (let (head, rest) = (parseAddSub toks) in (match rest with | ((TColonColon) :: rest2) -> (let (tail, rest3) = (parseCons rest2) in (((ECons head) tail) rest3)) | _ -> (head rest)))
and parseAddSub toks = (let (e, rest) = (parseMulDiv toks) in ((parseAddSubTail e) rest))
and parseAddSubTail lhs toks = (match toks with | ((TPlus) :: rest) -> (let (r, rest2) = (parseMulDiv rest) in ((parseAddSubTail ((EAdd lhs) r)) rest2)) | ((TMinus) :: rest) -> (let (r, rest2) = (parseMulDiv rest) in ((parseAddSubTail ((ESub lhs) r)) rest2)) | _ -> (lhs toks))
and parseMulDiv toks = (let (e, rest) = (parseApp toks) in ((parseMulDivTail e) rest))
and parseMulDivTail lhs toks = (match toks with | ((TStar) :: rest) -> (let (r, rest2) = (parseApp rest) in ((parseMulDivTail ((EMul lhs) r)) rest2)) | ((TSlash) :: rest) -> (let (r, rest2) = (parseApp rest) in ((parseMulDivTail ((EDiv lhs) r)) rest2)) | _ -> (lhs toks))
and parseApp toks = (let (e, rest) = (parseAtom toks) in ((parseAppTail e) rest))
and parseAppTail lhs toks = (match toks with | (t :: _) -> (if (isAtomStart t) then (let (arg, rest) = (parseAtom toks) in ((parseAppTail ((EApp lhs) arg)) rest)) else (lhs toks)) | _ -> (lhs toks))
and parseAtom toks = (match toks with | ((TInt n) :: ((TLBrack) :: ((TUpper ty) :: ((TRBrack) :: rest)))) -> (((ETagged (EInt n)) ty) rest) | ((TStr s) :: ((TLBrack) :: ((TUpper ty) :: ((TRBrack) :: rest)))) -> (((ETagged (EStr s)) ty) rest) | ((TInt n) :: rest) -> ((EInt n) rest) | ((TStr s) :: rest) -> ((EStr s) rest) | ((TChar c) :: rest) -> ((EChar c) rest) | ((TLower s) :: rest) -> ((EVar s) rest) | ((TUpper s) :: rest) -> ((EVar s) rest) | ((TLBrack) :: ((TRBrack) :: rest)) -> (ENil rest) | ((TLBrack) :: rest) -> (parseListLit rest) | ((TLParen) :: rest) -> (let (e, rest2) = (parseExpr rest) in (match rest2 with | ((TRParen) :: rest3) -> (e rest3) | _ -> (e rest2) | _ -> ((EInt 0L) toks))))
and parseListLit toks = (let (e, rest) = (parseAtom toks) in (match rest with | ((TRBrack) :: rest2) -> (((ECons e) ENil) rest2) | _ -> (let (tail, rest3) = (parseListLit rest) in (((ECons e) tail) rest3))))
and showTypeArg a = (match a with | (TAVar s) -> s | (TACon s) -> s | (TAApp head args) -> ((strConcat head) (showBrackArgs args)))
and showBrackArgs args = (((listFold (fun acc -> (fun a -> ((strConcat ((strConcat acc) "[")) ((strConcat (showTypeArg a)) "]"))))) "") args)
and showArgs args = (if (listIsEmpty args) then "" else (let inner = (((listFold (fun acc -> (fun a -> (if ((strLen acc) = 0L) then (showTypeArg a) else ((strConcat ((strConcat acc) ", ")) (showTypeArg a)))))) "") args) in ((strConcat ((strConcat "(") inner)) ")")))
and showCtor c = (match c with | (MkCtor name args) -> ((strConcat name) (showArgs args)))
and showCtors cs = (((listFold (fun acc -> (fun c -> (if ((strLen acc) = 0L) then (showCtor c) else ((strConcat ((strConcat acc) " | ")) (showCtor c)))))) "") cs)
and showTypeParams ps = (((listFold (fun acc -> (fun p -> ((strConcat ((strConcat acc) " (")) ((strConcat p) ")"))))) "") ps)
and showTypeDecl d = (match d with | (MkTypeDecl name prms ctors) -> (let head = ((strConcat ((strConcat "type ") name)) (showTypeParams prms)) in ((strConcat ((strConcat head) " = ")) (showCtors ctors))))
and showTypeRef t = (match t with | (TR s) -> s)
and showParam p = (match p with | (MkParam n t) -> ((strConcat ((strConcat ((strConcat "(") n)) ": ")) ((strConcat (showTypeRef t)) ")")))
and showFnParams ps = (if (listIsEmpty ps) then " ()" else (((listFold (fun acc -> (fun p -> ((strConcat ((strConcat acc) " ")) (showParam p))))) "") ps))
and showReturn r = (match r with | (Some t) -> (showTypeRef t) | (None) -> "?")
and showPat p = (match p with | (PInt n) -> (intToStr n) | (PStr s) -> ((strConcat ((strConcat "\"") s)) "\"") | (PVar s) -> s | (PWild) -> "_" | (PNil) -> "[]" | (PCons h t) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showPat h))) " :: ")) (showPat t))) ")") | (PCon name args) -> ((strConcat "(") ((strConcat name) ((strConcat (showPatArgs args)) ")"))))
and showPatArgs ps = (match ps with | [] -> "" | (p :: rest) -> ((strConcat " ") ((strConcat (showPat p)) (showPatArgs rest))))
and showArm p body = ((strConcat ((strConcat ((strConcat "| ") (showPat p))) " -> ")) (showExpr body))
and showArmsCons p ps bodies = (match bodies with | (b :: bs) -> (let head = ((showArm p) b) in (let tail = ((showArms ps) bs) in (if ((strLen tail) = 0L) then head else ((strConcat ((strConcat head) " ")) tail)))) | _ -> "")
and showArms pats bodies = (match pats with | (p :: ps) -> (((showArmsCons p) ps) bodies) | _ -> "")
and showExpr e = (match e with | (EInt n) -> (intToStr n) | (EStr s) -> ((strConcat ((strConcat "\"") s)) "\"") | (EChar c) -> ((strConcat ((strConcat "'") (strFromChars (c :: [])))) "'") | (EVar s) -> s | (ETagged inner t) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showExpr inner))) "[")) t)) "])") | (EAdd l r) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showExpr l))) " + ")) (showExpr r))) ")") | (ESub l r) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showExpr l))) " - ")) (showExpr r))) ")") | (EMul l r) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showExpr l))) " * ")) (showExpr r))) ")") | (EDiv l r) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showExpr l))) " / ")) (showExpr r))) ")") | (EEq l r) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showExpr l))) " == ")) (showExpr r))) ")") | (ELt l r) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showExpr l))) " < ")) (showExpr r))) ")") | (EGt l r) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showExpr l))) " > ")) (showExpr r))) ")") | (EApp f x) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showExpr f))) " ")) (showExpr x))) ")") | (ELam n body) -> ((strConcat ((strConcat ((strConcat ((strConcat "(fun ") n)) " -> ")) (showExpr body))) ")") | (ELetIn n e1 e2) -> (let p1 = ((strConcat ((strConcat "(let ") n)) " = ") in (let p2 = ((strConcat ((strConcat p1) (showExpr e1))) " in ") in ((strConcat ((strConcat p2) (showExpr e2))) ")"))) | (ELetInTup2 a b e1 e2) -> (let p1 = ((strConcat ((strConcat "(let (") a)) ", ") in (let p2 = ((strConcat ((strConcat p1) b)) ") = ") in (let p3 = ((strConcat ((strConcat p2) (showExpr e1))) " in ") in ((strConcat ((strConcat p3) (showExpr e2))) ")")))) | (EIf c a b) -> (let p1 = ((strConcat "(if ") (showExpr c)) in (let p2 = ((strConcat ((strConcat p1) " then ")) (showExpr a)) in ((strConcat ((strConcat ((strConcat p2) " else ")) (showExpr b))) ")"))) | (EMatch scrut pats bodies) -> (let head = ((strConcat ((strConcat "(match ") (showExpr scrut))) " with ") in ((strConcat ((strConcat head) ((showArms pats) bodies))) ")")) | (ENil) -> "[]" | (ECons h t) -> ((strConcat ((strConcat ((strConcat ((strConcat "(") (showExpr h))) " :: ")) (showExpr t))) ")"))
and showFnDecl d = (match d with | (MkFn name prms ret body) -> (let headPart = ((strConcat ((strConcat "fn ") name)) (showFnParams prms)) in (let arrowPart = ((strConcat ((strConcat headPart) " -> ")) (showReturn ret)) in ((strConcat ((strConcat arrowPart) " = ")) (showExpr body)))))
and showLetDecl d = (match d with | (MkLet name body) -> ((strConcat ((strConcat ((strConcat "let ") name)) " = ")) (showExpr body)))
and showDecl d = (match d with | (DType td) -> (showTypeDecl td) | (DLet ld) -> (showLetDecl ld) | (DFn fd) -> (showFnDecl fd) | (DTag name) -> ((strConcat "tag ") name) | (DImport path) -> ((strConcat "import ") path) | (DExport inner) -> ((strConcat "export ") (showDecl inner)))
and showDecls ds = (((listFold (fun acc -> (fun d -> (if ((strLen acc) = 0L) then (showDecl d) else ((strConcat ((strConcat acc) "\n")) (showDecl d)))))) "") ds)
and showModule m = (match m with | (MkModule name decls) -> (let header = ((strConcat "module ") name) in (if (listIsEmpty decls) then header else ((strConcat ((strConcat header) "\n")) (showDecls decls)))))

type Env =
    | MkEnv of List<string>

let rec envHas env name = (match env with | (MkEnv xs) -> (((listFold (fun acc -> (fun x -> (if acc then true else (if (x = name) then true else false))))) false) xs))
and envAdd env name = (match env with | (MkEnv xs) -> (MkEnv ((listAppend xs) (name :: []))))
and envAddAll env names = (((listFold (fun acc -> (fun n -> ((envAdd acc) n)))) env) names)
and patBinders p = (match p with | (PInt _) -> [] | (PStr _) -> [] | (PWild) -> [] | (PNil) -> [] | (PVar name) -> (name :: []) | (PCons h t) -> ((listAppend (patBinders h)) (patBinders t)) | (PCon _ args) -> (patBindersList args))
and patBindersList ps = (match ps with | [] -> [] | (p :: rest) -> ((listAppend (patBinders p)) (patBindersList rest)))
and paramName p = (match p with | (MkParam n _) -> n)
and paramNames ps = (((listFold (fun acc -> (fun p -> ((listAppend acc) ((paramName p) :: []))))) []) ps)
and checkExpr env e = (match e with | (EInt _) -> [] | (EStr _) -> [] | (EChar _) -> [] | (EVar name) -> (if ((envHas env) name) then [] else (let msg = ((strConcat "E002 UnboundVar ") name) in (msg :: []))) | (ETagged inner _) -> ((checkExpr env) inner) | (EAdd l r) -> ((listAppend ((checkExpr env) l)) ((checkExpr env) r)) | (ESub l r) -> ((listAppend ((checkExpr env) l)) ((checkExpr env) r)) | (EMul l r) -> ((listAppend ((checkExpr env) l)) ((checkExpr env) r)) | (EDiv l r) -> ((listAppend ((checkExpr env) l)) ((checkExpr env) r)) | (EEq l r) -> ((listAppend ((checkExpr env) l)) ((checkExpr env) r)) | (ELt l r) -> ((listAppend ((checkExpr env) l)) ((checkExpr env) r)) | (EGt l r) -> ((listAppend ((checkExpr env) l)) ((checkExpr env) r)) | (EApp f x) -> ((listAppend ((checkExpr env) f)) ((checkExpr env) x)) | (ELam param body) -> (let env2 = ((envAdd env) param) in ((checkExpr env2) body)) | (ELetIn name rhs body) -> (let rhsErrs = ((checkExpr env) rhs) in (let env2 = ((envAdd env) name) in (let bodyErrs = ((checkExpr env2) body) in ((listAppend rhsErrs) bodyErrs)))) | (ELetInTup2 a b rhs body) -> (let rhsErrs = ((checkExpr env) rhs) in (let env2 = ((envAdd env) a) in (let env3 = ((envAdd env2) b) in (let bodyErrs = ((checkExpr env3) body) in ((listAppend rhsErrs) bodyErrs))))) | (EIf cnd thn els) -> (let ce = ((checkExpr env) cnd) in (let te = ((checkExpr env) thn) in (let ee = ((checkExpr env) els) in ((listAppend ((listAppend ce) te)) ee)))) | (EMatch scrut pats bodies) -> (let scrutErrs = ((checkExpr env) scrut) in (let armErrs = (((checkArms env) pats) bodies) in ((listAppend scrutErrs) armErrs))) | (ENil) -> [] | (ECons h t) -> ((listAppend ((checkExpr env) h)) ((checkExpr env) t)))
and checkArms env pats bodies = (match pats with | (p :: ps) -> 0L)
