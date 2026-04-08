# H-M inference

Files:
- `src/LLLangCompiler/Types.fs` — `TypeScheme`, `Subst`, `applyType`, `compose`, `ftvType`, `generalize`, `instantiate`.
- `src/LLLangCompiler/TypedAST.fs` — typed AST node types.
- `src/LLLangCompiler/HMInfer.fs` — `unify`, `inferExpr`, `inferDecl`, top-level `infer`.

Textbook Algorithm W with a few twists dictated by the ll-lang surface
syntax (tags, unit algebra, trait constraints).

## Type variable convention

The critical distinction:

- **Rigid** vars — no `$` prefix. Example: `TyVar "A"`. These come from
  user-declared type parameters (`type Maybe A`, `fn id(x A) A = x`).
  They're treated as quantifiers in `TypeScheme.Vars` and are never
  substituted by unification.
- **Flex** vars — `$`-prefixed. Example: `TyVar "$0"`. Fresh unification
  variables generated during inference by `freshVar`. Only flex vars
  appear in `Subst` keys.

```fsharp
let private isFlex (v: Ident) = v.Length > 0 && v.[0] = '$'
```

The rule is enforced in `applyType`:

```fsharp
let rec applyType (s: Subst) (t: TypeExpr) : TypeExpr =
    match t with
    | TyVar v when isFlex v -> ... // may substitute
    | TyVar _ -> t                 // rigid — unchanged
    ...
```

And in `unify`:

```fsharp
| TyVar v, t when isFlex v -> ...     // solve flex
| t, TyVar v when isFlex v -> ...
| TyVar _, _ | _, TyVar _ -> Error (e001 ...)  // rigid != anything else
```

A rigid var against anything non-identical is a type error.

## `Subst` and `compose`

```fsharp
type Subst = Map<Ident, TypeExpr>   // always flex-var domain

let compose (s1: Subst) (s2: Subst) : Subst =
    let s2Applied = Map.map (fun _ t -> applyType s1 t) s2
    Map.fold (fun acc k v -> Map.add k v acc) s2Applied s1
```

Order matters: `compose s2 s1` applies `s1` first, then `s2`. This is
standard H-M composition.

## `unify`

```fsharp
let rec unify (t1: TypeExpr) (t2: TypeExpr) : Result<Subst, LLError> =
    match t1, t2 with
    | TyVar a, TyVar b when a = b -> Ok empty
    | TyVar v, t when isFlex v ->
        if occurs v t then Error (e008 v t) else Ok (singleton v t)
    | t, TyVar v when isFlex v ->
        if occurs v t then Error (e008 v t) else Ok (singleton v t)
    | TyVar _, _ | _, TyVar _ -> Error (e001 t1 t2)
    | TyName a, TyName b when a = b -> Ok empty
    | TyName _, TyName _ -> Error (e001 t1 t2)
    | TyFn(a1, r1), TyFn(a2, r2) -> ...  // unify args + recurse
    | TyApp(f1, a1), TyApp(f2, a2) -> ...
    | TyTagged(b1, u1), TyTagged(b2, u2) ->
        if u1 = u2 then unify b1 b2 else Error (e004 t1 t2)
    | TyTagged _, _ | _, TyTagged _ -> Error (e005 t1 t2)
    | _ -> Error (e001 t1 t2)
```

Notes:

- `occurs v t` is the occurs check — prevents `a = List[a]`. Failure
  emits `E008 OccursCheck`.
- `TyTagged` unification requires `u1 = u2` (exact unit equality). A
  mismatch emits `E004 UnitMismatch`.
- One side tagged, the other not: `E005 TagViolation`.
- Everything else: `E001 TypeMismatch`.

## Fresh variable supply

```fsharp
type FreshState = { mutable Next: int }

let freshVar (fs: FreshState) : TypeExpr =
    let n = fs.Next
    fs.Next <- n + 1
    TyVar $"${n}"
```

Monotonically numbered flex vars. Reset per-compilation.

## Generalize / instantiate

```fsharp
let generalize (env: Env) (ty: TypeExpr) : TypeScheme =
    let envFtv = ftvEnv env
    let toQuant = Set.difference (ftvType ty) envFtv |> Set.toList |> List.sort
    { Vars = toQuant; Body = ty }

let instantiate (fs: FreshState) (sch: TypeScheme) : TypeExpr =
    let subst =
        sch.Vars
        |> List.map (fun v -> v, freshVar fs)
        |> Map.ofList
    applyTypeAll subst sch.Body
```

- `generalize` closes over flex vars that aren't also free in the
  environment (standard let-generalization).
- `instantiate` replaces every quantified var (rigid or flex) with a
  fresh flex var. This is the only place rigid vars get rewritten —
  and it uses a separate `applyTypeAll` that does not restrict to
  flex-var domain.

## `inferExpr`

Walks an `Expr` and returns `(Subst, TypeExpr, TypedExpr)`. Canonical
cases:

### `EVar x`

```fsharp
| EVar x ->
    match Map.tryFind x env with
    | None -> // emit E002, return fresh
    | Some sch ->
        let ty = instantiate st.Fresh sch
        (empty, ty, mkTyped st (TEVar x) ty)
```

