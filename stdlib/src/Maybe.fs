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

and __test_main_Maybe () =
    (let r1 = ((maybeMap (fun x -> (x + 1L))) (Some 41L)) in (let ok1 = (match r1 with | Some(v) -> (v = 42L) | None -> false) in (let _ = ((check "1 maybeMap Some") ok1) in (let r2 = ((maybeMap (fun x -> (x + 1L))) None) in (let ok2 = (match r2 with | Some(_) -> false | None -> true) in (let _ = ((check "2 maybeMap None") ok2) in (let r3 = ((maybeBind (Some 10L)) doubleInMaybe) in (let ok3 = (match r3 with | Some(v) -> (v = 20L) | None -> false) in (let _ = ((check "3 maybeBind Some") ok3) in (let d1 = ((maybeWithDefault 99L) (Some 7L)) in (let d2 = ((maybeWithDefault 99L) None) in (let _ = ((check "4 maybeWithDefault") (if (d1 = 7L) then (d2 = 99L) else false)) in (let _ = ((check "5 isSome/isNone") (if (isSome (Some 1L)) then (isNone None) else false)) in 0L)))))))))))))