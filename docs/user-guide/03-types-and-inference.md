# Types and inference

ll-lang uses Hindley-Milner (Algorithm W) with let-generalization. Types are
inferred bottom-up from literals. Annotations are required only where
inference would be ambiguous or where you want to lock down a signature.

## Base types

| ll-lang  | Codegen (F#) | Literal form         |
|----------|--------------|----------------------|
| `Int`    | `int64`      | `42`                 |
| `Float`  | `float`      | `3.14`               |
| `Str`    | `string`     | `"hello"`            |
| `Bool`   | `bool`       | `true`, `false`      |
| `Char`   | `char`       | `'c'`, `'\n'`, `'\\'`|
| `Unit`   | `unit`       | (no literal)         |

Integer literals always produce `Int`, decimal literals always produce `Float`.
String and character literals share the escape set `\n \t \\ \" \'`.

## Inferred signatures

```lll
fn double(x Int) = x * 2
-- inferred: Int -> Int

fn square(x Int) = x * x
-- inferred: Int -> Int
```

The return type is optional when the body's type is determined by usage of
the parameters.

## Polymorphism

Type variables in ll-lang use single uppercase letters (`A`, `B`, `E`, ...).
They are rigid and universally quantified over the function:

```lll
fn id(x A) A = x
-- ∀A. A -> A
```

Constructors of a parametric `type` introduce their parameters automatically:

```lll
type Maybe A = Some A | None
-- Some : ∀A. A -> Maybe[A]
-- None : ∀A. Maybe[A]
```

## Let-generalization

```lll
fn f =
  let g = \x. x in
  g
```

Here `g` is generalized at the `let` — calling it with an `Int` and then a
`Str` in the same scope would both type-check.

## When you must annotate

### Parameters of top-level functions

The parser requires every parameter to carry a type:

```lll
fn add(a Int)(b Int) = a + b      -- OK, each param annotated
fn bad(a)(b) = a + b              -- parse error
```

### Ambiguous polymorphism

If a function is fully polymorphic and its return has no connection to its
input, annotate the return:

```lll
fn pure(a A) Maybe[A] = Some a
```

Without the `Maybe[A]` return annotation, the compiler cannot decide which
functor-like type `a` should be wrapped in.

### Tagged parameters

Tags are never inferred automatically on parameters. To require a tagged
argument you must write the annotation:

```lll
fn getUser(id Str[UserId]) Maybe[Str] = Some "alice"
```

### Trait-constrained generics

Use bracket syntax before the parameter list:

```lll
fn transform[F: Functor](xs F[A])(f A->B) F[B] = map f xs
```

This reads as "for any `F` with a `Functor` impl, and any `A`, `B`".

## When you can elide

- Return type of a `fn` whose body is a simple expression
- Types of locals introduced by `let x = expr in body`
- Types of lambda parameters bound to a known context
- Types of literals and constructor applications

## Error tie-ins

The inference pass emits:

- `E001 TypeMismatch` when unification fails on rigid base types (e.g.
  passing `Str` where `Int` is expected)
- `E004 UnitMismatch` when two tagged numeric types have the same base
  but different units (`Float[m]` vs `Float[kg]`)
- `E005 TagViolation` when an untagged value is passed where a tag is
  required (`Str` vs `Str[UserId]`)
- `E008 InfiniteType` if unification would produce a cycle like `a = List[a]`

See [07-error-codes](07-error-codes.md) for reproducible examples.