Variable lookups always instantiate — a polymorphic `id` can be used
at `Int` and `Str` in the same scope.

### `EApp(f, a)`

```fsharp
| EApp(f, a) ->
    let (s1, tauF, teF) = inferExpr env st f
    let env1 = applyEnv s1 env
    let (s2, tauA, teA) = inferExpr env1 st a
    let beta = freshVar st.Fresh
    let s3 = unifyS st (applyType s2 tauF) (TyFn(tauA, beta))
    let sAll = compose s3 (compose s2 s1)
    let retTy = applyType sAll beta
    ...
```

This is the Algorithm W APP rule verbatim: infer `f`, then `a`, then
unify `f`'s type with `arg -> β`, yielding `β` as the result.

### `ELet(x, e1, Some body)`

```fsharp
let (s1, tau1, te1) = inferExpr env st e1
let env1 = applyEnv s1 env
let sch = generalize env1 tau1       // let-generalization
let env2 = Map.add x sch env1
let (s2, tau2, te2) = inferExpr env2 st body
```

### `EMatch(branches)`

Match is inferred as a lambda from scrutinee to result. Fresh flex vars
`alpha` (scrutinee type) and `beta` (result type) are allocated; each
branch unifies its pattern against `alpha` and its body against `beta`.

```fsharp
let alpha = freshVar st.Fresh
let beta  = freshVar st.Fresh
// fold each branch: unify pattern type with alpha, body type with beta
let lamTy = TyFn(scrutTy, retTy)
(sAll, lamTy, mkTyped st (TELam([scrutName, scrutTy], matchTE)) lamTy)
```

### `ETagged(e, tag)`

```fsharp
let (s, tau, te) = inferExpr env st e
let ty = TyTagged(tau, UName tag)
```

Just wraps whatever type the inner expression has with a `UName` tag.

## `inferDecl`

### `DFn` — function declarations

The trickiest case because ll-lang has:

1. Type parameters declared in param/return types (rigid vars via
   `normalizeTy` that converts single-uppercase `TyName A` to
   `TyVar A`).
2. Optional return type annotations.
3. Match-body special-case: when the body is `EMatch` and the function
   has at least one param, the *last* param is treated as the
   scrutinee, so patterns unify against it rather than against a fresh
   dummy.

```fsharp
let declaredTyVars =
    let paramVars = normParams |> List.collect (fun (_, ty) -> collectRigidVars ty)
    let retVars = normRet |> Option.map collectRigidVars |> Option.defaultValue []
    (paramVars @ retVars) |> List.distinct |> List.sort
```

After inference, the rigid type vars from the signature are added to
the generalized scheme's `Vars` list so they remain quantified:

```fsharp
let baseSch = generalize (applyEnv sAll env) fnTy
let sch = { baseSch with Vars = (declaredTyVars @ baseSch.Vars) |> List.distinct |> List.sort }
```

### `DImpl` — trait implementations

Each impl method is inferred like a `DFn` but stored under a mangled
name `methodName_implType` in the environment. Example: `map` in
`impl Functor Maybe` becomes `map_Maybe` in the H-M environment and in
the Codegen output.

The mangling trick is how ll-lang currently dispatches without needing
a dictionary-passing transform: at call sites, you write the original
name and the compiler selects the right mangled entry based on the
instantiated type. (In the current phase, this resolution is manual
via constrained generics rather than fully automatic.)

## Trait dispatch (`TraitIndex`)

`TypedModule.Dispatch : Map<ExprId, DispatchInfo>` is the dispatch
table. Each entry records `(TraitName, ImplType, ResolvedName)` for a
trait-method call site.

In the current phase, `st.Dispatch` is initialized empty and populated
when trait method calls are resolved during inference. The machinery
is in place but the automatic resolution for `map xs` where `xs :
Maybe[Int]` is not yet wired — the user writes `map_Maybe` or uses a
constrained generic `[F: Functor]` that passes the impl through.

Completing automatic resolution is on the backlog.

## Error emission

All errors go through `InferState.Errors : LLError list`. Inference
never throws — `unifyS` catches failed unifications:

```fsharp
let private unifyS (st: InferState) (t1: TypeExpr) (t2: TypeExpr) : Subst =
    match unify t1 t2 with
    | Ok s -> s
    | Error e -> st.Errors <- st.Errors @ [e]; empty
```

Returning an empty substitution on failure means the tree walk
continues, so a single file can surface multiple errors in one pass.

## Tests

`tests/LLLangTests/HMInferTests.fs` has dedicated helpers:

```fsharp
let private inferOk (src: string) : TypedModule = ...
let private inferErrs (src: string) : LLError list = ...
let private schemeOf (tm: TypedModule) (name: string) : TypeScheme = ...
```

Use `inferOk` to assert a program type-checks. Use `inferErrs` to
assert a specific error code fires. Use `schemeOf tm "functionName"`
to pin a function's inferred polymorphic scheme. Tests include every
operator and error code.

Run:

```bash
dotnet test --filter HMInferTests
```
