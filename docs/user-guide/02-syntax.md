# Syntax

Every ll-lang file begins with `module`, followed by optional `import`
declarations, then top-level declarations: type declarations (Uppercase),
function declarations (lowercase + params), value bindings (lowercase),
`tag`, `unit`, `trait`, `impl`, `external`, and `opaque`.

Indentation is significant. Comments start with `--` and run to end of line;
both full-line (`-- header`) and trailing (`x + 1 -- increment`) forms are
accepted. ASCII only. No semicolons, no braces.

**Active keywords:** `let`, `tag`, `unit`, `trait`, `impl`, `import`, `export`,
`module`, `external`, `opaque`, `if`, `else`, `true`, `false`, `match`.
There is no `fn`, `type`, `in`, `then`, or `with` keyword.

## Module header

```lll
module Examples.Basics
```

The module path is one or more uppercase-starting segments separated by dots.
Must be the first non-comment token in the file.

## Value declarations

Three declaration forms at the top level:

| Form | Description | Example |
|------|-------------|---------|
| `name = expr` | value binding (no params) | `pi = 3.14159` |
| `name(p T)... = expr` | function declaration | `add(a Int)(b Int) = a + b` |
| `let name = expr` | constant (explicit `let`) | `let greeting = "hello"` |

`let` is optional for simple constants without parameters. Both `pi = 3.14159`
and `let pi = 3.14159` are accepted at the top level.

### Literals

| Form | Type | Notes |
|------|------|-------|
| `42` | `Int` | 64-bit signed |
| `3.14` | `Float` | requires a decimal point |
| `"hi"` | `Str` | supports escapes `\n`, `\t`, `\\`, `\"` |
| `true` / `false` | `Bool` | keywords, not constructors |
| `'c'` | `Char` | supports escapes `\n`, `\t`, `\\`, `\'` |

## Functions

Parameters come in individual `(name Type)` groups. Juxtaposition with
separate parens means currying, not multi-arg:

```lll
add(a Int)(b Int) = a + b
double(x Int) = x * 2
```

The return type after the last param is optional when H-M can infer it.
`double` above has no return annotation — the inferred type is `Int -> Int`.

### Multi-line body

Indent the body one level:

```lll
clamp(x Int)(lo Int)(hi Int) =
  if x < lo
    lo
  else if x > hi
    hi
  else x
```

### Zero-arg functions

Use empty parens (or bare binding):

```lll
main() = printfn "Hello, ll-lang!"
```

### Local bindings

Chain bindings by layout — no `let...in`:

```lll
example =
  y = double 5
  y + 1
```

## Lambdas

```lll
triple = \x. x * 3
```

Backslash, parameter names, a period, then the body expression. Multiple
parameters:

```lll
add = \a b. a + b
```

## `if` / `else`

`if` is an expression — both arms must have the same type. Body is indented
on the next line; `else` follows at the same indent as `if`. There is no
`then` keyword.

```lll
abs(n Int) =
  if n < 0
    0 - n
  else n
```

Chain with `else if`:

```lll
sign(x Int) =
  if x < 0
    "neg"
  else if x == 0
    "zero"
  else "pos"
```

## Type declarations

Types start with an uppercase identifier — no `type` keyword required:

### Sum types (tagged unions)

```lll
Shape = Circle Float | Rect Float Float | Empty
```

Constructors are uppercase identifiers followed by zero or more type arguments.
Branches are separated by `|`.

Multi-line form with an optional leading `|`:

```lll
Color =
  | Red
  | Green
  | Blue
```

### Parametric types

Bare type parameters after the type name:

```lll
Maybe A = Some A | None
Result A E = Ok A | Err E
```

### Phantom type parameters

Bracketed parameters carry no runtime value:

```lll
Email[state] = Str
```

See [04-tags-and-units](04-tags-and-units.md) for the phantom state pattern.

## `match`: pattern matching

The compiler enforces exhaustiveness (error `E003`) in all forms.

**Clause sugar** — when a function body is a match over its last parameter,
list arms directly:

```lll
area(s Shape) =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty -> 0.0
```

**Explicit form** — `match <scrutinee>` followed by indented arms (no `with`):

```lll
describe(m Maybe[Int]) =
  fallback = 0
  match m
    | Some n -> n
    | None -> fallback
```

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
uid = "user-42"[UserId]      -- type: Str[UserId]
dist = 5.0[m]                -- type: Float[m]
```

See [04-tags-and-units](04-tags-and-units.md).

## `trait`: higher-kinded type classes

```lll
trait Functor F =
  map(f A -> B)(fa F[A]) F[B]

trait Monad F =
  pure(a A) F[A]
  bind(fa F[A])(f A -> F[B]) F[B]
```

The trait body is indented and contains method signatures (no bodies, no `fn`).

## `impl`: trait implementations

```lll
impl Functor Maybe =
  map(f A -> B)(fa Maybe[A]) Maybe[B] =
    | Some a -> Some (f a)
    | None -> None
```

An `impl` provides concrete function definitions for a trait applied to a
specific type constructor.

See [05-traits](05-traits.md).

## `external`: platform-native functions

```lll
-- Declares a function implemented in the host platform (no ll-lang body).
external console_log(msg Str) Unit
external JSON_parse(src Str) Str

logGreeting(name Str) =
  console_log (strConcat "Hello, " name)
```

`external` functions are called exactly like regular ll-lang functions.
The compiler validates that the selected target has a mapping for the name in
discovered `FFI.lll` sidecars (SDK and/or vendored packages); if not, it emits
**E026 UnknownExternalMapping**.

Pre-mapped names available on all targets: `console_log`, `JSON_parse`.

## `opaque`: platform-native types

```lll
-- Declares a type whose internals are managed by the host platform.
opaque HttpClient
opaque Buffer
```

Opaque types can appear as parameter and return types on both `external` and
regular ll-lang functions.

## Expression-level constructs

### Application (juxtaposition)

```lll
add 1 2           -- == (add 1) 2, left-associative
getUser "id"[UserId]
```

Whitespace between atoms is function application. Left-associative — curried.

### Lists

```lll
xs = [1 2 3]
ys = [1; 2; 3]
```

Both space-separated and `;`-separated list elements are valid. Commas are not
list separators (`[1, 2]` is tuple syntax).

### Tuples

Comma-separated inside parens in a grouping context:

```lll
t = (1, "hi")
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
