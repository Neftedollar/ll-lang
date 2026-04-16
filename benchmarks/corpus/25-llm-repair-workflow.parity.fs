// FILE: Prelude.fs
module LLLang.Prelude

// --- ll-lang stdlib prelude (auto-generated) ---
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
let floatToStr (f: float) = f.ToString(System.Globalization.CultureInfo.InvariantCulture)
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
let dirList (path: string) : string list = System.IO.Directory.GetFiles(path) |> Array.toList
let exit (code: int64) : unit = System.Environment.Exit(int code)
let listConcat (xss: 'a list list) = List.concat xss
let listIsEmpty (xs: 'a list) = List.isEmpty xs
let getArgs : string list = System.Environment.GetCommandLineArgs() |> Array.toList |> List.tail
let processSpawn (cmd: string) (args: string list) : int64 =
    let psi = System.Diagnostics.ProcessStartInfo(cmd, System.String.Join(" ", List.toArray args))
    psi.UseShellExecute <- false
    let p = System.Diagnostics.Process.Start(psi) in p.WaitForExit(); int64 p.ExitCode
let listHead xs = match xs with [] -> None | x :: _ -> Some x
let listTail xs = match xs with [] -> None | _ :: t -> Some t
let maybeMap f m = match m with Some x -> Some (f x) | None -> None
let maybeBind m f = match m with Some x -> f x | None -> None
let maybeWithDefault d m = match m with Some x -> x | None -> d
let strToInt (s: string) =
    match System.Int64.TryParse(s: string) with
    | true, n -> Some n
    | false, _ -> None
let strToFloat (s: string) =
    match System.Double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
    | true, n -> Some n
    | false, _ -> None
let listAt (xs: 'a list) (i: int64) =
    if int i < 0 || int i >= List.length xs then None else Some (List.item (int i) xs)
// --- end prelude ---// FILE: Maybe.fs
module Std.Maybe

open LLLang.Prelude

type Maybe<'A> = 'A option

let rec isNone m =
    (match m with | Some(_) -> false | None -> true)

and isSome m =
    (match m with | Some(_) -> true | None -> false)

and check label ok =
    (if ok then (let _ = (printfn ((strConcat "OK ") label)) in 0L) else (let _ = (printfn ((strConcat "FAIL ") label)) in 1L))

and doubleInMaybe x =
    (Some (x * 2L))

and maybeCheck label got expected =
    (if (got = expected) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat " expected=") ((strConcat expected) ((strConcat " got=") got)))))))

and boolStr b =
    (if b then "true" else "false")

and maybeIntStr m =
    (match m with | Some(v) -> (intToStr v) | None -> "none")

and __test_main_Maybe =
    (let _ = (((maybeCheck "1 isSome") (boolStr (isSome (Some 1L)))) "true") in (let _ = (((maybeCheck "2 isSome None") (boolStr (isSome None))) "false") in (let _ = (((maybeCheck "3 isNone") (boolStr (isNone None))) "true") in (let _ = (((maybeCheck "4 isNone Some") (boolStr (isNone (Some 5L)))) "false") in (let _ = (((maybeCheck "5 withDefault Some") (maybeIntStr (Some 42L))) "42") in (let _ = (((maybeCheck "6 withDefault None") (maybeIntStr None)) "none") in (printfn "Done")))))))// FILE: Map.fs
module Std.Map

open LLLang.Prelude
open Std.Maybe

type Color =
    | Red
    | Black

type RBMap<'K, 'V> =
    | Leaf
    | Node of Color * RBMap<'K, 'V> * 'K * 'V * RBMap<'K, 'V>

let rec mapEmpty =
    Leaf

and mapSize m =
    (match m with | Leaf -> 0L | Node(_, left, _, _, right) -> ((1L + (mapSize left)) + (mapSize right)))

and balanceLeft left k v right =
    (match left with | Node(Red, ll, lk, lv, lr) -> (match ll with | Node(Red, a, xk, xv, b) -> (Node (Red, (Node (Black, a, xk, xv, b)), lk, lv, (Node (Black, lr, k, v, right)))) | _ -> (match lr with | Node(Red, b, yk, yv, c) -> (Node (Red, (Node (Black, ll, lk, lv, b)), yk, yv, (Node (Black, c, k, v, right)))) | _ -> (Node (Black, left, k, v, right)))) | _ -> (Node (Black, left, k, v, right)))

and balanceRight left k v right =
    (match right with | Node(Red, rl, rk, rv, rr) -> (match rl with | Node(Red, b, yk, yv, c) -> (Node (Red, (Node (Black, left, k, v, b)), yk, yv, (Node (Black, c, rk, rv, rr)))) | _ -> (match rr with | Node(Red, c, zk, zv, d) -> (Node (Red, (Node (Black, left, k, v, rl)), rk, rv, (Node (Black, c, zk, zv, d)))) | _ -> (Node (Black, left, k, v, right)))) | _ -> (Node (Black, left, k, v, right)))

and balance c left k v right =
    (match c with | Red -> (Node (Red, left, k, v, right)) | Black -> (match left with | Node(Red, _, _, _, _) -> (let result = ((((balanceLeft left) k) v) right) in (match result with | Node(Red, _, _, _, _) -> result | _ -> ((((balanceRight left) k) v) right))) | _ -> ((((balanceRight left) k) v) right)))

and ins cmp k v m =
    (match m with | Leaf -> (Node (Red, Leaf, k, v, Leaf)) | Node(c, left, nk, nv, right) -> (let d = ((cmp k) nk) in (if (d < 0L) then (((((balance c) ((((ins cmp) k) v) left)) nk) nv) right) else (if (d > 0L) then (((((balance c) left) nk) nv) ((((ins cmp) k) v) right)) else (Node (c, left, nk, v, right))))))

and mapInsert cmp k v m =
    (let t = ((((ins cmp) k) v) m) in (match t with | Leaf -> Leaf | Node(_, left, nk, nv, right) -> (Node (Black, left, nk, nv, right))))

and mapLookup cmp k m =
    (match m with | Leaf -> None | Node(_, left, nk, nv, right) -> (let d = ((cmp k) nk) in (if (d < 0L) then (((mapLookup cmp) k) left) else (if (d > 0L) then (((mapLookup cmp) k) right) else (Some nv)))))

and mapContains cmp k m =
    (match (((mapLookup cmp) k) m) with | Some(_) -> true | None -> false)

and mapFold f acc m =
    (match m with | Leaf -> acc | Node(_, left, k, v, right) -> (let acc1 = (((mapFold f) acc) left) in (let acc2 = (((f acc1) k) v) in (((mapFold f) acc2) right))))

and mapKeys m =
    (match m with | Leaf -> [] | Node(_, left, k, _, right) -> (let lkeys = (mapKeys left) in (let rkeys = (mapKeys right) in ((listAppend lkeys) (k :: rkeys)))))

and intCmp a b =
    (if (a < b) then (0L - 1L) else (if (a > b) then 1L else 0L))

and strCmp a b =
    (if (a < b) then (0L - 1L) else (if (a > b) then 1L else 0L))

and mapCheck label ok =
    (if ok then (let _ = (printfn ((strConcat "OK ") label)) in 0L) else (let _ = (printfn ((strConcat "FAIL ") label)) in 1L))

and __test_main_Map =
    (let m0 = Leaf in (let _ = ((mapCheck "1 mapEmpty size=0") ((mapSize m0) = 0L)) in (let m1 = ((((mapInsert intCmp) 3L) 30L) m0) in (let m2 = ((((mapInsert intCmp) 1L) 10L) m1) in (let m3 = ((((mapInsert intCmp) 4L) 40L) m2) in (let m4 = ((((mapInsert intCmp) 2L) 20L) m3) in (let m5 = ((((mapInsert intCmp) 5L) 50L) m4) in (let _ = ((mapCheck "2 mapInsert size=5") ((mapSize m5) = 5L)) in (let r3 = (((mapLookup intCmp) 3L) m5) in (let _ = ((mapCheck "3 mapLookup found") (((maybeWithDefault 0L) r3) = 30L)) in (let r4 = (((mapLookup intCmp) 9L) m5) in (let _ = ((mapCheck "4 mapLookup missing") (r4 = None)) in (let _ = ((mapCheck "5 mapContains present") (((mapContains intCmp) 2L) m5)) in (let _ = ((mapCheck "6 mapContains absent") ((((mapContains intCmp) 9L) m5) = false)) in (let sumV = (((mapFold (fun acc k v -> (acc + v))) 0L) m5) in (let _ = ((mapCheck "7 mapFold sum vals") (sumV = 150L)) in (let ks = (mapKeys m5) in (let _ = ((mapCheck "8 mapKeys length") ((listLen ks) = 5L)) in (let sm = ((((mapInsert strCmp) "b") 2L) ((((mapInsert strCmp) "a") 1L) Leaf)) in (let r9 = (((mapLookup strCmp) "a") sm) in (let _ = ((mapCheck "9 strCmp lookup") (((maybeWithDefault 0L) r9) = 1L)) in (let m10 = ((((mapInsert intCmp) 42L) 1L) Leaf) in (let _ = ((mapCheck "10 mapEmpty size=0") ((mapSize Leaf) = 0L)) in (let m11 = ((((mapInsert intCmp) 1L) 99L) m5) in (let r11 = (((mapLookup intCmp) 1L) m11) in (let _ = ((mapCheck "11 duplicate key insert") (((maybeWithDefault 0L) r11) = 99L)) in (let ksSorted = (mapKeys m5) in (let h = (listHead ksSorted) in (let _ = ((mapCheck "12 sorted order") (((maybeWithDefault 0L) h) = 1L)) in (printfn "Done"))))))))))))))))))))))))))))))// FILE: List.fs
