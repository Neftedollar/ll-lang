module Std.Maybe

open LLLang.Prelude

type Maybe<'A> = 'A option

let isNone m =
    (match m with | Some(_) -> false | None -> true)

let isSome m =
    (match m with | Some(_) -> true | None -> false)

let check label ok =
    (if ok then (let _ = (printfn ((strConcat "OK ") label)) in 0L) else (let _ = (printfn ((strConcat "FAIL ") label)) in 1L))

let doubleInMaybe x =
    (Some (x * 2L))