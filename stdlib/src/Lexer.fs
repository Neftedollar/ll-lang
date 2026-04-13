module Std.Lexer

open LLLang.Prelude
open Std.Maybe

type Token =
    | KwLet
    | KwTag
    | KwUnit
    | KwTrait
    | KwImpl
    | KwImport
    | KwExport
    | KwModule
    | KwIf
    | KwElse
    | KwTrue
    | KwFalse
    | KwMatch
    | Ident of string
    | TypeId of string
    | IntLit of int64
    | FloatLit of string
    | StrLit of string
    | CharLit of char
    | Arrow
    | Backslash
    | Dot
    | Comma
    | Colon
    | ColonColon
    | Eq
    | Bar
    | LBrack
    | RBrack
    | LParen
    | RParen
    | Plus
    | Minus
    | Star
    | Slash
    | Caret
    | Lt
    | Gt
    | Le
    | Ge
    | EqEq
    | Neq
    | Underscore
    | Newline
    | Indent
    | Dedent
    | Eof
    | TError of char

let rec isUpperChar c =
    (let n = (charToInt c) in (if (n < 65L) then false else (if (n > 90L) then false else true)))

and isLowerChar c =
    (let n = (charToInt c) in (if (n < 97L) then false else (if (n > 122L) then false else true)))

and isIdStart c =
    (if (isUpperChar c) then true else (isLowerChar c))

and isIdCont c =
    (if (isIdStart c) then true else (if (charIsDigit c) then true else (c = '_')))

and takeIdCont cs =
    (match cs with | (c :: rest) -> (if (isIdCont c) then ((listAppend [c]) (takeIdCont rest)) else []) | _ -> [])

and dropIdCont cs =
    (match cs with | (c :: rest) -> (if (isIdCont c) then (dropIdCont rest) else cs) | _ -> [])

and takeDigit cs =
    (match cs with | (c :: rest) -> (if (charIsDigit c) then ((listAppend [c]) (takeDigit rest)) else []) | _ -> [])

and dropDigit cs =
    (match cs with | (c :: rest) -> (if (charIsDigit c) then (dropDigit rest) else cs) | _ -> [])

and classifyIdent s =
    (match s with | "let" -> KwLet | "tag" -> KwTag | "unit" -> KwUnit | "trait" -> KwTrait | "impl" -> KwImpl | "import" -> KwImport | "export" -> KwExport | "module" -> KwModule | "if" -> KwIf | "else" -> KwElse | "true" -> KwTrue | "false" -> KwFalse | "match" -> KwMatch | _ -> (let cs = (strChars s) in (match cs with | (c :: _) -> (if (isUpperChar c) then (TypeId s) else (Ident s)) | _ -> (Ident s))))

and parseIntStr s =
    (match (strToInt s) with | Some(n) -> n | None -> 0L)

and lexId cs =
    (let idChars = (takeIdCont cs) in (let leftover = (dropIdCont cs) in (let tok = (classifyIdent (strFromChars idChars)) in ((listAppend [tok]) (lexChars leftover)))))

and lexNum cs =
    (let digits = (takeDigit cs) in (let rest0 = (dropDigit cs) in (match rest0 with | ('.' :: (c2 :: rest1)) -> (if (charIsDigit c2) then (let fracDigits = (takeDigit (c2 :: rest1)) in (let rest2 = (dropDigit (c2 :: rest1)) in (let intPart = (strFromChars digits) in (let fracPart = (strFromChars fracDigits) in (let floatStr = ((strConcat ((strConcat intPart) ".")) fracPart) in ((listAppend [(FloatLit floatStr)]) (lexChars rest2))))))) else (let n = (parseIntStr (strFromChars digits)) in ((listAppend [(IntLit n)]) (lexChars rest0)))) | _ -> (let n = (parseIntStr (strFromChars digits)) in ((listAppend [(IntLit n)]) (lexChars rest0))))))

and takeStrBody cs =
    (match cs with | [] -> ([], []) | (c :: rest) -> (if (c = '"') then ([], rest) else (if (c = '\\') then (takeStrBodyEsc rest) else (let pair = (takeStrBody rest) in (match pair with | (body, leftover) -> ((c :: body), leftover))))))

and takeStrBodyEsc cs =
    (match cs with | [] -> ([], []) | (esc :: rest) -> (let pair = (takeStrBody rest) in (match pair with | (body, leftover) -> (((decodeEscape esc) :: body), leftover))))

