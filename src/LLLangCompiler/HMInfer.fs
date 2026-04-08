module LLLang.HMInfer

open LLLang.AST
open LLLang.Types
open LLLang.TypedAST
open LLLang.Elaborator

// ---- Error helpers -------------------------------------------------------

let private mkErr code msg : LLError = { Code = code; Line = 0; Col = 0; Message = msg }
let private e001 t1 t2   = mkErr E001 $"E001 TypeMismatch {t1} vs {t2}"
let private e004 t1 t2   = mkErr E004 $"E004 UnitMismatch {t1} vs {t2}"
let private e005 t1 t2   = mkErr E005 $"E005 TaggedUntaggedMismatch {t1} vs {t2}"
let private e008 v  t    = mkErr E008 $"E008 OccursCheck {v} in {t}"

// ---- Helpers -------------------------------------------------------------

let private isFlex (v: Ident) = v.Length > 0 && v.[0] = '$'

let private occurs (v: Ident) (t: TypeExpr) : bool =
    let rec walk t =
        match t with
        | TyVar n -> n = v
        | TyApp(a, b) | TyFn(a, b) -> walk a || walk b
        | TyTagged(a, _) -> walk a
        | TyName _ -> false
    walk t

// ---- Unification ---------------------------------------------------------

/// Robinson unification. Returns Ok(Subst) or Error(LLError).
let rec unify (t1: TypeExpr) (t2: TypeExpr) : Result<Subst, LLError> =
    match t1, t2 with
    // Same var
    | TyVar a, TyVar b when a = b -> Ok empty

    // Flexible var on left
    | TyVar v, t when isFlex v ->
        if occurs v t then Error (e008 v t)
        else Ok (singleton v t)

    // Flexible var on right
    | t, TyVar v when isFlex v ->
        if occurs v t then Error (e008 v t)
        else Ok (singleton v t)

    // Rigid var — cannot unify with different type
    | TyVar _, _ | _, TyVar _ -> Error (e001 t1 t2)

    // Identical TyName
    | TyName a, TyName b when a = b -> Ok empty
    | TyName _, TyName _ -> Error (e001 t1 t2)

    // TyFn: unify param, apply to returns, unify returns
    | TyFn(a1, r1), TyFn(a2, r2) ->
        unify a1 a2 |> Result.bind (fun s1 ->
            unify (applyType s1 r1) (applyType s1 r2)
            |> Result.map (fun s2 -> compose s2 s1))

    // TyApp: unify functor, apply, unify arg
    | TyApp(f1, a1), TyApp(f2, a2) ->
        unify f1 f2 |> Result.bind (fun s1 ->
            unify (applyType s1 a1) (applyType s1 a2)
            |> Result.map (fun s2 -> compose s2 s1))

    // TyTagged: same unit -> unify bases; different unit -> E004; mixed -> E005
    | TyTagged(b1, u1), TyTagged(b2, u2) ->
        if u1 = u2 then unify b1 b2
        else Error (e004 t1 t2)

    | TyTagged _, _ | _, TyTagged _ -> Error (e005 t1 t2)

    // Anything else
    | _ -> Error (e001 t1 t2)

// ---- Main entry point (stub for Tasks 4-7) --------------------------------

let infer (m: LLModule) (env: Elaborator.TypeEnv) : Result<TypedModule, LLError list> =
    Error [{ Code = E001; Line = 0; Col = 0; Message = "HMInfer: not implemented" }]
