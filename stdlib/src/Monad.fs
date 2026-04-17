module Std.Monad

open LLLang.Prelude
open Std.Maybe
open Std.State
open Std.Result
open Std.List

let listPure x =
    [x]

let listBind f xs =
    ((listFlatMap f) xs)

let private check label ok =
    (if ok then (let _ = (printfn ((strConcat "OK ") label)) in 0L) else (let _ = (printfn ((strConcat "FAIL ") label)) in 1L))

let private showIntList xs =
    (let strs = ((listMap intToStr) xs) in (((listFold (fun acc s -> ((strConcat acc) ((strConcat ",") s)))) "") strs))

let doubleIfGt3 x =
    (if (x > 3L) then (Some (x * 2L)) else None)

let tripleIfGt5 x =
    (if (x > 5L) then (Some x) else None)

let tripleIfPos x =
    (if (x > 0L) then (Ok (x * 3L)) else (Err "neg"))

let keepIfGt10 x =
    (if (x > 10L) then (Ok x) else (Err "low"))

let keepIfGt2 x =
    (if (x > 2L) then [x] else [])

let duplicate x =
    (let y = (x * 10L) in [x; y])

let expand x =
    (let y = (x + 10L) in [x; y])

let wrapTimes100 x =
    (let y = (x * 100L) in [y])

[<EntryPoint>]
let main (argv: string[]) =
    (let r1 = ((maybeMap (fun x -> (x + 1L))) (Some 10L)) in (let _ = ((check "1 Maybe map") (((maybeWithDefault 0L) r1) = 11L)) in (let r2 = ((maybeBind (Some 5L)) doubleIfGt3) in (let _ = ((check "2 Maybe bind Some") (((maybeWithDefault 0L) r2) = 10L)) in (let r3 = ((maybeBind None) (fun x -> (Some (x + 1L)))) in (let _ = ((check "3 Maybe bind None") (((maybeWithDefault 0L) r3) = 0L)) in (let r4 = ((maybeBind ((maybeMap (fun x -> (x * 3L))) (Some 2L))) tripleIfGt5) in (let _ = ((check "4 Maybe map+bind") (((maybeWithDefault 0L) r4) = 6L)) in (let r5 = ((resultMap (fun x -> (x + 1L))) (Ok 10L)) in (let _ = ((check "5 Result map Ok") (((resultWithDefault 0L) r5) = 11L)) in (let r6 = ((resultBind (Ok 4L)) tripleIfPos) in (let _ = ((check "6 Result bind Ok") (((resultWithDefault 0L) r6) = 12L)) in (let r7 = ((resultBind (Err "fail")) tripleIfPos) in (let _ = ((check "7 Result bind Err") (((resultWithDefault 0L) r7) = 0L)) in (let r8 = ((resultBind ((resultMap (fun x -> (x + 10L))) (Ok 5L))) keepIfGt10) in (let _ = ((check "8 Result map+bind") (((resultWithDefault 0L) r8) = 15L)) in (let _ = ((check "9 listPure") ((showIntList (listPure 42L)) = ",42")) in (let _ = ((check "10 listBind") ((showIntList ((listBind duplicate) [1L; 2L; 3L])) = ",1,10,2,20,3,30")) in (let _ = ((check "11 listBind filter") ((showIntList ((listBind keepIfGt2) [1L; 2L; 3L; 4L])) = ",3,4")) in (let step1 = ((listBind expand) [1L; 2L]) in (let r12 = ((listBind wrapTimes100) step1) in (let _ = ((check "12 listBind chain") ((showIntList r12) = ",100,1100,200,1200")) in (let _ = ((check "13 State pure") (((stateEval (statePure 42L)) 0L) = 42L)) in (let _ = ((check "14 State map") (((stateEval ((stateMap (fun x -> (x * 3L))) (statePure 7L))) 0L) = 21L)) in (let prog15 = ((stateBind (statePut 99L)) (fun ignored -> (stateGet 0L))) in (let _ = ((check "15 State bind") (((stateEval prog15) 0L) = 99L)) in (let prog16 = ((stateBind (stateModify (fun s -> (s + 5L)))) (fun ignored -> (stateGet 0L))) in (let _ = ((check "16 State bind chain") (((stateEval prog16) 10L) = 15L)) in (printfn "Done")))))))))))))))))))))))))))))
    0