and decodeEscape c =
    (match c with | 'n' -> '\n' | 't' -> '\t' | 'r' -> '\r' | '\\' -> '\\' | '\'' -> '\'' | '"' -> '"' | '0' -> '\000' | _ -> c)

and lexStr cs =
    (let pair = (takeStrBody cs) in (match pair with | (body, leftover) -> ((listAppend [(StrLit (strFromChars body))]) (lexChars leftover))))

and lexCharLit cs =
    (match cs with | [] -> [Eof] | (ch :: rest) -> (if (ch = '\\') then (match rest with | [] -> [Eof] | (esc :: rest2) -> (match rest2 with | ('\'' :: rest3) -> ((listAppend [(CharLit (decodeEscape esc))]) (lexChars rest3)) | _ -> (lexChars cs))) else (match rest with | ('\'' :: rest2) -> ((listAppend [(CharLit ch)]) (lexChars rest2)) | _ -> (lexChars cs))))

and skipLineComment cs =
    (match cs with | [] -> [Eof] | (c :: rest) -> (if (c = '\n') then ((listAppend [Newline]) (lexChars rest)) else (skipLineComment rest)))

and lexEqOrEqEq cs =
    (match cs with | ('=' :: rest) -> ((listAppend [EqEq]) (lexChars rest)) | _ -> ((listAppend [Eq]) (lexChars cs)))

and lexNeq cs =
    (match cs with | ('=' :: rest) -> ((listAppend [Neq]) (lexChars rest)) | _ -> (lexChars cs))

and lexLtOrLe cs =
    (match cs with | ('=' :: rest) -> ((listAppend [Le]) (lexChars rest)) | _ -> ((listAppend [Lt]) (lexChars cs)))

and lexGtOrGe cs =
    (match cs with | ('=' :: rest) -> ((listAppend [Ge]) (lexChars rest)) | _ -> ((listAppend [Gt]) (lexChars cs)))

and lexMinusOrArrow cs =
    (match cs with | ('>' :: rest) -> ((listAppend [Arrow]) (lexChars rest)) | ('-' :: rest) -> (skipLineComment rest) | _ -> ((listAppend [Minus]) (lexChars cs)))

and lexColonOrCons cs =
    (match cs with | (':' :: rest) -> ((listAppend [ColonColon]) (lexChars rest)) | _ -> ((listAppend [Colon]) (lexChars cs)))

and lexUnderOrIdent cs =
    (match cs with | (c :: _) -> (if (isIdCont c) then (let idChars = ((listAppend ['_']) (takeIdCont cs)) in (let leftover = (dropIdCont cs) in (let tok = (classifyIdent (strFromChars idChars)) in ((listAppend [tok]) (lexChars leftover))))) else ((listAppend [Underscore]) (lexChars cs))) | _ -> ((listAppend [Underscore]) (lexChars cs)))

and lexChars cs =
    (match cs with | [] -> [Eof] | (c :: rest) -> (if (c = '\n') then ((listAppend [Newline]) (lexChars rest)) else (if (charIsSpace c) then (lexChars rest) else (if (c = '-') then (lexMinusOrArrow rest) else (if (isIdStart c) then (lexId cs) else (if (c = '_') then (lexUnderOrIdent rest) else (if (charIsDigit c) then (lexNum cs) else (if (c = '"') then (lexStr rest) else (if (c = '\'') then (lexCharLit rest) else (if (c = '=') then (lexEqOrEqEq rest) else (if (c = '!') then (lexNeq rest) else (if (c = '<') then (lexLtOrLe rest) else (if (c = '>') then (lexGtOrGe rest) else (if (c = ':') then (lexColonOrCons rest) else (if (c = '(') then ((listAppend [LParen]) (lexChars rest)) else (if (c = ')') then ((listAppend [RParen]) (lexChars rest)) else (if (c = '[') then ((listAppend [LBrack]) (lexChars rest)) else (if (c = ']') then ((listAppend [RBrack]) (lexChars rest)) else (if (c = '|') then ((listAppend [Bar]) (lexChars rest)) else (if (c = '+') then ((listAppend [Plus]) (lexChars rest)) else (if (c = '*') then ((listAppend [Star]) (lexChars rest)) else (if (c = '/') then ((listAppend [Slash]) (lexChars rest)) else (if (c = '^') then ((listAppend [Caret]) (lexChars rest)) else (if (c = '.') then ((listAppend [Dot]) (lexChars rest)) else (if (c = ',') then ((listAppend [Comma]) (lexChars rest)) else (if (c = '\\') then ((listAppend [Backslash]) (lexChars rest)) else ((listAppend [(TError c)]) (lexChars rest))))))))))))))))))))))))))))