module Std.List

open LLLang.Prelude
open Std.Maybe
open Std.Map

let rec listTake n xs =
    (if (n <= 0L) then [] else (match xs with | (h :: t) -> (h :: ((listTake (n - 1L)) t)) | [] -> []))

and listDrop n xs =
    (if (n <= 0L) then xs else (match xs with | (_ :: t) -> ((listDrop (n - 1L)) t) | [] -> []))

and listFlatMap f xs =
    (listConcat ((listMap f) xs))

and listAny p xs =
    (((listFold (fun acc x -> (if acc then true else (p x)))) false) xs)

and listAll p xs =
    (((listFold (fun acc x -> (if acc then (p x) else false))) true) xs)

and listFind p xs =
    (match xs with | (h :: t) -> (if (p h) then (Some h) else ((listFind p) t)) | [] -> None)

and listFindIndexFrom i p xs =
    (match xs with | (h :: t) -> (if (p h) then (Some i) else (((listFindIndexFrom (i + 1L)) p) t)) | [] -> None)

and listFindIndex p xs =
    (((listFindIndexFrom 0L) p) xs)

and listPartition p xs =
    (let foldedPair = (((listFold (fun acc x -> (match acc with | (ys, ns) -> (if (p x) then ((x :: ys), ns) else (ys, (x :: ns)))))) ([], [])) xs) in (match foldedPair with | (ys, ns) -> ((listReverse ys), (listReverse ns))))

and check label ok =
    (if ok then (let _ = (printfn ((strConcat "OK ") label)) in 0L) else (let _ = (printfn ((strConcat "FAIL ") label)) in 1L))

and listCheck label got expected =
    (if (got = expected) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat " expected=") ((strConcat expected) ((strConcat " got=") got)))))))

and boolStr b =
    (if b then "true" else "false")

and showIntList xs =
    (let strs = ((listMap intToStr) xs) in (((listFold (fun acc s -> ((strConcat acc) ((strConcat ",") s)))) "") strs))

and __test_main_List =
    (let _ = (((listCheck "1 listTake 0") (showIntList ((listTake 0L) [1L; 2L; 3L]))) "") in (let _ = (((listCheck "2 listTake 2") (showIntList ((listTake 2L) [1L; 2L; 3L]))) ",1,2") in (let _ = (((listCheck "3 listTake all") (showIntList ((listTake 10L) [1L; 2L; 3L]))) ",1,2,3") in (let _ = (((listCheck "4 listDrop 0") (showIntList ((listDrop 0L) [1L; 2L; 3L]))) ",1,2,3") in (let _ = (((listCheck "5 listDrop 2") (showIntList ((listDrop 2L) [1L; 2L; 3L]))) ",3") in (let _ = (((listCheck "6 listDrop all") (showIntList ((listDrop 10L) [1L; 2L; 3L]))) "") in (let _ = (((listCheck "7 listFlatMap") (showIntList ((listFlatMap (fun x -> [x; x])) [1L; 2L; 3L]))) ",1,1,2,2,3,3") in (let _ = (((listCheck "8 listAny true") (boolStr ((listAny (fun x -> (x > 2L))) [1L; 2L; 3L]))) "true") in (let _ = (((listCheck "9 listAny false") (boolStr ((listAny (fun x -> (x > 5L))) [1L; 2L; 3L]))) "false") in (let _ = (((listCheck "10 listAll true") (boolStr ((listAll (fun x -> (x > 0L))) [1L; 2L; 3L]))) "true") in (let _ = (((listCheck "11 listAll false") (boolStr ((listAll (fun x -> (x > 1L))) [1L; 2L; 3L]))) "false") in (let r12 = ((listFind (fun x -> (x > 2L))) [1L; 2L; 3L]) in (let _ = (((listCheck "12 listFind found") (intToStr ((maybeWithDefault 0L) r12))) "3") in (let r13 = ((listFind (fun x -> (x > 10L))) [1L; 2L; 3L]) in (let _ = (((listCheck "13 listFind none") (intToStr ((maybeWithDefault 0L) r13))) "0") in (let r14 = ((listFindIndex (fun x -> (x = 2L))) [1L; 2L; 3L]) in (let _ = (((listCheck "14 listFindIndex") (intToStr ((maybeWithDefault -1L) r14))) "1") in (let r15 = ((listFindIndex (fun x -> (x = 9L))) [1L; 2L; 3L]) in (let _ = (((listCheck "15 listFindIndex none") (intToStr ((maybeWithDefault -1L) r15))) "-1") in (let (yes, no) = ((listPartition (fun x -> (x > 2L))) [1L; 2L; 3L; 4L]) in (let _ = (((listCheck "16 partition yes") (showIntList yes)) ",3,4") in (let _ = (((listCheck "16 partition no") (showIntList no)) ",1,2") in (printfn "Done")))))))))))))))))))))))// FILE: Lexer.fs
module Std.Lexer

open LLLang.Prelude
open Std.Maybe
open Std.Map
open Std.List

type Token =
    | KwLet
    | KwTag
    | KwUnit
    | KwTrait
    | KwImpl
    | KwImport
    | KwExport
    | KwModule
    | KwExternal
    | KwOpaque
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
    (match s with | "let" -> KwLet | "tag" -> KwTag | "unit" -> KwUnit | "trait" -> KwTrait | "impl" -> KwImpl | "import" -> KwImport | "export" -> KwExport | "module" -> KwModule | "external" -> KwExternal | "opaque" -> KwOpaque | "if" -> KwIf | "else" -> KwElse | "true" -> KwTrue | "false" -> KwFalse | "match" -> KwMatch | _ -> (let cs = (strChars s) in (match cs with | (c :: _) -> (if (isUpperChar c) then (TypeId s) else (Ident s)) | _ -> (Ident s))))

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

and __test_main_Lexer =
    (let _ = (((checkTokens "1 int literal") "42") "Int:42 Eof") in (let _ = (((checkTokens "2 str literal") "\"hi\"") "Str:hi Eof") in (let _ = (((checkTokens "3 true") "true") "KwTrue Eof") in (let _ = (((checkTokens "4 false") "false") "KwFalse Eof") in (let _ = (((checkTokens "5 if") "if") "KwIf Eof") in (let _ = (((checkTokens "6 match") "match") "KwMatch Eof") in (let _ = (((checkTokens "7 ident") "foo") "Ident:foo Eof") in (let _ = (((checkTokens "8 upper ident") "Foo") "TypeId:Foo Eof") in (let _ = (((checkTokens "9 arrow") "->") "Arrow Eof") in (let _ = (((checkTokens "10 backslash") "\\") "Backslash Eof") in (let _ = (((checkTokens "11 pipe") "|") "Bar Eof") in (let _ = (((checkTokens "12 equals") "=") "Eq Eof") in (let _ = (((checkTokens "13 let") "let") "KwLet Eof") in (let _ = (((checkTokens "14 module") "module") "KwModule Eof") in (let _ = (((checkTokens "15 import") "import") "KwImport Eof") in (printfn "Done"))))))))))))))))// FILE: Parser.fs
module Std.Parser

open LLLang.Prelude
open Std.Maybe
open Std.Map
open Std.List
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
    | DImportUrl of string
    | DExport of Decl
    | DLet of string * Expr
    | DExternal of string * Param list * TypeExpr
    | DOpaque of string

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
    (match toks with | (KwImport :: (TypeId(head) :: rest)) -> (let (segs, rest2) = ((parseImportPath [head]) rest) in ((DImport segs), rest2)) | (KwImport :: (StrLit(url) :: rest)) -> ((DImportUrl url), rest) | _ -> ((DImport []), toks))

and parseExternalDecl toks =
    (match toks with | (KwExternal :: (Ident(name) :: rest)) -> (let (prms, rest2) = (parseParamGroups rest) in (let (retTy, rest3) = (parseReturnType rest2) in (let retFinal = (match retTy with | Some(ty) -> ty | None -> (TyName "?")) in ((DExternal (name, prms, retFinal)), rest3)))) | _ -> ((DExternal ("?", [], (TyName "?"))), toks))

and parseOpaqueDecl toks =
    (match toks with | (KwOpaque :: (TypeId(name) :: rest)) -> ((DOpaque name), rest) | _ -> ((DOpaque "?"), toks))

and parseOneDecl toks =
    (match toks with | (TypeId(_) :: _) -> (parseTypeDecl toks) | (KwLet :: _) -> (parseLetDecl toks) | (Ident(_) :: _) -> (parseFnDecl toks) | (KwImport :: _) -> (parseImportDecl toks) | (KwExternal :: _) -> (parseExternalDecl toks) | (KwOpaque :: _) -> (parseOpaqueDecl toks) | (KwExport :: rest) -> (let (inner, rest2) = (parseOneDecl rest) in ((DExport inner), rest2)) | _ -> ((DLet ("?", (EInt 0L))), toks))

