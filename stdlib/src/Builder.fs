module Std.Builder

open LLLang.Prelude
open Std.Maybe
open Std.State
open Std.Result
open Std.List
open Std.IO

let rec maybePipe2 m1 m2 f =
    ((maybeBind m1) (fun a -> ((maybeBind m2) (fun b -> ((f a) b)))))

and maybePipe3 m1 m2 m3 f =
    ((maybeBind m1) (fun a -> ((maybeBind m2) (fun b -> ((maybeBind m3) (fun c -> (((f a) b) c)))))))

and maybeAll xs =
    ((maybeAllAcc xs) [])

and maybeAllAcc xs acc =
    (match xs with | [] -> (Some (listReverse acc)) | (h :: t) -> (match h with | None -> None | Some(v) -> ((maybeAllAcc t) (v :: acc))))

and maybeGuard cond =
    (if cond then (Some 0L) else None)

and resultPipe2 r1 r2 f =
    ((resultBind r1) (fun a -> ((resultBind r2) (fun b -> ((f a) b)))))

and resultPipe3 r1 r2 r3 f =
    ((resultBind r1) (fun a -> ((resultBind r2) (fun b -> ((resultBind r3) (fun c -> (((f a) b) c)))))))

and resultAll xs =
    (resultSequence xs)

and resultTry f x =
    (Ok (f x))

and ioPipe2 io1 io2 f =
    ((ioBind io1) (fun a -> ((ioBind io2) (fun b -> ((f a) b)))))

and ioPipe3 io1 io2 io3 f =
    ((ioBind io1) (fun a -> ((ioBind io2) (fun b -> ((ioBind io3) (fun c -> (((f a) b) c)))))))

and ioAll actions =
    (ioSequence actions)

and statePipe2 s1 s2 f =
    ((stateBind s1) (fun a -> ((stateBind s2) (fun b -> ((f a) b)))))

and statePipe3 s1 s2 s3 f =
    ((stateBind s1) (fun a -> ((stateBind s2) (fun b -> ((stateBind s3) (fun c -> (((f a) b) c)))))))

and stateAll actions =
    ((stateAllAcc actions) [])

and stateAllAcc actions acc =
    (match actions with | [] -> (statePure (listReverse acc)) | (h :: t) -> ((stateBind h) (fun v -> ((stateAllAcc t) (v :: acc)))))

and check label ok =
    (if ok then (let _ = (printfn ((strConcat "OK ") label)) in 0L) else (let _ = (printfn ((strConcat "FAIL ") label)) in 1L))

and showIntList xs =
    (let strs = ((listMap intToStr) xs) in (((listFold (fun acc s -> ((strConcat acc) ((strConcat ",") s)))) "") strs))

and maybeIntStr m =
    (match m with | Some(v) -> (intToStr v) | None -> "none")

and resultIntStr r =
    (match r with | Ok(v) -> (intToStr v) | Err(e) -> ((strConcat "err:") e))

and addIfPositive x =
    (if (x > 0L) then (Some x) else None)

and safeDiv10 x =
    (if (x = 0L) then (Err "div0") else (Ok (10L / x)))

