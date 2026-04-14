module Std.Lazy

type Lazy<'A> =
    | Delayed of (int64 -> 'A)
    | Ready of 'A

let rec lazyDelay f =
    (Delayed f)

and lazyReady x =
    (Ready x)

and lazyForce node =
    (match node with | Ready(v) -> (v, (Ready v)) | Delayed(f) -> (let v = (f 0L) in (v, (Ready v))))

and lazyValue node =
    (match (lazyForce node) with | (v, _) -> v)

and lazyMap f node =
    (Delayed (fun ignored -> (f (lazyValue node))))

and lazyBind node k =
    (Delayed (fun ignored -> (let v = (lazyValue node) in (lazyValue (k v)))))