and parseDecls toks =
    (let toks2 = (skipNewlines toks) in (match toks2 with | (Eof :: _) -> [] | [] -> [] | (TypeId(_) :: _) -> (let (d, rest) = (parseTypeDecl toks2) in (d :: (parseDecls rest))) | (KwLet :: _) -> (let (d, rest) = (parseLetDecl toks2) in (d :: (parseDecls rest))) | (Ident(_) :: _) -> (let (d, rest) = (parseFnDecl toks2) in (d :: (parseDecls rest))) | (KwImport :: _) -> (let (d, rest) = (parseImportDecl toks2) in (d :: (parseDecls rest))) | (KwExternal :: _) -> (let (d, rest) = (parseExternalDecl toks2) in (d :: (parseDecls rest))) | (KwOpaque :: _) -> (let (d, rest) = (parseOpaqueDecl toks2) in (d :: (parseDecls rest))) | (KwExport :: rest) -> (let (inner, rest2) = (parseOneDecl rest) in ((DExport inner) :: (parseDecls rest2))) | _ -> []))

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
    (match d with | DFn(name, prms, retTy, body) -> ((strConcat "DFn ") ((strConcat name) ((strConcat " body=(") ((strConcat (showExpr body)) ")")))) | DType(name, prms, ctors) -> ((strConcat "DType ") name) | DImport(segs) -> (let path = (joinDot segs) in ((strConcat "DImport ") path)) | DImportUrl(url) -> ((strConcat "DImportUrl ") url) | DExport(inner) -> ((strConcat "DExport(") ((strConcat (showDecl inner)) ")")) | DLet(name, body) -> ((strConcat "DLet ") ((strConcat name) ((strConcat "=(") ((strConcat (showExpr body)) ")")))) | DExternal(name, _prms, _retTy) -> ((strConcat "DExternal ") name) | DOpaque(name) -> ((strConcat "DOpaque ") name))

and parserCheckExpr label toks expected =
    (let (e, _rest) = (parseExpr (skipNewlines toks)) in (let got = (showExpr e) in (if (got = expected) then (printfn ((strConcat "OK ") label)) else (let p1 = ((strConcat "FAIL ") label) in (let p2 = ((strConcat p1) "\n  expected: ") in (let p3 = ((strConcat p2) expected) in (let p4 = ((strConcat p3) "\n  got:      ") in (let p5 = ((strConcat p4) got) in (printfn p5)))))))))

and parserCheckDecl label toks expected =
    (let (d, _rest) = (parseOneDecl (skipNewlines toks)) in (let got = (showDecl d) in (if (got = expected) then (printfn ((strConcat "OK ") label)) else (let p1 = ((strConcat "FAIL ") label) in (let p2 = ((strConcat p1) "\n  expected: ") in (let p3 = ((strConcat p2) expected) in (let p4 = ((strConcat p3) "\n  got:      ") in (let p5 = ((strConcat p4) got) in (printfn p5)))))))))

and parserCheckModule label toks expectedDeclCount =
    (let m = (parseModule toks) in (match m with | MkModule(path, decls) -> (let n = (listLen decls) in (if (n = expectedDeclCount) then (printfn ((strConcat "OK ") label)) else (let p1 = ((strConcat "FAIL ") label) in (let p2 = ((strConcat p1) " expected ") in (let p3 = ((strConcat p2) (intToStr expectedDeclCount)) in (let p4 = ((strConcat p3) " decls, got ") in (let p5 = ((strConcat p4) (intToStr n)) in (printfn p5))))))))))

and __test_main_Parser =
    (let _ = (((parserCheckDecl "1 external fn") (KwExternal :: ((Ident "httpGet") :: (LParen :: ((Ident "url") :: ((TypeId "Str") :: (RParen :: ((TypeId "Str") :: (Eof :: []))))))))) "DExternal httpGet") in (let _ = (((parserCheckDecl "2 external no args") (KwExternal :: ((Ident "ping") :: (LParen :: (RParen :: ((TypeId "Bool") :: (Eof :: []))))))) "DExternal ping") in (let _ = (((parserCheckDecl "3 opaque") (KwOpaque :: ((TypeId "HttpClient") :: (Eof :: [])))) "DOpaque HttpClient") in (let _ = (((parserCheckDecl "4 opaque2") (KwOpaque :: ((TypeId "Promise") :: (Eof :: [])))) "DOpaque Promise") in (let toks5 = (KwModule :: ((TypeId "M") :: (Newline :: (KwExternal :: ((Ident "connect") :: (LParen :: ((Ident "url") :: ((TypeId "Str") :: (RParen :: ((TypeId "Bool") :: (Newline :: (KwOpaque :: ((TypeId "Conn") :: (Eof :: [])))))))))))))) in (let _ = (((parserCheckModule "5 module 2 decls") toks5) 2L) in (let _ = (((parserCheckExpr "6 EInt") ((IntLit 42L) :: (Eof :: []))) "EInt 42") in (let _ = (((parserCheckExpr "7 EStr") ((StrLit "hi") :: (Eof :: []))) "EStr hi") in (let _ = (((parserCheckExpr "8 EBool true") (KwTrue :: (Eof :: []))) "EBool true") in (let _ = (((parserCheckExpr "9 EBool false") (KwFalse :: (Eof :: []))) "EBool false") in (let _ = (((parserCheckExpr "10 EVar") ((Ident "x") :: (Eof :: []))) "EVar x") in (let _ = (((parserCheckExpr "11 ECon") ((TypeId "None") :: (Eof :: []))) "ECon None") in (let _ = (((parserCheckExpr "12 ENil") (LBrack :: (RBrack :: (Eof :: [])))) "ENil") in (let _ = (((parserCheckExpr "13 ELam") (Backslash :: ((Ident "x") :: (Dot :: ((Ident "x") :: (Eof :: [])))))) "ELam x.EVar x") in (let _ = (((parserCheckExpr "14 EBinOp") ((IntLit 1L) :: (Plus :: ((IntLit 2L) :: (Eof :: []))))) "EBinOp(+ EInt 1 EInt 2)") in (let _ = (((parserCheckExpr "15 EApp") ((Ident "f") :: ((Ident "x") :: (Eof :: [])))) "EApp(EVar f EVar x)") in (let _ = (((parserCheckDecl "16 DImport") (KwImport :: ((TypeId "Std") :: (Dot :: ((TypeId "Map") :: (Eof :: [])))))) "DImport Std.Map") in (let _ = (((parserCheckDecl "17 value binding") ((Ident "x") :: (Eq :: ((IntLit 42L) :: (Eof :: []))))) "DFn x body=(EInt 42)") in (let toks18 = ((Ident "add") :: (LParen :: ((Ident "a") :: ((TypeId "Int") :: (RParen :: (LParen :: ((Ident "b") :: ((TypeId "Int") :: (RParen :: (Eq :: ((Ident "a") :: (Plus :: ((Ident "b") :: (Eof :: [])))))))))))))) in (let _ = (((parserCheckDecl "18 DFn") toks18) "DFn add body=(EBinOp(+ EVar a EVar b))") in (printfn "Done")))))))))))))))))))))// FILE: Elaborator.fs
module Std.Elaborator

open LLLang.Prelude
open Std.Maybe
open Std.Map
open Std.List
open Std.Lexer
open Std.Parser

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
    (match d with | DFn(name, _, _, _) -> (Some name) | DLet(name, _) -> (Some name) | DType(name, _, _) -> (Some name) | DImport(_) -> None | DExport(inner) -> (declName inner) | DExternal(name, _, _) -> (Some name) | DOpaque(_) -> None)

and addDeclName acc d =
    (match (declName d) with | Some(name) -> ((listAppend acc) [name]) | None -> acc)

and collectDeclNames decls =
    (((listFold (fun acc d -> ((addDeclName acc) d))) []) decls)

and collectDecl d env =
    (match d with | DFn(name, _, _, _) -> ((envAdd name) env) | DLet(name, _) -> ((envAdd name) env) | DType(_, _, ctors) -> ((envAddAll (conNames ctors)) env) | DImport(_) -> env | DExport(inner) -> ((collectDecl inner) env) | DExternal(name, _, _) -> ((envAdd name) env) | DOpaque(_) -> env)

and collectDecls decls env =
    (((listFold (fun acc d -> ((collectDecl d) acc))) env) decls)

and checkExpr env e =
    (match e with | EInt(_) -> [] | EStr(_) -> [] | EBool(_) -> [] | EChar(_) -> [] | EFloat(_) -> [] | ENil -> [] | EVar(name) -> (if ((envHas name) env) then [] else [(MkError ((strConcat "Unbound variable: ") name))]) | ECon(name) -> (if ((envHas name) env) then [] else [(MkError ((strConcat "Unbound constructor: ") name))]) | EApp(f, x) -> ((listAppend ((checkExpr env) f)) ((checkExpr env) x)) | EBinOp(_, l, r) -> ((listAppend ((checkExpr env) l)) ((checkExpr env) r)) | EIf(cnd, thn, els) -> ((listAppend ((listAppend ((checkExpr env) cnd)) ((checkExpr env) thn))) ((checkExpr env) els)) | ELam(param, body) -> (let env2 = ((envAdd param) env) in ((checkExpr env2) body)) | ELet(name, rhs, body) -> (let rhsErrs = ((checkExpr env) rhs) in (let env2 = ((envAdd name) env) in (let bodyErrs = ((checkExpr env2) body) in ((listAppend rhsErrs) bodyErrs)))) | EMatch(scrut, pats, bodies) -> (let scrutErrs = ((checkExpr env) scrut) in (let armErrs = (((checkArms env) pats) bodies) in ((listAppend scrutErrs) armErrs))) | EList(items) -> (((listFold (fun acc item -> ((listAppend acc) ((checkExpr env) item)))) []) items) | ECons(h, t) -> ((listAppend ((checkExpr env) h)) ((checkExpr env) t)) | ETuple(a, b) -> ((listAppend ((checkExpr env) a)) ((checkExpr env) b)))

and checkArmsCons env p restPats bodies =
    (match bodies with | [] -> [] | (b :: restBodies) -> (let armEnv = ((envAddAll (patBinders p)) env) in (let armErrs = ((checkExpr armEnv) b) in (let restErrs = (((checkArms env) restPats) restBodies) in ((listAppend armErrs) restErrs)))))

and checkArms env pats bodies =
    (match pats with | [] -> [] | (p :: restPats) -> ((((checkArmsCons env) p) restPats) bodies))

