module LLLang.Types

open LLLang.AST

/// Polymorphic type scheme: ∀ Vars. Body
type TypeScheme = { Vars: Ident list; Body: TypeExpr }

/// Monomorphic scheme helper
let mono (t: TypeExpr) : TypeScheme = { Vars = []; Body = t }

/// Typing environment: name → scheme
type Env = Map<Ident, TypeScheme>

/// Substitution: flexible var name → TypeExpr
type Subst = Map<Ident, TypeExpr>

/// Empty substitution
let empty : Subst = Map.empty

/// True if v is a flexible (unification) variable (starts with '$')
let private isFlex (v: Ident) = v.Length > 0 && v.[0] = '$'

let singleton (v: Ident) (t: TypeExpr) : Subst = Map.ofList [v, t]

/// Apply substitution to a type (only replaces flexible vars)
let rec applyType (s: Subst) (t: TypeExpr) : TypeExpr =
    match t with
    | TyVar v when isFlex v ->
        match Map.tryFind v s with
        | Some t' -> applyType s t'   // follow chain
        | None -> t
    | TyVar _ -> t   // rigid — unchanged
    | TyName _ -> t
    | TyApp(a, b) -> TyApp(applyType s a, applyType s b)
    | TyFn(a, b) -> TyFn(applyType s a, applyType s b)
    | TyTagged(a, u) -> TyTagged(applyType s a, u)

let applyScheme (s: Subst) (sch: TypeScheme) : TypeScheme =
    // Remove quantified vars from substitution domain before applying
    let s' = List.fold (fun acc v -> Map.remove v acc) s sch.Vars
    { sch with Body = applyType s' sch.Body }

let applyEnv (s: Subst) (env: Env) : Env =
    Map.map (fun _ sch -> applyScheme s sch) env

/// compose s1 s2: apply s2 first, then s1
/// Result: apply s1 to each value in s2, then add all of s1
let compose (s1: Subst) (s2: Subst) : Subst =
    let s2Applied = Map.map (fun _ t -> applyType s1 t) s2
    Map.fold (fun acc k v -> Map.add k v acc) s2Applied s1

/// Free flexible type variables in a type
let rec ftvType (t: TypeExpr) : Set<Ident> =
    match t with
    | TyVar v when isFlex v -> Set.singleton v
    | TyVar _ | TyName _ -> Set.empty
    | TyApp(a, b) | TyFn(a, b) -> Set.union (ftvType a) (ftvType b)
    | TyTagged(a, _) -> ftvType a

let ftvScheme (sch: TypeScheme) : Set<Ident> =
    Set.difference (ftvType sch.Body) (Set.ofList sch.Vars)

let ftvEnv (env: Env) : Set<Ident> =
    Map.fold (fun acc _ sch -> Set.union acc (ftvScheme sch)) Set.empty env

/// Mutable fresh variable supply
type FreshState = { mutable Next: int }

let newFreshState () : FreshState = { Next = 0 }

let freshVar (fs: FreshState) : TypeExpr =
    let n = fs.Next
    fs.Next <- n + 1
    TyVar $"${n}"

/// Generalize a type over free flex vars not appearing in env
let generalize (env: Env) (ty: TypeExpr) : TypeScheme =
    let envFtv = ftvEnv env
    let toQuant = Set.difference (ftvType ty) envFtv |> Set.toList |> List.sort
    { Vars = toQuant; Body = ty }

/// Apply a substitution that can replace ANY var (flex or rigid) — used only for instantiation
let private applyTypeAll (s: Map<Ident, TypeExpr>) (t: TypeExpr) : TypeExpr =
    let rec go t =
        match t with
        | TyVar v ->
            match Map.tryFind v s with
            | Some t' -> t'
            | None -> t
        | TyName _ -> t
        | TyApp(a, b) -> TyApp(go a, go b)
        | TyFn(a, b) -> TyFn(go a, go b)
        | TyTagged(a, u) -> TyTagged(go a, u)
    go t

/// Instantiate a scheme: replace each quantified var with a fresh flex var
let instantiate (fs: FreshState) (sch: TypeScheme) : TypeExpr =
    let subst =
        sch.Vars
        |> List.map (fun v -> v, freshVar fs)
        |> Map.ofList
    applyTypeAll subst sch.Body

/// Convert Phase-3 elaborator env to H-M Env.
/// Rigid vars (uppercase or lowercase, no $) become quantifiers.
/// "?" wildcards become fresh-looking quantifiers (handled at instantiate time).
let fromElaboratorEnv (e3: LLLang.Elaborator.TypeEnv) : Env =
    let collectRigid (t: TypeExpr) : Set<Ident> =
        let rec walk t acc =
            match t with
            | TyVar v when not (isFlex v) -> Set.add v acc
            | TyApp(a, b) | TyFn(a, b) -> walk a acc |> walk b
            | TyTagged(a, _) -> walk a acc
            | _ -> acc
        walk t Set.empty
    Map.map (fun _ ty ->
        let rigids = collectRigid ty |> Set.toList |> List.sort
        { Vars = rigids; Body = ty }
    ) e3
