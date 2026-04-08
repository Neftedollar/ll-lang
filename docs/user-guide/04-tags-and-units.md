# Tags and units

ll-lang has a single `tag` mechanism that covers two distinct use cases:

1. **Newtype-style labels** — distinguish strings that happen to look alike
   (`UserId` vs `Email`) without runtime overhead.
2. **Units of measure** — dimensional analysis on numeric types at compile
   time (`Float[m]`, `Float[s]`, derived `Float[m/s]`).

Tags applied to the same base type with different names are incompatible —
passing one where the other is expected is a compile error.

## Declaring tags

```lll
tag UserId
tag Email
tag m
tag s
tag kg
```

A tag has no body — it's a pure type-level label.

## Applying tags to values

Postfix `[Tag]` syntax:

```lll
let uid = "user-42"[UserId]     -- Str[UserId]
let dist = 5.0[m]               -- Float[m]
```

The base type is whatever the inner expression had; the tag is attached on
top. Internally this is `TyTagged(baseType, UName tagName)`.

## Tagged parameters

Write the full `BaseType[Tag]` in the annotation:

```lll
fn getUser(id Str[UserId]) Maybe[Str] = Some "alice"
fn sendEmail(to Str[Email]) = to
```

Calling `getUser "raw-string"` is a compile error (`E005 TagViolation`):
the argument has base type `Str` but no `UserId` tag. You must explicitly
tag the value: `getUser "u1"[UserId]`.

## Unit algebra (numeric tags)

When a tag is applied to a numeric base (`Int` or `Float`), the compiler
tracks units through arithmetic operations:

```lll
tag m
tag s

fn speed(d Float[m])(t Float[s]) = d / t
-- inferred return: Float[m/s]
```

Supported unit operations (compile-time only):

- Multiplication: `Float[m] * Float[s]` → `Float[m*s]`
- Division: `Float[m] / Float[s]` → `Float[m/s]`
- Addition / subtraction require matching units; `Float[m] + Float[kg]`
  raises `E004 UnitMismatch`
- Exponentiation via `^`: `Float[m]^2` → `Float[m^2]`

Non-numeric tags (`Str[UserId]`, etc.) never compose — only identity check.

## Phantom types for state machines

A type with a bracketed phantom parameter can carry state information with
zero runtime cost:

```lll
tag Validated
tag Raw

type Email[state] = Str

fn validate(s Str) Result[Email[Validated] Str] =
  if s != "" then Ok s
  else Err "empty"
```

`Email[Validated]` and `Email[Raw]` are distinct types even though both
erase to `Str` at runtime. A function declared as
`fn send(e Email[Validated]) ...` cannot be called with an unvalidated
email — the compiler enforces the state transition.

## What's enforced

| Situation                                    | Result                |
|-----------------------------------------------|-----------------------|
| Passing `Str` where `Str[UserId]` expected    | `E005 TagViolation`   |
| Passing `Str[Email]` where `Str[UserId]`      | `E001 TypeMismatch` (different tags, same base, non-numeric) |
| Passing `Float[kg]` where `Float[m]`          | `E004 UnitMismatch`   |
| Adding `Float[m] + Float[s]`                  | `E004 UnitMismatch`   |
| `Float[m] / Float[s]`                         | OK, produces `Float[m/s]` |

## Worked example from the corpus

From `spec/examples/valid/03-tags.lll`:

```lll
module Examples.Tags

type Maybe A = Some A | None
type Result A E = Ok A | Err E

tag UserId
tag Email
tag m
tag s
tag kg

let uid = "user-42"[UserId]

fn getUser(id Str[UserId]) Maybe[Str] = Some "alice"
fn sendEmail(to Str[Email]) = to

fn speed(d Float[m])(t Float[s]) = d / t

tag Validated
tag Raw

type Email[state] = Str

fn validate(s Str) Result[Email[Validated] Str] =
  if s != "" then Ok s
  else Err "empty"
```

## Codegen note

Tags erase completely in the emitted F# source. `Float[m]` becomes `float`.
The checks all happen at compile time — runtime has no awareness of units.