and checkDecl env d =
    (match d with | DFn(_, __ll_params, _, body) -> (let bodyEnv = ((envAddAll (paramNames __ll_params)) env) in ((checkExpr bodyEnv) body)) | DLet(_, body) -> ((checkExpr env) body) | DType(_, _, _) -> [] | DImport(_) -> [] | DExport(inner) -> ((checkDecl env) inner) | DExternal(_, _, _) -> [] | DOpaque(_) -> [])

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

and __test_main_Elaborator =
    (let colorCtors1 = ((MkCon ("Red", [])) :: ((MkCon ("Black", [])) :: [])) in (let decls1 = ((DType ("Color", [], colorCtors1)) :: ((DFn ("id", ((MkParam ("x", (TyName "Int"))) :: []), None, (EVar "x"))) :: [])) in (let m1 = (MkModule (("M" :: []), decls1)) in (let _ = ((assertNoErrors "1 valid program") (elaborate m1)) in (let decls2 = ((DFn ("broken", ((MkParam ("x", (TyName "Int"))) :: []), None, (EVar "y"))) :: []) in (let m2 = (MkModule (("M" :: []), decls2)) in (let _ = (((assertHasError "2 unbound variable") "y") (elaborate m2)) in (let decls3 = ((DFn ("foo", ((MkParam ("x", (TyName "Int"))) :: []), None, (EVar "x"))) :: ((DFn ("foo", ((MkParam ("y", (TyName "Int"))) :: []), None, (EVar "y"))) :: [])) in (let m3 = (MkModule (("M" :: []), decls3)) in (let _ = (((assertHasError "3 duplicate function") "foo") (elaborate m3)) in (let matchExpr = (EMatch ((EVar "x"), ((PCon ("Red", [])) :: ((PCon ("Black", [])) :: [])), ((EInt 1L) :: ((EInt 2L) :: [])))) in (let colorCtors4 = ((MkCon ("Red", [])) :: ((MkCon ("Black", [])) :: [])) in (let decls4 = ((DType ("Color", [], colorCtors4)) :: ((DFn ("describeColor", ((MkParam ("x", (TyName "Color"))) :: []), None, matchExpr)) :: [])) in (let m4 = (MkModule (("M" :: []), decls4)) in (let _ = ((assertNoErrors "4 valid match with constructors") (elaborate m4)) in (let decls5 = ((DFn ("bad", ((MkParam ("x", (TyName "Int"))) :: []), None, (ECon "Nope"))) :: []) in (let m5 = (MkModule (("M" :: []), decls5)) in (let _ = (((assertHasError "5 unbound constructor") "Nope") (elaborate m5)) in (let letExpr = (ELet ("z", (EInt 10L), (EBinOp ("+", (EVar "z"), (EInt 1L))))) in (let decls6 = ((DFn ("useZ", [], None, letExpr)) :: []) in (let m6 = (MkModule (("M" :: []), decls6)) in (let _ = ((assertNoErrors "6 let binding scoping") (elaborate m6)) in (let decls7 = ((DExternal ("httpGet", ((MkParam ("url", (TyName "Str"))) :: []), (TyName "Str"))) :: ((DFn ("fetch", ((MkParam ("u", (TyName "Str"))) :: []), None, (EApp ((EVar "httpGet"), (EVar "u"))))) :: [])) in (let m7 = (MkModule (("M" :: []), decls7)) in (let _ = ((assertNoErrors "7 external decl registers name") (elaborate m7)) in (let decls8 = ((DOpaque "HttpClient") :: ((DExternal ("createClient", [], (TyName "HttpClient"))) :: [])) in (let m8 = (MkModule (("M" :: []), decls8)) in (let _ = ((assertNoErrors "8 opaque type and external") (elaborate m8)) in 0L))))))))))))))))))))))))))))// FILE: CompilerTypes.fs
module Std.CompilerTypes

open LLLang.Prelude
open Std.Maybe
open Std.Map
open Std.List
open Std.Lexer
open Std.Parser
open Std.Elaborator

type InferResult<'A> =
    | InferOk of 'A
    | InferErr of string

type Scheme =
    | MkScheme of string list * TypeExpr

type Fresh =
    | MkFresh of int64

let rec isFlex name =
    (if ((strLen name) > 0L) then ((((strSlice name) 0L) 1L) = "$") else false)

and freshVarName n =
    ((strConcat "$") (intToStr n))

and schemeMono t =
    (MkScheme ([], t))

and schemeVars s =
    (match s with | MkScheme(vs, _) -> vs)

and schemeBody s =
    (match s with | MkScheme(_, body) -> body)

and substEmpty =
    Leaf

and substInsert v t s =
    ((((mapInsert strCmp) v) t) s)

and substLookup v s =
    (((mapLookup strCmp) v) s)

and substRemove v s =
    (((mapFold (fun acc nk nv -> (if (((strCmp v) nk) = 0L) then acc else ((((mapInsert strCmp) nk) nv) acc)))) Leaf) s)

and applyType s t =
    (match t with | TyName(v) -> (if (isFlex v) then (match ((substLookup v) s) with | Some(t2) -> ((applyType s) t2) | None -> t) else t) | TyApp(a, b) -> (TyApp (((applyType s) a), ((applyType s) b))) | TyFn(a, b) -> (TyFn (((applyType s) a), ((applyType s) b))))

and applyScheme s sch =
    (match sch with | MkScheme(vs, body) -> (let s2 = (((listFold (fun acc v -> ((substRemove v) acc))) s) vs) in (MkScheme (vs, ((applyType s2) body)))))

and applyEnv s env =
    (((mapFold (fun acc k sch -> ((((mapInsert strCmp) k) ((applyScheme s) sch)) acc))) Leaf) env)

and substCompose s1 s2 =
    (let s2Applied = (((mapFold (fun acc k t -> ((((mapInsert strCmp) k) ((applyType s1) t)) acc))) Leaf) s2) in (((mapFold (fun acc k v -> (match ((substLookup k) acc) with | None -> ((((mapInsert strCmp) k) v) acc) | Some(_) -> acc))) s2Applied) s1))

and ftvTypeList t =
    (match t with | TyName(v) -> (if (isFlex v) then [v] else []) | TyApp(a, b) -> (strNub ((listAppend (ftvTypeList a)) (ftvTypeList b))) | TyFn(a, b) -> (strNub ((listAppend (ftvTypeList a)) (ftvTypeList b))))

and ftvSchemeList sch =
    (match sch with | MkScheme(vs, body) -> ((listFilter (fun v -> ((listAll (fun q -> (v <> q))) vs))) (ftvTypeList body)))

and ftvEnvList env =
    (strNub (((mapFold (fun acc k sch -> ((listAppend acc) (ftvSchemeList sch)))) []) env))

and strNub xs =
    (((listFold (fun acc x -> (if ((listAny (fun y -> (x = y))) acc) then acc else ((listAppend acc) [x])))) []) xs)

and freshInit =
    (MkFresh 0L)

and freshNext f =
    (match f with | MkFresh(n) -> ((TyName (freshVarName n)), (MkFresh (n + 1L))))

and generalize env t =
    (let envFtv = (ftvEnvList env) in (let toQuant = ((listFilter (fun v -> ((listAll (fun e -> (v <> e))) envFtv))) (ftvTypeList t)) in (MkScheme (toQuant, t))))

and instantiateWith f vs body =
    (match vs with | [] -> (body, f) | (v :: rest) -> (let (tv, f2) = (freshNext f) in (let s = (((substInsert v) tv) substEmpty) in (let body2 = ((applyType s) body) in (((instantiateWith f2) rest) body2)))))

and instantiate f sch =
    (match sch with | MkScheme(vs, body) -> (((instantiateWith f) vs) body))

and unify t1 t2 =
    (match t1 with | TyName(a) -> (match t2 with | TyName(b) -> (if (a = b) then (InferOk substEmpty) else (if (isFlex a) then ((bindVar a) t2) else (if (isFlex b) then ((bindVar b) t1) else (InferErr ((unifyErrMsg t1) t2))))) | _ -> (if (isFlex a) then ((bindVar a) t2) else (InferErr ((unifyErrMsg t1) t2)))) | TyApp(a1, b1) -> (match t2 with | TyApp(a2, b2) -> (match ((unify a1) a2) with | InferErr(e) -> (InferErr e) | InferOk(s1) -> (match ((unify ((applyType s1) b1)) ((applyType s1) b2)) with | InferErr(e) -> (InferErr e) | InferOk(s2) -> (InferOk ((substCompose s2) s1)))) | TyName(v) -> (if (isFlex v) then ((bindVar v) t1) else (InferErr ((unifyErrMsg t1) t2))) | _ -> (InferErr ((unifyErrMsg t1) t2))) | TyFn(a1, b1) -> (match t2 with | TyFn(a2, b2) -> (match ((unify a1) a2) with | InferErr(e) -> (InferErr e) | InferOk(s1) -> (match ((unify ((applyType s1) b1)) ((applyType s1) b2)) with | InferErr(e) -> (InferErr e) | InferOk(s2) -> (InferOk ((substCompose s2) s1)))) | TyName(v) -> (if (isFlex v) then ((bindVar v) t1) else (InferErr ((unifyErrMsg t1) t2))) | _ -> (InferErr ((unifyErrMsg t1) t2))))

and unifyErrMsg t1 t2 =
    ((strConcat "Cannot unify ") ((strConcat (renderType t1)) ((strConcat " with ") (renderType t2))))

and bindVar v t =
    (match t with | TyName(w) -> (if (v = w) then (InferOk substEmpty) else (InferOk (((substInsert v) t) substEmpty))) | _ -> (if ((occursIn v) t) then (InferErr ((strConcat "Infinite type: ") ((strConcat v) ((strConcat " in ") (renderType t))))) else (InferOk (((substInsert v) t) substEmpty))))