and tokenize src =
    (lexChars (strChars src))

and showToken t =
    (match t with | KwLet -> "KwLet" | KwTag -> "KwTag" | KwUnit -> "KwUnit" | KwTrait -> "KwTrait" | KwImpl -> "KwImpl" | KwImport -> "KwImport" | KwExport -> "KwExport" | KwModule -> "KwModule" | KwIf -> "KwIf" | KwElse -> "KwElse" | KwTrue -> "KwTrue" | KwFalse -> "KwFalse" | KwMatch -> "KwMatch" | Ident(s) -> ((strConcat "Ident:") s) | TypeId(s) -> ((strConcat "TypeId:") s) | IntLit(n) -> ((strConcat "Int:") (intToStr n)) | FloatLit(s) -> ((strConcat "Float:") s) | StrLit(s) -> ((strConcat "Str:") s) | CharLit(c) -> ((strConcat "Char:") (strFromChars [c])) | Arrow -> "Arrow" | Backslash -> "Backslash" | Dot -> "Dot" | Comma -> "Comma" | Colon -> "Colon" | ColonColon -> "ColonColon" | Eq -> "Eq" | Bar -> "Bar" | LBrack -> "LBrack" | RBrack -> "RBrack" | LParen -> "LParen" | RParen -> "RParen" | Plus -> "Plus" | Minus -> "Minus" | Star -> "Star" | Slash -> "Slash" | Caret -> "Caret" | Lt -> "Lt" | Gt -> "Gt" | Le -> "Le" | Ge -> "Ge" | EqEq -> "EqEq" | Neq -> "Neq" | Underscore -> "Underscore" | Newline -> "Newline" | Indent -> "Indent" | Dedent -> "Dedent" | Eof -> "Eof" | TError(c) -> ((strConcat "Error:") (strFromChars [c])))

and showTokens ts =
    (((listFold (fun acc t -> (let s = (showToken t) in (if ((strLen acc) = 0L) then s else ((strConcat ((strConcat acc) " ")) s))))) "") ts)

and stripNewlines ts =
    (match ts with | [] -> [] | (Newline :: rest) -> (stripNewlines rest) | (t :: rest) -> (t :: (stripNewlines rest)))

and checkTokens label src expected =
    (let got = (showTokens (stripNewlines (tokenize src))) in (if (got = expected) then (printfn ((strConcat "OK ") label)) else (let p1 = ((strConcat "FAIL ") label) in (let p2 = ((strConcat p1) "\n  expected: ") in (let p3 = ((strConcat p2) expected) in (let p4 = ((strConcat p3) "\n  got:      ") in (let p5 = ((strConcat p4) got) in (printfn p5))))))))

and __test_main_Lexer () =
    (let _ = (((checkTokens "1 keywords") "match if else") "KwMatch KwIf KwElse Eof") in (let _ = (((checkTokens "2 identifiers") "foo Bar") "Ident:foo TypeId:Bar Eof") in (let _ = (((checkTokens "3 integer") "42") "Int:42 Eof") in (let _ = (((checkTokens "4 string") "\"hello\"") "Str:hello Eof") in (let _ = (((checkTokens "5 operators") "== -> |") "EqEq Arrow Bar Eof") in (let _ = (((checkTokens "6 mixed match") "match x | Some v -> v") "KwMatch Ident:x Bar TypeId:Some Ident:v Arrow Ident:v Eof") in (let _ = (((checkTokens "7 comment") "x -- comment") "Ident:x Eof") in (let _ = (((checkTokens "8 float") "3.14") "Float:3.14 Eof") in (let _ = (((checkTokens "9 char literal") "'a'") "Char:a Eof") in (let _ = (((checkTokens "10 all keywords") "let tag unit trait impl import export module if else true false match") "KwLet KwTag KwUnit KwTrait KwImpl KwImport KwExport KwModule KwIf KwElse KwTrue KwFalse KwMatch Eof") in (let _ = (((checkTokens "11 comparison ops") "!= <= >=") "Neq Le Ge Eof") in (let _ = (((checkTokens "12 cons pattern") "x :: xs") "Ident:x ColonColon Ident:xs Eof") in 0L))))))))))))