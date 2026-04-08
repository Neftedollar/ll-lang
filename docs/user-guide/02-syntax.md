# Syntax

Every ll-lang file begins with `module`, followed by optional `import`
declarations, then top-level `let`, `fn`, `type`, `tag`, `trait`, and `impl`
declarations.

Indentation is significant. Comments start with `--` and run to end of line.
ASCII only. No semicolons, no braces.

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
```

Type is inferred from the literal. Integer literals default to `Int` (emitted
as `int64`), decimal literals to `Float`, quoted text to `Str`.

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

Inside a function body, a pattern match is a series of `| pattern -> expr`
branches. The compiler enforces exhaustiveness (error `E003`):

```lll
fn area(s Shape) Float =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty -> 0.0
```

Supported patterns:

- `ConstructorName arg1 arg2` — constructor pattern, binds args by position
- `name` — variable binding (catches anything, binds to `name`)
- `42`, `"hi"`, `true` — literal match
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
```

Space-separated, no commas.

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
