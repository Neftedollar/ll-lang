# ll-lang Type System

## Foundation: Hindley-Milner (Algorithm W)

Type inference follows standard H-M. Types are inferred bottom-up;
annotations are optional (added where inference would be ambiguous).

**Typing rules (notation: Γ ⊢ e : τ)**

```
(VAR)   x : τ ∈ Γ
        ─────────────
        Γ ⊢ x : τ

(APP)   Γ ⊢ f : τ₁ → τ₂    Γ ⊢ a : τ₁
        ──────────────────────────────────
        Γ ⊢ f(a) : τ₂

(LAM)   Γ, x : τ₁ ⊢ e : τ₂
        ──────────────────────
        Γ ⊢ \x. e : τ₁ → τ₂

(LET)   Γ ⊢ e₁ : τ₁    Γ, x : gen(τ₁, Γ) ⊢ e₂ : τ₂
        ────────────────────────────────────────────────
        Γ ⊢ let x = e₁ in e₂ : τ₂
```

`gen(τ, Γ)` = generalize free type vars not in Γ (standard let-generalization).

## HKT — Higher-Kinded Types

Type variables can range over type constructors, not just types.

- `F` in `trait Functor F` has kind `* → *`
- `Maybe` has kind `* → *`; `Maybe Int` has kind `*`
- Compiler tracks kinds; ill-kinded applications are E001

## Tag System

A `tag T` declaration introduces a semantic label.

- `v[T]` creates a value of type `τ[T]` where `τ` = type of `v`
- `τ[T] ≠ τ` — not implicitly convertible (E005 if misused)
- Tags on numeric types participate in unit algebra:
  - `Float[m] / Float[s]` → `Float[m/s]`
  - `Float[kg] * Float[m/s]` → `Float[kg*m/s]`
  - `Float[m] + Float[m]` → `Float[m]` (same unit required, E004 if different)
- Tags on non-numeric types (Str, custom): identity check only — no algebra

## Phantom Types

`type Email[state] = Str` introduces a type with a phantom parameter.
`Email[Validated]` and `Email[Raw]` are distinct types.
The `state` parameter carries no runtime value — compile-time only.

## Exhaustive Pattern Matching

The compiler requires all constructors of a sum type to be covered.
Missing branches → E003. Wildcard `_` is allowed as a catch-all.

## No Null, No Exceptions

`null` does not exist. Functions returning optional values use `Maybe A`.
Functions that can fail use `Result A E`. All paths must be handled.
