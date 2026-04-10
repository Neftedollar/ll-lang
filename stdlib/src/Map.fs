module Std.Map

type Color =
    | Red
    | Black

type RBMap<'K, 'V> =
    | Leaf
    | Node of Color * RBMap<'K, 'V> * 'K * 'V * RBMap<'K, 'V>

type Maybe<'A> =
    | Some of 'A
    | None

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
    (match (((mapLookup cmp) k) m) with | Some _ -> true | None -> false)

and mapFold f acc m =
    (match m with | Leaf -> acc | Node(_, left, k, v, right) -> (let acc1 = (((mapFold f) acc) left) in (let acc2 = (((f acc1) k) v) in (((mapFold f) acc2) right))))

and mapKeys m =
    (match m with | Leaf -> [] | Node(_, left, k, _, right) -> (let lkeys = (mapKeys left) in (let rkeys = (mapKeys right) in ((listAppend lkeys) (k :: rkeys)))))

and intCmp a b =
    (if (a < b) then (0L - 1L) else (if (a > b) then 1L else 0L))

and strCmp a b =
    (if (a < b) then (0L - 1L) else (if (a > b) then 1L else 0L))

and check label ok =
    (if ok then (let _ = (printfn ((strConcat "OK ") label)) in 0L) else (let _ = (printfn ((strConcat "FAIL ") label)) in 1L))

[<EntryPoint>]
let main (argv: string[]) =
    (let m0 = mapEmpty in (let _ = ((check "1 mapEmpty size=0") ((mapSize m0) = 0L)) in (let m1 = ((((mapInsert intCmp) 3L) "three") m0) in (let m2 = ((((mapInsert intCmp) 1L) "one") m1) in (let m3 = ((((mapInsert intCmp) 5L) "five") m2) in (let m4 = ((((mapInsert intCmp) 2L) "two") m3) in (let m5 = ((((mapInsert intCmp) 4L) "four") m4) in (let _ = ((check "2 mapInsert size=5") ((mapSize m5) = 5L)) in (let r1 = (((mapLookup intCmp) 3L) m5) in (let ok1 = (match r1 with | Some v -> (v = "three") | None -> false) in (let _ = ((check "3 mapLookup found") ok1) in (let r2 = (((mapLookup intCmp) 99L) m5) in (let ok2 = (match r2 with | Some _ -> false | None -> true) in (let _ = ((check "4 mapLookup missing") ok2) in (let _ = ((check "5 mapContains present") (((mapContains intCmp) 2L) m5)) in (let _ = ((check "6 mapContains absent") ((((mapContains intCmp) 99L) m5) = false)) in (let keySum = (((mapFold (fun acc k v -> (acc + k))) 0L) m5) in (let _ = ((check "7 mapFold sum keys") (keySum = 15L)) in (let keys = (mapKeys m5) in (let _ = ((check "8 mapKeys length") ((listLen keys) = 5L)) in (let ms = ((((mapInsert strCmp) "banana") 2L) ((((mapInsert strCmp) "apple") 1L) mapEmpty)) in (let r3 = (((mapLookup strCmp) "apple") ms) in (let ok3 = (match r3 with | Some n -> (n = 1L) | None -> false) in (let _ = ((check "9 strCmp lookup") ok3) in (let me = mapEmpty in (let _ = ((check "10 mapEmpty size=0") ((mapSize me) = 0L)) in (let md1 = ((((mapInsert intCmp) 42L) "first") mapEmpty) in (let md2 = ((((mapInsert intCmp) 42L) "second") md1) in (let okDup = (match (((mapLookup intCmp) 42L) md2) with | Some v -> (v = "second") | None -> false) in (let _ = ((check "11 duplicate key insert") (if ((mapSize md2) = 1L) then okDup else false)) in (let ms1 = ((((mapInsert intCmp) 5L) 0L) mapEmpty) in (let ms2 = ((((mapInsert intCmp) 3L) 0L) ms1) in (let ms3 = ((((mapInsert intCmp) 1L) 0L) ms2) in (let ms4 = ((((mapInsert intCmp) 4L) 0L) ms3) in (let ms5 = ((((mapInsert intCmp) 2L) 0L) ms4) in (let skeys = (mapKeys ms5) in (let firstOk = (match skeys with | (h :: _) -> (h = 1L) | [] -> false) in (let lastKey = ((listAt skeys) 4L) in (let lastOk = (match lastKey with | Some v -> (v = 5L) | None -> false) in (let _ = ((check "12 sorted order") (if firstOk then lastOk else false)) in 0L))))))))))))))))))))))))))))))))))))))))
    0