and occursIn v t =
    ((listAny (fun w -> (v = w))) (ftvTypeList t))

and renderType t =
    (match t with | TyName(n) -> n | TyApp(f, a) -> (let aStr = (match a with | TyApp(_, _) -> ((strConcat "(") ((strConcat (renderType a)) ")")) | TyFn(_, _) -> ((strConcat "(") ((strConcat (renderType a)) ")")) | _ -> (renderType a)) in ((strConcat (renderType f)) ((strConcat " ") aStr))) | TyFn(a, b) -> (let aStr = (match a with | TyFn(_, _) -> ((strConcat "(") ((strConcat (renderType a)) ")")) | _ -> (renderType a)) in ((strConcat aStr) ((strConcat " -> ") (renderType b)))))

and typeEnvEmpty =
    Leaf

and typeEnvInsert name sch env =
    ((((mapInsert strCmp) name) sch) env)

and typeEnvLookup name env =
    (((mapLookup strCmp) name) env)

and typeEnvApply s env =
    ((applyEnv s) env)

and extractBound v r =
    (match r with | InferOk(s) -> (match ((substLookup v) s) with | Some(t) -> (renderType t) | None -> "none") | InferErr(e) -> e)

and typesCheck label got expected =
    (if (got = expected) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat " expected=") ((strConcat expected) ((strConcat " got=") got)))))))

and boolStr b =
    (if b then "true" else "false")

and strJoin xs =
    (match xs with | [] -> "" | (x :: []) -> x | (x :: rest) -> ((strConcat x) ((strConcat " ") (strJoin rest))))

and __test_main_CompilerTypes =
    (let _ = (((typesCheck "1 isFlex $0=true") (boolStr (isFlex "$0"))) "true") in (let _ = (((typesCheck "2 isFlex Int=false") (boolStr (isFlex "Int"))) "false") in (let _ = (((typesCheck "3 isFlex a=false") (boolStr (isFlex "a"))) "false") in (let _ = (((typesCheck "4 freshVarName 0") (freshVarName 0L)) "$0") in (let _ = (((typesCheck "5 freshVarName 42") (freshVarName 42L)) "$42") in (let _ = (((typesCheck "6 renderType Name") (renderType (TyName "Int"))) "Int") in (let _ = (((typesCheck "7 renderType Fn") (renderType (TyFn ((TyName "Int"), (TyName "Str"))))) "Int -> Str") in (let _ = (((typesCheck "8 renderType App") (renderType (TyApp ((TyName "Maybe"), (TyName "Int"))))) "Maybe Int") in (let s0 = substEmpty in (let _ = (((typesCheck "9 applyType no-op") (renderType ((applyType s0) (TyName "Int")))) "Int") in (let s1 = (((substInsert "$0") (TyName "Str")) substEmpty) in (let _ = (((typesCheck "10 applyType flex->Str") (renderType ((applyType s1) (TyName "$0")))) "Str") in (let _ = (((typesCheck "11 applyType rigid") (renderType ((applyType s1) (TyName "a")))) "a") in (let _ = (((typesCheck "12 applyType TyFn") (renderType ((applyType s1) (TyFn ((TyName "$0"), (TyName "Int")))))) "Str -> Int") in (let _ = (((typesCheck "13 unify same") (match ((unify (TyName "Int")) (TyName "Int")) with | InferOk(_) -> "ok" | InferErr(_) -> "err")) "ok") in (let _ = (((typesCheck "14 unify mismatch") (match ((unify (TyName "Int")) (TyName "Str")) with | InferOk(_) -> "ok" | InferErr(_) -> "err")) "err") in (let u3 = ((unify (TyName "$0")) (TyName "Int")) in (let _ = (((typesCheck "15 unify flex=Int") ((extractBound "$0") u3)) "Int") in (let u4 = ((unify (TyFn ((TyName "$0"), (TyName "$1")))) (TyFn ((TyName "Int"), (TyName "Str")))) in (let _ = (((typesCheck "16 unify TyFn") ((extractBound "$0") u4)) "Int") in (let sch1 = ((generalize Leaf) (TyFn ((TyName "$0"), (TyName "$0")))) in (let _ = (((typesCheck "17 generalize") (strJoin (schemeVars sch1))) "$0") in (let sch2 = (MkScheme (["$0"], (TyFn ((TyName "$0"), (TyName "$0"))))) in (let (t5, _) = ((instantiate (MkFresh 1L)) sch2) in (let _ = (((typesCheck "18 instantiate") (renderType t5)) "$1 -> $1") in (let _ = (((typesCheck "19 occursIn true") (boolStr ((occursIn "$0") (TyApp ((TyName "List"), (TyName "$0")))))) "true") in (let _ = (((typesCheck "20 occursIn false") (boolStr ((occursIn "$0") (TyApp ((TyName "List"), (TyName "$1")))))) "false") in (printfn "Done"))))))))))))))))))))))))))))// FILE: Codegen.fs
module Std.Codegen

open LLLang.Prelude
open Std.Maybe
open Std.Map
open Std.List
open Std.Lexer
open Std.Parser
open Std.Elaborator
open Std.CompilerTypes

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
    (match d with | DType(name, tvars, ctors) -> (let header = ((strConcat "type ") ((strConcat name) (emitTypeParams tvars))) in (let body = (emitCtors ctors) in ((strConcat header) ((strConcat " =\n") body)))) | DFn(name, __ll_params, _, body) -> (let paramStr = (emitParamStr __ll_params) in ((strConcat "let rec ") ((strConcat (safeIdent name)) ((strConcat paramStr) ((strConcat " =\n    ") (emitExpr body)))))) | DLet(name, body) -> ((strConcat "let ") ((strConcat (safeIdent name)) ((strConcat " = ") (emitExpr body)))) | DImport(parts) -> ((strConcat "// import ") ((joinWith ".") parts)) | DExport(inner) -> (emitDecl inner) | DExternal(name, __ll_params, retTy) -> (let paramStr = (emitParamStr __ll_params) in ((strConcat "let ") ((strConcat (safeIdent name)) ((strConcat paramStr) ((strConcat " = failwith \"external: ") ((strConcat name) "\"")))))) | DOpaque(name) -> ((strConcat "type ") ((strConcat name) " = obj")))

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

and __test_main_Codegen =
    (let _ = (((codegenCheck "1 EInt 42") (emitExpr (EInt 42L))) "42L") in (let _ = (((codegenCheck "2 EStr hello") (emitExpr (EStr "hello"))) "\"hello\"") in (let _ = (((codegenCheck "3 EBinOp +") (emitExpr (EBinOp ("+", (EInt 1L), (EInt 2L))))) "(1L + 2L)") in (let _ = (((codegenCheck "4 EApp f x") (emitExpr (EApp ((EVar "f"), (EVar "x"))))) "(f x)") in (let _ = (((codegenCheck "5 EIf") (emitExpr (EIf ((EBool true), (EInt 1L), (EInt 0L))))) "(if true then 1L else 0L)") in (let _ = (((codegenCheck "6 ELet") (emitExpr (ELet ("x", (EInt 1L), (EVar "x"))))) "(let x = 1L in x)") in (let _ = (((codegenCheck "7 PCon Some v") (emitPattern (PCon ("Some", ((PVar "v") :: []))))) "Some(v)") in (let _ = (((codegenCheck "8 TyName Int") (emitType (TyName "Int"))) "int64") in (let _ = (((codegenCheck "9 TyApp List Int") (emitType (TyApp ((TyName "List"), (TyName "Int"))))) "int64 list") in (let colorCtors = ((MkCon ("Red", [])) :: ((MkCon ("Blue", [])) :: [])) in (let colorDecl = (DType ("Color", [], colorCtors)) in (let _ = (((checkContains "10 DType Color") (emitDecl colorDecl)) "type Color") in (let addParams = ((MkParam ("x", (TyName "Int"))) :: ((MkParam ("y", (TyName "Int"))) :: [])) in (let addDecl = (DFn ("add", addParams, None, (EBinOp ("+", (EVar "x"), (EVar "y"))))) in (let _ = (((checkContains "11 DFn add") (emitDecl addDecl)) "let rec add") in (let idParams = ((MkParam ("x", (TyName "Int"))) :: []) in (let modDecls = ((DType ("Color", [], colorCtors)) :: ((DFn ("id", idParams, None, (EVar "x"))) :: [])) in (let m = (MkModule (("Test" :: ("Mod" :: [])), modDecls)) in (let out = (emitModule m) in (let _ = (((checkContains "12 module header") out) "module Test.Mod") in (let _ = (((checkContains "12 module type") out) "type Color") in (let _ = (((checkContains "12 module fn") out) "let rec id") in (let _ = (((codegenCheck "13 PWild") (emitPattern PWild)) "_") in (let _ = (((codegenCheck "14 PNil") (emitPattern PNil)) "[]") in (let _ = (((codegenCheck "15 ELam") (emitExpr (ELam ("x", (EVar "x"))))) "(fun x -> x)") in (let twoItems = ((EInt 1L) :: ((EInt 2L) :: [])) in (let _ = (((codegenCheck "16 EList") (emitExpr (EList twoItems))) "[1L; 2L]") in (let _ = (((codegenCheck "17 ETuple") (emitExpr (ETuple ((EVar "a"), (EVar "b"))))) "(a, b)") in (let _ = (((codegenCheck "18 safeIdent let") (safeIdent "let")) "__ll_let") in (let _ = (((codegenCheck "19 safeIdent foo") (safeIdent "foo")) "foo") in (let _ = (((codegenCheck "20 TyFn") (emitType (TyFn ((TyName "Int"), (TyName "Bool"))))) "int64 -> bool") in (let extDecl = (DExternal ("httpGet", ((MkParam ("url", (TyName "Str"))) :: []), (TyName "Str"))) in (let _ = (((checkContains "21 DExternal stub") (emitDecl extDecl)) "failwith \"external: httpGet\"") in (let _ = (((codegenCheck "22 DOpaque obj alias") (emitDecl (DOpaque "HttpClient"))) "type HttpClient = obj") in 0L))))))))))))))))))))))))))))))))))// FILE: CompilerInfer.fs
