# Syntax

Every ll-lang file begins with `module`, followed by optional `import`
declarations, then top-level `let`, `fn`, `type`, `tag`, `trait`, and `impl`
declarations.

Indentation is significant. Comments start with `--` and run to end of line;
both full-line (`-- header`) and trailing (`x + 1 -- increment`) forms are
accepted. ASCII only. No semicolons, no braces.

## Module header

```lll
module Examples.Basics
```

The module path is one or more uppercase-starting segments separated by dots.
Must be the first non-comment token in the file.

## `let`: top-level constants

```lll
let pi = 3.14159
let greeting = "hello"
let ready = true
let bang = '!'
```

Type is inferred from the literal. Integer literals default to `Int` (emitted
as `int64`), decimal literals to `Float`, quoted text to `Str`, `true`/`false`
to `Bool`, and single-quoted characters to `Char`.

### Literals in detail

| Form        | Type   | Notes                                       |
|-------------|--------|---------------------------------------------|
| `42`        | `Int`  | 64-bit signed                               |
| `3.14`      | `Float`| requires a decimal point                    |
| `"hi"`      | `Str`  | supports escapes `\n`, `\t`, `\\`, `\"`     |
| `true` / `false` | `Bool` | keywords, not constructors             |
| `'c'`       | `Char` | supports escapes `\n`, `\t`, `\\`, `\'`     |

## `fn`: functions

Parameters come in individual `(name Type)` groups. Juxtaposition with
separate parens means currying, not multi-arg:

```lll
fn add(a Int)(b Int) Int = a + b
fn double(x Int) = x * 2
```

The return type after the last param is optional when H-M can infer it.
`fn double` above has no return annotation — the inferred type is
`Int -> Int`.

### Multi-line body

Indent the body one level:

```lll
fn clamp(x Int)(lo Int)(hi Int) Int =
  if x < lo then lo
  else if x > hi then hi
  else x
```

### Zero-arg functions

Use empty parens:

```lll
fn main() = printfn "Hello, ll-lang!"
```

### Local `let`

```lll
fn example = let y = double 5 in y + 1
```

The `in` form introduces a local binding. It's an expression and returns the
body's value.

## Lambdas

```lll
let triple = \x. x * 3
```

Backslash, parameter names, a period, then the body expression. Multiple
parameters are supported:

```lll
let add = \a b. a + b
```

## `if` / `then` / `else`

```lll
fn abs(n Int) Int =
  if n < 0 then 0 - n
  else n
```

`if` is an expression — both arms must have the same type. There's no standalone
statement form. Chain with `else if`:

```lll
if x < 0 then "neg"
else if x == 0 then "zero"
else "pos"
```

## `type`: algebraic data types

### Sum types (tagged unions)

```lll
type Shape = Circle Float | Rect Float Float | Empty
```

Constructors are uppercase identifiers followed by zero or more type arguments.
Branches are separated by `|`.

A multi-line form with an optional leading `|` is also accepted for readability:

```lll
type Color =
  | Red
  | Green
  | Blue
```

### Product types (records)

```lll
type Point = x Float, y Float
```

Field syntax is `name Type`, comma-separated. No braces.

### Parametric types

Bare type parameters after the type name:

```lll
type Maybe A = Some A | None
type Result A E = Ok A | Err E
```

### Phantom type parameters

Bracketed parameters carry no runtime value — they exist purely to distinguish
types at compile time:

```lll
type Email[state] = Str
```

See [04-tags-and-units](04-tags-and-units.md) for the phantom state pattern.

## `match`: pattern matching

There are two surface forms. The compiler enforces exhaustiveness (error
`E003`) in both.

**Shortcut form** — when a function body is a single match over its last
parameter, omit the `match ... with` header and list branches directly:

```lll
fn area(s Shape) Float =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty -> 0.0
```

**Explicit form** — `match <scrutinee> with` followed by branches. Useful
inside a larger expression or after a `let ... in`:

```lll
fn describe(m Maybe[Int]) Int =
  let fallback = 0 in
  match m with
    | Some n -> n
    | None -> fallback
```

Newlines are tolerated between `=` and `match`, between `in` and `match`,
and before the first `|` — both `match m with | Some n -> n ...` on one
line and the indented variant above parse identically.

Supported patterns:

- `ConstructorName arg1 arg2` — constructor pattern, binds args by position
  (e.g. `Some n`, `None`, `Rect w h`)
- `name` — variable binding (catches anything, binds to `name`)
- `42`, `"hi"`, `true`, `'c'` — literal match (Int, Str, Bool, Char)
- `_` — wildcard

## `tag`: semantic labels

```lll
tag UserId
tag Email
tag m
tag s
tag kg
```

A `tag` declaration introduces a label with no runtime representation. Applied
with postfix `[Tag]`:

```lll
let uid = "user-42"[UserId]      -- type: Str[UserId]
let dist = 5.0[m]                -- type: Float[m]
```

See [04-tags-and-units](04-tags-and-units.md).

## `trait`: higher-kinded type classes

```lll
trait Functor F =
  fn map(f A->B)(fa F[A]) F[B]

trait Monad F =
  fn pure(a A) F[A]
  fn bind(fa F[A])(f A->F[B]) F[B]
```

The trait body is indented and contains function signatures (no bodies).

## `impl`: trait implementations

```lll
impl Functor Maybe =
  fn map(f A->B)(fa Maybe[A]) Maybe[B] =
    | Some a -> Some (f a)
    | None -> None
```

An `impl` provides concrete function definitions for a trait applied to a
specific type constructor.

See [05-traits](05-traits.md).

## Expression-level constructs

### Application (juxtaposition)

```lll
add 1 2           -- == (add 1) 2, left-associative
getUser "id"[UserId]
```

Whitespace between atoms is function application. Left-associative — curried.

### Lists

```lll
let xs = [1 2 3]
let ys = [1; 2; 3]
```

Both space-separated and `;`-separated list elements are valid. Commas are not list separators (`[1, 2]` is tuple syntax).

### Tuples

Comma-separated inside parens in a grouping context:

```lll
let t = (1, "hi")
```

### Pipes

`->` is the pipe operator in expression context:

```lll
s -> trim -> len    -- equivalent to len (trim s)
```

The type-arrow `->` (as in `Int -> Bool`) only appears in type positions, so
the two uses do not conflict.

### Arithmetic and comparison

Standard set: `+ - * / < > <= >= == !=`. Precedence follows math convention:
`*` and `/` bind tighter than `+` and `-`, comparisons lower still.
