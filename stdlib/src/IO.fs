module Std.IO

open LLLang.Prelude
open Std.Maybe
open Std.List

type IO<'A> =
    | MkIO of (int64 -> 'A)

let rec ioPure x =
    (MkIO (fun dummy -> x))

and ioRun io =
    (match io with | MkIO(f) -> (f 0L))

and ioMap f io =
    (MkIO (fun dummy -> (let a = (ioRun io) in (f a))))

and ioBind io k =
    (MkIO (fun dummy -> (let a = (ioRun io) in (ioRun (k a)))))

and ioThen first second =
    (MkIO (fun dummy -> (let _ = (ioRun first) in (ioRun second))))

and ioPutStrLn s =
    (MkIO (fun dummy -> (let _ = (printfn s) in 0L)))

and ioReadFile path =
    (MkIO (fun dummy -> (readFile path)))

and ioWriteFile path content =
    (MkIO (fun dummy -> (let _ = ((writeFile path) content) in 0L)))

and ioDirList path =
    (MkIO (fun dummy -> (dirList path)))

and ioForEach xs f =
    (MkIO (fun dummy -> (((listFold (fun acc x -> (let _ = (ioRun (f x)) in 0L))) 0L) xs)))

and ioWhen cond action =
    (if cond then action else (ioPure 0L))

and ioSequence actions =
    (MkIO (fun dummy -> (let reversed = (((listFold (fun acc act -> (let result = (ioRun act) in (result :: acc)))) []) actions) in (listReverse reversed))))

and check label ok =
    (if ok then (let _ = (printfn ((strConcat "OK ") label)) in 0L) else (let _ = (printfn ((strConcat "FAIL ") label)) in 1L))

and showIntList xs =
    (let strs = ((listMap intToStr) xs) in (((listFold (fun acc s -> ((strConcat acc) ((strConcat ",") s)))) "") strs))

[<EntryPoint>]
let main (argv: string[]) =
    (let _ = ((check "1 ioPure+ioRun") ((ioRun (ioPure 42L)) = 42L)) in (let _ = ((check "2 ioPure str") ((ioRun (ioPure "hello")) = "hello")) in (let r3 = (ioRun ((ioMap (fun x -> (x * 3L))) (ioPure 7L))) in (let _ = ((check "3 ioMap") (r3 = 21L)) in (let r4 = (ioRun ((ioMap (fun x -> (x + 1L))) ((ioMap (fun x -> (x * 2L))) (ioPure 5L)))) in (let _ = ((check "4 ioMap compose") (r4 = 11L)) in (let r5 = (ioRun ((ioBind (ioPure 10L)) (fun x -> (ioPure (x + 5L))))) in (let _ = ((check "5 ioBind") (r5 = 15L)) in (let r6 = (ioRun ((ioBind (ioPure 2L)) (fun a -> ((ioBind (ioPure (a * 3L))) (fun b -> (ioPure (b + 1L))))))) in (let _ = ((check "6 ioBind chain") (r6 = 7L)) in (let r7 = (ioRun ((ioThen (ioPure 99L)) (ioPure 1L))) in (let _ = ((check "7 ioThen") (r7 = 1L)) in (let r8 = (ioRun (ioPutStrLn "  (test 8 output)")) in (let _ = ((check "8 ioPutStrLn") (r8 = 0L)) in (let r9 = (ioRun ((ioThen (ioPutStrLn "  (test 9a)")) (ioPutStrLn "  (test 9b)"))) in (let _ = ((check "9 ioThen print") (r9 = 0L)) in (let items = [1L; 2L; 3L] in (let r10 = (ioRun ((ioForEach items) (fun x -> (ioPutStrLn ((strConcat "  forEach ") (intToStr x)))))) in (let _ = ((check "10 ioForEach") (r10 = 0L)) in (let r11 = (ioRun ((ioForEach []) (fun x -> (ioPutStrLn "never")))) in (let _ = ((check "11 ioForEach empty") (r11 = 0L)) in (let r12 = (ioRun ((ioWhen true) (ioPutStrLn "  (test 12 ran)"))) in (let _ = ((check "12 ioWhen true") (r12 = 0L)) in (let r13 = (ioRun ((ioWhen false) (ioPutStrLn "SHOULD NOT PRINT"))) in (let _ = ((check "13 ioWhen false") (r13 = 0L)) in (let actions14 = [(ioPure 10L); (ioPure 20L); (ioPure 30L)] in (let r14 = (ioRun (ioSequence actions14)) in (let _ = ((check "14 ioSequence") ((showIntList r14) = ",10,20,30")) in (let r15 = (ioRun (ioSequence [])) in (let _ = ((check "15 ioSequence empty") ((showIntList r15) = "")) in (let pipeline = ((ioBind (ioPure 4L)) (fun x -> ((ioMap (fun y -> (y * y))) (ioPure (x + 1L))))) in (let r16 = (ioRun pipeline) in (let _ = ((check "16 bind+map pipeline") (r16 = 25L)) in (let r17 = (ioRun ((ioThen (ioPutStrLn "  (17a)")) ((ioThen (ioPutStrLn "  (17b)")) (ioPure 42L)))) in (let _ = ((check "17 ioThen chain") (r17 = 42L)) in (let actions18 = [(ioPutStrLn "  seq-A"); (ioPutStrLn "  seq-B"); (ioPutStrLn "  seq-C")] in (let r18 = (ioRun (ioSequence actions18)) in (let _ = ((check "18 ioSequence effects") ((showIntList r18) = ",0,0,0")) in (printfn "Done")))))))))))))))))))))))))))))))))))))))
    0