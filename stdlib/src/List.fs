module Std.List

open LLLang.Prelude
open Std.Maybe

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

[<EntryPoint>]
let main (argv: string[]) =
    (let xs = [1L; 2L; 3L; 4L] in (let t1 = ((listTake 2L) xs) in (let _ = ((check "1 take") (if ((listLen t1) = 2L) then (match (listHead t1) with | Some(v) -> (v = 1L) | None -> false) else false)) in (let d1 = ((listDrop 2L) xs) in (let _ = ((check "2 drop") (if ((listLen d1) = 2L) then (match (listHead d1) with | Some(v) -> (v = 3L) | None -> false) else false)) in (let fm = ((listFlatMap (fun x -> [x; (x * 10L)])) [1L; 2L]) in (let _ = ((check "3 flatMap") (if ((listLen fm) = 4L) then (match ((listAt fm) 1L) with | Some(v) -> (v = 10L) | None -> false) else false)) in (let _ = ((check "4 any") ((listAny (fun x -> (x = 3L))) xs)) in (let _ = ((check "5 all") ((listAll (fun x -> (x > 0L))) xs)) in (let f1 = ((listFind (fun x -> (x > 2L))) xs) in (let ok6 = (match f1 with | Some(v) -> (v = 3L) | None -> false) in (let _ = ((check "6 find") ok6) in (let fi = ((listFindIndex (fun x -> (x = 4L))) xs) in (let ok7 = (match fi with | Some(i) -> (i = 3L) | None -> false) in (let _ = ((check "7 findIndex") ok7) in (let parts = ((listPartition (fun x -> (x = ((x / 2L) * 2L)))) xs) in (let ok8 = (match parts with | (evens, odds) -> (if ((listLen evens) = 2L) then (if ((listLen odds) = 2L) then (match (listHead evens) with | Some(v) -> (v = 2L) | None -> false) else false) else false)) in (let _ = ((check "8 partition") ok8) in 0L))))))))))))))))))
    0