module Std.CompilerInfer

open LLLang.Prelude
open Std.Maybe
open Std.Map
open Std.List
open Std.Lexer
open Std.Parser
open Std.Elaborator
open Std.CompilerTypes
open Std.Codegen

type InferState =
    | MkInferState of RBMap<string, Scheme> * Fresh * string list * RBMap<string, TypeExpr>

let rec inferEnv st =
    (match st with | MkInferState(env, _, _, _) -> env)

and inferFresh st =
    (match st with | MkInferState(_, f, _, _) -> f)

and inferErrors st =
    (match st with | MkInferState(_, _, errs, _) -> errs)

and inferSubst st =
    (match st with | MkInferState(_, _, _, s) -> s)

and inferAddErr msg st =
    (match st with | MkInferState(env, f, errs, s) -> (MkInferState (env, f, ((listAppend errs) [msg]), s)))

and inferWithEnv env st =
    (match st with | MkInferState(_, f, errs, s) -> (MkInferState (env, f, errs, s)))

and inferWithFresh f st =
    (match st with | MkInferState(env, _, errs, s) -> (MkInferState (env, f, errs, s)))

and inferWithSubst s2 st =
    (match st with | MkInferState(env, f, errs, _) -> (MkInferState (env, f, errs, s2)))

and inferFreshVar st =
    (let f = (inferFresh st) in (let (tv, f2) = (freshNext f) in (tv, ((inferWithFresh f2) st))))

and tyInt =
    (TyName "Int")

and tyStr =
    (TyName "Str")

and tyBool =
    (TyName "Bool")

and tyChar =
    (TyName "Char")

and tyUnit =
    (TyName "Unit")

and tyList a =
    (TyApp ((TyName "List"), a))

and baseScheme t =
    (schemeMono t)

and tyArith =
    (TyFn (tyInt, (TyFn (tyInt, tyInt))))

and tyCmp =
    (TyFn (tyInt, (TyFn (tyInt, tyBool))))

and tyStrEq =
    (TyFn (tyStr, (TyFn (tyStr, tyBool))))

and baseEnv =
    (let e0 = typeEnvEmpty in (let e1 = (((typeEnvInsert "+") (baseScheme tyArith)) e0) in (let e2 = (((typeEnvInsert "-") (baseScheme tyArith)) e1) in (let e3 = (((typeEnvInsert "*") (baseScheme tyArith)) e2) in (let e4 = (((typeEnvInsert "/") (baseScheme tyArith)) e3) in (let e5 = (((typeEnvInsert "==") (baseScheme tyCmp)) e4) in (let e6 = (((typeEnvInsert "!=") (baseScheme tyCmp)) e5) in (let e7 = (((typeEnvInsert "<") (baseScheme tyCmp)) e6) in (let e8 = (((typeEnvInsert ">") (baseScheme tyCmp)) e7) in (let e9 = (((typeEnvInsert "<=") (baseScheme tyCmp)) e8) in (let e10 = (((typeEnvInsert ">=") (baseScheme tyCmp)) e9) in (let e11 = (((typeEnvInsert "printfn") (baseScheme (TyFn (tyStr, tyUnit)))) e10) in (let e12 = (((typeEnvInsert "intToStr") (baseScheme (TyFn (tyInt, tyStr)))) e11) in (let e13 = (((typeEnvInsert "strConcat") (baseScheme (TyFn (tyStr, (TyFn (tyStr, tyStr)))))) e12) in (let e14 = (((typeEnvInsert "strLen") (baseScheme (TyFn (tyStr, tyInt)))) e13) in e14)))))))))))))))

and inferPattern st pat =
    (match pat with | PVar(name) -> (let (tv, st2) = (inferFreshVar st) in ([(name, (schemeMono tv))], tv, st2)) | PWild -> (let (tv, st2) = (inferFreshVar st) in ([], tv, st2)) | PCon(ctorName, args) -> (let ctorSchemeOpt = ((typeEnvLookup ctorName) (inferEnv st)) in (match ctorSchemeOpt with | None -> (let (tv, st2) = (inferFreshVar st) in (let st3 = ((inferAddErr ((strConcat "E002 UnboundVar ") ctorName)) st2) in ([], tv, st3))) | Some(sch) -> (let (ctorTy, st2) = ((inferInstantiate st) sch) in (let (bindings, st3) = ((inferPatternArgs st2) args) in (let retTy = ((ctorResultType ctorTy) (listLen args)) in (bindings, retTy, st3)))))) | PLitInt(_) -> ([], tyInt, st) | PLitStr(_) -> ([], tyStr, st) | PCons(h, t) -> (let (hbinds, hTy, st2) = ((inferPattern st) h) in (let (tbinds, tTy, st3) = ((inferPattern st2) t) in (let st4 = ((((inferUnify st3) tTy) (tyList hTy)) "pattern cons") in (((listAppend hbinds) tbinds), (tyList hTy), st4)))) | PNil -> (let (tv, st2) = (inferFreshVar st) in ([], (tyList tv), st2)))

and inferPatternArgs st args =
    (match args with | [] -> ([], st) | (p :: rest) -> (let (binds, _, st2) = ((inferPattern st) p) in (let (restBinds, st3) = ((inferPatternArgs st2) rest) in (((listAppend binds) restBinds), st3))))

and ctorResultType ty n =
    (if (n <= 0L) then ty else (match ty with | TyFn(_, ret) -> ((ctorResultType ret) (n - 1L)) | _ -> ty))

and inferInstantiate st sch =
    (let f = (inferFresh st) in (let (ty, f2) = ((instantiate f) sch) in (ty, ((inferWithFresh f2) st))))

and inferUnify st t1 t2 ctx =
    (let t1a = ((applyType (inferSubst st)) t1) in (let t2a = ((applyType (inferSubst st)) t2) in (match ((unify t1a) t2a) with | InferOk(s) -> (let s2 = ((substCompose s) (inferSubst st)) in (let env2 = ((typeEnvApply s) (inferEnv st)) in (let st2 = ((inferWithEnv env2) st) in ((inferWithSubst s2) st2)))) | InferErr(msg) -> ((inferAddErr ((strConcat ctx) ((strConcat ": ") msg))) st))))

and inferExpr st expr =
    (match expr with | EInt(_) -> (tyInt, st) | EStr(_) -> (tyStr, st) | EBool(_) -> (tyBool, st) | EChar(_) -> (tyChar, st) | EFloat(_) -> ((TyName "Float"), st) | ENil -> (let (tv, st2) = (inferFreshVar st) in ((tyList tv), st2)) | EVar(name) -> (let schOpt = ((typeEnvLookup name) (inferEnv st)) in (match schOpt with | None -> (let (tv, st2) = (inferFreshVar st) in (let st3 = ((inferAddErr ((strConcat "E002 UnboundVar ") name)) st2) in (tv, st3))) | Some(sch) -> (let (ty, st2) = ((inferInstantiate st) sch) in (ty, st2)))) | ECon(name) -> (let schOpt = ((typeEnvLookup name) (inferEnv st)) in (match schOpt with | None -> (let (tv, st2) = (inferFreshVar st) in (let st3 = ((inferAddErr ((strConcat "E002 UnboundCon ") name)) st2) in (tv, st3))) | Some(sch) -> (let (ty, st2) = ((inferInstantiate st) sch) in (ty, st2)))) | EApp(f, a) -> (let (fTy, st2) = ((inferExpr st) f) in (let (aTy, st3) = ((inferExpr st2) a) in (let (retTy, st4) = (inferFreshVar st3) in (let st5 = ((((inferUnify st4) fTy) (TyFn (aTy, retTy))) "application") in (let retTy2 = ((applyType (inferEnvSubst st5)) retTy) in (retTy2, st5)))))) | ELam(param, body) -> (let (paramTy, st2) = (inferFreshVar st) in (let env2 = (((typeEnvInsert param) (schemeMono paramTy)) (inferEnv st2)) in (let st3 = ((inferWithEnv env2) st2) in (let (bodyTy, st4) = ((inferExpr st3) body) in (let paramTy2 = ((applyType (inferEnvSubst st4)) paramTy) in ((TyFn (paramTy2, bodyTy)), st4)))))) | ELet(name, __ll_val, body) -> (let (valTy, st2) = ((inferExpr st) __ll_val) in (let sch = ((generalize (inferEnv st2)) valTy) in (let env2 = (((typeEnvInsert name) sch) (inferEnv st2)) in (let st3 = ((inferWithEnv env2) st2) in ((inferExpr st3) body))))) | EIf(cond, then_, else_) -> (let (condTy, st2) = ((inferExpr st) cond) in (let st3 = ((((inferUnify st2) condTy) tyBool) "if condition") in (let (thenTy, st4) = ((inferExpr st3) then_) in (let (elseTy, st5) = ((inferExpr st4) else_) in (let st6 = ((((inferUnify st5) thenTy) elseTy) "if branches") in (let thenTy2 = ((applyType (inferEnvSubst st6)) thenTy) in (thenTy2, st6))))))) | EMatch(scrut, pats, bodies) -> (let (scrutTy, st2) = ((inferExpr st) scrut) in (let (tv, st3) = (inferFreshVar st2) in (let st4 = (((((inferMatchArms st3) scrutTy) tv) pats) bodies) in (let resultTy = ((applyType (inferEnvSubst st4)) tv) in (resultTy, st4))))) | EBinOp(op, l, r) -> (let (opTy, st2) = ((inferExpr st) (EVar op)) in (let (lTy, st3) = ((inferExpr st2) l) in (let (rTy, st4) = ((inferExpr st3) r) in (let (retTy, st5) = (inferFreshVar st4) in (let st6 = ((((inferUnify st5) opTy) (TyFn (lTy, (TyFn (rTy, retTy))))) "binary op") in (let retTy2 = ((applyType (inferEnvSubst st6)) retTy) in (retTy2, st6))))))) | ETuple(a, b) -> (let (aTy, st2) = ((inferExpr st) a) in (let (bTy, st3) = ((inferExpr st2) b) in ((TyApp ((TyApp ((TyName "Tuple"), aTy)), bTy)), st3))) | EList(elems) -> (let (tv, st2) = (inferFreshVar st) in (let st3 = (((listFold (fun acc e -> (let (eTy, acc2) = ((inferExpr acc) e) in ((((inferUnify acc2) eTy) tv) "list element")))) st2) elems) in (let elemTy = ((applyType (inferEnvSubst st3)) tv) in ((tyList elemTy), st3)))) | ECons(h, t) -> (let (hTy, st2) = ((inferExpr st) h) in (let (tTy, st3) = ((inferExpr st2) t) in (let st4 = ((((inferUnify st3) tTy) (tyList hTy)) "list cons") in ((tyList hTy), st4)))))

