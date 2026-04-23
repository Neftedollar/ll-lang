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
| `E007` | PlatformMismatch    | Platform module unavailable on selected target |
| `E008` | InfiniteType        | Unification would produce an infinite type (occurs check) |
| `E026` | UnknownExternalMapping | Unknown external declaration for selected target |
| `E027` | InvalidFixityAssoc | Invalid fixity associativity for current phase |
| `E028` | InvalidFixityPrecedence | Fixity precedence outside supported range |
| `E029` | DuplicateFixity | Duplicate fixity declaration for same operator |
| `E030` | ReservedOperatorFixity | Fixity declaration targets reserved/unsupported operator |

Below: the minimal program that reproduces each error, sourced from
`spec/examples/invalid/`.

## E001 — TypeMismatch

```lll
-- expect: E001
module Invalid.E001

greet(name Str) = name

bad = greet 42
```

`greet` expects `Str`, got `Int`.

**Compact output example:**
```
E001 TypeMismatch Int vs Str
```

**Fix:** pass a string, not an int. `bad = greet "alice"`.

## E002 — UnboundVar

```lll
-- expect: E002
module Invalid.E002

broken = undefinedFunction 42
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

Shape = Circle Float | Rect Float Float | Empty

area(s Shape) =
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

mkM(x Float) = x[m]
mkKg(x Float) = x[kg]

speed(d Float[m])(t Float[kg]) = d / t

bad = speed (mkKg 5.0) (mkM 2.0)
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

getUser(id Str[UserId]) = id

bad = getUser "raw-string"
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
  map(f A -> B)(fa F[A]) F[B]

Box A = Box A

doubled[F: Functor](xs F[Int]) = map (\x. x * 2) xs

bad = doubled (Box 5)
```

`doubled` is constrained to any `Functor F`, but there's no
`impl Functor Box` in scope.

**Compact output example:**
```
E006 MissingImpl Functor for Box
```

**Fix:** add an `impl Functor Box` block defining `map`, or call `doubled` with
a type that already has an impl (e.g. `Maybe`).

## E007 — PlatformMismatch

Fires when a `Platform.*` module is imported but the selected target does
not support that module surface.

**Compact output example:**
```
E007 2:1 PlatformMismatch module:Platform.DotNet.ASP target:python
```

**Fix:** switch to a target that supports the imported module, or remove the
platform-specific import.

## E008 — InfiniteType

```lll
-- no corpus file (triggered by recursive lambda without annotation)
oops = \x. x x
```

The unifier tries to solve `a = a -> b`, which creates a cycle. The
occurs check catches it.

**Compact output example:**
```
E008 OccursCheck $0 in TyFn(TyVar "$0", TyVar "$1")
```

**Fix:** recursive self-application requires an explicit fixed-point
combinator or recursive type declaration. In practice, avoid the pattern.

## E026 — UnknownExternalMapping

```lll
-- expect: E026
module Invalid.E026

external host_log(msg Str) Unit
main() Unit = host_log "hi"
```

The selected backend has no mapping for the declared external name. Compilation
now fails before codegen.

**Compact output example:**
```
E026 2:1 UnknownExternalMapping target:python name:host_log
```

**Fix:** add a backend mapping for that external, or switch to a known external
name.

## E027 — InvalidFixityAssoc

```lll
-- expect: E027
module Invalid.E027

infixl +
ok(a Int)(b Int) = a == b
```

The declaration is malformed (`infixl` missing precedence and operator position).

**Compact output example:**
```
E027 3:1 InvalidFixityAssoc assoc:invalid
```

**Fix:** use canonical syntax, e.g. `infixl 4 ==`.

## E028 — InvalidFixityPrecedence

```lll
-- expect: E028
module Invalid.E028

infixl 12 +
add(a Int)(b Int) = a + b
```

Precedence must be in range `1..9`.

**Compact output example:**
```
E028 3:1 InvalidFixityPrecedence value:12
```

**Fix:** choose a precedence inside `1..9`.

## E029 — DuplicateFixity

```lll
-- expect: E029
module Invalid.E029

infixl 6 +
infixl 7 +
add(a Int)(b Int) = a + b
```

Only one fixity declaration is allowed per operator in a module.

**Compact output example:**
```
E029 4:1 DuplicateFixity op:+
```

**Fix:** keep exactly one declaration for `+`.

## E030 — ReservedOperatorFixity

```lll
-- expect: E030
module Invalid.E030

infixl 6 =
x = 1
```

`=` is reserved and not part of the supported fixity surface.

**Compact output example:**
```
E030 3:1 ReservedOperatorFixity op:=
```

**Fix:** declare fixity only for supported expression operators.

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
