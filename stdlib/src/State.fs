module Std.State

type StateUnit =
    | StateUnit

type StatePair<'A, 'S> =
    | MkStatePair of 'A * 'S

type State<'S, 'A> =
    | MkState of ('S -> StatePair<'A, 'S>)

let stateRun = (fun st -> (fun s -> (match st with | MkState(f) -> (f s))))

let statePure = (fun a -> (MkState (fun s -> (MkStatePair (a, s)))))

let stateMap = (fun f -> (fun st -> (MkState (fun s -> (match st with | MkState(run) -> (match (run s) with | MkStatePair(a, s1) -> (MkStatePair ((f a), s1))))))))

let stateBind = (fun st -> (fun k -> (MkState (fun s -> (match st with | MkState(run) -> (match (run s) with | MkStatePair(a, s1) -> (match (k a) with | MkState(run2) -> (run2 s1))))))))

let stateEval = (fun st -> (fun s -> (match st with | MkState(run) -> (match (run s) with | MkStatePair(a, _) -> a))))

let stateExec = (fun st -> (fun s -> (match st with | MkState(run) -> (match (run s) with | MkStatePair(_, s1) -> s1))))

let rec pickSame x y =
    x

and stateGet dummy =
    (MkState (fun s -> (let s2 = ((pickSame s) dummy) in (MkStatePair (s2, s2)))))

let statePut = (fun next -> (MkState (fun ignored -> (let next2 = ((pickSame next) ignored) in (MkStatePair (StateUnit, next2))))))

let stateModify = (fun f -> (MkState (fun s -> (let s1 = (f s) in (MkStatePair (StateUnit, s1))))))