and inferMatchArms st scrutTy retTy pats bodies =
    (match pats with | [] -> st | (pat :: restPats) -> (match bodies with | [] -> st | (body :: restBodies) -> (let (bindings, patTy, st2) = ((inferPattern st) pat) in (let st3 = ((((inferUnify st2) scrutTy) patTy) "match scrutinee") in (let env2 = (((listFold (fun acc pair -> (let (name, sch) = pair in (((typeEnvInsert name) sch) acc)))) (inferEnv st3)) bindings) in (let st4 = ((inferWithEnv env2) st3) in (let (bodyTy, st5) = ((inferExpr st4) body) in (let st6 = ((((inferUnify st5) bodyTy) retTy) "match arm") in (let st7 = ((inferWithEnv (inferEnv st3)) st6) in (((((inferMatchArms st7) scrutTy) retTy) restPats) restBodies))))))))))

and inferEnvSubst st =
    (inferSubst st)

and inferDecl st decl =
    (match decl with | DFn(name, __ll_params, retTyOpt, body) -> (let (paramSchemes, paramTys, st2) = ((inferParams st) __ll_params) in (let env2 = (((listFold (fun acc pair -> (let (pname, sch) = pair in (((typeEnvInsert pname) sch) acc)))) (inferEnv st2)) paramSchemes) in (let (selfTy, st3) = (inferFreshVar ((inferWithEnv env2) st2)) in (let env3 = (((typeEnvInsert name) (schemeMono selfTy)) (inferEnv st3)) in (let st4 = ((inferWithEnv env3) st3) in (let (bodyTy, st5) = ((inferExpr st4) body) in (let st6 = (match retTyOpt with | None -> st5 | Some(retTy) -> ((((inferUnify st5) bodyTy) retTy) "declared return type")) in (let fnTy = ((buildFnType paramTys) bodyTy) in (let sch = ((generalize (inferEnv st6)) fnTy) in (let env4 = (((typeEnvInsert name) sch) (inferEnv st6)) in ((inferWithEnv env4) st6))))))))))) | DType(typeName, tvars, ctors) -> (let st2 = (((listFold (fun acc ctor -> (match ctor with | MkCon(ctorName, argTys) -> (let retTy = ((applyTypeVars typeName) tvars) in (let ctorTy = ((buildFnType argTys) retTy) in (let ctorSch = (MkScheme (tvars, ctorTy)) in ((inferWithEnv (((typeEnvInsert ctorName) ctorSch) (inferEnv acc))) acc))))))) st) ctors) in st2) | DLet(name, expr) -> (let (ty, st2) = ((inferExpr st) expr) in (let sch = ((generalize (inferEnv st2)) ty) in ((inferWithEnv (((typeEnvInsert name) sch) (inferEnv st2))) st2))) | DExternal(name, __ll_params, retTy) -> (let paramTys = ((listMap (fun p -> (match p with | MkParam(_, t) -> t))) __ll_params) in (let fnTy = ((buildFnType paramTys) retTy) in (let sch = (schemeMono fnTy) in ((inferWithEnv (((typeEnvInsert name) sch) (inferEnv st))) st)))) | DOpaque(typeName) -> st | DImport(_) -> st | DImportUrl(_) -> st | DExport(inner) -> ((inferDecl st) inner))

and inferParams st __ll_params =
    (match __ll_params with | [] -> ([], [], st) | (p :: rest) -> (match p with | MkParam(pname, pty) -> (let pty2 = (match pty with | TyName("?") -> (let (tv, _) = (inferFreshVar st) in tv) | _ -> pty) in (let (restPairs, restTys, st2) = ((inferParams st) rest) in (let sch = (schemeMono pty2) in (((listAppend [(pname, sch)]) restPairs), ((listAppend [pty2]) restTys), st2))))))

and buildFnType argTys retTy =
    (match argTys with | [] -> retTy | (a :: rest) -> (TyFn (a, ((buildFnType rest) retTy))))

and applyTypeVars __ll_base vars =
    (match vars with | [] -> (TyName __ll_base) | (v :: rest) -> (TyApp (((applyTypeVars __ll_base) rest), (TyName v))))

and inferModule decls =
    (let st0 = (MkInferState (baseEnv, freshInit, [], substEmpty)) in (let stFinal = (((listFold inferDecl) st0) decls) in (inferErrors stFinal)))

and inferModuleStep env decls =
    (let st0 = (MkInferState (env, freshInit, [], substEmpty)) in (let stFinal = (((listFold inferDecl) st0) decls) in ((inferErrors stFinal), (inferEnv stFinal))))

and strStartsWith prefix s =
    (let plen = (strLen prefix) in (let slen = (strLen s) in (if (plen > slen) then false else ((((strSlice s) 0L) plen) = prefix))))

and inferCheck label got expected =
    (if (got = expected) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat " expected=") ((strConcat expected) ((strConcat " got=") got)))))))

and runInfer expr =
    (let st0 = (MkInferState (baseEnv, freshInit, [], substEmpty)) in (let (ty, st1) = ((inferExpr st0) expr) in (let errs = (inferErrors st1) in (if (listIsEmpty errs) then (renderType ((applyType (inferSubst st1)) ty)) else ((strConcat "error: ") (match errs with | (e :: _) -> e | [] -> "?"))))))

and __test_main_CompilerInfer =
    (let _ = (((inferCheck "1 EInt") (runInfer (EInt 42L))) "Int") in (let _ = (((inferCheck "2 EStr") (runInfer (EStr "hello"))) "Str") in (let _ = (((inferCheck "3 EBool") (runInfer (EBool true))) "Bool") in (let _ = (((inferCheck "4 lambda id") (runInfer (ELam ("x", (EVar "x"))))) "$0 -> $0") in (let _ = (((inferCheck "5 lambda const") (runInfer (ELam ("x", (EInt 42L))))) "$0 -> Int") in (let _ = (((inferCheck "6 app id") (runInfer (EApp ((ELam ("x", (EVar "x"))), (EInt 42L))))) "Int") in (let _ = (((inferCheck "7 let") (runInfer (ELet ("x", (EInt 42L), (EVar "x"))))) "Int") in (let _ = (((inferCheck "8 if") (runInfer (EIf ((EBool true), (EInt 1L), (EInt 2L))))) "Int") in (let _ = (((inferCheck "9 binop +") (runInfer (EBinOp ("+", (EInt 1L), (EInt 2L))))) "Int") in (let _ = (((inferCheck "10 list") (runInfer (EList [(EInt 1L); (EInt 2L); (EInt 3L)]))) "List Int") in (let r11 = (runInfer (EVar "noSuchName")) in (let _ = (((inferCheck "11 unbound") (match ((strStartsWith "error:") r11) with | true -> "err" | false -> "ok")) "err") in (let _ = (((inferCheck "12 lambda arith") (runInfer (ELam ("n", (EBinOp ("+", (EVar "n"), (EInt 1L))))))) "Int -> Int") in (printfn "Done"))))))))))))))// FILE: Compiler.fs
module Std.Compiler

open LLLang.Prelude
open Std.Maybe
open Std.Map
open Std.List
open Std.Lexer
open Std.Parser
open Std.Elaborator
open Std.CompilerTypes
open Std.Codegen
open Std.CompilerInfer

let rec showErrors errs =
    (((listFold (fun acc e -> (let msg = (errMsg e) in (if ((strLen acc) = 0L) then msg else ((strConcat acc) ((strConcat "\n") msg)))))) "") errs)

and compile src =
    (let tokens = (tokenize src) in (let ast = (parseModule tokens) in (let errors = (elaborate ast) in (if (listIsEmpty errors) then (emitModule ast) else (showErrors errors)))))

and collectErrors src =
    (let tokens = (tokenize src) in (let ast = (parseModule tokens) in (elaborate ast)))

and firstErrorMessage errs =
    (match errs with | (e :: _) -> (errMsg e) | [] -> "")

