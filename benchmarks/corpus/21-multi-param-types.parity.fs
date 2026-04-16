// FILE: 21-multi-param-types.fs
module Examples.MultiParam

type Maybe<'A> = 'A option

type Color =
    | Red
    | Black

type RBMap<'K, 'V> =
    | Leaf
    | Node of Color * RBMap<'K, 'V> * 'K * 'V * RBMap<'K, 'V>

let rbEmpty =
    Leaf

let rec rbSize m =
    (match m with | Leaf -> 0L | Node(_, left, _, _, right) -> ((1L + (rbSize left)) + (rbSize right)))

[<EntryPoint>]
let main (argv: string[]) =
    (let m = rbEmpty in (rbSize m))
    0