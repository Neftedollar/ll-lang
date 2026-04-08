# Error codes

All compiler errors follow a compact, single-line format:

```
EXXX line:col ErrorName <details>
```

Designed so an LLM agent can parse errors without extracting from prose.

The table:

| Code   | Name                | When it fires                                |
|--------|---------------------|-----------------------------------------------|
| `E001` | TypeMismatch        | Two rigid types fail to unify                |
| `E002` | UnboundVar          | Identifier not in scope                      |
| `E003` | NonExhaustiveMatch  | Match expression misses a sum constructor    |
| `E004` | UnitMismatch        | Two numeric tags have the same base but different units |
| `E005` | TagViolation        | Untagged value passed where tag required     |
| `E006` | MissingImpl         | No `impl` found for a constrained type       |
| `E007` | PlatformMismatch    | Platform module unavailable on target (reserved) |
| `E008` | InfiniteType        | Unification would produce an infinite type (occurs check) |

Below: the minimal program that reproduces each error, sourced from
`spec/examples/invalid/`.

## E001 — TypeMismatch

```lll
-- expect: E001
module Invalid.E001

fn greet(name Str) Str = name

let bad = greet 42
```

`greet` expects `Str`, got `Int`.

**Compact output example:**
```
E001 TypeMismatch Int vs Str
```

**Fix:** pass a string, not an int. `let bad = greet "alice"`.

## E002 — UnboundVar

```lll
-- expect: E002
module Invalid.E002

fn broken = undefinedFunction 42
```

`undefinedFunction` is not declared anywhere in the module and has no builtin.

**Compact output example:**
```
E002 UnboundVar undefinedFunction
```

**Fix:** declare the missing function or import a module that provides it.

## E003 — NonExhaustiveMatch

```lll
-- expect: E003
module Invalid.E003

type Shape = Circle Float | Rect Float Float | Empty

fn area(s Shape) Float =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
```

The `Empty` constructor of `Shape` is not covered.

**Compact output example:**
```
E003 0:0 NonExhaustiveMatch Shape missing:Empty
```

**Fix:** add a branch for the missing constructor, or use `_ -> default` as
a catch-all.

## E004 — UnitMismatch

```lll
-- expect: E004
module Invalid.E004

unit m
unit kg

fn mkM(x Float) Float[m] = x
fn mkKg(x Float) Float[kg] = x

fn speed(d Float[m])(t Float[kg]) = d / t

let bad = speed (mkKg 5.0) (mkM 2.0)
```

`speed` is declared with `d Float[m]` but receives `Float[kg]`.

**Compact output example:**
```
E004 UnitMismatch Float[kg] vs Float[m]
```

**Fix:** produce the value with the expected unit. Unit mismatches almost
always mean the math is wrong somewhere upstream.

## E005 — TagViolation

```lll
-- expect: E005
module Invalid.E005

tag UserId

fn getUser(id Str[UserId]) Str = id

let bad = getUser "raw-string"
```

`getUser` expects `Str[UserId]`; a raw `Str` is not automatically tagged.

**Compact output example:**
```
E005 TaggedUntaggedMismatch Str vs Str[UserId]
```

**Fix:** tag the value explicitly: `getUser "raw-string"[UserId]`.

## E006 — MissingImpl

```lll
-- expect: E006
module Invalid.E006

trait Functor F =
  fn map(f A->B)(fa F[A]) F[B]

type Box A = Box A

fn doubled[F: Functor](xs F[Int]) F[Int] = map (\x. x * 2) xs

let bad = doubled (Box 5)
```

`doubled` is constrained to any `Functor F`, but there's no
`impl Functor Box` in scope.

**Compact output example:**
```
E006 MissingImpl Functor for Box
```

**Fix:** add `impl Functor Box = fn map ... = ...` or call `doubled` with
a type that already has an impl (e.g. `Maybe`).

## E007 — PlatformMismatch (reserved)

Fires when a `Platform.*` module is imported on a target that doesn't
implement it. The code is reserved in `spec/error-codes.md` but
`Platform.*` is not implemented in the current phase, so this error is
not emitted in practice.

## E008 — InfiniteType

```lll
-- no corpus file (triggered by recursive lambda without annotation)
let oops = \x. x x
```

The unifier tries to solve `a = a -> b`, which creates a cycle. The
occurs check catches it.

**Compact output example:**
```
E008 OccursCheck $0 in TyFn(TyVar "$0", TyVar "$1")
```

**Fix:** recursive self-application requires an explicit fixed-point
combinator or recursive type declaration. In practice, avoid the pattern.

## Testing expected errors

Every `.lll` file in `spec/examples/invalid/` declares the expected code
on line 1:

```lll
-- expect: E001
module Invalid.E001
...
```

The compiler test runner asserts that running the elaborator+inference
pipeline on that file produces exactly that error code.