and checkCompact src =
    (let errors = (collectErrors src) in (if (listIsEmpty errors) then "{\"ok\":true,\"stage\":\"ok\",\"primary_error\":\"\",\"secondary_count\":0}" else (let msg = (firstErrorMessage errors) in (let secondary = ((listLen errors) - 1L) in (let p1 = "{\"ok\":false,\"stage\":\"elaborator\",\"primary_error\":\"" in (let p2 = ((strConcat p1) msg) in (let p3 = ((strConcat p2) "\",\"secondary_count\":") in (let p4 = ((strConcat p3) (intToStr secondary)) in ((strConcat p4) "}")))))))))

and renderCompact src =
    src

and tokenEstimate src =
    (let toks = (tokenize src) in (let n = (listLen toks) in (let p1 = "{\"ok\":true,\"tokens\":" in (let p2 = ((strConcat p1) (intToStr n)) in ((strConcat p2) "}")))))

and nextBlocker src =
    (let errors = (collectErrors src) in (if (listIsEmpty errors) then "{\"ok\":true,\"stage\":\"none\",\"message\":\"\"}" else (let msg = (firstErrorMessage errors) in (let p1 = "{\"ok\":false,\"stage\":\"elaborator\",\"message\":\"" in (let p2 = ((strConcat p1) msg) in ((strConcat p2) "\"}"))))))

and declKind d name =
    (match d with | DFn(n, _, _, _) -> (if (n = name) then "fn" else "") | DType(n, _, _) -> (if (n = name) then "type" else "") | DLet(n, _) -> (if (n = name) then "let" else "") | DExternal(n, _, _) -> (if (n = name) then "external" else "") | DOpaque(_) -> "" | DImport(_) -> "" | DExport(inner) -> ((declKind inner) name))

and findDeclKind decls name =
    (match decls with | [] -> "" | (d :: rest) -> (let kind = ((declKind d) name) in (if ((strLen kind) = 0L) then ((findDeclKind rest) name) else kind)))

and lookupSymbol src name =
    (let tokens = (tokenize src) in (let ast = (parseModule tokens) in (match ast with | MkModule(_, decls) -> (let kind = ((findDeclKind decls) name) in (if ((strLen kind) = 0L) then (let p1 = "{\"found\":false,\"name\":\"" in (let p2 = ((strConcat p1) name) in ((strConcat p2) "\"}"))) else (let p1 = "{\"found\":true,\"name\":\"" in (let p2 = ((strConcat p1) name) in (let p3 = ((strConcat p2) "\",\"kind\":\"") in (let p4 = ((strConcat p3) kind) in ((strConcat p4) "\"}"))))))))))

and compileFiles srcs env =
    (match srcs with | [] -> ([], env) | (src :: rest) -> (let tokens = (tokenize src) in (let ast = (parseModule tokens) in (let elabErrs = (elaborate ast) in (if (listIsEmpty elabErrs) then (match ast with | MkModule(_, decls) -> (let (typeErrs, env2) = ((inferModuleStep env) decls) in (if (listIsEmpty typeErrs) then ((compileFiles rest) env2) else (typeErrs, env2)))) else (((listMap errMsg) elabErrs), env))))))

and compileProject srcs =
    (let (errs, _) = ((compileFiles srcs) baseEnv) in errs)

and showTypeErrors errs =
    (((listFold (fun acc e -> (if ((strLen acc) = 0L) then e else ((strConcat acc) ((strConcat "\n") e))))) "") errs)

and compileWithInfer src =
    (let tokens = (tokenize src) in (let ast = (parseModule tokens) in (let elabErrs = (elaborate ast) in (if (listIsEmpty elabErrs) then (match ast with | MkModule(_, decls) -> (let typeErrs = (inferModule decls) in (if (listIsEmpty typeErrs) then (emitModule ast) else (showTypeErrors typeErrs)))) else (showErrors elabErrs)))))

and collectAllErrors src =
    (let tokens = (tokenize src) in (let ast = (parseModule tokens) in (let elabErrs = (elaborate ast) in (if (listIsEmpty elabErrs) then (match ast with | MkModule(_, decls) -> (inferModule decls)) else ((listMap errMsg) elabErrs)))))

and nextBlockerFull src =
    (let tokens = (tokenize src) in (let ast = (parseModule tokens) in (let elabErrs = (elaborate ast) in (if (listIsEmpty elabErrs) then (match ast with | MkModule(_, decls) -> (let typeErrs = (inferModule decls) in (if (listIsEmpty typeErrs) then "{\"ok\":true,\"stage\":\"none\",\"message\":\"\"}" else (let msg = (match typeErrs with | (e :: _) -> e | [] -> "") in (let p1 = "{\"ok\":false,\"stage\":\"inference\",\"message\":\"" in ((strConcat p1) ((strConcat msg) "\"}"))))))) else (let msg = (firstErrorMessage elabErrs) in (let p1 = "{\"ok\":false,\"stage\":\"elaborator\",\"message\":\"" in ((strConcat p1) ((strConcat msg) "\"}"))))))))

and compilerCheckContains label got needle =
    (if ((strContains needle) got) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat "\n  missing:  ") ((strConcat needle) ((strConcat "\n  in output:\n") got)))))))

and checkHasError label got needle =
    (if ((strContains needle) got) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat "\n  expected error containing: ") ((strConcat needle) ((strConcat "\n  got: ") got)))))))

and compilerCheck label got expected =
    (if (got = expected) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat " expected=") ((strConcat expected) ((strConcat " got=") got)))))))

and __test_main_Compiler =
    (let src1 = "module Test\nadd(a Int)(b Int) = a + b\n" in (let _ = (((compilerCheckContains "1 compile basic fn") (compile src1)) "add") in (let src2 = "module Test\nfoo = undeclaredName\n" in (let _ = (((checkHasError "2 elab error undeclared") (compile src2)) "undeclared") in (let errs3 = (collectAllErrors src1) in (let _ = (((compilerCheck "3 no errors on valid") (intToStr (listLen errs3))) "0") in (let _ = (((compilerCheckContains "4 compileWithInfer basic") (compileWithInfer src1)) "add") in (let nb5 = (nextBlockerFull src1) in (let _ = (((compilerCheckContains "5 nextBlockerFull ok") nb5) "\"ok\":true") in (let nb6 = (nextBlockerFull src2) in (let _ = (((compilerCheckContains "6 nextBlockerFull elab") nb6) "\"stage\":\"elaborator\"") in (let sym7 = ((lookupSymbol src1) "add") in (let _ = (((compilerCheckContains "7 lookupSymbol found") sym7) "\"found\":true") in (let sym8 = ((lookupSymbol src1) "unknown") in (let _ = (((compilerCheckContains "8 lookupSymbol not found") sym8) "\"found\":false") in (let te9 = (tokenEstimate src1) in (let _ = (((compilerCheckContains "9 tokenEstimate ok") te9) "\"ok\":true") in (let srcA = "module A\nfoo(x Int) = x\n" in (let srcB = "module B\nbar(x Int) = x\n" in (let proj10 = (compileProject [srcA; srcB]) in (let _ = (((compilerCheck "10 compileProject no errs") (intToStr (listLen proj10))) "0") in (let srcBad = "module Bad\nresult = badFn 1\n" in (let proj11 = (compileProject [srcBad]) in (let proj11status = (match proj11 with | [] -> "none" | (_ :: _) -> "err") in (let _ = (((compilerCheck "11 compileProject unbound err") proj11status) "err") in (printfn "Done"))))))))))))))))))))))))))// FILE: LlmRepairWorkflow.fs
module Examples.LlmRepairWorkflow

open LLLang.Prelude
open Std.Maybe
open Std.Map
open Std.List
open Std.Lexer
open Std.Parser
open Std.Elaborator
open Std.CompilerTypes
open Std.Codegen
open Std.CompilerInfer
open Std.Compiler

let demo1 =
    (let src = "module Demo\nadd(a Int)(b Int) = a + b\n" in (let errs = (collectAllErrors src) in (listIsEmpty errs)))

let demo2 =
    (let src = "module Demo\nresult = undefinedFunc 1\n" in (let errs = (collectAllErrors src) in (let firstErr = (match errs with | (e :: _) -> e | [] -> "") in ((strContains "Unbound") firstErr))))

let demo3 =
    (let src = "module Demo\nMaybe A = Some A | None\nunwrap(m Maybe[Int]) = match m | Some n -> n\n" in (let errs = (collectAllErrors src) in ((strContains "xhaustive") (match errs with | (e :: _) -> e | [] -> ""))))

let demo4 =
    (let good = "module Demo\nfoo(x Int) = x\n" in (let result = (nextBlockerFull good) in ((strContains "\"ok\":true") result)))

let demo5 =
    (let bad = "module Demo\nresult = badName\n" in (let result = (nextBlockerFull bad) in ((strContains "\"ok\":false") result)))

let demo6 =
    (let src = "module Demo\nid(x Int) = x\n" in (let out = (compile src) in ((strLen out) > 0L)))

let demo7 =
    (let broken = "module Demo\nresult = missingFn\n" in (let __ll_fixed = "module Demo\nresult = 42\n" in (let errs1 = (collectAllErrors broken) in (let errs2 = (collectAllErrors __ll_fixed) in (((listIsEmpty errs1) = false) = ((listIsEmpty errs2) = false))))))

let checkBool label ok =
    (if ok then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") label)))

[<EntryPoint>]
let main (argv: string[]) =
    (let _ = ((checkBool "1 valid program no errors") demo1) in (let _ = ((checkBool "2 unbound var E002 detected") demo2) in (let _ = ((checkBool "3 non-exhaustive E003 detected") demo3) in (let _ = ((checkBool "4 MCP JSON ok:true on good src") demo4) in (let _ = ((checkBool "5 MCP JSON ok:false on bad src") demo5) in (let _ = ((checkBool "6 compile produces output") demo6) in (let _ = ((checkBool "7 broken and fixed differ") demo7) in (printfn "Done"))))))))
    0