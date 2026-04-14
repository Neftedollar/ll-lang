module Std.Map

open LLLang.Prelude
open Std.Maybe

type Color =
    | Red
    | Black

type RBMap<'K, 'V> =
    | Leaf
    | Node of Color * RBMap<'K, 'V> * 'K * 'V * RBMap<'K, 'V>

let rec mapEmpty =
    Leaf

and mapSize m =
    (match m with | Leaf -> 0L | Node(_, left, _, _, right) -> ((1L + (mapSize left)) + (mapSize right)))

and balanceLeft left k v right =
    (match left with | Node(Red, ll, lk, lv, lr) -> (match ll with | Node(Red, a, xk, xv, b) -> (Node (Red, (Node (Black, a, xk, xv, b)), lk, lv, (Node (Black, lr, k, v, right)))) | _ -> (match lr with | Node(Red, b, yk, yv, c) -> (Node (Red, (Node (Black, ll, lk, lv, b)), yk, yv, (Node (Black, c, k, v, right)))) | _ -> (Node (Black, left, k, v, right)))) | _ -> (Node (Black, left, k, v, right)))

and balanceRight left k v right =
    (match right with | Node(Red, rl, rk, rv, rr) -> (match rl with | Node(Red, b, yk, yv, c) -> (Node (Red, (Node (Black, left, k, v, b)), yk, yv, (Node (Black, c, rk, rv, rr)))) | _ -> (match rr with | Node(Red, c, zk, zv, d) -> (Node (Red, (Node (Black, left, k, v, rl)), rk, rv, (Node (Black, c, zk, zv, d)))) | _ -> (Node (Black, left, k, v, right)))) | _ -> (Node (Black, left, k, v, right)))

and balance c left k v right =
    (match c with | Red -> (Node (Red, left, k, v, right)) | Black -> (match left with | Node(Red, _, _, _, _) -> (let result = ((((balanceLeft left) k) v) right) in (match result with | Node(Red, _, _, _, _) -> result | _ -> ((((balanceRight left) k) v) right))) | _ -> ((((balanceRight left) k) v) right)))

and ins cmp k v m =
    (match m with | Leaf -> (Node (Red, Leaf, k, v, Leaf)) | Node(c, left, nk, nv, right) -> (let d = ((cmp k) nk) in (if (d < 0L) then (((((balance c) ((((ins cmp) k) v) left)) nk) nv) right) else (if (d > 0L) then (((((balance c) left) nk) nv) ((((ins cmp) k) v) right)) else (Node (c, left, nk, v, right))))))

and mapInsert cmp k v m =
    (let t = ((((ins cmp) k) v) m) in (match t with | Leaf -> Leaf | Node(_, left, nk, nv, right) -> (Node (Black, left, nk, nv, right))))

and mapLookup cmp k m =
    (match m with | Leaf -> None | Node(_, left, nk, nv, right) -> (let d = ((cmp k) nk) in (if (d < 0L) then (((mapLookup cmp) k) left) else (if (d > 0L) then (((mapLookup cmp) k) right) else (Some nv)))))

and mapContains cmp k m =
    (match (((mapLookup cmp) k) m) with | Some(_) -> true | None -> false)

and mapFold f acc m =
    (match m with | Leaf -> acc | Node(_, left, k, v, right) -> (let acc1 = (((mapFold f) acc) left) in (let acc2 = (((f acc1) k) v) in (((mapFold f) acc2) right))))

and mapKeys m =
    (match m with | Leaf -> [] | Node(_, left, k, _, right) -> (let lkeys = (mapKeys left) in (let rkeys = (mapKeys right) in ((listAppend lkeys) (k :: rkeys)))))

and intCmp a b =
    (if (a < b) then (0L - 1L) else (if (a > b) then 1L else 0L))

and strCmp a b =
    (if (a < b) then (0L - 1L) else (if (a > b) then 1L else 0L))

and mapCheck label ok =
    (if ok then (let _ = (printfn ((strConcat "OK ") label)) in 0L) else (let _ = (printfn ((strConcat "FAIL ") label)) in 1L))