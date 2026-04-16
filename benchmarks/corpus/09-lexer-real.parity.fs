// FILE: 09-lexer-real.fs
module Examples.RealLexer

type Token =
    | TIdent of string
    | TInt of string
    | TLet
    | TFn
    | TIf
    | TElse
    | TPlus
    | TMinus
    | TStar
    | TSlash
    | TEq
    | TLt
    | TGt
    | TLParen
    | TRParen
    | TUnknown of string

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
let exit (code: int64) : unit = System.Environment.Exit(int code)
let listConcat (xss: 'a list list) = List.concat xss
let listIsEmpty (xs: 'a list) = List.isEmpty xs
let getArgs : string list = System.Environment.GetCommandLineArgs() |> Array.toList |> List.tail
let ll_dirList (path: string) = System.IO.Directory.GetFiles(path) |> Array.toList
let ll_processRun (cmd: string) (args: string list) =
    let psi = System.Diagnostics.ProcessStartInfo(cmd)
    for a in args do psi.ArgumentList.Add(a)
    psi.RedirectStandardOutput <- true
    psi.UseShellExecute <- false
    let proc = System.Diagnostics.Process.Start(psi)
    let output = proc.StandardOutput.ReadToEnd()
    proc.WaitForExit()
    output
// --- end prelude ---

let rec isIdStart c =
    (charIsAlpha c)

and isIdCont c =
    (if (charIsAlpha c) then true else (charIsDigit c))

and takeWhilePred p cs =
    (match cs with
    | (c :: rest) -> (if (p c) then ((listAppend [c]) ((takeWhilePred p) rest)) else [])
    | _ -> [])

and dropWhilePred p cs =
    (match cs with
    | (c :: rest) -> (if (p c) then ((dropWhilePred p) rest) else cs)
    | _ -> [])

and keywordOrIdent s =
    (match s with | "let" -> TLet | "fn" -> TFn | "if" -> TIf | "else" -> TElse | _ -> (TIdent s))

and lexId cs =
    (let idChars = ((takeWhilePred isIdCont) cs) in (let leftover = ((dropWhilePred isIdCont) cs) in (let tok = (keywordOrIdent (strFromChars idChars)) in ((listAppend [tok]) (lexChars leftover)))))

and lexNum cs =
    (let digits = ((takeWhilePred charIsDigit) cs) in (let leftover = ((dropWhilePred charIsDigit) cs) in ((listAppend [(TInt (strFromChars digits))]) (lexChars leftover))))

and lexOp c rest =
    (let tok = (match c with | '+' -> TPlus | '-' -> TMinus | '*' -> TStar | '/' -> TSlash | '=' -> TEq | '<' -> TLt | '>' -> TGt | '(' -> TLParen | ')' -> TRParen | _ -> (TUnknown (strFromChars [c]))) in ((listAppend [tok]) (lexChars rest)))

and lexChars cs =
    (match cs with
    | (c :: rest) -> (if (charIsSpace c) then (lexChars rest) else (if (isIdStart c) then (lexId cs) else (if (charIsDigit c) then (lexNum cs) else ((lexOp c) rest))))
    | _ -> [])

and tokenize src =
    (lexChars (strChars src))

and tokenName t =
    (match t with
    | TIdent(s) -> ((strConcat "id:") s)
    | TInt(s) -> ((strConcat "int:") s)
    | TLet -> "kw:let"
    | TFn -> "kw:fn"
    | TIf -> "kw:if"
    | TElse -> "kw:else"
    | TPlus -> "+"
    | TMinus -> "-"
    | TStar -> "*"
    | TSlash -> "/"
    | TEq -> "="
    | TLt -> "<"
    | TGt -> ">"
    | TLParen -> "("
    | TRParen -> ")"
    | TUnknown(s) -> ((strConcat "?:") s))

and joinNames ts =
    (((listFold (fun acc t -> (if ((strLen acc) = 0L) then (tokenName t) else ((strConcat ((strConcat acc) " ")) (tokenName t))))) "") ts)

[<EntryPoint>]
let main (argv: string[]) =
    (let src = "fn add(a)(b) = a + b" in (let toks = (tokenize src) in (printfn (joinNames toks))))
    0