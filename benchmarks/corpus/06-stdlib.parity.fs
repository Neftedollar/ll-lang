// FILE: 06-stdlib.fs
module Examples.Stdlib

type Maybe<'A> = 'A option

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
let dirList (path: string) : string list = if System.IO.Directory.Exists(path) then System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories) |> Array.toList else []
let exit (code: int64) : unit = System.Environment.Exit(int code)
let listConcat (xss: 'a list list) = List.concat xss
let listIsEmpty (xs: 'a list) = List.isEmpty xs
let getArgs : string list = System.Environment.GetCommandLineArgs() |> Array.toList |> List.tail
let processSpawn (cmd: string) (args: string list) : int64 =
    let psi = System.Diagnostics.ProcessStartInfo(cmd)
    psi.UseShellExecute <- false
    args |> List.iter (fun a -> psi.ArgumentList.Add(a))
    let p = System.Diagnostics.Process.Start(psi) in p.WaitForExit(); int64 p.ExitCode
// --- end prelude ---

let rec double x =
    (x * 2L)

and sumList xs =
    (((listFold (fun acc -> (fun x -> (acc + x)))) 0L) xs)

and doubleAll xs =
    ((listMap double) xs)

and nameLen name =
    (strLen name)

[<EntryPoint>]
let main (argv: string[]) =
    (printfn "stdlib example")
    0