[<EntryPoint>]
let main (argv: string[]) =
    (let r1 = (((maybePipe2 (Some 3L)) (Some 7L)) (fun a b -> (Some (a + b)))) in (let _ = ((check "1 maybePipe2 both Some") ((maybeIntStr r1) = "10")) in (let r2 = (((maybePipe2 None) (Some 7L)) (fun a b -> (Some (a + b)))) in (let _ = ((check "2 maybePipe2 first None") ((maybeIntStr r2) = "none")) in (let r3 = (((maybePipe2 (Some 3L)) None) (fun a b -> (Some (a + b)))) in (let _ = ((check "3 maybePipe2 second None") ((maybeIntStr r3) = "none")) in (let r4 = ((((maybePipe3 (Some 1L)) (Some 2L)) (Some 3L)) (fun a b c -> (Some ((a + b) + c)))) in (let _ = ((check "4 maybePipe3 all Some") ((maybeIntStr r4) = "6")) in (let r5 = ((((maybePipe3 (Some 1L)) None) (Some 3L)) (fun a b c -> (Some ((a + b) + c)))) in (let _ = ((check "5 maybePipe3 middle None") ((maybeIntStr r5) = "none")) in (let r6 = ((((maybePipe3 (Some 1L)) (Some 2L)) (Some 3L)) (fun a b c -> None)) in (let _ = ((check "6 maybePipe3 f returns None") ((maybeIntStr r6) = "none")) in (let r7 = (maybeAll [(Some 10L); (Some 20L); (Some 30L)]) in (let s7 = (match r7 with | Some(xs) -> (showIntList xs) | None -> "none") in (let _ = ((check "7 maybeAll all Some") (s7 = ",10,20,30")) in (let r8 = (maybeAll [(Some 10L); None; (Some 30L)]) in (let s8 = (match r8 with | Some(xs) -> (showIntList xs) | None -> "none") in (let _ = ((check "8 maybeAll one None") (s8 = "none")) in (let r9 = (maybeAll []) in (let s9 = (match r9 with | Some(xs) -> (showIntList xs) | None -> "none") in (let _ = ((check "9 maybeAll empty") (s9 = "")) in (let r10 = (maybeGuard true) in (let _ = ((check "10 maybeGuard true") ((maybeIntStr r10) = "0")) in (let r11 = (maybeGuard false) in (let _ = ((check "11 maybeGuard false") ((maybeIntStr r11) = "none")) in (let r12 = (((resultPipe2 (Ok 4L)) (Ok 6L)) (fun a b -> (Ok (a * b)))) in (let _ = ((check "12 resultPipe2 both Ok") ((resultIntStr r12) = "24")) in (let r13 = (((resultPipe2 (Err "bad")) (Ok 6L)) (fun a b -> (Ok (a * b)))) in (let _ = ((check "13 resultPipe2 first Err") ((resultIntStr r13) = "err:bad")) in (let r14 = (((resultPipe2 (Ok 4L)) (Err "fail")) (fun a b -> (Ok (a * b)))) in (let _ = ((check "14 resultPipe2 second Err") ((resultIntStr r14) = "err:fail")) in (let r15 = ((((resultPipe3 (Ok 2L)) (Ok 3L)) (Ok 5L)) (fun a b c -> (Ok ((a + b) + c)))) in (let _ = ((check "15 resultPipe3 all Ok") ((resultIntStr r15) = "10")) in (let r16 = ((((resultPipe3 (Ok 2L)) (Err "mid")) (Ok 5L)) (fun a b c -> (Ok ((a + b) + c)))) in (let _ = ((check "16 resultPipe3 middle Err") ((resultIntStr r16) = "err:mid")) in (let r17 = ((((resultPipe3 (Ok 2L)) (Ok 3L)) (Ok 5L)) (fun a b c -> (Err "nope"))) in (let _ = ((check "17 resultPipe3 f returns Err") ((resultIntStr r17) = "err:nope")) in (let r18 = (resultAll [(Ok 1L); (Ok 2L); (Ok 3L)]) in (let s18 = (match r18 with | Ok(xs) -> (showIntList xs) | Err(e) -> ((strConcat "err:") e)) in (let _ = ((check "18 resultAll all Ok") (s18 = ",1,2,3")) in (let r19 = (resultAll [(Ok 1L); (Err "boom"); (Ok 3L)]) in (let s19 = (match r19 with | Ok(xs) -> (showIntList xs) | Err(e) -> ((strConcat "err:") e)) in (let _ = ((check "19 resultAll one Err") (s19 = "err:boom")) in (let r20 = ((resultTry (fun x -> (x * 2L))) 21L) in (let _ = ((check "20 resultTry") ((resultIntStr r20) = "42")) in (let r21 = (ioRun (((ioPipe2 (ioPure 3L)) (ioPure 7L)) (fun a b -> (ioPure (a + b))))) in (let _ = ((check "21 ioPipe2") (r21 = 10L)) in (let r22 = (ioRun ((((ioPipe3 (ioPure 2L)) (ioPure 3L)) (ioPure 4L)) (fun a b c -> (ioPure ((a * b) * c))))) in (let _ = ((check "22 ioPipe3") (r22 = 24L)) in (let r23 = (ioRun (((ioPipe2 (ioPutStrLn "  (test 23a)")) (ioPutStrLn "  (test 23b)")) (fun a b -> (ioPure (a + b))))) in (let _ = ((check "23 ioPipe2 effects") (r23 = 0L)) in (let r24 = (ioRun (ioAll [(ioPure 10L); (ioPure 20L); (ioPure 30L)])) in (let _ = ((check "24 ioAll") ((showIntList r24) = ",10,20,30")) in (let r25 = (ioRun (ioAll [])) in (let _ = ((check "25 ioAll empty") ((showIntList r25) = "")) in (let prog26 = (((statePipe2 (statePure 10L)) (statePure 20L)) (fun a b -> (statePure (a + b)))) in (let _ = ((check "26 statePipe2") (((stateEval prog26) 0L) = 30L)) in (let prog27 = ((((statePipe3 (statePure 2L)) (statePure 3L)) (statePure 5L)) (fun a b c -> (statePure ((a + b) + c)))) in (let _ = ((check "27 statePipe3") (((stateEval prog27) 0L) = 10L)) in (let prog28 = (((statePipe2 (stateModify (fun s -> (s + 1L)))) (stateGet 0L)) (fun u s -> (statePure s))) in (let _ = ((check "28 statePipe2 modify+get") (((stateEval prog28) 5L) = 6L)) in (let prog29 = (stateAll [(statePure 1L); (statePure 2L); (statePure 3L)]) in (let r29 = ((stateEval prog29) 0L) in (let _ = ((check "29 stateAll") ((showIntList r29) = ",1,2,3")) in (let prog30 = (stateAll []) in (let r30 = ((stateEval prog30) 0L) in (let _ = ((check "30 stateAll empty") ((showIntList r30) = "")) in (let r31 = ((maybeBind (maybeGuard true)) (fun g -> (((maybePipe2 (Some 3L)) (Some 4L)) (fun a b -> (Some (a + b)))))) in (let _ = ((check "31 guard+pipe2 pass") ((maybeIntStr r31) = "7")) in (let r32 = ((maybeBind (maybeGuard false)) (fun g -> (((maybePipe2 (Some 3L)) (Some 4L)) (fun a b -> (Some (a + b)))))) in (let _ = ((check "32 guard+pipe2 fail") ((maybeIntStr r32) = "none")) in (let inner33 = (((maybePipe2 (Some 2L)) (Some 3L)) (fun a b -> (Some (a + b)))) in (let r33 = (((maybePipe2 inner33) (Some 10L)) (fun x y -> (Some (x * y)))) in (let _ = ((check "33 nested maybePipe2") ((maybeIntStr r33) = "50")) in (let inner34 = (((resultPipe2 (Ok 5L)) (Ok 3L)) (fun a b -> (Ok (a - b)))) in (let r34 = (((resultPipe2 inner34) (Ok 10L)) (fun x y -> (Ok (x + y)))) in (let _ = ((check "34 nested resultPipe2") ((resultIntStr r34) = "12")) in (let xs35 = ((listMap (fun x -> (addIfPositive x))) [1L; 2L; 3L]) in (let r35 = (maybeAll xs35) in (let s35 = (match r35 with | Some(vs) -> (showIntList vs) | None -> "none") in (let _ = ((check "35 maybeAll computed") (s35 = ",1,2,3")) in (let xs36 = ((listMap (fun x -> (addIfPositive x))) [1L; 0L; -1L; 3L]) in (let r36 = (maybeAll xs36) in (let s36 = (match r36 with | Some(vs) -> (showIntList vs) | None -> "none") in (let _ = ((check "36 maybeAll computed None") (s36 = "none")) in (printfn "Done"))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))
    0