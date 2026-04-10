module Std.Toml

type Maybe<'A> =
    | Some of 'A
    | None

type Manifest =
    | MkManifest of string * string * string * string list * string list

type Section =
    | SecProject
    | SecDeps
    | SecPlatform
    | SecOther

type ParseState =
    | MkState of Section * string Maybe * string * string * string list * string list * string Maybe

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
let listHead xs = match xs with [] -> None | x :: _ -> Some x
let listTail xs = match xs with [] -> None | _ :: t -> Some t
let maybeMap f m = match m with Some x -> Some (f x) | None -> None
let maybeBind m f = match m with Some x -> f x | None -> None
let maybeWithDefault d m = match m with Some x -> x | None -> d
let strToInt (s: string) =
    match System.Int64.TryParse(s: string) with
    | true, n -> Some n
    | false, _ -> None
let listAt (xs: 'a list) (i: int64) =
    if int i < 0 || int i >= List.length xs then None else Some (List.item (int i) xs)
// --- end prelude ---

let rec strStartsWith prefix s =
    (if ((strLen prefix) > (strLen s)) then false else ((((strSlice s) 0L) (strLen prefix)) = prefix))

and strDropPrefix n s =
    (if (n >= (strLen s)) then "" else (((strSlice s) n) ((strLen s) - n)))

and strTakeUntil ch s =
    (let idx = ((strIndexOf ch) s) in (if (idx < 0L) then s else (((strSlice s) 0L) idx)))

and stripComment s =
    (strTrim ((strTakeUntil "#") s))

and decodeEscape e =
    (if (e = 'n') then '\n' else (if (e = 't') then '\t' else (if (e = '"') then '"' else (if (e = '\\') then '\\' else e))))

and parseQuotedChars cs =
    (match cs with | [] -> ([], []) | (c :: rest) -> (if (c = '"') then ([], rest) else (if (c = '\\') then (parseQuotedEsc rest) else (let (body, leftover) = (parseQuotedChars rest) in ((c :: body), leftover)))))

and parseQuotedEsc cs =
    (match cs with | [] -> ([], []) | (e :: rest2) -> (let (body, leftover) = (parseQuotedChars rest2) in (((decodeEscape e) :: body), leftover)))

and parseQuotedStr s =
    (let cs = (strChars s) in (match cs with | (c :: rest) -> (if (c = '"') then (let (body, _) = (parseQuotedChars rest) in (Some (strFromChars body))) else None) | [] -> None))

and cleanArrayItem s =
    (if ((strStartsWith "[") s) then (strTrim ((strDropPrefix 1L) s)) else (if ((strStartsWith "]") s) then "" else s))

and parseArrayItems items =
    (match items with | [] -> [] | (item :: rest) -> (let cleaned = (cleanArrayItem (strTrim item)) in (let final = (strTrim ((strTakeUntil "]") cleaned)) in (if ((strLen final) = 0L) then (parseArrayItems rest) else (match (parseQuotedStr final) with | Some v -> (v :: (parseArrayItems rest)) | None -> (parseArrayItems rest))))))

and parseStringArray s =
    (let inner = ((strTakeUntil "]") ((strDropPrefix 1L) s)) in (let parts = ((strSplit ",") inner) in (parseArrayItems parts)))

and parseSectionHeader line =
    (let inner = (strTrim ((strTakeUntil "]") ((strDropPrefix 1L) line))) in (if (inner = "project") then SecProject else (if (inner = "deps") then SecDeps else (if (inner = "platform") then SecPlatform else SecOther))))

and parseKeyValue line =
    (let eqIdx = ((strIndexOf "=") line) in (if (eqIdx < 0L) then ("", "") else (let key = (strTrim (((strSlice line) 0L) eqIdx)) in (let valRaw = (strTrim ((strDropPrefix (eqIdx + 1L)) line)) in (key, valRaw)))))

and processProjectLine key valRaw st =
    (match (parseQuotedStr valRaw) with | None -> st | Some v -> (match st with | MkState(sec, _, ver, entry, deps, plat, err) -> (if (key = "name") then (MkState (sec, (Some v), ver, entry, deps, plat, err)) else (if (key = "version") then (MkState (sec, (Some v), v, entry, deps, plat, err)) else (if (key = "entry") then (MkState (sec, (Some v), ver, v, deps, plat, err)) else st)))))

and processDepsLine key valRaw st =
    (match (parseQuotedStr valRaw) with | None -> st | Some v -> (let cleanKey = (match (parseQuotedStr key) with | Some k -> k | None -> key) in (match st with | MkState(sec, name, ver, entry, deps, plat, err) -> (MkState (sec, name, ver, entry, ((listAppend deps) [cleanKey; v]), plat, err)))))

and processPlatformLine key valRaw st =
    (if (key = "use") then (if ((strStartsWith "[") valRaw) then (let items = (parseStringArray valRaw) in (match st with | MkState(sec, name, ver, entry, deps, _, err) -> (MkState (sec, name, ver, entry, deps, items, err)))) else st) else st)

and processLine st line =
    (let trimmed = (stripComment line) in (if ((strLen trimmed) = 0L) then st else (if ((strStartsWith "[") trimmed) then (let sec = (parseSectionHeader trimmed) in (match st with | MkState(_, name, ver, entry, deps, plat, err) -> (MkState (sec, name, ver, entry, deps, plat, err)))) else (if ((strContains "=") trimmed) then (let (key, valRaw) = (parseKeyValue trimmed) in (match st with | MkState(sec, _, _, _, _, _, _) -> (match sec with | SecProject -> (((processProjectLine key) valRaw) st) | SecDeps -> (((processDepsLine key) valRaw) st) | SecPlatform -> (((processPlatformLine key) valRaw) st) | SecOther -> st))) else st))))

and foldLines f acc lines =
    (match lines with | [] -> acc | (line :: rest) -> (((foldLines f) ((f acc) line)) rest))

and parseManifest src =
    (let lines = ((strSplit "\n") src) in (let init = (MkState (SecOther, None, "0.0.0", "src/Main.lll", [], [], None)) in (let final = (((foldLines processLine) init) lines) in (match final with | MkState(_, name, ver, entry, deps, plat, _) -> (match name with | Some n -> (Some (MkManifest (n, ver, entry, deps, plat))) | None -> None)))))

and check label ok =
    (if ok then (let _ = (printfn ((strConcat "OK ") label)) in 0L) else (let _ = (printfn ((strConcat "FAIL ") label)) in 1L))

[<EntryPoint>]
let main (argv: string[]) =
    (let src1 = "[project]\nname = \"myapp\"" in (let r1 = (parseManifest src1) in (let ok1 = (match r1 with | Some MkManifest(n, _, _, _, _) -> (n = "myapp") | None -> false) in (let _ = ((check "1 parse name") ok1) in (let src2 = "[project]\nname = \"test\"\nversion = \"1.2.3\"\nentry = \"src/App.lll\"" in (let r2 = (parseManifest src2) in (let ok2 = (match r2 with | Some MkManifest(_, ver, entry, _, _) -> (ver = "1.2.3") | None -> false) in (let _ = ((check "2 parse version") ok2) in (let src3 = "[project]\nversion = \"1.0\"" in (let r3 = (parseManifest src3) in (let ok3 = (match r3 with | Some _ -> false | None -> true) in (let _ = ((check "3 missing name") ok3) in (let src4 = "# comment\n\n[project]\n# another comment\nname = \"app\"" in (let r4 = (parseManifest src4) in (let ok4 = (match r4 with | Some MkManifest(n, _, _, _, _) -> (n = "app") | None -> false) in (let _ = ((check "4 comments") ok4) in (let src5 = "[project]\nname = \"p\"\n[platform]\nuse = [\"fsharp\", \"node\"]" in (let r5 = (parseManifest src5) in (let ok5 = (match r5 with | Some MkManifest(_, _, _, _, plat) -> ((listLen plat) = 2L) | None -> false) in (let _ = ((check "5 platform array") ok5) in (let src6 = "[project]\nname = \"d\"\n[deps]\ncore = \"1.0\"\nutils = \"2.0\"" in (let r6 = (parseManifest src6) in (let ok6 = (match r6 with | Some MkManifest(_, _, _, deps, _) -> ((listLen deps) = 4L) | None -> false) in (let _ = ((check "6 deps") ok6) in (let realSrc = (readFile "stdlib/ll.toml") in (let r7 = (parseManifest realSrc) in (let ok7 = (match r7 with | Some MkManifest(n, _, _, _, _) -> (n = "std") | None -> false) in (let _ = ((check "7 real ll.toml") ok7) in (let src8 = "[project]\nname = \"hello\\nworld\"" in (let r8 = (parseManifest src8) in (let ok8 = (match r8 with | Some MkManifest(n, _, _, _, _) -> ((strLen n) = 11L) | None -> false) in (let _ = ((check "8 escaped string") ok8) in (let src9 = "[project]\nname = \"val\" # inline comment" in (let r9 = (parseManifest src9) in (let ok9 = (match r9 with | Some MkManifest(n, _, _, _, _) -> (n = "val") | None -> false) in (let _ = ((check "9 inline comment") ok9) in 0L))))))))))))))))))))))))))))))))))))
    0