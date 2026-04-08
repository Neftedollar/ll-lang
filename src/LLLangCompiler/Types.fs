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

// TODO: Task 2 — implement all stubs below

let singleton (v: Ident) (t: TypeExpr) : Subst = Map.ofList [v, t]

let applyType (s: Subst) (t: TypeExpr) : TypeExpr = t  // TODO: Task 2

let applyScheme (s: Subst) (sch: TypeScheme) : TypeScheme = sch  // TODO: Task 2

let applyEnv (s: Subst) (env: Env) : Env = env  // TODO: Task 2

let compose (s1: Subst) (s2: Subst) : Subst = Map.fold (fun acc k v -> Map.add k v acc) s1 s2  // TODO: Task 2

let ftvType (t: TypeExpr) : Set<Ident> = Set.empty  // TODO: Task 2

let ftvScheme (sch: TypeScheme) : Set<Ident> = Set.empty  // TODO: Task 2

let ftvEnv (env: Env) : Set<Ident> = Set.empty  // TODO: Task 2

/// Mutable fresh variable supply
type FreshState = { mutable Next: int }

let newFreshState () : FreshState = { Next = 0 }

let freshVar (fs: FreshState) : TypeExpr =
    let n = fs.Next
    fs.Next <- n + 1
    TyVar ($"${n}")  // TODO: Task 2

let generalize (env: Env) (ty: TypeExpr) : TypeScheme = { Vars = []; Body = ty }  // TODO: Task 2

let instantiate (fs: FreshState) (sch: TypeScheme) : TypeExpr = sch.Body  // TODO: Task 2

let fromElaboratorEnv (e3: LLLang.Elaborator.TypeEnv) : Env =
    Map.map (fun _ ty -> { Vars = []; Body = ty }) e3  // TODO: Task 2
